using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using KKAPI;
using KKAPI.Chara;
using ExtensibleSaveFormat;
using MessagePack;
using static BlendshapeCreator.BlendshapeCreator;
using ToolBox.Extensions;

namespace BlendshapeCreator
{
    class CharacterController : CharaCustomFunctionController
    {
        // Thanks to Njaecha :)
        // Taken from his ObjImport plugin

        private bool isLoadingBlendshapes = false;
        public OCIBlendShapeData CharaBlendShapesData = new OCIBlendShapeData();

        IEnumerator ReloadCharaBlendShapes(float delay, bool skipExisting = false, bool log = false)
        {
            if (!isLoadingBlendshapes && CharaBlendShapesData != null)
            {
                isLoadingBlendshapes = true;
                yield return new WaitForSeconds(delay);
            }
            else
                yield break;

            CharaBlendShapesData.ReloadBlendShapes(ChaControl.transform, skipExisting);

            if (log)
                BlendshapeCreator.Logger.LogInfo($"All blendshapes loaded for: {ChaControl.chaFile.GetFancyCharacterName()}.");

            isLoadingBlendshapes = false;
        }

        internal void CoordintateChangeEvent()
        {
            StartCoroutine(ReloadCharaBlendShapes(1f, true));
        }

        protected override void OnCardBeingSaved(GameMode currentGameMode)
        {
            PluginData data = new PluginData();

            ClearHighlightRenderer(true, null);

            data.data.Add("CharaBlendShapesData", MessagePackSerializer.Serialize(CharaBlendShapesData));
                
            SetExtendedData(data);
        }

        protected override void OnReload(GameMode currentGameMode, Boolean maintainState)
        {
            ClearHighlightRenderer(true, null);
            if (KoikatuAPI.GetCurrentGameMode() == GameMode.Maker && !KKAPI.Maker.MakerAPI.InsideAndLoaded)
                return;
            
            CharaBlendShapesData = new OCIBlendShapeData();

            PluginData data = GetExtendedData();
            if (data != null)
            {
                List<BlendShape> blendShapesMigration = new List<BlendShape>();

                // Migrate old data from version < 0.3
                if (data.data.TryGetValue("blendShapeDataList", out var serializedBSList))
                {
                    List<BlendShapeData> blendShapeDataList = MessagePackSerializer.Deserialize<List<BlendShapeData>>((byte[])serializedBSList);
                    List<float> blendShapeWeightList = new List<float>();

                    if (data.data.TryGetValue("blendShapeWeightList", out var serializedWeightList))
                        blendShapeWeightList = MessagePackSerializer.Deserialize<List<float>>((byte[])serializedWeightList);

                    // Fixing weights for version < 0.2
                    while (blendShapeWeightList.Count < blendShapeDataList[0].Names.Count)
                        blendShapeWeightList.Add(0f);


                    SkinnedMeshRenderer[] skinnedMeshRenderers = ChaControl.GetComponentsInChildren<SkinnedMeshRenderer>(true);

                    foreach (BlendShapeData bsData in blendShapeDataList)
                    {
                        for (int y = 0; y < bsData.Names.Count; y++)
                        {
                            string nameBS = bsData.Names[y];
                            string sourceRendererName = bsData.Renderers[y];
                            string deltaVertices = bsData.deltaVertices[y];
                            float weight = blendShapeWeightList[y];

                            SkinnedMeshRenderer sourceRenderer = null;

                            for (int i = 0; i < skinnedMeshRenderers.Length; i++)
                                if (skinnedMeshRenderers[i].name == sourceRendererName)
                                    sourceRenderer = skinnedMeshRenderers[i];
                            
                            if (sourceRenderer == null) continue;

                            string path = sourceRenderer.transform.GetPathFrom(ChaControl.transform);
                            blendShapesMigration.Add(new BlendShape(path, nameBS, deltaVertices, null, null, weight));
                        }
                    }
                }

                // New data from version >= 1.0
                if (data.data.TryGetValue("CharaBlendShapesData", out var serializedData))
                    CharaBlendShapesData = MessagePackSerializer.Deserialize<OCIBlendShapeData>((byte[])serializedData);
                
                CharaBlendShapesData.blendShapes.AddRange(blendShapesMigration);
                
                if (CharaBlendShapesData.blendShapes.Count > 0)
                    StartCoroutine(ReloadCharaBlendShapes(1f, true));
            }
        }

    }
}