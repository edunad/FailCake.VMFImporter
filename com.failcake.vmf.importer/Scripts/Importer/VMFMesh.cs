#region

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AssetImporters;
#endif

#endregion

namespace FailCake.VMF
{
    #if UNITY_EDITOR

    internal static class VMFMesh
    {
        public static Matrix4x4 GetDefaultTransform()
        {
            // VMF to Unity coordinate system conversion
            // VMF: Z-up, right-handed -> Unity: Y-up, left-handed
            return Matrix4x4.TRS(
                Vector3.zero,
                Quaternion.Euler(-90, 0, 180),
                new Vector3(-0.022F, 0.022F, 0.022F)
            );
        }

        public static Mesh GenerateMesh(AssetImportContext ctx, Dictionary<string, List<VMFSide>> materialGroups, ref List<Material> generatedMaterials, VMFMaterials vmfMaterials = null)
        {
            if (materialGroups is not { Count: > 0 })
            {
                Debug.LogWarning("No material groups provided for mesh generation");
                return null;
            }

            try
            {
                Dictionary<MaterialType, List<string>> groupMaterials = VMFTextures.GroupMaterials(materialGroups.Keys);

                // Create batch groups - maximum 32 textures per array
                Dictionary<string, TextureArrayInfo> textureArrayMappings = null;
                Dictionary<MaterialType, List<List<string>>> materialBatches = VMFTextures.BatchMaterials(groupMaterials);

                if (generatedMaterials != null && materialBatches?.Count > 0)
                {
                    textureArrayMappings = VMFTextures.CreateTextureArrays(ctx, materialBatches, ref generatedMaterials, vmfMaterials);
                    if (textureArrayMappings is not { Count: > 0 })
                    {
                        Debug.LogWarning("No texture arrays created, proceeding without textures");
                        // Continue without textures rather than failing completely
                    }
                }

                // Generate mesh with texture arrays
                return VMFMesh.GenerateMeshWithTextureArrays(materialGroups, textureArrayMappings);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Error generating mesh: {ex.Message}");
                return null;
            }
        }

        public static Mesh GenerateMeshWithSharedTextures(Dictionary<string, List<VMFSide>> materialGroups, Dictionary<string, TextureArrayInfo> sharedTextureArrays)
        {
            if (materialGroups is not { Count: > 0 })
            {
                Debug.LogWarning("No material groups provided for mesh generation with shared textures");
                return null;
            }

            if (sharedTextureArrays == null)
            {
                Debug.LogWarning("No shared texture arrays provided, falling back to no-texture generation");
                return VMFMesh.GenerateMeshWithTextureArrays(materialGroups, null);
            }

            return VMFMesh.GenerateMeshWithTextureArrays(materialGroups, sharedTextureArrays);
        }

        #region PRIVATE

        private static Mesh GenerateMeshWithTextureArrays(Dictionary<string, List<VMFSide>> materialGroups, Dictionary<string, TextureArrayInfo> textureArrayMappings)
        {
            if (materialGroups is not { Count: > 0 })
            {
                Debug.LogWarning("No material groups provided for mesh generation");
                return null;
            }

            Dictionary<int, List<VMFSide>> submeshGroups = new Dictionary<int, List<VMFSide>>();

            // Process materials with texture array mappings
            if (textureArrayMappings?.Count > 0)
            {
                foreach (KeyValuePair<string, List<VMFSide>> materialEntry in materialGroups)
                {
                    string materialName = materialEntry.Key;
                    if (!textureArrayMappings.TryGetValue(materialName, out TextureArrayInfo info))
                    {
                        Debug.LogWarning($"No texture array mapping for material: {materialName}, skipping");
                        continue;
                    }

                    int materialIndex = info.ArrayIndex;
                    if (!submeshGroups.TryGetValue(materialIndex, out List<VMFSide> sides))
                    {
                        sides = new List<VMFSide>();
                        submeshGroups[materialIndex] = sides;
                    }

                    // Set texture indices for each side
                    foreach (VMFSide side in materialEntry.Value)
                    {
                        if (side != null)
                        {
                            side.textureIndex = info.TextureIndex;
                            side.materialIndex = info.ArrayIndex;
                            sides.Add(side);
                        }
                    }
                }
            }
            else
            {
                // Fallback: combine all materials into single submesh
                submeshGroups[0] = new List<VMFSide>();
                foreach (KeyValuePair<string, List<VMFSide>> materialEntry in materialGroups)
                {
                    if (materialEntry.Value != null)
                    {
                        submeshGroups[0].AddRange(materialEntry.Value);
                    }
                }
            }

            if (submeshGroups.Count == 0)
            {
                Debug.LogWarning("No submeshes created from material groups");
                return null;
            }

            return VMFMesh.CreateCombinedMesh(submeshGroups);
        }

        private static Mesh CreateCombinedMesh(Dictionary<int, List<VMFSide>> submeshGroups)
        {
            List<CombineInstance> combineInstances = new List<CombineInstance>();
            List<int> materialIndices = new List<int>(submeshGroups.Keys);
            materialIndices.Sort();

            Matrix4x4 transform = VMFMesh.GetDefaultTransform();

            foreach (int materialIndex in materialIndices)
            {
                List<VMFSide> sides = submeshGroups[materialIndex];
                if (sides is not { Count: > 0 })
                {
                    Debug.LogWarning($"No sides found for material index: {materialIndex}");
                    continue;
                }

                try
                {
                    Mesh submesh = VMFMesh.CreateSubmeshWithTextureIndices(sides);
                    if (submesh?.vertexCount > 0)
                    {
                        combineInstances.Add(new CombineInstance
                        {
                            mesh = submesh,
                            transform = transform
                        });
                    }
                    else
                    {
                        Debug.LogWarning($"Invalid submesh created for material index: {materialIndex}");
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"Error creating submesh for material index {materialIndex}: {ex.Message}");
                }
            }

            if (combineInstances.Count == 0)
            {
                Debug.LogWarning("No valid submeshes available for combining");
                return null;
            }

            try
            {
                Mesh combinedMesh = new Mesh
                {
                    name = "vmf_combined_mesh",
                    indexFormat = IndexFormat.UInt32
                };

                combinedMesh.CombineMeshes(combineInstances.ToArray(), false, true);
                combinedMesh.RecalculateBounds();
                combinedMesh.Optimize();

                // Apply mesh compression for better performance
                MeshUtility.SetMeshCompression(combinedMesh, ModelImporterMeshCompression.High);

                return combinedMesh;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Error combining meshes: {ex.Message}");
                return null;
            }
        }

        private static Mesh CreateSubmeshWithTextureIndices(List<VMFSide> sides)
        {
            if (sides is not { Count: > 0 })
            {
                return null;
            }

            List<Vector3> vertices = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<Vector4> uvs = new List<Vector4>();
            List<int> triangles = new List<int>();

            int baseVertexIndex = 0;
            foreach (VMFSide side in sides)
            {
                if (side?.vertices == null || side.vertices.Count < 3)
                {
                    Debug.LogWarning($"Side has insufficient vertices (< 3): {side?.material ?? "unknown"}");
                    continue;
                }

                try
                {
                    // Add vertices for this side
                    foreach (Vertex vert in side.vertices)
                    {
                        if (vert != null)
                        {
                            vertices.Add(vert.position);
                            normals.Add(vert.normal);
                            // Pack material and texture indices into UV.zw components
                            uvs.Add(new Vector4(vert.uv.x, vert.uv.y, side.materialIndex, side.textureIndex));
                        }
                    }

                    // Add triangles for this side
                    var sideTriangles = side.GetTriangles();
                    foreach (int triIndex in sideTriangles)
                    {
                        triangles.Add(baseVertexIndex + triIndex);
                    }

                    baseVertexIndex = vertices.Count;
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"Error processing side {side?.material ?? "unknown"}: {ex.Message}");
                }
            }

            if (vertices.Count == 0)
            {
                return null;
            }

            try
            {
                Mesh mesh = new Mesh
                {
                    name = "vmf_submesh",
                    indexFormat = IndexFormat.UInt32
                };

                mesh.SetVertices(vertices);
                mesh.SetNormals(normals);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(triangles, 0);
                mesh.Optimize();

                return mesh;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Error creating submesh: {ex.Message}");
                return null;
            }
        }

        #endregion
    }
    #endif
}
/*# MIT License Copyright (c) 2026 FailCake

# Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the
# "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish,
# distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to
# the following conditions:
#
# The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
#
# THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
# MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR
# ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
# SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.*/