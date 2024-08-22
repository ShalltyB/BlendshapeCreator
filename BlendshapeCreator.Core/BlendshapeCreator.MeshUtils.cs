using UnityEngine;

namespace BlendshapeCreator
{
    /// <summary>
    /// Contains the BindPose skinning logic, taken from https://github.com/thojmr/PregnancyPlus/ by @thojmr <3
    /// plus some utils extensions.
    /// </summary>
    public static class MeshUtils
    {
        /// <summary>
        /// Convert an unskinned mesh vert into the default T-pose mesh vert using the bindpose bone positions
        /// </summary>
        public static Vector3 UnskinnedToSkinnedVertex(Vector3 unskinnedVert, Matrix4x4[] boneMatrices, BoneWeight boneWeight)
        {
            if (boneWeight == null) return Vector3.zero;
            if (boneMatrices == null) return Vector3.zero;

            Matrix4x4 skinningMatrix = GetSkinningMatrix(boneMatrices, boneWeight);
            return skinningMatrix.MultiplyPoint3x4(unskinnedVert);
        }

        /// <summary>
        /// Get the skin matrix used to convert skinned verts into unskinned verts
        /// </summary>
        public static Matrix4x4 GetSkinningMatrix(Matrix4x4[] boneMatrices, BoneWeight weight)
        {
            Matrix4x4 bm0;
            Matrix4x4 bm1;
            Matrix4x4 bm2;
            Matrix4x4 bm3;
            Matrix4x4 reverseSkinningMatrix = new Matrix4x4();

            // Thank you for leving this comment <3
            //If you wanted to `reverse skin` from BakedMesh -> unskinned, you would add '.inverse' to these matricies, but we are `forward skinning` unskinned -> skinned so we don't need it here
            bm0 = boneMatrices[weight.boneIndex0].inverse;
            bm1 = boneMatrices[weight.boneIndex1].inverse;
            bm2 = boneMatrices[weight.boneIndex2].inverse;
            bm3 = boneMatrices[weight.boneIndex3].inverse;

            reverseSkinningMatrix.m00 = bm0.m00 * weight.weight0 + bm1.m00 * weight.weight1 + bm2.m00 * weight.weight2 + bm3.m00 * weight.weight3;
            reverseSkinningMatrix.m01 = bm0.m01 * weight.weight0 + bm1.m01 * weight.weight1 + bm2.m01 * weight.weight2 + bm3.m01 * weight.weight3;
            reverseSkinningMatrix.m02 = bm0.m02 * weight.weight0 + bm1.m02 * weight.weight1 + bm2.m02 * weight.weight2 + bm3.m02 * weight.weight3;
            reverseSkinningMatrix.m03 = bm0.m03 * weight.weight0 + bm1.m03 * weight.weight1 + bm2.m03 * weight.weight2 + bm3.m03 * weight.weight3;

            reverseSkinningMatrix.m10 = bm0.m10 * weight.weight0 + bm1.m10 * weight.weight1 + bm2.m10 * weight.weight2 + bm3.m10 * weight.weight3;
            reverseSkinningMatrix.m11 = bm0.m11 * weight.weight0 + bm1.m11 * weight.weight1 + bm2.m11 * weight.weight2 + bm3.m11 * weight.weight3;
            reverseSkinningMatrix.m12 = bm0.m12 * weight.weight0 + bm1.m12 * weight.weight1 + bm2.m12 * weight.weight2 + bm3.m12 * weight.weight3;
            reverseSkinningMatrix.m13 = bm0.m13 * weight.weight0 + bm1.m13 * weight.weight1 + bm2.m13 * weight.weight2 + bm3.m13 * weight.weight3;

            reverseSkinningMatrix.m20 = bm0.m20 * weight.weight0 + bm1.m20 * weight.weight1 + bm2.m20 * weight.weight2 + bm3.m20 * weight.weight3;
            reverseSkinningMatrix.m21 = bm0.m21 * weight.weight0 + bm1.m21 * weight.weight1 + bm2.m21 * weight.weight2 + bm3.m21 * weight.weight3;
            reverseSkinningMatrix.m22 = bm0.m22 * weight.weight0 + bm1.m22 * weight.weight1 + bm2.m22 * weight.weight2 + bm3.m22 * weight.weight3;
            reverseSkinningMatrix.m23 = bm0.m23 * weight.weight0 + bm1.m23 * weight.weight1 + bm2.m23 * weight.weight2 + bm3.m23 * weight.weight3;

            return reverseSkinningMatrix;
        }

        /// <summary>
        /// creates a cloned mesh with the original name bypassing hideFlags.
        /// </summary>
        public static Mesh CloneSharedMesh(this Renderer source)
        {
            Mesh mesh = GetSharedMesh(source);
            if (mesh) mesh = mesh.Clone() as Mesh;
            return mesh;
        }

        /// <summary>
        /// creates a cloned mesh with the original name bypassing hideFlags.
        /// </summary>
        /// <param name="source"></param>
        /// <returns>can return null if the object does not met the minimum requirements.</returns>
        public static Object Clone(this Mesh source)
        {
            if (!source) return null;
            HideFlags hideFlags = source.hideFlags;
            source.hideFlags = HideFlags.None;
            var clone = Mesh.Instantiate(source);
            source.hideFlags = hideFlags;
            clone.name = EnforceString(source.name);
            clone.name.Replace("(Clone)", "");
            return clone;
        }

        /// <summary>
        /// returns the shared mesh of the renderer
        /// </summary>
        /// <param name="source"></param>
        /// <returns></returns>
        public static Mesh GetSharedMesh(this Renderer source)
        {
            Mesh mesh = null;
            if (!source) return mesh;
            var mr = source.GetComponent<MeshFilter>();
            if (mr) mesh = mr.sharedMesh;
            var smr = source.GetComponent<SkinnedMeshRenderer>();
            if (smr) mesh = smr.sharedMesh;
            return mesh;
        }

        /// <summary>
        /// returns "No Name" instead of null, "", or " ".
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static string EnforceString(string value)
        {
            if (string.IsNullOrEmpty(value)) return "No Name";
            return value;
        }
    }
}