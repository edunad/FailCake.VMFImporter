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

        public bool GenerateUVLightmap;

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
            finally
            {
                VMFTextures.Cleanup();
            }
        }

        private void ProcessWorldSolids(AssetImportContext ctx, GameObject rootModel, List<VMFSolid> worldSolids, Dictionary<string, TextureArrayInfo> sharedTextureArrays, List<Material> sharedMaterials) {
            GameObject worldModel = new GameObject("world");
            worldModel.transform.SetParent(rootModel.transform, false);

            Dictionary<string, List<VMFSide>> groupedSolids = this.GroupSolidsByMaterial(worldSolids);
            if (groupedSolids?.Count > 0) this.GenerateModel(ctx, worldModel, groupedSolids, sharedTextureArrays, sharedMaterials);
        }

        private void ProcessEntitySolids(AssetImportContext ctx, GameObject rootModel, List<VMFSolid> entitySolids, Dictionary<string, TextureArrayInfo> sharedTextureArrays, List<Material> sharedMaterials) {
            Dictionary<string, List<VMFSolid>> entityGroups = this.GroupEntitySolidsByName(entitySolids);
            foreach (KeyValuePair<string, List<VMFSolid>> entityGroup in entityGroups)
            {
                GameObject entityParent = new GameObject(entityGroup.Key);
                entityParent.transform.SetParent(rootModel.transform, false);

                Dictionary<string, List<VMFSide>> groupedEntitySolids = this.GroupSolidsByMaterial(entityGroup.Value);
                if (groupedEntitySolids?.Count > 0) this.GenerateModel(ctx, entityParent, groupedEntitySolids, sharedTextureArrays, sharedMaterials);
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
            if (!this.GenerateColliders || solids is not { Count: > 0 }) return;

            bool calculateMaterials = this.GenerateMaterialData && this.GenerateTextures;

            GameObject colliderRoot = new GameObject("func_colliders");
            colliderRoot.transform.SetParent(rootModel.transform, false);
            colliderRoot.isStatic = true;

            List<(Bounds bounds, string material)> colliderData = new List<(Bounds, string)>();
            List<(OrientedBox box, string material)> orientedData = new List<(OrientedBox, string)>();

            foreach (VMFSolid solid in solids)
            {
                string material = calculateMaterials ? this.GetDominantMaterial(solid) : "default";
                if (string.IsNullOrEmpty(material)) material = "default";

                if (this.TryGetOrientedBox(solid, out OrientedBox box) && box.IsRotated)
                {
                    if (this.IsOrientedBoxValid(box)) orientedData.Add((box, material));
                    continue;
                }

                Bounds? bounds = this.GetBoundsForSolid(solid);
                if (!bounds.HasValue || !this.IsBoundsValid(bounds.Value)) continue;

                colliderData.Add((bounds.Value, material));
            }

            int colliderId = 0;
            List<(Bounds bounds, string material)> merged = this.MergeColliders(colliderData);
            foreach (var entry in merged) this.CreateColliderObject(entry, colliderId++, colliderRoot);
            foreach (var entry in orientedData) this.CreateOrientedColliderObject(entry.box, entry.material, colliderId++, colliderRoot);
        }

        private List<(Bounds bounds, string material)> MergeColliders(List<(Bounds bounds, string material)> input) {
            List<(Bounds bounds, string material)> result = new List<(Bounds, string)>();
            foreach (IGrouping<string, (Bounds bounds, string material)> group in input.GroupBy(x => x.material))
            {
                List<Bounds> boxes = group.Select(x => x.bounds).ToList();
                VMFImporter.MergeBoxesInPlace(boxes);
                foreach (Bounds b in boxes) result.Add((b, group.Key));
            }
            return result;
        }

        private static void MergeBoxesInPlace(List<Bounds> boxes) {
            int i = 0;
            while (i < boxes.Count)
            {
                bool merged = false;
                for (int j = i + 1; j < boxes.Count; j++)
                {
                    if (!VMFImporter.TryMergeBounds(boxes[i], boxes[j], out Bounds result)) continue;

                    boxes[i] = result;
                    boxes.RemoveAt(j);
                    merged = true;
                    break;
                }

                if (!merged) i++;
            }
        }

        private static bool TryMergeBounds(Bounds a, Bounds b, out Bounds result) {
            const float eps = 0.001f;

            if (VMFImporter.Contains(a, b, eps)) { result = a; return true; }
            if (VMFImporter.Contains(b, a, eps)) { result = b; return true; }

            for (int axis = 0; axis < 3; axis++)
            {
                int ax1 = (axis + 1) % 3;
                int ax2 = (axis + 2) % 3;

                if (Mathf.Abs(a.min[ax1] - b.min[ax1]) > eps) continue;
                if (Mathf.Abs(a.max[ax1] - b.max[ax1]) > eps) continue;
                if (Mathf.Abs(a.min[ax2] - b.min[ax2]) > eps) continue;
                if (Mathf.Abs(a.max[ax2] - b.max[ax2]) > eps) continue;

                if (a.max[axis] < b.min[axis] - eps) continue;
                if (b.max[axis] < a.min[axis] - eps) continue;

                Vector3 min = Vector3.Min(a.min, b.min);
                Vector3 max = Vector3.Max(a.max, b.max);
                result = new Bounds((min + max) * 0.5f, max - min);
                return true;
            }

            result = default;
            return false;
        }

        private static bool Contains(Bounds outer, Bounds inner, float eps) {
            return outer.min.x <= inner.min.x + eps && outer.max.x >= inner.max.x - eps
                && outer.min.y <= inner.min.y + eps && outer.max.y >= inner.max.y - eps
                && outer.min.z <= inner.min.z + eps && outer.max.z >= inner.max.z - eps;
        }

        private Bounds? GetBoundsForSolid(VMFSolid solid) {
            if (solid?.sides == null || solid.sides.Count == 0) return null;

            Matrix4x4 transform = VMFMesh.GetDefaultTransform();
            Vector3 min = Vector3.positiveInfinity;
            Vector3 max = Vector3.negativeInfinity;
            bool hasVertex = false;

            foreach (VMFSide side in solid.sides)
            {
                if (side?.vertices == null) continue;

                foreach (Vertex vert in side.vertices)
                {
                    if (vert == null) continue;

                    Vector3 pos = transform.MultiplyPoint3x4(vert.position);
                    min = Vector3.Min(min, pos);
                    max = Vector3.Max(max, pos);
                    hasVertex = true;
                }
            }

            if (!hasVertex) return null;
            return new Bounds((min + max) * 0.5f, max - min);
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

        private void CreateOrientedColliderObject(OrientedBox box, string material, int solidId, GameObject colliderRoot) {
            GameObject colliderObj = new GameObject($"collider_{solidId}");

            int collisionLayer = LayerMask.NameToLayer(VMFImporter.Settings.collisionMask);
            if (collisionLayer != -1) colliderObj.layer = collisionLayer;

            colliderObj.transform.SetParent(colliderRoot.transform, false);
            colliderObj.transform.localPosition = box.center;
            colliderObj.transform.localRotation = box.rotation;
            colliderObj.isStatic = true;

            BoxCollider boxCollider = colliderObj.AddComponent<BoxCollider>();
            boxCollider.center = Vector3.zero;
            boxCollider.size = box.size;

            this.AddMaterialComponent(colliderObj, material);
        }

        private bool IsOrientedBoxValid(OrientedBox box) {
            return box.size is { x: >= 0.01f, y: >= 0.01f, z: >= 0.01f };
        }

        private bool TryGetOrientedBox(VMFSolid solid, out OrientedBox box) {
            box = default;
            if (solid?.sides is not { Count: >= 3 }) return false;

            Matrix4x4 transform = VMFMesh.GetDefaultTransform();

            List<Vector3> corners = new List<Vector3>();
            List<Vector3> axes = new List<Vector3>();

            foreach (VMFSide side in solid.sides)
            {
                if (side == null || side.isDisplacement || side.vertices is not { Count: >= 3 }) return false;

                Vector3 normal = VMFImporter.GetSideNormal(side, transform);
                if (normal == Vector3.zero) return false;

                VMFImporter.AddUniqueAxis(axes, normal);

                foreach (Vertex vert in side.vertices)
                {
                    if (vert == null) continue;
                    VMFImporter.AddUniqueCorner(corners, transform.MultiplyPoint3x4(vert.position));
                }
            }

            if (corners.Count != 8 || axes.Count != 3) return false;
            if (!VMFImporter.AreOrthogonal(axes[0], axes[1], axes[2])) return false;

            Quaternion rotation = Quaternion.LookRotation(axes[2], axes[1]);

            Vector3 right = rotation * Vector3.right;
            Vector3 up = rotation * Vector3.up;
            Vector3 forward = rotation * Vector3.forward;

            Vector3 min = Vector3.positiveInfinity;
            Vector3 max = Vector3.negativeInfinity;

            foreach (Vector3 corner in corners)
            {
                Vector3 projected = new Vector3(Vector3.Dot(corner, right), Vector3.Dot(corner, up), Vector3.Dot(corner, forward));
                min = Vector3.Min(min, projected);
                max = Vector3.Max(max, projected);
            }

            const float tol = 0.001f;
            foreach (Vector3 corner in corners)
            {
                float r = Vector3.Dot(corner, right);
                float u = Vector3.Dot(corner, up);
                float f = Vector3.Dot(corner, forward);

                bool onR = Mathf.Abs(r - min.x) <= tol || Mathf.Abs(r - max.x) <= tol;
                bool onU = Mathf.Abs(u - min.y) <= tol || Mathf.Abs(u - max.y) <= tol;
                bool onF = Mathf.Abs(f - min.z) <= tol || Mathf.Abs(f - max.z) <= tol;
                if (!onR || !onU || !onF) return false;
            }

            Vector3 mid = (min + max) * 0.5f;
            box = new OrientedBox(right * mid.x + up * mid.y + forward * mid.z, max - min, rotation);
            return true;
        }

        private static Vector3 GetSideNormal(VMFSide side, Matrix4x4 transform) {
            List<Vector3> polygon = new List<Vector3>();
            foreach (Vertex vert in side.vertices)
            {
                if (vert == null) continue;
                VMFImporter.AddUniqueCorner(polygon, transform.MultiplyPoint3x4(vert.position));
            }

            if (polygon.Count < 3) return Vector3.zero;

            Vector3 normal = Vector3.zero;
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector3 current = polygon[i];
                Vector3 next = polygon[(i + 1) % polygon.Count];

                normal.x += (current.y - next.y) * (current.z + next.z);
                normal.y += (current.z - next.z) * (current.x + next.x);
                normal.z += (current.x - next.x) * (current.y + next.y);
            }

            return normal.sqrMagnitude > 1e-10f ? normal.normalized : Vector3.zero;
        }

        private static void AddUniqueAxis(List<Vector3> axes, Vector3 normal) {
            foreach (Vector3 axis in axes)
                if (Mathf.Abs(Vector3.Dot(axis, normal)) > 0.999f) return;

            axes.Add(normal);
        }

        private static void AddUniqueCorner(List<Vector3> corners, Vector3 position) {
            foreach (Vector3 corner in corners)
                if ((corner - position).sqrMagnitude < 1e-8f) return;

            corners.Add(position);
        }

        private static bool AreOrthogonal(Vector3 a, Vector3 b, Vector3 c) {
            const float tol = 0.02f;
            return Mathf.Abs(Vector3.Dot(a, b)) < tol
                && Mathf.Abs(Vector3.Dot(b, c)) < tol
                && Mathf.Abs(Vector3.Dot(a, c)) < tol;
        }

        private static bool IsWorldAligned(Vector3 v) {
            const float tol = 0.001f;
            int alignedAxes = 0;

            if (Mathf.Abs(Mathf.Abs(v.x) - 1f) < tol) alignedAxes++;
            else if (Mathf.Abs(v.x) > tol) return false;

            if (Mathf.Abs(Mathf.Abs(v.y) - 1f) < tol) alignedAxes++;
            else if (Mathf.Abs(v.y) > tol) return false;

            if (Mathf.Abs(Mathf.Abs(v.z) - 1f) < tol) alignedAxes++;
            else if (Mathf.Abs(v.z) > tol) return false;

            return alignedAxes == 1;
        }

        private readonly struct OrientedBox
        {
            public readonly Vector3 center;
            public readonly Vector3 size;
            public readonly Quaternion rotation;

            public OrientedBox(Vector3 center, Vector3 size, Quaternion rotation) {
                this.center = center;
                this.size = size;
                this.rotation = rotation;
            }

            public bool IsRotated =>
                !(VMFImporter.IsWorldAligned(this.rotation * Vector3.right)
                  && VMFImporter.IsWorldAligned(this.rotation * Vector3.up)
                  && VMFImporter.IsWorldAligned(this.rotation * Vector3.forward));
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
                    !side.isTool &&
                    !string.IsNullOrEmpty(side.material) &&
                    (!VMFImporter.Settings.HasMaterialOverride(side.material) ||
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
				this.TryCreateEntity(entity, entityOverrides, transformMatrix, rootModel);
        }

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

        private void GenerateModel(AssetImportContext ctx, GameObject modelObject, Dictionary<string, List<VMFSide>> groupedSolids, Dictionary<string, TextureArrayInfo> sharedTextureArrays = null,
            List<Material> sharedMaterials = null) {
            if (groupedSolids is not { Count: > 0 }) return;

            try
            {
                VMFMaterials vmfMaterials = null;
                if (this.GenerateTextures && this.GenerateMaterialData) vmfMaterials = modelObject.AddComponent<VMFMaterials>();

                List<Material> materials = null;
                if (this.GenerateTextures) materials = sharedMaterials?.Count > 0 ? sharedMaterials : new List<Material>();

                Mesh combinedMesh;
                if (this.GenerateTextures && sharedTextureArrays != null)
                {
                    combinedMesh = VMFMesh.GenerateMeshWithSharedTextures(groupedSolids, sharedTextureArrays);

                    if (vmfMaterials) this.PopulateSharedMaterialDictionary(vmfMaterials, groupedSolids, sharedTextureArrays);
                    if (materials != null && combinedMesh) materials = this.CreateMaterialsForSharedArrays(sharedMaterials, groupedSolids, sharedTextureArrays);
                }
                else
                    combinedMesh = VMFMesh.GenerateMeshWithSharedTextures(groupedSolids, null);

                if (!combinedMesh)
                {
                    Debug.LogError($"Failed to generate mesh for {modelObject.name}");
                    return;
                }

                Mesh finalMesh = this.SimplifyMesh(combinedMesh);
                finalMesh.name = "default";
                
                if(this.GenerateUVLightmap) Unwrapping.GenerateSecondaryUVSet(finalMesh);

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
            meshRenderer.staticShadowCaster = true;

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
                    if (side.isTool) continue; // Tool sides are collision-only, never rendered

                    string materialKey = this.GenerateTextures ? side.material : "__IGNORE__";

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