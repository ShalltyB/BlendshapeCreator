using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace BlendshapeCreator
{
    public class TextureBaker : MonoBehaviour
    {
        private Mesh meshToDraw;
        private Camera cameraToDraw;
        private Material materialToDraw;

        private bool isBaking = false;
        private bool drawMesh = false;

        private Queue<BakingRequest> requestQueue = new Queue<BakingRequest>();

        void Update()
        {
            if (drawMesh && meshToDraw != null && cameraToDraw != null && materialToDraw != null)
                Graphics.DrawMesh(meshToDraw, Vector3.zero, Quaternion.identity, materialToDraw, 1, cameraToDraw, 0, null, false, false, false);

            if (!isBaking && requestQueue.Count > 0)
            {
                StartCoroutine(ProcessNextRequest());
            }
        }

        public void StartTextureBaking(string path, Renderer renderer, int width, int height)
        {
            requestQueue.Enqueue(new BakingRequest { Path = path, Renderer = renderer, Width = width, Height = height });
        }

        private IEnumerator ProcessNextRequest()
        {
            if (requestQueue.Count == 0)
                yield break;

            isBaking = true;
            var request = requestQueue.Dequeue();

            if (cameraToDraw == null)
            {
                // Create a temporary camera to render the mesh
                cameraToDraw = new GameObject("TempCamera").AddComponent<Camera>();
                cameraToDraw.backgroundColor = Color.clear;
                cameraToDraw.clearFlags = CameraClearFlags.Color;
                cameraToDraw.orthographic = true;
                cameraToDraw.orthographicSize = 0.5f;
                cameraToDraw.cullingMask = 1 << 1;
            }

            Mesh mr;
            if (request.Renderer is MeshRenderer meshRenderer)
                mr = meshRenderer.GetComponent<MeshFilter>().mesh;
            else if (request.Renderer is SkinnedMeshRenderer skinnedMeshRenderer)
                mr = skinnedMeshRenderer.sharedMesh;
            else yield break;

            // Unwrap mesh UV
            Mesh uvMesh = Mesh.Instantiate(mr);
            uvMesh.bindposes = null;
            uvMesh.boneWeights = null;
            uvMesh.ClearBlendShapes();
            Vector3[] vertices = uvMesh.vertices;
            for (var i = 0; i < vertices.Length; i++)
            {
                vertices[i] = new Vector3(Reduce(uvMesh.uv[i].x), Reduce(uvMesh.uv[i].y), 0);
            }
            uvMesh.vertices = vertices;

            RenderTexture renderTexture = new RenderTexture(request.Width, request.Height, 32);

            RenderTexture currentRT = RenderTexture.active;
            RenderTexture.active = renderTexture;

            // Position the camera to see the UV mesh correctly
            cameraToDraw.transform.position = new Vector3(0.5f, 0.5f, -10);
            cameraToDraw.transform.rotation = Quaternion.identity;

            cameraToDraw.targetTexture = renderTexture;

            materialToDraw = request.Renderer.sharedMaterial;
            meshToDraw = uvMesh;
            drawMesh = true;

            yield return null;
            yield return null;

            cameraToDraw.Render();

            RenderTexture.active = cameraToDraw.targetTexture;

            Texture2D texture = new Texture2D(cameraToDraw.targetTexture.width, cameraToDraw.targetTexture.height, TextureFormat.ARGB32, false);
            texture.ReadPixels(new Rect(0, 0, cameraToDraw.targetTexture.width, cameraToDraw.targetTexture.height), 0, 0);
            texture.Apply();

            yield return null;

            // Perform dilation pass before saving the texture
            Texture2D dilatedTexture = DilateTexture(texture, 1);

            // Save the texture to a PNG
            BlendshapeCreator.SaveTex(dilatedTexture, request.Path);

            yield return null;
            

            // Clean up resources
            DrawMeshCleanup();
            RenderTextureCleanup(renderTexture, currentRT);
            TextureCleanup(texture, dilatedTexture);
            
            isBaking = false;
            
            // Destroy camera if no more requests are left
            if (requestQueue.Count == 0)
            {
                BlendshapeCreator.Logger.LogMessage($"All textures exported..");
                Destroy(cameraToDraw.gameObject);
                cameraToDraw = null;
            }
        }

        private static float Reduce(float num)
        {
            if (num > 1f)
            {
                num -= 1f;
                return Reduce(num);
            }
            if (num < 0f)
            {
                num += 1f;
                return Reduce(num);
            }
            return num;
        }

        private void DrawMeshCleanup()
        {
            if (meshToDraw != null)
            {
                Destroy(meshToDraw);
                meshToDraw = null;
            }
            drawMesh = false;
            materialToDraw = null;
        }

        private void RenderTextureCleanup(RenderTexture renderTexture, RenderTexture currentRT)
        {
            if (renderTexture != null)
            {
                renderTexture.Release();
                RenderTexture.active = currentRT;
            }
        }

        private void TextureCleanup(Texture2D texture, Texture2D dilatedTexture)
        {
            if (texture != null)
            {
                Destroy(texture);
            }

            if (dilatedTexture != null)
            {
                Destroy(dilatedTexture);
            }
        }

        private Texture2D DilateTexture(Texture2D source, int iterations)
        {
            Texture2D result = new Texture2D(source.width, source.height, source.format, false);

            for (int i = 0; i < iterations; i++)
            {
                for (int y = 0; y < source.height; y++)
                {
                    for (int x = 0; x < source.width; x++)
                    {
                        Color maxColor = new Color(0, 0, 0, 0);
                        for (int offsetY = -1; offsetY <= 1; offsetY++)
                        {
                            for (int offsetX = -1; offsetX <= 1; offsetX++)
                            {
                                int sampleX = Mathf.Clamp(x + offsetX, 0, source.width - 1);
                                int sampleY = Mathf.Clamp(y + offsetY, 0, source.height - 1);
                                Color sampleColor = source.GetPixel(sampleX, sampleY);

                                if (sampleColor.a > maxColor.a)
                                    maxColor = sampleColor;
                            }
                        }
                        result.SetPixel(x, y, maxColor);
                    }
                }
                result.Apply();
                source = result;
            }

            return result;
        }

        void OnGUI()
        {
            if(requestQueue.Count > 0)
                GUI.Label(new Rect(10, 10, 300, 20), $"Baking {requestQueue.Count} textures...");
        }

        private class BakingRequest
        {
            public string Path;
            public Renderer Renderer;
            public int Width;
            public int Height;
        }
    }

}