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


        public static void Init()
        {
            // --- LOAD VPKS ---
            Cleanup();

            foreach (var vpkPath in VMFImporter.Settings.vpkFiles)
            {
                if (string.IsNullOrEmpty(vpkPath) || VpkReaders.ContainsKey(vpkPath)) continue;

                var reader = new VPKReader();
                if (reader.Open(vpkPath))
                {
                    Debug.Log($"Loaded VPK: {vpkPath}");
                    VpkReaders[vpkPath] = reader;
                }
                else
                {
                    Debug.LogError($"Failed to load VPK: {vpkPath}");
                }
            }
            //---------

            // Setup error Texture
            var errorTexture = VPKReader.CreateErrorTexture();
            LoadedTextures.Add("__ERROR__", errorTexture);
            // ---------------------
        }

        public static void Cleanup()
        {
            foreach (var reader in VpkReaders.Values)
                reader?.Close();

            VpkReaders.Clear();
            LoadedTextures.Clear();
        }

        public static void PreloadTextures(HashSet<string> materials)
        {
            if (materials == null || materials.Count == 0) return;

            foreach (var material in materials)
            {
                if (string.IsNullOrEmpty(material)) continue;
                if (LoadedTextures.ContainsKey(material)) continue;

                Texture2D texture = null;
                foreach (var vpk in VpkReaders.Values)
                {
                    if (vpk == null) continue;

                    try
                    {
                        var file = vpk.GetFile($"{material}.vtf");
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
                    texture = LoadedTextures["__ERROR__"];
                }

                LoadedTextures[material] = texture;
            }
        }

        public static Texture2D GetVMFTextureByName(string material)
        {
            if (LoadedTextures.TryGetValue(material, out var texture)) return texture;
            if (LoadedTextures.TryGetValue("__ERROR__", out var errTexture)) return errTexture;

            throw new KeyNotFoundException($"Texture size for material '{material}' not found.");
        }

        public static Dictionary<string, TextureArrayInfo> CreateTextureArrays(AssetImportContext ctx,
            Dictionary<MaterialType, List<List<string>>> materialBatches, ref List<Material> materials,
            VMFMaterials vmfMaterials = null)
        {
            if (materials == null) return null;

            var mappings = new Dictionary<string, TextureArrayInfo>();
            var shader = Shader.Find("FailCake/VMF/VMFLit") ??
                         throw new UnityException("Shader not found: FailCake/VMF/VMFLit");

            var typeOffset = 0;
            foreach (var materialGroup in materialBatches)
            {
                var type = materialGroup.Key;
                var batches = materialGroup.Value;

                for (var batchIndex = 0; batchIndex < batches.Count; batchIndex++)
                {
                    var batch = batches[batchIndex];
                    if (batch.Count == 0) continue;

                    Texture2DArray textureArray = null;
                    if (type < MaterialType.CUSTOM) textureArray = CreateTextureArray(ctx, type, batchIndex, batch);

                    var overrideMaterial = ProcessTextures(textureArray, batch,
                        vmfMaterials, batchIndex + typeOffset, mappings);

                    var material = CreateMaterial(ctx, type, batchIndex, shader,
                        textureArray, overrideMaterial);

                    materials.Add(material);
                }

                typeOffset += batches.Count;
            }

            return mappings;
        }

        public static Dictionary<MaterialType, List<List<string>>> BatchMaterials(
            Dictionary<MaterialType, List<string>> materialsByType)
        {
            if (materialsByType == null || materialsByType.Count == 0) return null;

            var batches = new Dictionary<MaterialType, List<List<string>>>();
            foreach (var kvp in materialsByType)
            {
                var materialBatches = OptimizeBatching(kvp.Value);
                batches[kvp.Key] = materialBatches;
            }

            return batches;
        }

        private static List<List<string>> OptimizeBatching(List<string> materials)
        {
            var batches = new List<List<string>>();
            var currentBatch = new List<string>();

            foreach (var material in materials)
                if (currentBatch.Count < MAX_TEXTURES_PER_ARRAY)
                {
                    currentBatch.Add(material);
                }
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

        public static Dictionary<MaterialType, List<string>> GroupMaterials(IEnumerable<string> materialNames)
        {
            if (materialNames == null) return null;

            var materialsByType = new Dictionary<MaterialType, List<string>>();
            var processedMaterials = new HashSet<string>();

            var customMaterialCount = 0;
            foreach (var materialName in materialNames)
            {
                if (string.IsNullOrEmpty(materialName)) continue;
                if (materialName == "__IGNORE__") continue;

                if (!processedMaterials.Add(materialName)) continue;

                if (VMFImporter.Settings.HasMaterialOverride(materialName))
                {
                    materialsByType.Add(MaterialType.CUSTOM + customMaterialCount,
                        new List<string> { materialName }); // Allow additional custom material override
                    customMaterialCount++;
                    continue;
                }

                var texture = GetVMFTextureByName(materialName);
                if (!texture) continue;

                var textureType = IsTextureAlpha(texture) ? MaterialType.TRANSPARENT : MaterialType.OPAQUE;
                if (!materialsByType.TryGetValue(textureType, out var materialList))
                {
                    materialList = new List<string>();
                    materialsByType[textureType] = materialList;
                }

                materialsByType[textureType].Add(materialName);
            }

            return materialsByType;
        }

        #region PRIVATE

        private static Texture2DArray CreateTextureArray(AssetImportContext ctx, MaterialType type, int arrayIndex,
            List<string> batch)
        {
            var textureArray = new Texture2DArray(TEXTURE_ARRAY_SIZE, TEXTURE_ARRAY_SIZE, batch.Count,
                TextureFormat.DXT5, false, false)
            {
                name = $"{type}_TextureArray_{arrayIndex}",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat
            };

            ctx.AddObjectToAsset(textureArray.name, textureArray);
            return textureArray;
        }

        private static Material CreateMaterial(AssetImportContext ctx, MaterialType type, int arrayIndex, Shader shader,
            Texture2DArray textureArray, Material overrideMaterial)
        {
            Material material;

            if (overrideMaterial)
            {
                material = new Material(overrideMaterial)
                {
                    parent = overrideMaterial,
                    name = $"{type}_OverrideMaterial_{arrayIndex}"
                };
            }
            else
            {
                material = new Material(shader)
                {
                    name = $"{type}_Material_{arrayIndex}"
                };

                SetupMaterialProperties(material, type);
            }


            if (!overrideMaterial && material.HasTexture(MainTexture)) material.SetTexture(MainTexture, textureArray);

            ctx.AddObjectToAsset(material.name, material);
            return material;
        }

        private static Material ProcessTextures(Texture2DArray textureArray, List<string> batch,
            VMFMaterials vmfMaterials, int batchOffset, Dictionary<string, TextureArrayInfo> mappings)
        {
            Material overrideMaterial = null;

            for (var i = 0; i < batch.Count; i++)
            {
                var materialName = batch[i];
                if (string.IsNullOrEmpty(materialName)) continue;

                if (mappings.ContainsKey(materialName)) continue;

                if (textureArray)
                {
                    var texture = GetVMFTextureByName(materialName);
                    if (!texture) continue;

                    var uncompressedTexture = ResizeTexture(texture, TEXTURE_ARRAY_SIZE, TEXTURE_ARRAY_SIZE);

                    EditorUtility.CompressTexture(uncompressedTexture, TextureFormat.DXT5,
                        TextureCompressionQuality.Fast);
                    Graphics.CopyTexture(uncompressedTexture, 0, 0, textureArray, i, 0);

                    if (texture != uncompressedTexture) Object.DestroyImmediate(uncompressedTexture);
                }

                mappings[materialName] = new TextureArrayInfo
                {
                    ArrayIndex = batchOffset,
                    TextureIndex = i
                };

                if (vmfMaterials) StoreMaterialFootstep(vmfMaterials, materialName, batchOffset, i);
                if (VMFImporter.Settings && !overrideMaterial)
                    overrideMaterial = VMFImporter.Settings.GetMaterialOverride(materialName);
            }

            return overrideMaterial;
        }

        private static Texture2D ResizeTexture(Texture2D source, int width, int height)
        {
            if (!source) return null;

            var resized = new Texture2D(width, height, TextureFormat.RGBA32, false);

            var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            var prevRT = RenderTexture.active;

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
            int textureIndex)
        {
            var materialType = VMFMaterials.GetMaterialTexture(materialName);
            var byteArrayIndex = (byte)arrayIndex;
            var byteTextureIndex = (byte)textureIndex;

            if (!vmfMaterials.materialDictionary.TryGetValue(byteArrayIndex,
                    out var textureMaterials))
            {
                textureMaterials = new SerializedDictionary<byte, VMFMaterial>();
                vmfMaterials.materialDictionary[byteArrayIndex] = textureMaterials;
            }

            textureMaterials[byteTextureIndex] = materialType;
        }


        private static void SetupMaterialProperties(Material material, MaterialType type)
        {
            if (type == MaterialType.TRANSPARENT)
            {
                material.EnableKeyword("_ALPHATEST_ON");
                material.SetFloat(Clip, 0.015f);
            }
            else
            {
                material.DisableKeyword("_ALPHATEST_ON");
                material.SetFloat(Clip, 0f);
            }

            material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");

            material.SetFloat(SpecularHighlights, 0);
            material.SetFloat(EnvironmentReflections, 0);
            material.SetFloat(GlossyReflections, 0);
        }

        private static bool IsTextureAlpha(Texture2D texture)
        {
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