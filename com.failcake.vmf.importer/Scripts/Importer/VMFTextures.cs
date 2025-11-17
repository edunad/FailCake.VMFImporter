#region

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

#endregion

namespace FailCake.VMF
{
    #if UNITY_EDITOR
    internal enum MaterialType
    {
        OPAQUE = 0,
        TRANSPARENT = 1,
        CUSTOM = 2
    }

    internal class TextureArrayInfo
    {
        public int ArrayIndex;
        public int TextureIndex;
    }

    internal static class VMFTextures
    {
        public static readonly Dictionary<string, VPKReader> VpkReaders = new Dictionary<string, VPKReader>();
        public static readonly Dictionary<string, Texture2D> LoadedTextures = new Dictionary<string, Texture2D>();

        private static readonly int MainTexture = Shader.PropertyToID("_MainTexture");
        private static readonly int SpecularHighlights = Shader.PropertyToID("_SpecularHighlights");
        private static readonly int EnvironmentReflections = Shader.PropertyToID("_EnvironmentReflections");
        private static readonly int GlossyReflections = Shader.PropertyToID("_GlossyReflections");

        private static readonly int Clip = Shader.PropertyToID("_Clip");

        public static readonly int MAX_TEXTURES_PER_ARRAY = 32;
        public static readonly int TEXTURE_ARRAY_SIZE = 512;


        public static void Init() {
            // --- LOAD VPKS ---
            VMFTextures.VpkReaders.Clear();
            VMFTextures.LoadedTextures.Clear();

            foreach (string vpkPath in VMFImporter.Settings.vpkFiles)
            {
                if (string.IsNullOrEmpty(vpkPath) || VMFTextures.VpkReaders.ContainsKey(vpkPath)) continue;

                VPKReader reader = new VPKReader();
                if (reader.Open(vpkPath))
                {
                    Debug.Log($"Loaded VPK: {vpkPath}");
                    VMFTextures.VpkReaders[vpkPath] = reader;
                }
                else
                    Debug.LogError($"Failed to load VPK: {vpkPath}");
            }
            //---------

            // Setup error Texture
            Texture2D errorTexture = VPKReader.CreateErrorTexture();
            VMFTextures.LoadedTextures.Add("__ERROR__", errorTexture);
            // ---------------------
        }

        public static void PreloadTextures(HashSet<string> materials) {
            if (materials == null || materials.Count == 0) return;

            foreach (string material in materials)
            {
                if (string.IsNullOrEmpty(material)) continue;
                if (VMFTextures.LoadedTextures.ContainsKey(material)) continue;

                Texture2D texture = null;
                foreach (VPKReader vpk in VMFTextures.VpkReaders.Values)
                {
                    if (vpk == null) continue;

                    try
                    {
                        VPKReader.EntryInfo file = vpk.GetFile($"{material}.vtf");
                        if (file == null) continue;

                        texture = vpk.LoadTexture(file);
                        if (texture) break;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"Error loading VTF for material '{material}': {ex.Message}");
                    }
                }

                if (!texture)
                {
                    Debug.LogWarning($"Failed to find VTF material {material}");
                    texture = VMFTextures.LoadedTextures["__ERROR__"];
                }

                VMFTextures.LoadedTextures[material] = texture;
            }
        }

        public static Texture2D GetVMFTextureByName(string material) {
            if (VMFTextures.LoadedTextures.TryGetValue(material, out Texture2D texture)) return texture;
            if (VMFTextures.LoadedTextures.TryGetValue("__ERROR__", out Texture2D errTexture)) return errTexture;

            throw new KeyNotFoundException($"Texture size for material '{material}' not found.");
        }

        public static Dictionary<string, TextureArrayInfo> CreateTextureArrays(AssetImportContext ctx, Dictionary<MaterialType, List<List<string>>> materialBatches, ref List<Material> materials, VMFMaterials vmfMaterials = null) {
            if (materials == null) return null;

            Dictionary<string, TextureArrayInfo> mappings = new Dictionary<string, TextureArrayInfo>();
            Shader shader = Shader.Find("FailCake/VMF/VMFLit") ?? throw new UnityException("Shader not found: FailCake/VMF/VMFLit");

            int typeOffset = 0;
            foreach (KeyValuePair<MaterialType, List<List<string>>> materialGroup in materialBatches)
            {
                MaterialType type = materialGroup.Key;
                List<List<string>> batches = materialGroup.Value;

                for (int batchIndex = 0; batchIndex < batches.Count; batchIndex++)
                {
                    List<string> batch = batches[batchIndex];
                    if (batch.Count == 0) continue;

                    Texture2DArray textureArray = null;
                    if (type < MaterialType.CUSTOM) textureArray = VMFTextures.CreateTextureArray(ctx, type, batchIndex, batch);

                    Material overrideMaterial = VMFTextures.ProcessTextures(textureArray, batch,
                        vmfMaterials, batchIndex + typeOffset, mappings);

                    Material material = VMFTextures.CreateMaterial(ctx, type, batchIndex, shader,
                        textureArray, overrideMaterial);

                    materials.Add(material);
                }

                typeOffset += batches.Count;
            }

            return mappings;
        }

        public static Dictionary<MaterialType, List<List<string>>> BatchMaterials(Dictionary<MaterialType, List<string>> materialsByType) {
            if (materialsByType == null || materialsByType.Count == 0) return null;

            Dictionary<MaterialType, List<List<string>>> batches = new Dictionary<MaterialType, List<List<string>>>();
            foreach (KeyValuePair<MaterialType, List<string>> kvp in materialsByType)
            {
                List<List<string>> materialBatches = VMFTextures.OptimizeBatching(kvp.Value);
                batches[kvp.Key] = materialBatches;
            }

            return batches;
        }

        private static List<List<string>> OptimizeBatching(List<string> materials) {
            List<List<string>> batches = new List<List<string>>();
            List<string> currentBatch = new List<string>();

            foreach (string material in materials)
                if (currentBatch.Count < VMFTextures.MAX_TEXTURES_PER_ARRAY)
                    currentBatch.Add(material);
                else
                {
                    if (currentBatch.Count > 0)
                    {
                        batches.Add(currentBatch);
                        currentBatch = new List<string>();
                    }

                    currentBatch.Add(material);
                }

            if (currentBatch.Count > 0) batches.Add(currentBatch);
            return batches;
        }

        public static Dictionary<MaterialType, List<string>> GroupMaterials(IEnumerable<string> materialNames) {
            if (materialNames == null) return null;

            Dictionary<MaterialType, List<string>> materialsByType = new Dictionary<MaterialType, List<string>>();
            HashSet<string> processedMaterials = new HashSet<string>();

            int customMaterialCount = 0;
            foreach (string materialName in materialNames)
            {
                if (string.IsNullOrEmpty(materialName)) continue;
                if (materialName == "__IGNORE__") continue;

                if (!processedMaterials.Add(materialName)) continue;

                Material overrideMaterial = VMFImporter.Settings.GetMaterialOverride(materialName);
                if (overrideMaterial)
                {
                    materialsByType.Add(MaterialType.CUSTOM + customMaterialCount, new List<string> { materialName }); // Allow additional custom material override
                    customMaterialCount++;
                    continue;
                }

                Texture2D texture = VMFTextures.GetVMFTextureByName(materialName);
                if (!texture) continue;

                MaterialType textureType = VMFTextures.IsTextureAlpha(texture) ? MaterialType.TRANSPARENT : MaterialType.OPAQUE;
                if (!materialsByType.TryGetValue(textureType, out List<string> materialList))
                {
                    materialList = new List<string>();
                    materialsByType[textureType] = materialList;
                }

                materialsByType[textureType].Add(materialName);
            }

            return materialsByType;
        }

        #region PRIVATE

        private static Texture2DArray CreateTextureArray(AssetImportContext ctx, MaterialType type, int arrayIndex, List<string> batch) {
            Texture2DArray textureArray = new Texture2DArray(VMFTextures.TEXTURE_ARRAY_SIZE, VMFTextures.TEXTURE_ARRAY_SIZE, batch.Count, TextureFormat.DXT5, false, false) {
                name = $"{type}_TextureArray_{arrayIndex}",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat
            };

            ctx.AddObjectToAsset(textureArray.name, textureArray);
            return textureArray;
        }

        private static Material CreateMaterial(AssetImportContext ctx, MaterialType type, int arrayIndex, Shader shader, Texture2DArray textureArray, Material overrideMaterial) {
            Material material;

            if (overrideMaterial)
                material = new Material(overrideMaterial) {
                    parent = overrideMaterial,
                    name = $"{type}_OverrideMaterial_{arrayIndex}"
                };
            else
            {
                material = new Material(shader) {
                    name = $"{type}_Material_{arrayIndex}"
                };

                VMFTextures.SetupMaterialProperties(material, type);
            }


            if (!overrideMaterial && material.HasTexture(VMFTextures.MainTexture)) material.SetTexture(VMFTextures.MainTexture, textureArray);

            ctx.AddObjectToAsset(material.name, material);
            return material;
        }

        private static Material ProcessTextures(Texture2DArray textureArray, List<string> batch, VMFMaterials vmfMaterials, int batchOffset, Dictionary<string, TextureArrayInfo> mappings) {
            Material overrideMaterial = null;

            for (int i = 0; i < batch.Count; i++)
            {
                string materialName = batch[i];
                if (string.IsNullOrEmpty(materialName)) continue;

                if (mappings.ContainsKey(materialName)) continue;

                if (textureArray)
                {
                    Texture2D texture = VMFTextures.GetVMFTextureByName(materialName);
                    if (!texture) continue;

                    Texture2D uncompressedTexture = VMFTextures.ResizeTexture(texture, VMFTextures.TEXTURE_ARRAY_SIZE, VMFTextures.TEXTURE_ARRAY_SIZE);

                    EditorUtility.CompressTexture(uncompressedTexture, TextureFormat.DXT5, TextureCompressionQuality.Fast);
                    Graphics.CopyTexture(uncompressedTexture, 0, 0, textureArray, i, 0);

                    if (texture != uncompressedTexture) Object.DestroyImmediate(uncompressedTexture);
                }

                mappings[materialName] = new TextureArrayInfo {
                    ArrayIndex = batchOffset,
                    TextureIndex = i
                };

                if (vmfMaterials) VMFTextures.StoreMaterialFootstep(vmfMaterials, materialName, batchOffset, i);
                if (VMFImporter.Settings && !overrideMaterial) overrideMaterial = VMFImporter.Settings.GetMaterialOverride(materialName);
            }

            return overrideMaterial;
        }

        private static Texture2D ResizeTexture(Texture2D source, int width, int height) {
            if (!source) return null;

            Texture2D resized = new Texture2D(width, height, TextureFormat.RGBA32, false);

            RenderTexture rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            RenderTexture prevRT = RenderTexture.active;

            Graphics.Blit(source, rt);
            RenderTexture.active = rt;

            resized.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            resized.Apply();

            // Clean up
            RenderTexture.active = prevRT;
            RenderTexture.ReleaseTemporary(rt);

            return resized;
        }

        private static void StoreMaterialFootstep(
            VMFMaterials vmfMaterials,
            string materialName,
            int arrayIndex,
            int textureIndex) {
            VMFMaterial materialType = VMFMaterials.GetMaterialTexture(materialName);
            byte byteArrayIndex = (byte)arrayIndex;
            byte byteTextureIndex = (byte)textureIndex;

            if (!vmfMaterials.materialDictionary.TryGetValue(byteArrayIndex,
                    out SerializedDictionary<byte, VMFMaterial> textureMaterials))
            {
                textureMaterials = new SerializedDictionary<byte, VMFMaterial>();
                vmfMaterials.materialDictionary[byteArrayIndex] = textureMaterials;
            }

            textureMaterials[byteTextureIndex] = materialType;
        }


        private static void SetupMaterialProperties(Material material, MaterialType type) {
            if (type == MaterialType.TRANSPARENT)
            {
                material.EnableKeyword("_ALPHATEST_ON");
                material.SetFloat(VMFTextures.Clip, 0.015f);
            }
            else
            {
                material.DisableKeyword("_ALPHATEST_ON");
                material.SetFloat(VMFTextures.Clip, 0f);
            }

            material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");

            material.SetFloat(VMFTextures.SpecularHighlights, 0);
            material.SetFloat(VMFTextures.EnvironmentReflections, 0);
            material.SetFloat(VMFTextures.GlossyReflections, 0);
        }

        private static bool IsTextureAlpha(Texture2D texture) {
            return texture.format == TextureFormat.RGBA32 ||
                   texture.format == TextureFormat.ARGB32 ||
                   texture.format == TextureFormat.RGBA4444 ||
                   texture.format == TextureFormat.ARGB4444 ||
                   texture.alphaIsTransparency;
        }

        #endregion
    }
    #endif
}