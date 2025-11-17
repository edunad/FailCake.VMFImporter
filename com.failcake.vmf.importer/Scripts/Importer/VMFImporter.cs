#region

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityMeshSimplifier;
using System.Linq;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AssetImporters;
#endif

#endregion

namespace FailCake.VMF
{
    #if UNITY_EDITOR
    [ScriptedImporter(1, "vmf")]
    public class VMFImporter : ScriptedImporter
    {
        [Header("Settings"), Tooltip("Generate material data for footstep parsing")]
        public bool GenerateMaterialData;

        public bool GenerateTextures = true;

        public bool GenerateEntities = true;

        public bool GenerateColliders;

        public bool IsReadable = true;

        [Range(0, 1)]
        public float Quality = 1f;

        public static VMFSettings Settings;

        #region PRIVATE

        private VMFWorld _vmfWorld;

        #endregion

        private static void LoadSettings() {
            const string settingsPath = "Assets/VMFImporterSettings.asset";
            VMFImporter.Settings = AssetDatabase.LoadAssetAtPath<VMFSettings>(settingsPath)
                                   ?? ScriptableObject.CreateInstance<VMFSettings>();

            if (!AssetDatabase.Contains(VMFImporter.Settings)) AssetDatabase.CreateAsset(VMFImporter.Settings, settingsPath);

            VMFImporter.Settings.Init();
            VMFTextures.Init();
        }

        public override void OnImportAsset(AssetImportContext ctx) {
            try
            {
                VMFImporter.LoadSettings();

                if (!this.ReadAsset(out List<VMFDataBlock> root))
                {
                    Debug.LogError($"Failed to read VMF asset: {ctx.assetPath}");
                    return;
                }

                GameObject rootModel = new GameObject(ctx.assetPath);

                ctx.AddObjectToAsset(rootModel.name, rootModel);
                ctx.SetMainObject(rootModel);

                // LOAD WORLD ----
                this._vmfWorld = new VMFWorld(root);
                if (this.GenerateTextures) this._vmfWorld.LoadWorldTextures(this.GenerateEntities);
                // --------------------------

                // WORLD SOLIDS
                List<VMFSolid> worldSolids = this.ParseSolids(this._vmfWorld.GetSolids());
                // --------------------------

                // ENTITY SOLIDS
                List<VMFSolid> entitySolids = this.GenerateEntities ? this.ParseSolids(this._vmfWorld.GetEntitySolids()) : null;
                // --------------------------

                // GENERATE TEXTURES
                Dictionary<string, TextureArrayInfo> sharedTextureArrays = null;
                List<Material> sharedMaterials = new List<Material>();

                if (this.GenerateTextures) sharedTextureArrays = this.CreateSharedTextureArrays(ctx, worldSolids, entitySolids, ref sharedMaterials);
                // --------------------------

                // GENERATE SOLIDS
                if (worldSolids?.Count > 0) this.ProcessWorldSolids(ctx, rootModel, worldSolids, sharedTextureArrays, sharedMaterials);
                if (entitySolids?.Count > 0) this.ProcessEntitySolids(ctx, rootModel, entitySolids, sharedTextureArrays, sharedMaterials);
                // --------------------------

                // GENERATE COLLIDERS
                if (worldSolids?.Count > 0) this.ParseColliders(worldSolids, rootModel);
                // --------------------------

                // GENERATE ENTITIES
                this.ParseEntities(rootModel);
                // --------------------------
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error importing VMF asset {ctx.assetPath}: {ex.Message}");
                throw;
            }
        }

        private void ProcessWorldSolids(AssetImportContext ctx, GameObject rootModel, List<VMFSolid> worldSolids, Dictionary<string, TextureArrayInfo> sharedTextureArrays, List<Material> sharedMaterials) {
            GameObject worldModel = new GameObject("world");
            worldModel.transform.SetParent(rootModel.transform, false);

            Dictionary<string, List<VMFSide>> groupedSolids = this.GroupSolidsByMaterial(worldSolids);
            if (groupedSolids?.Count > 0) this.GenerateModel(ctx, worldModel, groupedSolids, true, sharedTextureArrays, sharedMaterials);
        }

        private void ProcessEntitySolids(AssetImportContext ctx, GameObject rootModel, List<VMFSolid> entitySolids, Dictionary<string, TextureArrayInfo> sharedTextureArrays, List<Material> sharedMaterials) {
            Dictionary<string, List<VMFSolid>> entityGroups = this.GroupEntitySolidsByName(entitySolids);
            foreach (KeyValuePair<string, List<VMFSolid>> entityGroup in entityGroups)
            {
                GameObject entityParent = new GameObject(entityGroup.Key);
                entityParent.transform.SetParent(rootModel.transform, false);

                Dictionary<string, List<VMFSide>> groupedEntitySolids = this.GroupSolidsByMaterial(entityGroup.Value);
                if (groupedEntitySolids?.Count > 0) this.GenerateModel(ctx, entityParent, groupedEntitySolids, true, sharedTextureArrays, sharedMaterials);
            }
        }

        private Dictionary<string, List<VMFSolid>> GroupEntitySolidsByName(List<VMFSolid> entitySolids) {
            Dictionary<string, List<VMFSolid>> entityGroups = new Dictionary<string, List<VMFSolid>>();

            foreach (VMFSolid solid in entitySolids)
            {
                if (string.IsNullOrEmpty(solid.ID)) continue;

                if (!entityGroups.TryGetValue(solid.ID, out List<VMFSolid> solidsList))
                {
                    solidsList = new List<VMFSolid>();
                    entityGroups[solid.ID] = solidsList;
                }

                solidsList.Add(solid);
            }

            return entityGroups;
        }

        private bool ReadAsset(out List<VMFDataBlock> root) {
            root = new List<VMFDataBlock>();

            try
            {
                using StreamReader reader = new StreamReader(File.OpenRead(this.assetPath));
                while (!reader.EndOfStream)
                {
                    VMFDataBlock block = VMFReader.ReadBlockNamed(reader);
                    if (block == null) continue;

                    root.Add(block);
                }

                return root.Count > 0;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error reading VMF file {this.assetPath}: {ex.Message}");
                return false;
            }
        }

        #region COLLISIONS

        private void ParseColliders(List<VMFSolid> solids, GameObject rootModel) {
            if (!this.GenerateColliders || solids?.Count == 0) return;

            bool calculateMaterials = this.GenerateMaterialData && this.GenerateTextures;

            GameObject colliderRoot = new GameObject("func_colliders");
            colliderRoot.transform.SetParent(rootModel.transform, false);
            colliderRoot.isStatic = true;

            var colliderData = solids
                .SelectMany(solid => new[] { (solid, bounds: this.GetBoundsForSolid(solid)) })
                .Where(x => x.bounds.HasValue && this.IsBoundsValid(x.bounds.Value.bounds))
                .Select(x => (x.bounds.Value.bounds, material: calculateMaterials ? this.GetDominantMaterial(x.solid) : "default"));

            int colliderIndex = 0;
            foreach ((Bounds bounds, string material) data in colliderData) this.CreateColliderObject(data, colliderIndex++, colliderRoot);
        }

        private (Bounds bounds, string material)? GetBoundsForSolid(VMFSolid solid) {
            if (solid?.sides == null || solid.sides.Count == 0) return null;

            var vertices = solid.sides
                .SelectMany(side => side.vertices)
                .Select(vertex => VMFMesh.GetDefaultTransform().MultiplyPoint3x4(vertex.position));

            if (!vertices.Any()) return null;

            Vector3 min = vertices.Aggregate(Vector3.positiveInfinity, Vector3.Min);
            Vector3 max = vertices.Aggregate(Vector3.negativeInfinity, Vector3.Max);
            Vector3 center = (min + max) * 0.5f;
            Vector3 size = max - min;

            return (new Bounds(center, size), null);
        }

        private bool IsBoundsValid(Bounds bounds) {
            return bounds.size is { x: >= 0.01f, y: >= 0.01f, z: >= 0.01f };
        }

        private void CreateColliderObject((Bounds bounds, string material) data, int solidId, GameObject colliderRoot) {
            GameObject colliderObj = new GameObject($"collider_{solidId}");

            int collisionLayer = LayerMask.NameToLayer(VMFImporter.Settings.collisionMask);
            if (collisionLayer != -1) colliderObj.layer = collisionLayer;

            colliderObj.transform.SetParent(colliderRoot.transform, false);
            colliderObj.transform.position = data.bounds.center;
            colliderObj.isStatic = true;

            BoxCollider boxCollider = colliderObj.AddComponent<BoxCollider>();
            boxCollider.center = Vector3.zero;
            boxCollider.size = data.bounds.size;

            this.AddMaterialComponent(colliderObj, data.material);
        }

        private void AddMaterialComponent(GameObject colliderObj, string material) {
            if (string.IsNullOrEmpty(material) || material == "default") return;

            if (material.Contains("LAYER_TEXTURE", StringComparison.OrdinalIgnoreCase))
            {
                VMFLayerMaterial layerMaterial = colliderObj.AddComponent<VMFLayerMaterial>();
                layerMaterial.layer = VMFUtils.ExtractLayerMaterial(material);
            }
            else
            {
                VMFBoxMaterial vmfMaterial = colliderObj.AddComponent<VMFBoxMaterial>();
                vmfMaterial.materialType = VMFMaterials.GetMaterialTexture(material);
            }
        }

        private string GetDominantMaterial(VMFSolid solid) {
            if (solid?.sides == null || solid.sides.Count == 0) return null;

            var filteredSides = solid.sides
                .Where(side =>
                    !string.IsNullOrEmpty(side.material) &&
                    (!VMFImporter.Settings.GetMaterialOverride(side.material) ||
                     side.material.Contains("LAYER_TEXTURE", StringComparison.OrdinalIgnoreCase))
                );

            if (!filteredSides.Any()) return null;

            var materialWithMaxCount = filteredSides
                .GroupBy(side => side.material)
                .Select(g => new { Material = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .FirstOrDefault();

            return materialWithMaxCount?.Material;
        }

        #endregion

        #region ENTITIES

        private void ParseEntities(GameObject rootModel) {
            if (!this.GenerateEntities) return;

            List<VMFDataBlock> entities = this._vmfWorld.GetEntities();
            if (entities is not { Count: > 0 }) return;

            Dictionary<string, GameObject> entityOverrides = VMFImporter.Settings.GetEntityOverrides();
            Matrix4x4 transformMatrix = VMFMesh.GetDefaultTransform();

            foreach (VMFDataBlock entity in entities)
                if (!this.TryCreateEntity(entity, entityOverrides, transformMatrix, rootModel)) { }
        }

        #endregion

        #region ENTITIES

        private bool TryCreateEntity(VMFDataBlock entity, Dictionary<string, GameObject> entityOverrides,
            Matrix4x4 transformMatrix, GameObject rootModel) {
            string classname = entity.GetSingle("classname") as string;

            if (string.IsNullOrEmpty(classname)) return false;
            if (classname.StartsWith("func_", StringComparison.OrdinalIgnoreCase)) return false; // FUNC_ are custom blocks with code attached to them

            string id = entity.GetSingle("id") as string;
            if (string.IsNullOrEmpty(id))
            {
                Debug.LogWarning($"Entity of type '{classname}' found without ID, skipping");
                return false;
            }

            string positionStr = entity.GetSingle("origin") as string;
            if (string.IsNullOrEmpty(positionStr))
            {
                Debug.LogWarning($"Entity '{classname}' (ID: {id}) found without origin, skipping");
                return false;
            }

            try
            {
                GameObject createdEntity = this.CreateEntityGameObject(classname, id, entityOverrides);
                if (!createdEntity) return false;

                createdEntity.transform.SetParent(rootModel.transform, false);
                createdEntity.transform.localPosition = transformMatrix.MultiplyPoint3x4(VMFUtils.ParseVector(positionStr));
                createdEntity.transform.localRotation = Quaternion.identity;

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error creating entity '{classname}' (ID: {id}): {ex.Message}");
                return false;
            }
        }

        private GameObject CreateEntityGameObject(string classname, string id, Dictionary<string, GameObject> entityOverrides) {
            string entityName = $"{classname}_{id}";
            if (!entityOverrides.TryGetValue(classname, out GameObject prefab)) return new GameObject(entityName);

            GameObject createdEntity = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (!createdEntity) return null;

            createdEntity.name = entityName;
            return createdEntity;
        }

        private List<VMFSolid> ParseSolids(List<VMFDataBlock> rawSolids) {
            if (rawSolids is not { Count: > 0 }) return null;

            List<VMFSolid> vmfSolids = new List<VMFSolid>();
            foreach (VMFDataBlock solidBlock in rawSolids)
                try
                {
                    VMFSolid solid = this.BuildSolid(solidBlock);
                    if (solid != null) vmfSolids.Add(solid);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Error building solid from block: {ex.Message}");
                }

            return vmfSolids.Count > 0 ? vmfSolids : null;
        }

        private VMFSolid BuildSolid(VMFDataBlock solidBlock) {
            if (solidBlock == null) return null;
            VMFSolid solid = new VMFSolid { ID = solidBlock.ID };

            try
            {
                if (solid.AddSides(solidBlock, this.GenerateTextures) && solid.sides?.Count > 0) return solid;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error adding sides to solid {solid.ID}: {ex.Message}");
            }

            return null;
        }

        private void GenerateModel(AssetImportContext ctx, GameObject modelObject, Dictionary<string, List<VMFSide>> groupedSolids, bool generateMaterialData, Dictionary<string, TextureArrayInfo> sharedTextureArrays = null,
            List<Material> sharedMaterials = null) {
            if (groupedSolids is not { Count: > 0 }) return;

            try
            {
                VMFMaterials vmfMaterials = null;
                if (this.GenerateTextures && this.GenerateMaterialData && generateMaterialData) vmfMaterials = modelObject.AddComponent<VMFMaterials>();

                List<Material> materials = null;
                if (this.GenerateTextures && generateMaterialData) materials = sharedMaterials?.Count > 0 ? sharedMaterials : new List<Material>();

                Mesh combinedMesh;
                if (this.GenerateTextures && sharedTextureArrays != null && generateMaterialData)
                {
                    combinedMesh = VMFMesh.GenerateMeshWithSharedTextures(groupedSolids, sharedTextureArrays);

                    if (vmfMaterials) this.PopulateSharedMaterialDictionary(vmfMaterials, groupedSolids, sharedTextureArrays);
                    if (materials != null && combinedMesh) materials = this.CreateMaterialsForSharedArrays(sharedMaterials, groupedSolids, sharedTextureArrays);
                }
                else if (this.GenerateTextures)
                    combinedMesh = VMFMesh.GenerateMesh(ctx, groupedSolids, ref materials, vmfMaterials);
                else
                    combinedMesh = VMFMesh.GenerateMeshWithSharedTextures(groupedSolids, null);

                if (!combinedMesh)
                {
                    Debug.LogError($"Failed to generate mesh for {modelObject.name}");
                    return;
                }

                Mesh finalMesh = this.SimplifyMesh(combinedMesh);
                finalMesh.name = "default";

                ctx.AddObjectToAsset(finalMesh.name, finalMesh);
                this.SetupMeshComponents(modelObject, finalMesh, materials, vmfMaterials);

                finalMesh.UploadMeshData(!this.IsReadable);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error generating model for {modelObject.name}: {ex.Message}");
            }
        }

        private Mesh SimplifyMesh(Mesh originalMesh) {
            try
            {
                MeshSimplifier meshSimplifier = new MeshSimplifier();
                meshSimplifier.Initialize(originalMesh);
                meshSimplifier.SimplifyMesh(this.Quality);

                return meshSimplifier.ToMesh();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Mesh simplification failed, using original mesh: {ex.Message}");
                return originalMesh;
            }
        }

        private void SetupMeshComponents(GameObject modelObject, Mesh mesh, List<Material> materials, VMFMaterials vmfMaterials) {
            MeshFilter meshFilter = modelObject.AddComponent<MeshFilter>();
            meshFilter.mesh = mesh;

            if (!this.GenerateTextures || materials == null) return;

            MeshRenderer meshRenderer = modelObject.AddComponent<MeshRenderer>();
            meshRenderer.materials = materials.ToArray();

            if (!vmfMaterials) return;

            vmfMaterials.meshFilter = meshFilter;
            EditorUtility.SetDirty(vmfMaterials);
        }

        private Dictionary<string, List<VMFSide>> GroupSolidsByMaterial(List<VMFSolid> solids) {
            Dictionary<string, List<VMFSide>> materialGroups = new Dictionary<string, List<VMFSide>>();
            if (solids == null || solids.Count == 0) return null;

            foreach (VMFSolid solid in solids)
            {
                foreach (VMFSide side in solid.sides)
                {
                    string materialKey = side.material;
                    if (!this.GenerateTextures) side.material = "__IGNORE__";

                    if (string.IsNullOrEmpty(materialKey)) continue;
                    if (!materialGroups.TryGetValue(materialKey, out List<VMFSide> sidesList))
                    {
                        sidesList = new List<VMFSide>();
                        materialGroups[materialKey] = sidesList;
                    }

                    sidesList.Add(side);
                }
            }

            return materialGroups.Count > 0 ? materialGroups : null;
        }

        private Dictionary<string, TextureArrayInfo> CreateSharedTextureArrays(AssetImportContext ctx, List<VMFSolid> worldSolids, List<VMFSolid> entitySolids, ref List<Material> sharedMaterials) {
            HashSet<string> allMaterials = new HashSet<string>();

            if (worldSolids?.Count > 0)
            {
                Dictionary<string, List<VMFSide>> worldMaterials = this.GroupSolidsByMaterial(worldSolids);
                if (worldMaterials != null)
                    foreach (string material in worldMaterials.Keys)
                        allMaterials.Add(material);
            }

            if (entitySolids?.Count > 0)
            {
                Dictionary<string, List<VMFSide>> entityMaterials = this.GroupSolidsByMaterial(entitySolids);
                if (entityMaterials != null)
                    foreach (string material in entityMaterials.Keys)
                        allMaterials.Add(material);
            }

            if (allMaterials.Count == 0) return new Dictionary<string, TextureArrayInfo>();

            Dictionary<MaterialType, List<string>> groupedMaterials = VMFTextures.GroupMaterials(allMaterials);
            Dictionary<MaterialType, List<List<string>>> materialBatches = VMFTextures.BatchMaterials(groupedMaterials);

            return VMFTextures.CreateTextureArrays(ctx, materialBatches, ref sharedMaterials);
        }

        private void PopulateSharedMaterialDictionary(VMFMaterials vmfMaterials, Dictionary<string, List<VMFSide>> groupedSolids, Dictionary<string, TextureArrayInfo> sharedTextureArrays) {
            foreach (KeyValuePair<string, List<VMFSide>> materialGroup in groupedSolids)
            {
                string materialName = materialGroup.Key;
                if (sharedTextureArrays.TryGetValue(materialName, out TextureArrayInfo arrayInfo))
                {
                    VMFMaterial materialType = VMFMaterials.GetMaterialTexture(materialName);
                    byte byteArrayIndex = (byte)arrayInfo.ArrayIndex;
                    byte byteTextureIndex = (byte)arrayInfo.TextureIndex;

                    if (!vmfMaterials.materialDictionary.TryGetValue(byteArrayIndex,
                            out SerializedDictionary<byte, VMFMaterial> textureMaterials))
                    {
                        textureMaterials = new SerializedDictionary<byte, VMFMaterial>();
                        vmfMaterials.materialDictionary[byteArrayIndex] = textureMaterials;
                    }

                    textureMaterials[byteTextureIndex] = materialType;
                }
            }
        }

        private List<Material> CreateMaterialsForSharedArrays(List<Material> sharedMaterials, Dictionary<string, List<VMFSide>> groupedSolids, Dictionary<string, TextureArrayInfo> sharedTextureArrays) {
            Dictionary<int, Material> submeshMaterials = new Dictionary<int, Material>();
            foreach (KeyValuePair<string, List<VMFSide>> materialGroup in groupedSolids)
            {
                string materialName = materialGroup.Key;
                if (sharedTextureArrays.TryGetValue(materialName, out TextureArrayInfo arrayInfo))
                {
                    int arrayIndex = arrayInfo.ArrayIndex;
                    if (arrayIndex < sharedMaterials.Count && !submeshMaterials.ContainsKey(arrayIndex)) submeshMaterials[arrayIndex] = sharedMaterials[arrayIndex];
                }
            }

            List<Material> materialsForSubmeshes = new List<Material>();
            List<int> sortedIndices = new List<int>(submeshMaterials.Keys);
            sortedIndices.Sort();

            foreach (int arrayIndex in sortedIndices) materialsForSubmeshes.Add(submeshMaterials[arrayIndex]);
            return materialsForSubmeshes;
        }

        #endregion
    }
    #endif
}

/*# MIT License Copyright (c) 2025 FailCake

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
