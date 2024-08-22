using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using Studio;
using KKAPI.Studio.SaveLoad;
using KKAPI.Utilities;
using ExtensibleSaveFormat;
using MessagePack;
using static BlendshapeCreator.BlendshapeCreator;
using ToolBox.Extensions;
#if HS2
using AIChara;
#endif

namespace BlendshapeCreator
{
    class SceneController : SceneCustomFunctionController
    {

        // Thanks to Njaecha :)
        // Taken from his ObjImport plugin
        protected override void OnSceneSave()
        {
            PluginData data = new PluginData();

            Dictionary<int, OCIBlendShapeData> blendShapesDataDict = new Dictionary<int, OCIBlendShapeData>();

            Dictionary<int, ObjectCtrlInfo> idObjectPairs = Studio.Studio.Instance.dicObjectCtrl;
            foreach (int id in idObjectPairs.Keys)
            {
                if (ociBlendShapesData.ContainsKey(idObjectPairs[id]))
                {
                    BlendshapeCreator.Logger.LogInfo($"Saving blendshape for ID [{id}] | {idObjectPairs[id]}");
                    blendShapesDataDict.Add(id, ociBlendShapesData[idObjectPairs[id]]);
                }
            }

            if (blendShapesDataDict.Count > 0)
                data.data.Add("SceneBlendShapesData", MessagePackSerializer.Serialize(blendShapesDataDict));
            
            SetExtendedData(data);
        }

       

        protected override void OnSceneLoad(SceneOperationKind operation, ReadOnlyDictionary<int, ObjectCtrlInfo> loadedItems)
        {
            var data = GetExtendedData();

            if (operation == SceneOperationKind.Clear || operation == SceneOperationKind.Load)
            {
                ociBlendShapesData.Clear();
                ClearHighlightRenderer(true, null);
            }

            if (data == null || operation == SceneOperationKind.Clear) return;

            // Migrate old data from version < 0.3
            if (data.data.TryGetValue("blendShapeDataList", out var serializedBSList))
            {
                List<BlendShapeData> blendShapeDataList = MessagePackSerializer.Deserialize<List<BlendShapeData>>((byte[])serializedBSList);

                List<int> IDs = new List<int>();
                if (data.data.TryGetValue("ids", out var ids) && ids != null)
                    IDs = MessagePackSerializer.Deserialize<List<int>>((byte[])ids);

                if (IDs.Count <= 0) return;

                for (int x = 0; x < IDs.Count; x++)
                {
                    ObjectCtrlInfo oci = loadedItems[IDs[x]];

                    SkinnedMeshRenderer[] skinnedMeshRenderers = oci.guideObject.transformTarget.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                    BlendShapeData bsData = blendShapeDataList[x];

                    OCIBlendShapeData ociBlendShapeData = new OCIBlendShapeData();

                    for (int y = 0; y < bsData.Names.Count; y++)
                    {
                        string nameBS = bsData.Names[y];
                        string sourceRendererName = bsData.Renderers[y];
                        string deltaVertices = bsData.deltaVertices[y];

                        SkinnedMeshRenderer sourceRenderer = null;

                        for (int i = 0; i < skinnedMeshRenderers.Length; i++)
                            if (skinnedMeshRenderers[i].name == sourceRendererName)
                                sourceRenderer = skinnedMeshRenderers[i];
                        
                        if (sourceRenderer == null) continue;

                        string path = sourceRenderer.transform.GetPathFrom(oci.guideObject.transformTarget);
                        ociBlendShapeData.blendShapes.Add(new BlendShape(path, nameBS, deltaVertices, null, null));
                    }

                    if (ociBlendShapeData.blendShapes.Count > 0)
                        ociBlendShapesData[oci] = ociBlendShapeData;
                }
            }

            // New data from version >= 1.0
            if (data.data.TryGetValue("SceneBlendShapesData", out var serializedData) && serializedData != null)
            {
                Dictionary<int, OCIBlendShapeData> blendShapesDataDict = MessagePackSerializer.Deserialize<Dictionary<int, OCIBlendShapeData>>((byte[])serializedData);

                foreach (var item in blendShapesDataDict)
                    ociBlendShapesData[loadedItems[item.Key]] = item.Value;
            }

            if (ociBlendShapesData.Count > 0)
                StartCoroutine(ReloadSceneBlendShapes(1f, true));
        }
       
        protected override void OnObjectDeleted(ObjectCtrlInfo oci)
        {
            if (ociBlendShapesData.Keys.Contains(oci))
            {
                ociBlendShapesData.Remove(oci);
                ClearHighlightRenderer(true, null);
            }
        }
        protected override void OnObjectsCopied(ReadOnlyDictionary<Int32, ObjectCtrlInfo> copiedItems)
        {
            Dictionary<int, ObjectCtrlInfo> sceneObjects = Studio.Studio.Instance.dicObjectCtrl;

            foreach (int id in copiedItems.Keys)
            {
                if (ociBlendShapesData.ContainsKey(sceneObjects[id]))
                {
                    ObjectCtrlInfo ociTarget = copiedItems[id];
                    OCIBlendShapeData data = ociBlendShapesData[sceneObjects[id]];

                    ociBlendShapesData[ociTarget] = new OCIBlendShapeData(data);
                }
            }

            StartCoroutine(ReloadSceneBlendShapes(1f, true));
            ClearHighlightRenderer(true, null);
        }

        IEnumerator ReloadSceneBlendShapes(float delay, bool skipExisting = false, bool log = false)
        {
            if (ociBlendShapesData.Count == 0) yield break;

            yield return new WaitForSeconds(delay);

            foreach (var data in ociBlendShapesData)
            {
                if (data.Key == null || data.Key.guideObject == null || data.Value == null) continue;
                data.Value.ReloadBlendShapes(data.Key.guideObject.transformTarget, skipExisting);
                if (log)
                    BlendshapeCreator.Logger.LogInfo($"All blendshapes loaded for: {data.Key.treeNodeObject.textName}.");
            }
        }
    }
}
