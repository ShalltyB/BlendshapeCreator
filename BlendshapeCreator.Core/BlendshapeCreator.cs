using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using KKAPI.Chara;
using KKAPI.Maker;
using KKAPI.Maker.UI.Sidebar;
using KKAPI.Studio.SaveLoad;
using KKAPI.Utilities;
using MessagePack;
using Studio;
using System;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml;
using ToolBox.Extensions;
using UniRx;
using UnityEngine;
using KKAPI.Studio;
#if HS2
using AIChara;
#endif

namespace BlendshapeCreator
{
    [BepInPlugin(GUID, PluginName, Version)]
    [BepInDependency(KKAPI.KoikatuAPI.GUID, KKAPI.KoikatuAPI.VersionConst)]
    [BepInDependency(ExtensibleSaveFormat.ExtendedSave.GUID, ExtensibleSaveFormat.ExtendedSave.Version)]
    public class BlendshapeCreator : BaseUnityPlugin
    {
        // My first plugin!, thanks to Njaecha and Marco :)
        // I learned a lot from their plugins, also for helping me in the Discord server ^^
        // Highlight shader and associated code belongs to ManlyMarco.

        #region CONFIG VARIABLES

        public const string GUID = "com.shallty.blendshapecreator";
        public const string PluginName = "BlendshapeCreator";
#if KK
        public const string PluginNameInternal = "KK_BlendshapeCreator";
#elif KKS
        public const string PluginNameInternal = "KKS_BlendshapeCreator";
#elif HS2
        public const string PluginNameInternal = "HS2_BlendshapeCreator";
#endif
        public const string Version = "1.0";
        private const int _uniqueId = ('B' << 24) | ('S' << 16) | ('C' << 8) | 'T';
        internal static new ManualLogSource Logger;
        internal static Type blendShapeEditorType;

        #endregion CONFIG VARIABLES

        #region PRIVATE VARIABLES

        private ConfigEntry<string> defaultDir;
        private IEnumerable<ObjectCtrlInfo> selectedObjects;
        private ObjectCtrlInfo firstObject;
        private OCIItem firstItem;
        private OCIChar firstChar;
        private List<Renderer> selectedRenderers = new List<Renderer>();
        private List<Renderer> allRenderers = new List<Renderer>();

        private Action lastImport;

        internal static BlendshapeCreator _self;

        private static Material _mat, _matSolid;
        private static Dictionary<Renderer, Material[]> highlightedRenderers = new Dictionary<Renderer, Material[]>();
        private static Color highlightColor = Color.green;

        #endregion PRIVATE VARIABLES

        #region UI VARIABLES

        private SidebarToggle toggleBS;
        private ConfigEntry<KeyboardShortcut> keyShortcut;
        private Rect windowRect = new Rect(80, 300, 210, 440f);
        private Color defColor = GUI.color;
        public static bool toggleUI = false;
        private bool exportTextures = true;
        private bool inverseX = true;
        private bool bakeMesh = true;
        private bool showHelp = false;
        private bool shiftKey = false;
        private string blendShapesSearch = "";
        private string RenderersSearch = "";
        private bool highlightEnabled = true;
        public string path = "";
        private Vector2 scrollPosition;
        private Vector2 blendShapesScroll;
        private bool showBlendshapeData = false;
        private bool showOnlyBSC = false;
        private bool showHidden = true;
        private float rectDefaultWidth = 250f;
        private float rectDefaultHeight = 523f;
        private float rectBlendshapeSlidersWidth = 647f;
        private static Regex regexFilter = new Regex(".*", RegexOptions.IgnoreCase);

        private static GUISkin NewGUISkin;
        private static GUISkin DefaultGUISkin;

        private static TextureBaker textureBaker;

        #endregion UI VARIABLES

        #region CLASSES

        // OLD DATA KEEPED FOR COMPATIBILITY
        [MessagePackObject]
        public class BlendShapeData
        {
            [Key(0)]
            public List<string> Renderers { get; set; }
            [Key(1)]
            public List<string> Names { get; set; }
            [Key(2)]
            public List<string> deltaVertices { get; set; }

            public BlendShapeData()
            {
                Renderers = new List<string>();
                Names = new List<string>();
                deltaVertices = new List<string>();
            }
        }

        // NEW DATA

        [MessagePackObject]
        public class BlendShape
        {
            [Key(0)]
            public string name;
            [Key(1)]
            public string deltaVertices;
            [Key(2)]
            public string deltaNormals;
            [Key(3)]
            public string deltaTangents;
            [Key(4)]
            public string rendererPath;
            [Key(5)]
            public float weight = 0;

            public BlendShape(string rendererPath, string name, string deltaVertices, string deltaNormals, string deltaTangents, float weight = 0)
            {
                this.name = name;
                this.deltaVertices = deltaVertices;
                this.deltaNormals = deltaNormals;
                this.deltaTangents = deltaTangents;
                this.rendererPath = rendererPath;
                this.weight = weight;
            }

            public void DeleteBlendShape(Transform t)
            {
                SkinnedMeshRenderer renderer = t.Find(this.rendererPath)?.GetComponent<SkinnedMeshRenderer>();
                if (renderer == null) return;

                RemoveBlendshape(renderer, this.name);
            }

            public void LoadBlendShape(Transform t, bool skipExisting = false)
            {
                SkinnedMeshRenderer renderer = t.Find(this.rendererPath)?.GetComponent<SkinnedMeshRenderer>();
                if (renderer == null) return;

                if (renderer.sharedMesh.GetBlendShapeIndex(this.name) != -1 && skipExisting) return;

                Vector3[] deltaVertices = Vector3Array.FromString(this.deltaVertices);
                Vector3[] deltaNormals = Vector3Array.FromString(this.deltaNormals);
                Vector3[] deltaTangents = Vector3Array.FromString(this.deltaTangents);

                Mesh mesh = renderer.CloneSharedMesh();

                if (deltaVertices.Length != mesh.vertexCount) return;

                int n = 0;
                string nameBSModified = this.name;
                while (mesh.GetBlendShapeIndex(nameBSModified) != -1)
                {
                    n++;
                    nameBSModified = this.name + "_" + n;
                }
                mesh.AddBlendShapeFrame(nameBSModified, 100f, deltaVertices, deltaNormals, deltaTangents);
                renderer.sharedMesh = null;
                renderer.sharedMesh = mesh;

                renderer.SetBlendShapeWeight(renderer.sharedMesh.GetBlendShapeIndex(nameBSModified), this.weight);
                Logger.LogInfo($"New blendshape loaded: {nameBSModified}");
            }
        }
        [MessagePackObject]
        public class OCIBlendShapeData
        {
            [IgnoreMember]
            public string size = "0.0 MB";
            [Key(1)]
            public List<BlendShape> blendShapes = new List<BlendShape>();

            public OCIBlendShapeData() { }

            public OCIBlendShapeData(OCIBlendShapeData other)
            {
                size = other.size;
                blendShapes = new List<BlendShape>();

                foreach (BlendShape blendShape in other.blendShapes)
                    blendShapes.Add(new BlendShape(blendShape.rendererPath, blendShape.name, blendShape.deltaVertices, blendShape.deltaNormals, blendShape.deltaTangents));
            }

            public void UpdateDataSize()
            {
                byte[] data = MessagePackSerializer.Serialize(this);
                double dataSize = data.Length / (1024.0 * 1024.0);
                size = $"{dataSize:F2} MB";
            }

            public void ReloadBlendShapes(Transform t, bool skipExisting = false)
            {
                foreach (BlendShape blendShape in blendShapes)
                    if (blendShape != null)
                        blendShape.LoadBlendShape(t, skipExisting);
            }
        }

        public class BlendShapeDisplayData
        {
            public string displayName;
            public OCIBlendShapeData blendShapeData;
            public Transform root;

            public BlendShapeDisplayData(string displayName, OCIBlendShapeData blendShapeData, Transform root)
            {
                this.displayName = displayName;
                this.blendShapeData = blendShapeData;
                this.root = root;
            }
        }

        public class Vector3Array
        {
            private const string Delimiter = ",";

            /// <summary>
            /// Converts a Vector3[] array into a string representation
            /// </summary>
            /// <param name="array">The array to convert</param>
            /// <returns>string</returns>
            public static string ToString(Vector3[] array)
            {
                return string.Join(Delimiter, array.Select(v => v.x + Delimiter + v.y + Delimiter + v.z).ToArray());
            }

            /// <summary>
            /// Converts a string representation into a Vector3[] array
            /// </summary>
            /// <param name="str">The string to convert</param>
            /// <returns>Vector3[]</returns>
            public static Vector3[] FromString(string str)
            {
                if (string.IsNullOrEmpty(str)) return null;

                string[] parts = str.Split(new string[] { Delimiter }, StringSplitOptions.None);
                Vector3[] array = new Vector3[parts.Length / 3];
                for (int i = 0; i < array.Length; i++)
                {
                    array[i] = new Vector3(
                        float.Parse(parts[i * 3]),
                        float.Parse(parts[i * 3 + 1]),
                        float.Parse(parts[i * 3 + 2])
                    );
                }
                return array;
            }
        }

        #endregion CLASSES

        #region PUBLIC VARIABLES

        public static Renderer selectedRenderer = null;

        public static Dictionary<ObjectCtrlInfo, OCIBlendShapeData> ociBlendShapesData = new Dictionary<ObjectCtrlInfo, OCIBlendShapeData>();

        #endregion PUBLIC VARIABLES

        internal void Awake()
        {
            #region BEPINEX CONFIG

            Logger = base.Logger;
#if !HS2
            Harmony harmony = Harmony.CreateAndPatchAll(typeof(Hooks), null);
#endif
            _self = this;

            /// Load KKPE

            Chainloader.PluginInfos.TryGetValue("com.joan6694.kkplugins.kkpe", out PluginInfo pluginInfo);
            if (pluginInfo != null && pluginInfo.Instance != null)
            {
                blendShapeEditorType = pluginInfo.Instance.GetType().Assembly.GetType("HSPE.PoseController");
            }
            else
            {
                blendShapeEditorType = null;
                Logger.LogWarning("There was a problem loading KKPE.");
            }

            //

            textureBaker = gameObject.GetOrAddComponent<TextureBaker>();

            var _defaultDir = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "BlendshapeCreator");
            if (!Directory.Exists(_defaultDir))
                Directory.CreateDirectory(_defaultDir);

            path = _defaultDir;

            KeyboardShortcut _keyShortcut = new KeyboardShortcut(KeyCode.B, KeyCode.LeftControl);
            keyShortcut = Config.Bind("MAIN", "Keyboard shortcut", _keyShortcut, "Press this button to launch the UI.");
            defaultDir = Config.Bind("MAIN", "Default directory", _defaultDir, "The default location for the file dialog box.");

            #endregion BEPINEX CONFIG

            #region EXTRA BEHAVIOURS

            StudioSaveLoadApi.RegisterExtraBehaviour<SceneController>(GUID);
            CharacterApi.RegisterExtraBehaviour<CharacterController>(GUID);

            #endregion EXTRA BEHAVIOURS

            #region STUDIO SETUP

            if (KKAPI.KoikatuAPI.GetCurrentGameMode() == KKAPI.GameMode.Studio)
            {
                selectedObjects = KKAPI.Studio.StudioAPI.GetSelectedObjects();
                firstObject = selectedObjects.FirstOrDefault();
                if (firstObject is OCIChar)
                {
                    firstItem = null;
                    firstChar = (OCIChar)firstObject;
                }
                else if (firstObject is OCIItem)
                {
                    firstChar = null;
                    firstItem = (OCIItem)firstObject;
                }

                StudioSaveLoadApi.ObjectsSelected += (sender, e) =>
                {
                    if (KKAPI.KoikatuAPI.GetCurrentGameMode() == KKAPI.GameMode.Studio)
                    {
                        if (selectedObjects != KKAPI.Studio.StudioAPI.GetSelectedObjects())
                        {
                            selectedObjects = KKAPI.Studio.StudioAPI.GetSelectedObjects();
                            firstObject = selectedObjects.FirstOrDefault();
                            if (firstObject is OCIChar)
                            {
                                firstItem = null;
                                firstChar = (OCIChar)firstObject;
                            }
                            else if (firstObject is OCIItem)
                            {
                                firstChar = null;
                                firstItem = (OCIItem)firstObject;
                            }
                        }
                    }

                    RefreshRenderersList();
                    selectedRenderer = null;
                    selectedRenderers.Clear();
                };
                StudioSaveLoadApi.ObjectDeleted += (sender, e) =>
                {
                    if (KKAPI.KoikatuAPI.GetCurrentGameMode() == KKAPI.GameMode.Studio)
                    {
                        if (selectedObjects != KKAPI.Studio.StudioAPI.GetSelectedObjects())
                        {
                            selectedObjects = KKAPI.Studio.StudioAPI.GetSelectedObjects();
                            firstObject = selectedObjects.FirstOrDefault();
                            if (firstObject is OCIChar)
                            {
                                firstItem = null;
                                firstChar = (OCIChar)firstObject;
                            }
                            else if (firstObject is OCIItem)
                            {
                                firstChar = null;
                                firstItem = (OCIItem)firstObject;
                            }
                        }
                    }

                    RefreshRenderersList();
                    selectedRenderer = null;
                    selectedRenderers.Clear();
                };
            }

            #endregion STUDIO SETUP

            #region MAKER SETUP

            MakerAPI.RegisterCustomSubCategories += (EventHandler<RegisterSubCategoriesEvent>)((sender, e) =>
            {
                toggleBS = new SidebarToggle("Blendshape Creator", false, this);
                e.AddSidebarControl<SidebarToggle>(toggleBS).ValueChanged.Subscribe<bool>((Action<bool>)(b => toggleUI = toggleBS.Value));
            });
            MakerAPI.MakerExiting += (EventHandler)((sender, e) =>
            {
                toggleBS = (SidebarToggle)null;
            });

            #endregion MAKER SETUP

            LoadResources();
        }

        internal void Update()
        {
            if (keyShortcut.Value.IsDown() && (KKAPI.KoikatuAPI.GetCurrentGameMode() == KKAPI.GameMode.Maker || KKAPI.KoikatuAPI.GetCurrentGameMode() == KKAPI.GameMode.Studio))
            {
                RefreshRenderersList();
                ClearHighlightRenderer(true, null);
                //changeWatcher?.Dispose();
                lastImport = null;
                toggleUI = !toggleUI;
            }

            if (KKAPI.KoikatuAPI.GetCurrentGameMode() == KKAPI.GameMode.Studio)
            {
                if (selectedObjects != StudioAPI.GetSelectedObjects())
                {
                    selectedObjects = StudioAPI.GetSelectedObjects();
                    firstObject = selectedObjects.FirstOrDefault();
                    if (firstObject is OCIChar)
                    {
                        firstItem = null;
                        firstChar = (OCIChar)firstObject;
                    }
                    else if (firstObject is OCIItem)
                    {
                        firstChar = null;
                        firstItem = (OCIItem)firstObject;
                    }
                }
            }
        }

        private void OnGUI()
        {
            // if (NewGUISkin != null && NewGUISkin.font == null)
            //    NewGUISkin.font = GUI.skin.font;

            //DefaultGUISkin = GUI.skin;
            //GUI.skin = NewGUISkin == null ? IMGUIUtils.SolidBackgroundGuiSkin : NewGUISkin;

            var guiSkin = GUI.skin;
            GUI.skin = IMGUIUtils.SolidBackgroundGuiSkin;

            if (toggleUI && ((selectedObjects != null && selectedObjects.Count() > 0) || KKAPI.KoikatuAPI.GetCurrentGameMode() == KKAPI.GameMode.Maker))
            {
                KeyboardShortcut _shiftKey = new KeyboardShortcut(KeyCode.LeftShift);
                shiftKey = _shiftKey.IsPressed();
                windowRect = GUILayout.Window(_uniqueId, windowRect, GUILogic, PluginName + "  " + Version);
                if (!shiftKey) IMGUIUtils.EatInputInRect(windowRect);
            }
            GUI.skin = guiSkin;
        }
        private void GUILogic(int WindowID)
        {
            GUI.color = defColor;

            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical();

            if (GUILayout.Button(showBlendshapeData ? new GUIContent("Go back", "Return to main menu") : new GUIContent("Show Blendshapes Data", "Show the list of all the saved data in the current scene/card")))
            {
                showBlendshapeData = !showBlendshapeData;
                RefreshRenderersList();

                List<BlendShapeDisplayData> displayBlendshapeData = new List<BlendShapeDisplayData>();

                if (KKAPI.KoikatuAPI.GetCurrentGameMode() == KKAPI.GameMode.Studio)
                {
                    var ociDir = Studio.Studio.Instance.dicObjectCtrl;
                    foreach (var pair in ociDir)
                    {
                        if (pair.Value is OCIChar chara)
                        {
                            var chaCtrl = KKAPI.Studio.StudioObjectExtensions.GetChaControl(chara);
                            var controller = chaCtrl.GetComponent<CharacterController>();
                            if (controller != null && controller.CharaBlendShapesData != null)
                                displayBlendshapeData.Add(new BlendShapeDisplayData(chaCtrl.chaFile.GetFancyCharacterName(), controller.CharaBlendShapesData, chaCtrl.transform));
                        }
                    }

                    foreach (var pair in ociBlendShapesData)
                        displayBlendshapeData.Add(new BlendShapeDisplayData(pair.Key.treeNodeObject.textName, pair.Value, pair.Key.guideObject.transformTarget));
                }
                else if (KKAPI.KoikatuAPI.GetCurrentGameMode() == KKAPI.GameMode.Maker)
                {
                    ChaControl chaCtrl = MakerAPI.GetCharacterControl();
                    var controller = chaCtrl.gameObject.GetComponent<CharacterController>();
                    if (controller != null && controller.CharaBlendShapesData != null)
                        displayBlendshapeData.Add(new BlendShapeDisplayData(chaCtrl.chaFile.GetFancyCharacterName(), controller.CharaBlendShapesData, chaCtrl.transform));
                }

                foreach (var item in displayBlendshapeData)
                    item.blendShapeData.UpdateDataSize();
            }

            if (!showBlendshapeData)
            {
                #region SCROLL: SkinnedMeshRenderer List

                GUI.color = defColor;
                GUILayout.BeginHorizontal();
                {
                    GUILayout.BeginVertical(GUI.skin.box);

                    GUILayout.BeginHorizontal();
                    GUILayout.FlexibleSpace();
                    GUILayout.Label("Meshes List:");
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();

                    showHidden = GUILayout.Toggle(showHidden, new GUIContent("Show Hidden", "Show all the renderers, even the hidden ones (if any)"));

                    if (GUILayout.Button(new GUIContent("Refresh List", "Refresh list of renderers, do it to update the meshes (after changing clothes/accs/etc)")))
                        RefreshRenderersList();
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    {
                        GUI.changed = false;
                        GUI.SetNextControlName("sbox");
                        var showTipString = RenderersSearch.Length == 0 && GUI.GetNameOfFocusedControl() != "sbox";
                        var newVal = GUILayout.TextField(showTipString ? "Search..." : RenderersSearch, GUILayout.ExpandWidth(true));
                        if (GUI.changed)
                        {
                            RenderersSearch = newVal;
                            UpdateFilterRegex(RenderersSearch);
                        }

                        if (GUILayout.Button("X", GUILayout.ExpandWidth(false)))
                        { 
                            RenderersSearch = "";
                            UpdateFilterRegex(RenderersSearch);
                        }
                    }
                    GUILayout.EndHorizontal();
                    GUILayout.BeginVertical(GUI.skin.box);

                    scrollPosition = GUILayout.BeginScrollView(scrollPosition, false, true, GUILayout.Height(250));

                    if (allRenderers.Count == 0)
                    {
                        GUILayout.FlexibleSpace();
                        GUILayout.BeginHorizontal();
                        GUILayout.FlexibleSpace();
                        GUILayout.Label("Selected object doesn't\n contain meshes!");
                        GUILayout.FlexibleSpace();
                        GUILayout.EndHorizontal();
                        GUILayout.FlexibleSpace();
                    }
                    else
                    {
                        for (int i = 0; i < allRenderers.Count; i++)
                        {
                            Renderer renderer = allRenderers[i];
                            if (renderer == null) continue;

                            if (!showHidden && (!renderer.gameObject.activeInHierarchy || !renderer.enabled)) continue;

                            var meshName = renderer.name;
                            if (IsFilterMatch(meshName))
                            {
                                GUILayout.BeginHorizontal();

                                bool isSelected = selectedRenderers.Contains(renderer);
                                IMGUIExtensions.BoolValue("", isSelected, (b) =>
                                {
                                    if (b)
                                    {
                                        if (!selectedRenderers.Contains(renderer))
                                        {
                                            selectedRenderers.Add(renderer);
                                            HighlightRenderer(renderer, false);
                                        }
                                    }
                                    else
                                    {
                                        if (selectedRenderers.Contains(renderer))
                                        {
                                            selectedRenderers.Remove(renderer);
                                            ClearHighlightRenderer(false, renderer);
                                        }
                                    }
                                });

                                if (renderer is MeshRenderer) GUI.color = Color.yellow;
                                if (selectedRenderers.Contains(renderer)) GUI.color = Color.magenta;
                                if (renderer == selectedRenderer) GUI.color = Color.cyan;


                                if (GUILayout.Button(meshName, GUILayout.ExpandWidth(true)))
                                {
                                    if (renderer == selectedRenderer)
                                    {
                                        if (!selectedRenderers.Contains(renderer))
                                            ClearHighlightRenderer(false, renderer);
                                        selectedRenderer = null;
                                        windowRect.width = rectDefaultWidth;
                                    }
                                    else
                                    {
                                        if (!selectedRenderers.Contains(renderer))
                                        {
                                            HighlightRenderer(renderer, false);
                                            if (selectedRenderer != null && !selectedRenderers.Contains(selectedRenderer))
                                                ClearHighlightRenderer(false, selectedRenderer);
                                        }
                                        selectedRenderer = renderer;
                                    }
                                }
                                GUILayout.EndHorizontal();
                                GUI.color = defColor;
                            }
                            else
                            {
                                GUILayout.BeginHorizontal();
                                GUILayout.FlexibleSpace();
                                GUILayout.EndHorizontal();
                            }
                        }
                    }
                    GUILayout.EndScrollView();

                    if (selectedRenderers.Count > 0)
                    {
                        if (GUILayout.Button("Clear Selection", GUILayout.ExpandWidth(true)))
                        {
                            selectedRenderers.Clear();
                            selectedRenderer = null;
                            ClearHighlightRenderer(true, null);
                        }
                    }

                    GUILayout.EndVertical();

                    GUILayout.Space(10);

                    #endregion SCROLL: SkinnedMeshRenderer List

                    #region BUTTON: CREATE BLENDSHAPE

                    GUI.color = Color.green;
                    GUI.enabled = (selectedRenderer != null && selectedRenderer is SkinnedMeshRenderer);
                    if (GUILayout.Button(new GUIContent("Import BlendShapes", "Import BlendShapes from a .bscd file into the selected mesh"), GUILayout.ExpandWidth(true)))
                    {
                        ImportBlendshapesButton();
                    }

                    #endregion BUTTON: CREATE BLENDSHAPE

                    #region BUTTON: REPEAT LAST IMPORT

                    if (lastImport != null)
                    {
                        GUI.color = defColor;
                        GUI.enabled = true;
                        if (GUILayout.Button(new GUIContent("Repeat Last Import", "Repeat the last import operation without duplicating existing blendshapes (Existing blendshapes will be updated)"), GUILayout.ExpandWidth(true)))
                        {
                            lastImport?.Invoke();
                        }
                    }

                    #endregion BUTTON: REPEAT LAST IMPORT

                    #region BUTTON: EXPORT MESH

                    GUI.color = defColor;
                    GUI.enabled = selectedRenderer != null || selectedRenderers.Count > 0;
                    if (GUILayout.Button(new GUIContent("Export Meshes", "Export the selected meshes to a .obj files"), GUILayout.ExpandWidth(true)))
                    {
                        ExportSelectedMeshes();
                    }

                    #endregion BUTTON: EXPORT MESH

                    #region TOGGLES

                    GUI.enabled = true;
                    exportTextures = GUILayout.Toggle(exportTextures, new GUIContent("Export Textures", "Export the main texture of the selected meshes along with their .mtl file"));
                    inverseX = GUILayout.Toggle(inverseX, new GUIContent("Mirror X Axis", "Mirror the X axis when importing a BlendShape"));
                    bakeMesh = GUILayout.Toggle(bakeMesh, new GUIContent("Bake Mesh", "The exported meshes will be baked with the current pose"));

                    IMGUIExtensions.BoolValue("Highlight Selected", highlightEnabled, (b) =>
                    {
                        ClearHighlightRenderer(true, null);
                        highlightEnabled = b;
                        if (highlightEnabled)
                        {
                            if (selectedRenderer != null)
                                HighlightRenderer(selectedRenderer, false);

                            foreach (var renderer in selectedRenderers)
                                HighlightRenderer(renderer, false);
                        }
                    });

                    #endregion TOGGLES

                    #region BUTTON: HELP
                    /*
                    if (showHelp) GUI.color = Color.cyan;
                    if (GUILayout.Button("Help"))
                    {
                        showHelp = !showHelp;
                        windowRect.height = rectDefaultHeight;
                    }
                    */
                    GUI.color = defColor;
                    GUI.enabled = true;

                    #endregion BUTTON: HELP

                    GUILayout.EndVertical();

                    #region BLENDSHAPES SLIDERS

                    GUILayout.BeginVertical();

                    if (selectedRenderer != null && selectedRenderers.Count <= 0 && selectedRenderer is SkinnedMeshRenderer skinnedMeshRenderer && skinnedMeshRenderer.sharedMesh.blendShapeCount != 0)
                    {
                        windowRect.width = rectBlendshapeSlidersWidth;
                        GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandHeight(true), GUILayout.Width(390));
                        GUILayout.BeginHorizontal();
                        {
                            GUILayout.Label("Search", GUILayout.ExpandWidth(false));
                            blendShapesSearch = GUILayout.TextField(blendShapesSearch, GUILayout.ExpandWidth(true));
                            if (GUILayout.Button("X", GUILayout.ExpandWidth(false)))
                                blendShapesSearch = "";
                        }
                        GUILayout.EndHorizontal();

                        showOnlyBSC = GUILayout.Toggle(showOnlyBSC, new GUIContent("Hide vanilla blendshapes", "Only show the BlendShapes created by this plugin"));

                        GUILayout.BeginVertical(GUI.skin.box);
                        blendShapesScroll = GUILayout.BeginScrollView(blendShapesScroll, false, true, GUILayout.ExpandWidth(false));

                        bool zeroResult = true;
                        for (int i = 0; i < skinnedMeshRenderer.sharedMesh.blendShapeCount; ++i)
                        {
                            string blendShapeName = skinnedMeshRenderer.sharedMesh.GetBlendShapeName(i);
                            if (blendShapeName.IndexOf(blendShapesSearch, StringComparison.CurrentCultureIgnoreCase) != -1)
                            {
                                var shouldShow = !showOnlyBSC;
                                zeroResult = false;
                                float blendShapeWeight = skinnedMeshRenderer.GetBlendShapeWeight(i);

                                GUILayout.BeginHorizontal();
                                GUILayout.BeginVertical(GUILayout.ExpandHeight(false));
                                GUILayout.BeginHorizontal();
                                if (KKAPI.KoikatuAPI.GetCurrentGameMode() == KKAPI.GameMode.Studio)
                                {
                                    if (ociBlendShapesData.ContainsKey(firstObject))
                                    {
                                        int index = ociBlendShapesData[firstObject].blendShapes.Select(x => x.name).ToList().IndexOf(blendShapeName);
                                        if (index != -1)
                                        {
                                            GUI.color = Color.red;
                                            if (GUILayout.Button("Delete", GUILayout.ExpandWidth(false)))
                                            {
                                                ociBlendShapesData[firstObject].blendShapes.RemoveAt(index);
                                                RemoveBlendshape(skinnedMeshRenderer, blendShapeName);
                                                break;
                                            }
                                            if (ociBlendShapesData[firstObject].blendShapes[index].weight != blendShapeWeight) ociBlendShapesData[firstObject].blendShapes[index].weight = blendShapeWeight;
                                            GUI.color = Color.green;
                                            shouldShow = true;
                                        }
                                    }

                                    if (firstObject is OCIChar chara)
                                    {
                                        var controller = chara.GetChaControl().GetComponent<CharacterController>();
                                        if (controller != null && controller.CharaBlendShapesData != null)
                                        {
                                            int index = controller.CharaBlendShapesData.blendShapes.Select(x => x.name).ToList().IndexOf(blendShapeName);
                                            if (index != -1)
                                            {
                                                GUI.color = Color.red;
                                                if (GUILayout.Button("Delete", GUILayout.ExpandWidth(false)))
                                                {
                                                    controller.CharaBlendShapesData.blendShapes.RemoveAt(index);
                                                    RemoveBlendshape(skinnedMeshRenderer, blendShapeName);
                                                    break;
                                                }
                                                if (controller.CharaBlendShapesData.blendShapes[index].weight != blendShapeWeight) controller.CharaBlendShapesData.blendShapes[index].weight = blendShapeWeight;
                                                GUI.color = Color.green;
                                                shouldShow = true;
                                            }
                                        }
                                    }
                                }
                                else if (KKAPI.KoikatuAPI.GetCurrentGameMode() == KKAPI.GameMode.Maker)
                                {
                                    ChaControl chaCtrl = MakerAPI.GetCharacterControl();
                                    var controller = chaCtrl.gameObject.GetComponent<CharacterController>();
                                    var CharaBlendShapes = controller.CharaBlendShapesData;

                                    int index = CharaBlendShapes.blendShapes.Select(x => x.name).ToList().IndexOf(blendShapeName);
                                    if (index != -1)
                                    {
                                        GUI.color = Color.red;
                                        if (GUILayout.Button("Delete", GUILayout.ExpandWidth(false)))
                                        {
                                            CharaBlendShapes.blendShapes.RemoveAt(index);
                                            RemoveBlendshape(skinnedMeshRenderer, blendShapeName);
                                            break;
                                        }
                                        if (CharaBlendShapes.blendShapes[index].weight != blendShapeWeight) CharaBlendShapes.blendShapes[index].weight = blendShapeWeight;
                                        GUI.color = Color.green;
                                        shouldShow = true;
                                    }
                                }

                                if (!shouldShow)
                                {
                                    GUI.color = defColor;
                                    GUI.enabled = true;
                                    GUILayout.EndHorizontal();
                                    GUILayout.EndVertical();
                                    GUILayout.EndHorizontal();
                                    continue;
                                }

                                GUILayout.Label($"[{i}] - {blendShapeName}");
                                GUI.color = defColor;
                                GUILayout.EndHorizontal();

                                IMGUIExtensions.FloatValue("", blendShapeWeight, 0f, 100f, "000.0", (w) => skinnedMeshRenderer.SetBlendShapeWeight(i, w));

                                /*
                                GUILayout.BeginHorizontal();
                                GUILayout.Label(blendShapeWeight.ToString("000") + " | ", GUILayout.ExpandWidth(false));
                                float newBlendShapeWeight = GUILayout.HorizontalSlider(blendShapeWeight, 0f, 100f);
                                if (GUILayout.Button("  000  ", GUILayout.ExpandWidth(false)))
                                    skinnedMeshRenderer.SetBlendShapeWeight(i, 0);

                                newBlendShapeWeight = Mathf.Clamp(newBlendShapeWeight, -100, 200);

                                GUILayout.EndHorizontal();

                                if (Mathf.Approximately(newBlendShapeWeight, blendShapeWeight) == false)
                                {
                                    skinnedMeshRenderer.SetBlendShapeWeight(i, newBlendShapeWeight);
                                }*/
                                GUILayout.EndVertical();
                                GUILayout.EndHorizontal();
                            }
                        }
                        if (zeroResult)
                        {
                            GUILayout.BeginHorizontal();
                            GUILayout.FlexibleSpace();
                            GUILayout.EndHorizontal();
                        }

                        GUILayout.EndScrollView();
                        GUILayout.EndVertical();
                        GUILayout.EndVertical();
                    }
                    else
                    {
                        if (windowRect.width != rectDefaultWidth)
                            windowRect.width = rectDefaultWidth;
                    }

                    GUILayout.EndVertical();
                }
                GUILayout.EndHorizontal();

                #endregion BLENDSHAPES SLIDERS

                #region HELP TEXT

                if (showHelp)
                {
                    GUILayout.Space(10);
                    GUILayout.BeginVertical(GUI.skin.box);
                    GUILayout.Label("Workflow: ");
                    GUILayout.Label("Select mesh --> Select save path --> Export mesh --> Import in your 3D Program (Always Keep Vertex Order) --> Modify the mesh (don't add new vertices) --> Export (Keep Vertex Order) --> Select the .obj --> Create Blendshape.");
                    GUILayout.Label("The name of the blendshape will be the .OBJ file name.");
                    GUILayout.Label("You can invert axis if the blendshape is not created in the correct position.");
                    GUILayout.Label("You can select multiple meshes with Left Shift (only for exporting).");

                    if (KKAPI.KoikatuAPI.GetCurrentGameMode() == KKAPI.GameMode.Maker)
                    {
                        GUILayout.Label("The sliders values will be saved in the card.");
                        GUILayout.Label("After deleting you have to save and reload the card to see the changes.");
                    }
                    else if (KKAPI.KoikatuAPI.GetCurrentGameMode() == KKAPI.GameMode.Studio)
                    {
                        GUILayout.Label("These sliders values will not be saved, use KKPE sliders instead, they also works with Timeline.");
                        GUILayout.Label("After deleting you have to save and reload the scene to see the changes.");
                    }
                    GUILayout.EndVertical();
                }
                else
                {
                    //if (windowRect.height != 500f)
                    //windowRect.height = 500f;
                }
                GUI.DragWindow();

                #endregion HELP TEXT
            }
            else
            {
                //windowRect.width = rectDefaultWidth;
                windowRect.height = rectDefaultHeight;

                #region SCROLL: BlendShapeData List

                GUI.color = defColor;
                GUILayout.BeginHorizontal();
                GUILayout.BeginVertical();

                List<BlendShapeDisplayData> displayBlendshapeData = new List<BlendShapeDisplayData>();

                if (KKAPI.KoikatuAPI.GetCurrentGameMode() == KKAPI.GameMode.Studio)
                {
                    var ociDir = Studio.Studio.Instance.dicObjectCtrl;
                    foreach (var pair in ociDir)
                    {
                        if (pair.Value is OCIChar chara)
                        {
                            var chaCtrl = KKAPI.Studio.StudioObjectExtensions.GetChaControl(chara);
                            var controller = chaCtrl.GetComponent<CharacterController>();
                            if (controller != null && controller.CharaBlendShapesData != null)
                                displayBlendshapeData.Add(new BlendShapeDisplayData(chaCtrl.chaFile.GetFancyCharacterName(), controller.CharaBlendShapesData, chaCtrl.transform));
                        }
                    }

                    foreach (var pair in ociBlendShapesData)
                        displayBlendshapeData.Add(new BlendShapeDisplayData(pair.Key.treeNodeObject.textName, pair.Value, pair.Key.guideObject.transformTarget));
                }
                else if (KKAPI.KoikatuAPI.GetCurrentGameMode() == KKAPI.GameMode.Maker)
                {
                    ChaControl chaCtrl = MakerAPI.GetCharacterControl();
                    var controller = chaCtrl.gameObject.GetComponent<CharacterController>();
                    if (controller != null && controller.CharaBlendShapesData != null)
                        displayBlendshapeData.Add(new BlendShapeDisplayData(chaCtrl.chaFile.GetFancyCharacterName(), controller.CharaBlendShapesData, chaCtrl.transform));
                }

                if (displayBlendshapeData.Count == 0)
                {
                    GUILayout.FlexibleSpace();
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("There is no BlendshapeCreator data!");
                    GUILayout.EndHorizontal();
                    GUILayout.FlexibleSpace();

                    GUILayout.EndVertical();
                    GUILayout.EndHorizontal();

                    GUILayout.EndVertical();
                    GUILayout.EndHorizontal();
                    GUI.DragWindow();
                    return;
                }

                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUILayout.Label("BlendshapeCreator Data");
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                scrollPosition = GUILayout.BeginScrollView(scrollPosition, false, true);

                GUILayout.BeginVertical();
                foreach (var item in displayBlendshapeData)
                {
                    var bs = item.blendShapeData;

                    if (bs == null || bs.blendShapes.Count == 0) continue;

                    var ociName = item.displayName;

                    GUILayout.BeginVertical(GUI.skin.box);

                    GUILayout.BeginHorizontal();
                    GUILayout.FlexibleSpace();
                    GUI.color = defColor;
                    GUILayout.Label(ociName);
                    GUI.color = Color.yellow;
                    GUILayout.Label($" - {bs.size}");
                    GUILayout.FlexibleSpace();
                    GUI.color = defColor;
                    if (GUILayout.Button(new GUIContent("Reload", "Reload all the blendshapes for this item."), GUILayout.ExpandWidth(false)))
                    {
                        bs.ReloadBlendShapes(item.root, true);
                    }
                    GUILayout.EndHorizontal();
                    GUILayout.Space(5);
                    for (int x = 0; x < bs.blendShapes.Count; x++)
                    {
                        GUILayout.BeginHorizontal(GUI.skin.box);
                        GUI.color = Color.green;
                        GUILayout.Label($"{bs.blendShapes[x].name} ({Path.GetFileName(bs.blendShapes[x].rendererPath)}) - {bs.blendShapes[x].weight} %");
                        GUI.color = Color.red;
                        if (GUILayout.Button("Delete", GUILayout.ExpandWidth(false)))
                        {
                            if (KKAPI.KoikatuAPI.GetCurrentGameMode() == KKAPI.GameMode.Maker)
                            {
                                ChaControl chaCtrl = MakerAPI.GetCharacterControl();
                                var controller = chaCtrl.gameObject.GetComponent<CharacterController>();
                                if (controller != null)
                                {
                                    controller.CharaBlendShapesData.blendShapes[x].DeleteBlendShape(item.root);
                                    controller.CharaBlendShapesData.blendShapes.RemoveAt(x);
                                    controller.CharaBlendShapesData.UpdateDataSize();
                                }
                                Logger.LogMessage("Blendshape deleted.");
                            }
                            else
                            {
                                bs.blendShapes[x].DeleteBlendShape(item.root);
                                bs.blendShapes.RemoveAt(x);
                                bs.UpdateDataSize();
                                Logger.LogMessage("Blendshape deleted.");
                            }
                            break;
                        }
                        GUI.color = defColor;
                        GUILayout.EndHorizontal();
                    }
                    GUILayout.EndVertical();
                    GUILayout.Space(10);
                }
                GUILayout.EndVertical();

                GUILayout.EndScrollView();

                GUILayout.EndVertical();
                GUILayout.EndHorizontal();

                GUI.DragWindow();

                #endregion SCROLL: BlendShapeData List
            }

            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            IMGUIUtils.DrawTooltip(windowRect, (int)(windowRect.width / 2));
        }

        private void RefreshRenderersList()
        {
            allRenderers.Clear();
            ClearHighlightRenderer(true, null);
            try
            {
                if (KKAPI.KoikatuAPI.GetCurrentGameMode() == KKAPI.GameMode.Studio)
                {
                    if (firstChar != null)
                    {
                        ChaControl chaCtrl = KKAPI.Studio.StudioObjectExtensions.GetChaControl(firstChar);
                        allRenderers.AddRange(chaCtrl.GetComponentsInChildren<SkinnedMeshRenderer>(true));
                        allRenderers.AddRange(chaCtrl.GetComponentsInChildren<MeshRenderer>(true));
                    }
                    else if (firstItem != null)
                    {
                        allRenderers.AddRange(firstItem.objectItem.GetComponentsInChildren<SkinnedMeshRenderer>(true));
                        allRenderers.AddRange(firstItem.objectItem.GetComponentsInChildren<MeshRenderer>(true));
                    }
                }
                else if (KKAPI.KoikatuAPI.GetCurrentGameMode() == KKAPI.GameMode.Maker)
                {
                    ChaControl chaCtrl = MakerAPI.GetCharacterControl();
                    allRenderers.AddRange(chaCtrl.GetComponentsInChildren<SkinnedMeshRenderer>(true));
                    allRenderers.AddRange(chaCtrl.GetComponentsInChildren<MeshRenderer>(true));
                }
            }
            catch { }
        }

        private void ExportSelectedMeshes()
        {
            path = path.Replace("\\", "/");
            string dir = (path == "") ? defaultDir.Value : path.Replace(path.Substring(path.LastIndexOf("/")), "");
            OpenFileDialog.OpenSaveFileDialgueFlags SingleFileFlags =
             OpenFileDialog.OpenSaveFileDialgueFlags.OFN_LONGNAMES |
             OpenFileDialog.OpenSaveFileDialgueFlags.OFN_EXPLORER;
            string[] file = OpenFileDialog.ShowDialog("OBJ file", dir, "OBJ files (*.obj)|*.obj", "obj", SingleFileFlags);
            if (file == null) return;

            path = file[0];

            if (path == "")
            {
                Logger.LogMessage("Please select the export path.");
                return;
            }
            else if (Directory.Exists(path))
            {
                Logger.LogMessage("Please write a file name.");
                return;
            }
            path = path.Replace("\"", "");
            path = path.Replace("\\", "/");

            List<Renderer> renderersToExport = new List<Renderer>(selectedRenderers);
            if (selectedRenderer != null) renderersToExport.Add(selectedRenderer);

            if (renderersToExport.Count > 0)
            {
                int exportesMeshesCount = 0;
                foreach (Renderer rend in renderersToExport)
                {
                    try
                    {
                        Mesh mesh = rend.GetSharedMesh();
                        string nameMultiple = mesh.name == rend.name ? $"({mesh.name}) " : $"({rend.name} - ({mesh.name})) ";
                        string filename = renderersToExport.Count == 1 ? Path.GetFileNameWithoutExtension(path) : nameMultiple + Path.GetFileNameWithoutExtension(path);
                        string filepath = Path.Combine(Path.GetDirectoryName(path), filename + ".obj");

                        if (renderersToExport.Count > 1)
                        {
                            int count = 0;
                            while (File.Exists(filepath))
                            {
                                filepath = Path.Combine(Path.GetDirectoryName(path), filename + "_" + count + ".obj");
                                count++;
                            }
                        }

                        Matrix4x4[] boneMatrices = null;
                        BoneWeight[] boneWeights = null;

                        if (rend is SkinnedMeshRenderer smr)
                        {
                            // Skinning
                            boneMatrices = smr.bones.Select(x => x.localToWorldMatrix).ToArray();
                            boneWeights = mesh.boneWeights;
                        }

                        if (exportTextures && textureBaker != null)
                        {
                            var mat = rend.sharedMaterial;
                            if (mat != null)
                            {
                                var tex = mat.GetTexture($"_MainTex");
                                string texture_filename = Path.Combine(Path.GetDirectoryName(filepath), filename + ".png");

                                try
                                {
                                    int size = 2046;

                                    if (tex != null)
                                        size = tex.width > tex.height ? tex.width : tex.height;

                                    textureBaker.StartTextureBaking(texture_filename, rend, size, size);
                                }
                                catch (Exception ex)
                                {
                                    Logger.LogError($"Error baking texture: {ex.Message}");

                                    if (tex != null)
                                    {
                                        SaveTex(tex, texture_filename);
                                    }
                                }

                                using (StreamWriter sw = new StreamWriter(Path.Combine(Path.GetDirectoryName(filepath), filename + ".mtl")))
                                {
                                    StringBuilder sb = new StringBuilder();
                                    sb.AppendLine($"newmtl {filename}");
                                    sb.AppendLine($"map_Kd {filename}.png");

                                    string str = sb.ToString();
                                    if (!str.IsNullOrEmpty())
                                        sw.Write(str);
                                }
                            }
                        }

                        using (StreamWriter sw = new StreamWriter(filepath))
                        {
                            StringBuilder sb = new StringBuilder();

                            Mesh subMesh = new Mesh();

                            if (bakeMesh && rend is SkinnedMeshRenderer smr2)
                            {
                                smr2.BakeMesh(subMesh);
                                subMesh.name = mesh.name;
                            }
                            else
                                subMesh = mesh;

                            var scale = rend.transform.lossyScale;
                            var inverseScale = Matrix4x4.Scale(scale).inverse;

                            sb.AppendLine($"g {subMesh.name}");

                            if (exportTextures)
                            {
                                sb.AppendLine($"mtlib {filename}.mtl");
                                sb.AppendLine($"usemtl {filename}");
                            }

                            for (var i = 0; i < subMesh.vertices.Length; i++)
                            {
                                Vector3 v = subMesh.vertices[i];
                                if (bakeMesh)
                                    v = rend.transform.TransformPoint(inverseScale.MultiplyPoint(v));
                                sb.AppendLine($"v {-v.x} {v.y} {v.z}");
                            }

                            for (var i = 0; i < subMesh.uv.Length; i++)
                            {
                                Vector3 v = subMesh.uv[i];
                                sb.AppendLine($"vt {v.x} {v.y}");
                            }

                            for (var i = 0; i < subMesh.normals.Length; i++)
                            {
                                Vector3 v = subMesh.normals[i];
                                sb.AppendLine($"vn {-v.x} {v.y} {v.z}");
                            }

                            int[] triangles = subMesh.triangles;
                            for (int i = 0; i < triangles.Length; i += 3)
                            {
                                sb.AppendFormat("f {0}/{0}/{0} {1}/{1}/{1} {2}/{2}/{2}\n", triangles[i] + 1, triangles[i + 2] + 1, triangles[i + 1] + 1);
                            }

                            if (bakeMesh && boneMatrices != null && boneWeights != null)
                            {
                                string boneMatricesStr = "";
                                for (int i = 0; i < boneMatrices.Length; i++)
                                {
                                    Matrix4x4 matrix = boneMatrices[i];
                                    boneMatricesStr += $"{matrix.m00} {matrix.m01} {matrix.m02} {matrix.m03} {matrix.m10} {matrix.m11} {matrix.m12} {matrix.m13} {matrix.m20} {matrix.m21} {matrix.m22} {matrix.m23} {matrix.m30} {matrix.m31} {matrix.m32} {matrix.m33} ";
                                }
                                if (!boneMatricesStr.IsNullOrEmpty())
                                {
                                    sb.AppendLine($"");
                                    sb.AppendLine($"boneMatrices {boneMatricesStr}");
                                }
                                string boneWeightsStr = "";
                                for (int i = 0; i < boneWeights.Length; i++)
                                {
                                    BoneWeight bw = boneWeights[i];
                                    boneWeightsStr += $"{bw.boneIndex0} {bw.boneIndex1} {bw.boneIndex2} {bw.boneIndex3} {bw.weight0} {bw.weight1} {bw.weight2} {bw.weight3} ";
                                }
                                if (!boneWeightsStr.IsNullOrEmpty())
                                {
                                    sb.AppendLine($"");
                                    sb.AppendLine($"boneWeights {boneWeightsStr}");
                                }
                            }

                            string strMesh = sb.ToString();

                            if (!strMesh.IsNullOrEmpty())
                            {
                                sw.Write(strMesh);
                                Logger.LogInfo($"Exported {filepath}");
                                exportesMeshesCount++;
                            }
                        }
                    }
                    catch
                    {
                        Logger.LogError($"Failed to export {rend.name}");
                    }
                }

                Logger.LogMessage($"Exported {exportesMeshesCount} meshes in directory {Path.GetDirectoryName(path)}");
                selectedRenderers.Clear();
                ClearHighlightRenderer(true, null);
            }
        }

        private void ImportBlendshapesButton()
        {
            path = path.Replace("\\", "/");
            string dir = (path == "") ? defaultDir.Value : path.Replace(path.Substring(path.LastIndexOf("/")), "");
            OpenFileDialog.OpenSaveFileDialgueFlags SingleFileFlags =
            OpenFileDialog.OpenSaveFileDialgueFlags.OFN_LONGNAMES |
            OpenFileDialog.OpenSaveFileDialgueFlags.OFN_EXPLORER;
            string[] file = OpenFileDialog.ShowDialog("Select a BlendShapeCreatorData (.bscd) file.", dir, "BSCD files (*.bscd)|*.bscd", "bscd", SingleFileFlags);
            if (file == null) return;
            path = file[0];

            if (path.IsNullOrWhiteSpace())
            {
                Logger.LogMessage("Please choose a .BSCD file.");
                return;
            }

            path = path.Replace("\"", "");
            path = path.Replace("\\", "/");
            if (!File.Exists(path))
            {
                Logger.LogMessage($"File [{path}] doesn't exist.");
                return;
            }
            else
            {
                SkinnedMeshRenderer renderer = selectedRenderer as SkinnedMeshRenderer;
                Mesh sourceMesh = renderer.CloneSharedMesh();

                ImportBlendshapesFromDAE(sourceMesh, path);
                renderer.sharedMesh = sourceMesh;

                string filePath = path;
                lastImport = () =>
                {
                    if (File.Exists(filePath))
                    {
                        Mesh mesh = renderer.CloneSharedMesh();
                        ImportBlendshapesFromDAE(mesh, filePath, true);
                        renderer.sharedMesh = mesh;
                    }
                };
            }
        }

        // From ManlyMarco's plugin.
        private static void LoadResources()
        {
            /*
            // GUISKIN

            AssetBundle guiSkinAB = null;
            try
            {
                var res = ResourceUtils.GetEmbeddedResource("guiskin.unity3d") ?? throw new ArgumentNullException("GetEmbeddedResource");
                guiSkinAB = AssetBundle.LoadFromMemory(res) ?? throw new ArgumentNullException("LoadFromMemory");
                NewGUISkin = ScriptableObject.CreateInstance<GUISkin>();

                NewGUISkin.box.normal.background = guiSkinAB.LoadAsset<Texture2D>("box.png") ?? throw new ArgumentNullException("LoadAsset");
                NewGUISkin.scrollView.normal.background = NewGUISkin.box.normal.background;

                NewGUISkin.window.normal.background = guiSkinAB.LoadAsset<Texture2D>("window.png") ?? throw new ArgumentNullException("LoadAsset");
                NewGUISkin.window.onNormal.background = guiSkinAB.LoadAsset<Texture2D>("window.png") ?? throw new ArgumentNullException("LoadAsset");

                NewGUISkin.button.normal.background = guiSkinAB.LoadAsset<Texture2D>("button.png") ?? throw new ArgumentNullException("LoadAsset");
                NewGUISkin.button.hover.background = guiSkinAB.LoadAsset<Texture2D>("buttonhover.png") ?? throw new ArgumentNullException("LoadAsset");
                NewGUISkin.button.active.background = guiSkinAB.LoadAsset<Texture2D>("buttonactive.png") ?? throw new ArgumentNullException("LoadAsset");

                NewGUISkin.toggle.normal.background = guiSkinAB.LoadAsset<Texture2D>("toggle.png") ?? throw new ArgumentNullException("LoadAsset");
                NewGUISkin.toggle.hover.background = NewGUISkin.toggle.normal.background;
                NewGUISkin.toggle.onNormal.background = guiSkinAB.LoadAsset<Texture2D>("ontoggle.png") ?? throw new ArgumentNullException("LoadAsset");
                NewGUISkin.toggle.onHover.background = guiSkinAB.LoadAsset<Texture2D>("ontogglehover.png") ?? throw new ArgumentNullException("LoadAsset");

                // HORIZONTAL
                NewGUISkin.horizontalScrollbarThumb.normal.background = guiSkinAB.LoadAsset<Texture2D>("scrollbarthumb.png") ?? throw new ArgumentNullException("LoadAsset");
                NewGUISkin.horizontalScrollbar.normal.background = guiSkinAB.LoadAsset<Texture2D>("slider.png") ?? throw new ArgumentNullException("LoadAsset");
                NewGUISkin.horizontalSliderThumb.normal.background = guiSkinAB.LoadAsset<Texture2D>("sliderthumb.png") ?? throw new ArgumentNullException("LoadAsset");
                NewGUISkin.horizontalSliderThumb.hover.background = guiSkinAB.LoadAsset<Texture2D>("sliderthumbhover.png") ?? throw new ArgumentNullException("LoadAsset");
                NewGUISkin.horizontalSlider.normal.background = NewGUISkin.horizontalScrollbar.normal.background;

                // VERTICAL
                NewGUISkin.verticalScrollbarThumb.normal.background = guiSkinAB.LoadAsset<Texture2D>("scrollbarthumbvert.png") ?? throw new ArgumentNullException("LoadAsset");
                NewGUISkin.verticalScrollbar.normal.background = guiSkinAB.LoadAsset<Texture2D>("slidervert.png") ?? throw new ArgumentNullException("LoadAsset");
                NewGUISkin.verticalSliderThumb.normal.background = NewGUISkin.horizontalSliderThumb.normal.background;
                NewGUISkin.verticalSliderThumb.hover.background = NewGUISkin.horizontalSliderThumb.hover.background;
                NewGUISkin.verticalSlider.normal.background = NewGUISkin.horizontalScrollbar.normal.background;

                NewGUISkin.textField.normal.background = guiSkinAB.LoadAsset<Texture2D>("textfield.png") ?? throw new ArgumentNullException("LoadAsset");
                NewGUISkin.textField.hover.background = guiSkinAB.LoadAsset<Texture2D>("textfieldhover.png") ?? throw new ArgumentNullException("LoadAsset");

                NewGUISkin.textArea.normal.background = NewGUISkin.textField.normal.background;
                NewGUISkin.textArea.hover.background = NewGUISkin.textField.hover.background;

                var jsonBytes = ResourceUtils.GetEmbeddedResource("guiskindata.json") ?? throw new ArgumentNullException("GetEmbeddedResource");
                string jsonData = Encoding.UTF8.GetString(jsonBytes);
                NewGUISkin = ToolBox.GUISkinSerializer.LoadGUISkin(jsonData, NewGUISkin);

                guiSkinAB.Unload(false);
            }
            catch (Exception)
            {
                if (guiSkinAB != null) guiSkinAB.Unload(true);
                throw;
            }
            */

            // BONELYFANS

            if (_mat != null) return;

            AssetBundle bonelyfansAB = null;
            try
            {
                var res = ResourceUtils.GetEmbeddedResource("bonelyfans.unity3d") ?? throw new ArgumentNullException("GetEmbeddedResource");
                bonelyfansAB = AssetBundle.LoadFromMemory(res) ?? throw new ArgumentNullException("LoadFromMemory");
                var assetName = bonelyfansAB.GetAllAssetNames().First(x => x.Contains("bonelyfans"));
                var sha = bonelyfansAB.LoadAsset<Shader>(assetName) ?? throw new ArgumentNullException("LoadAsset");
                bonelyfansAB.Unload(false);

                _mat = new Material(sha);
                _mat.SetInt("_UseMaterialColor", 0);

                _matSolid = new Material(sha);
                _matSolid.SetInt("_UseMaterialColor", 1);
                _matSolid.SetColor("_Color", highlightColor);
            }
            catch (Exception)
            {
                if (bonelyfansAB != null) bonelyfansAB.Unload(true);
                throw;
            }
        }

        private void HighlightRenderer(Renderer renderer, bool clearBeforeAdding)
        {
            if (clearBeforeAdding) ClearHighlightRenderer(true, null);

            if (!highlightEnabled) return;

            if (!highlightedRenderers.ContainsKey(renderer))
            {
                var materials = renderer.sharedMaterials;
                highlightedRenderers.Add(renderer, materials);
                renderer.sharedMaterials = materials.AddToArray(_matSolid);
            }
        }

        public static void ClearHighlightRenderer(bool deleteAll, Renderer rendererToClear)
        {
            if (highlightedRenderers.Count == 0) return;

            if (deleteAll)
            {
                foreach (var kvp in highlightedRenderers)
                {
                    if (kvp.Key != null)
                        kvp.Key.sharedMaterials = kvp.Value;
                }
                highlightedRenderers.Clear();
            }
            else
            {
                if (rendererToClear != null)
                {
                    if (highlightedRenderers.ContainsKey(rendererToClear))
                    {
                        rendererToClear.sharedMaterials = highlightedRenderers[rendererToClear];
                        highlightedRenderers.Remove(rendererToClear);
                    }
                }
            }
        }

        private static void ReplaceRenderer(Renderer currentRenderer, Renderer newRenderer, Renderer[][] renderers)
        {
            foreach (Renderer[] rendererArray in renderers)
            {
                for (int i = 0; i < rendererArray.Length; i++)
                {
                    if (rendererArray[i] == currentRenderer)
                    {
                        rendererArray[i] = newRenderer;
                    }
                }
            }
        }

        public void ImportBlendshapesFromDAE(Mesh targetMesh, string filePath, bool replace = false)
        {
            SkinnedMeshRenderer sourceRenderer = selectedRenderer as SkinnedMeshRenderer;

            string[] lines = File.ReadAllLines(filePath);
            string xmlData = "";

            // Read Bone Matrices and Weights.

            int expectedBoneMatrices = sourceRenderer.bones.Length;
            int expectedBoneWeights = sourceRenderer.sharedMesh.boneWeights.Length;

            Matrix4x4[] boneMatrices = null;
            BoneWeight[] boneWeights = null;

            bool keepReading = true;
            foreach (string line in lines)
            {
                if (line.StartsWith("boneMatrices "))
                {
                    keepReading = false;

                    string boneMatricesString = line.Substring("boneMatrices ".Length);
                    string[] boneMatricesStringArray = boneMatricesString.Split(' ');
                    int boneMatricesCount = boneMatricesStringArray.Length / 16;

                    if (boneMatricesCount != expectedBoneMatrices)
                    {
                        Logger.LogWarning($"Expected {expectedBoneMatrices} bone matrices, but found {boneMatricesCount} in {filePath}");
                        break;
                    }

                    boneMatrices = new Matrix4x4[boneMatricesCount];
                    for (int i = 0; i < boneMatricesCount; i++)
                    {
                        int baseIndex = i * 16;
                        boneMatrices[i] = new Matrix4x4();
                        float.TryParse(boneMatricesStringArray[baseIndex], out boneMatrices[i].m00);
                        float.TryParse(boneMatricesStringArray[baseIndex + 1], out boneMatrices[i].m01);
                        float.TryParse(boneMatricesStringArray[baseIndex + 2], out boneMatrices[i].m02);
                        float.TryParse(boneMatricesStringArray[baseIndex + 3], out boneMatrices[i].m03);
                        float.TryParse(boneMatricesStringArray[baseIndex + 4], out boneMatrices[i].m10);
                        float.TryParse(boneMatricesStringArray[baseIndex + 5], out boneMatrices[i].m11);
                        float.TryParse(boneMatricesStringArray[baseIndex + 6], out boneMatrices[i].m12);
                        float.TryParse(boneMatricesStringArray[baseIndex + 7], out boneMatrices[i].m13);
                        float.TryParse(boneMatricesStringArray[baseIndex + 8], out boneMatrices[i].m20);
                        float.TryParse(boneMatricesStringArray[baseIndex + 9], out boneMatrices[i].m21);
                        float.TryParse(boneMatricesStringArray[baseIndex + 10], out boneMatrices[i].m22);
                        float.TryParse(boneMatricesStringArray[baseIndex + 11], out boneMatrices[i].m23);
                        float.TryParse(boneMatricesStringArray[baseIndex + 12], out boneMatrices[i].m30);
                        float.TryParse(boneMatricesStringArray[baseIndex + 13], out boneMatrices[i].m31);
                        float.TryParse(boneMatricesStringArray[baseIndex + 14], out boneMatrices[i].m32);
                        float.TryParse(boneMatricesStringArray[baseIndex + 15], out boneMatrices[i].m33);
                    }
                }
                else if (line.StartsWith("boneWeights "))
                {
                    keepReading = false;

                    string boneWeightsString = line.Substring("boneWeights ".Length);
                    string[] boneWeightsStringArray = boneWeightsString.Split(' ');
                    int boneWeightsCount = boneWeightsStringArray.Length / 8;

                    if (boneWeightsCount != expectedBoneWeights)
                    {
                        Logger.LogWarning($"Expected {expectedBoneWeights} bone weights, but found {boneWeightsCount} in {filePath}");
                        break;
                    }

                    boneWeights = new BoneWeight[boneWeightsCount];
                    for (int i = 0; i < boneWeightsCount; i++)
                    {
                        int baseIndex = i * 8;
                        if (int.TryParse(boneWeightsStringArray[baseIndex], out int boneIndex0)) boneWeights[i].boneIndex0 = boneIndex0;
                        if (int.TryParse(boneWeightsStringArray[baseIndex + 1], out int boneIndex1)) boneWeights[i].boneIndex1 = boneIndex1;
                        if (int.TryParse(boneWeightsStringArray[baseIndex + 2], out int boneIndex2)) boneWeights[i].boneIndex2 = boneIndex2;
                        if (int.TryParse(boneWeightsStringArray[baseIndex + 3], out int boneIndex3)) boneWeights[i].boneIndex3 = boneIndex3;
                        if (float.TryParse(boneWeightsStringArray[baseIndex + 4], out float weight0)) boneWeights[i].weight0 = weight0;
                        if (float.TryParse(boneWeightsStringArray[baseIndex + 5], out float weight1)) boneWeights[i].weight1 = weight1;
                        if (float.TryParse(boneWeightsStringArray[baseIndex + 6], out float weight2)) boneWeights[i].weight2 = weight2;
                        if (float.TryParse(boneWeightsStringArray[baseIndex + 7], out float weight3)) boneWeights[i].weight3 = weight3;
                    }
                }

                if (keepReading)
                    xmlData += line + "\n";
            }

            // Load the .dae file as an XML document
            XmlDocument xmlDoc = new XmlDocument();
            xmlDoc.LoadXml(xmlData);

            XmlNamespaceManager nsMgr = new XmlNamespaceManager(xmlDoc.NameTable);
            nsMgr.AddNamespace("c", "http://www.collada.org/2005/11/COLLADASchema");

            XmlNode library_geometriesNode = xmlDoc.SelectSingleNode("//c:library_geometries", nsMgr);
            if (library_geometriesNode == null)
                return;

            Matrix4x4 localToWorld = selectedRenderer.transform.worldToLocalMatrix;

            // Get the first <geometry> node as base mesh.

            XmlNode firstMeshNode = library_geometriesNode.ChildNodes[0];

            string[] floatArrayValuesbase = firstMeshNode.FirstChild.FirstChild.FirstChild.InnerText.Trim().Split(' ');
            int frameCountbase = floatArrayValuesbase.Length / 3;
            Vector3[] baseVertices = new Vector3[frameCountbase];
            for (int i = 0; i < frameCountbase; i++)
            {
                int baseIndex = i * 3;
                float x = float.Parse(floatArrayValuesbase[baseIndex]);
                float y = float.Parse(floatArrayValuesbase[baseIndex + 1]);
                float z = float.Parse(floatArrayValuesbase[baseIndex + 2]);
                if (inverseX)
                    baseVertices[i] = new Vector3(-x, y, z);
                else
                    baseVertices[i] = new Vector3(x, y, z);
            }

            // <geometry> node
            foreach (XmlNode geometryNode in library_geometriesNode.ChildNodes)
            {
                if (geometryNode.Attributes["id"].Value.Contains("morph"))
                {
                    XmlNode meshNode = geometryNode.ChildNodes[0];
                    XmlNode sourceNode = meshNode.ChildNodes[0];

                    if (sourceNode.Name != "source" && !(sourceNode.Attributes["id"].Value.Contains("positions"))) continue;

                    string sourceId = geometryNode.Attributes["name"].Value;

                    XmlNode floatArrayNode = sourceNode.ChildNodes[0];

                    if (floatArrayNode == null)
                        continue;

                    // Extract the blendshape data from the float array
                    string[] floatArrayValues = floatArrayNode.InnerText.Trim().Split(' ');
                    int frameCount = floatArrayValues.Length / 3;
                    Vector3[] frameVertices = new Vector3[frameCount];

                    for (int i = 0; i < frameCount; i++)
                    {
                        int baseIndex = i * 3;
                        float x = float.Parse(floatArrayValues[baseIndex]);
                        float y = float.Parse(floatArrayValues[baseIndex + 1]);
                        float z = float.Parse(floatArrayValues[baseIndex + 2]);

                        if (inverseX)
                            frameVertices[i] = new Vector3(-x, y, z);
                        else
                            frameVertices[i] = new Vector3(x, y, z);
                    }

                    if (frameVertices.Length != targetMesh.vertexCount) continue;

                    for (int i = 0; i < frameVertices.Length; i++)
                    {
                        Vector3 oldVertice = frameVertices[i];
                        Vector3 deltaVertice = oldVertice - baseVertices[i];

                        if (boneWeights != null && boneMatrices != null)
                        {
                            Vector3 unskinnedBaseVertice = MeshUtils.UnskinnedToSkinnedVertex(baseVertices[i], boneMatrices, boneWeights[i]);
                            Vector3 unskinnedOldVertice = MeshUtils.UnskinnedToSkinnedVertex(oldVertice, boneMatrices, boneWeights[i]);

                            deltaVertice = unskinnedOldVertice - unskinnedBaseVertice;
                        }

                        frameVertices[i] = deltaVertice;
                    }

                    string nameBS = sourceId;

                    int n = 0;
                    string name = "BS-C: " + nameBS;
                    string nameBSModified = name;
                    bool wasReplaced = false;
                    if (!replace)
                    {
                        while (targetMesh.GetBlendShapeIndex(nameBSModified) != -1)
                        {
                            n++;
                            nameBSModified = name + "_" + n;
                        }
                    }
                    else if (targetMesh.GetBlendShapeIndex(nameBSModified) != -1)
                    {
                        wasReplaced = true;
                        RemoveBlendshape(targetMesh, new List<string>() { nameBSModified });
                    }
                    targetMesh.AddBlendShapeFrame(nameBSModified, 100f, frameVertices, null, null);
                    Logger.LogMessage("New BlendShape created: " + nameBSModified);

                    string deltaVertices = Vector3Array.ToString(frameVertices);

                    if (wasReplaced) continue;

                    // NEW SAVE DATA
                    if (KKAPI.KoikatuAPI.GetCurrentGameMode() == KKAPI.GameMode.Studio)
                    {
                        if (ociBlendShapesData.TryGetValue(firstObject, out OCIBlendShapeData blendShapeData))
                        {
                            string path = sourceRenderer.transform.GetPathFrom(firstObject.guideObject.transformTarget);
                            blendShapeData.blendShapes.Add(new BlendShape(path, nameBSModified, deltaVertices, null, null));
                            Logger.LogInfo($"Saving Blendshape: {nameBSModified} for Renderer: {sourceRenderer.name}");
                        }
                        else
                        {
                            OCIBlendShapeData newBlendShapeData = new OCIBlendShapeData();
                            string path = sourceRenderer.transform.GetPathFrom(firstObject.guideObject.transformTarget);
                            newBlendShapeData.blendShapes.Add(new BlendShape(path, nameBSModified, deltaVertices, null, null));
                            ociBlendShapesData[firstObject] = newBlendShapeData;
                            Logger.LogInfo($"(New OCI) Saving Blendshape: {nameBSModified} for Renderer: {sourceRenderer.name}");
                        }
                    }
                    else if (KKAPI.KoikatuAPI.GetCurrentGameMode() == KKAPI.GameMode.Maker)
                    {
                        ChaControl chaCtrl = MakerAPI.GetCharacterControl();
                        var controller = chaCtrl.gameObject.GetComponent<CharacterController>();
                        var blendShapeData = controller.CharaBlendShapesData;

                        string path = sourceRenderer.transform.GetPathFrom(chaCtrl.transform);
                        blendShapeData.blendShapes.Add(new BlendShape(path, nameBSModified, deltaVertices, null, null));
                        Logger.LogInfo($"Saving Blendshape: {nameBSModified} for Renderer: {sourceRenderer.name}");
                    }
                }
            }
        }

        private static void RemoveBlendshape(Mesh mesh, List<string> blendshapesNamesToDelete)
        {
            try
            {
                List<int> blendshapeIndices = new List<int>();
                foreach (string blendshape in blendshapesNamesToDelete)
                    blendshapeIndices.Add(mesh.GetBlendShapeIndex(blendshape));

                int blendshapeCount = mesh.blendShapeCount;

                List<List<Vector3[]>> blendshapeVertices = new List<List<Vector3[]>>();
                List<List<Vector3[]>> blendshapeNormals = new List<List<Vector3[]>>();
                List<List<Vector3[]>> blendshapeTangents = new List<List<Vector3[]>>();
                List<string> blendshapeNames = new List<string>();
                List<List<float>> blendshapeWeights = new List<List<float>>();

                for (int i = 0; i < blendshapeCount; i++)
                {
                    int frameCount = mesh.GetBlendShapeFrameCount(i);

                    List<Vector3[]> frame_blendshapeVertices = new List<Vector3[]>();
                    List<Vector3[]> frame_blendshapeNormals = new List<Vector3[]>();
                    List<Vector3[]> frame_blendshapeTangents = new List<Vector3[]>();
                    List<float> frame_blendshapeWeights = new List<float>();

                    for (int j = 0; j < frameCount; j++)
                    {
                        Vector3[] BSvertices = new Vector3[mesh.vertexCount];
                        Vector3[] BSnormals = new Vector3[mesh.vertexCount];
                        Vector3[] BStangents = new Vector3[mesh.vertexCount];

                        mesh.GetBlendShapeFrameVertices(i, j, BSvertices, BSnormals, BStangents);

                        frame_blendshapeVertices.Add(BSvertices);
                        frame_blendshapeNormals.Add(BSnormals);
                        frame_blendshapeTangents.Add(BStangents);
                        frame_blendshapeWeights.Add(mesh.GetBlendShapeFrameWeight(i, j));
                    }

                    blendshapeNormals.Add(frame_blendshapeNormals);
                    blendshapeTangents.Add(frame_blendshapeTangents);
                    blendshapeVertices.Add(frame_blendshapeVertices);
                    blendshapeWeights.Add(frame_blendshapeWeights);
                    blendshapeNames.Add(mesh.GetBlendShapeName(i));
                }

                mesh.ClearBlendShapes();

                for (int i = 0; i < blendshapeCount; i++)
                {
                    if (blendshapeIndices.Contains(i))
                        continue;

                    int frameCount = blendshapeVertices[i].Count;

                    for (int j = 0; j < frameCount; j++)
                    {
                        mesh.AddBlendShapeFrame(blendshapeNames[i], blendshapeWeights[i][j], blendshapeVertices[i][j], blendshapeNormals[i][j], blendshapeTangents[i][j]);
                    }
                }
            }
            catch
            { }
        }

        private static void RemoveBlendshape(SkinnedMeshRenderer renderer, List<string> blendshapesNamesToDelete)
        {
            try
            {
                Mesh mesh = Instantiate(renderer.sharedMesh);

                RemoveBlendshape(mesh, blendshapesNamesToDelete);

                renderer.sharedMesh = null;
                renderer.sharedMesh = mesh;
            }
            catch
            { }
        }

        private static void RemoveBlendshape(SkinnedMeshRenderer renderer, string blendshapeName)
        {
            RemoveBlendshape(renderer, new List<string>() { blendshapeName });
        }

        #region Regex filter by takahiro0327 taken from Timeline

        private void UpdateFilterRegex(string filterText)
        {
            filterText = filterText.Trim();

            if (string.IsNullOrEmpty(filterText))
            {
                regexFilter = new Regex(".*", RegexOptions.IgnoreCase);
                return;
            }

            var filters = filterText.Split('|');
            StringBuilder builder = new StringBuilder();

            for (int i = 0; i < filters.Length; ++i)
            {
                var filter = filters[i].Trim();

                if (string.IsNullOrEmpty(filter))
                    continue;

                if (builder.Length > 0)
                    builder.Append('|');

                var fs = filter.Split('&', ',')
                    .Select(s => Regex.Escape(s.Trim()).Replace("\\?", ".").Replace("\\*", ".*"))
                    .Where(s => s.Length > 0)
                    .ToArray();

                if (fs.Length <= 0)
                    continue;

                int[] indices = new int[fs.Length];
                for (int j = 0; j < indices.Length; ++j) indices[j] = j;

                //Reorder the filter keywords so that they can be entered in any order.
                while (true)
                {
                    builder.Append("(");

                    for (int j = 0; j < fs.Length; ++j)
                    {
                        builder.Append(".*");
                        builder.Append(fs[indices[j]]);
                    }

                    builder.Append(".*)");

                    if (NextPermutation(indices))
                        builder.Append('|');
                    else
                        break;
                }
            }

            try
            {
                if (builder.Length > 0)
                {
                    regexFilter = new Regex(builder.ToString(), RegexOptions.IgnoreCase);
                    return;
                }
            }
            catch (System.Exception e)
            {
                Logger.LogError(e);
            }

            regexFilter = new Regex(".*", RegexOptions.IgnoreCase);
        }

        private static bool NextPermutation(int[] array)
        {
            int i = array.Length - 2;
            while (i >= 0 && array[i] >= array[i + 1])
            {
                i--;
            }

            if (i < 0)
            {
                return false;
            }

            int j = array.Length - 1;
            while (array[j] <= array[i])
            {
                j--;
            }

            int tmp = array[i];
            array[i] = array[j];
            array[j] = tmp;

            Array.Reverse(array, i + 1, array.Length - (i + 1));
            return true;
        }

        private bool IsFilterMatch(string name)
        {
            return regexFilter.IsMatch(name);
        }

        #endregion

        #region Texture Utils taken from MaterialEditor

        private static Texture2D GetT2D(RenderTexture renderTexture)
        {
            var currentActiveRT = RenderTexture.active;
            RenderTexture.active = renderTexture;
            var tex = new Texture2D(renderTexture.width, renderTexture.height);
            tex.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
            RenderTexture.active = currentActiveRT;
            return tex;
        }
        public static void SaveTex(Texture tex, string path, RenderTextureFormat rtf = RenderTextureFormat.Default, RenderTextureReadWrite cs = RenderTextureReadWrite.Default)
        {
            var tmp = RenderTexture.GetTemporary(tex.width, tex.height, 0, rtf, cs);
            var currentActiveRT = RenderTexture.active;
            RenderTexture.active = tmp;
            GL.Clear(false, true, new Color(0, 0, 0, 0));
            Graphics.Blit(tex, tmp);
            SaveTexR(tmp, path);
            RenderTexture.active = currentActiveRT;
            RenderTexture.ReleaseTemporary(tmp);
        }

        private static void SaveTexR(RenderTexture renderTexture, string path)
        {
            var tex = GetT2D(renderTexture);
#if KK
            File.WriteAllBytes(path, tex.EncodeToPNG());
#else
            File.WriteAllBytes(path, ImageConversion.EncodeToPNG(tex));
#endif
            DestroyImmediate(tex);
        }

#endregion Texture Utils taken from MaterialEditor
    }
}