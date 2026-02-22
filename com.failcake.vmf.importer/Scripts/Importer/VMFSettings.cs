#region

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting;

#endregion

namespace FailCake.VMF
{
    [Serializable]
    public struct MaterialOverride
    {
        public string key;
        public Material val;
    }

    [Serializable]
    public struct EntityOverride
    {
        public string key;
        public GameObject prefab;
    }

    [Preserve, Serializable]
    public class VMFSettings : ScriptableObject
    {
        [Header("VPK"), Tooltip("Path to the VPK files. This is used to load the textures from the VPK files.")]
        public List<string> vpkFiles = new List<string>();

        [Header("Textures")]
        public bool removeToolTextures = true;

        [Space(3), Header("Overrides")]
        public List<MaterialOverride> materialOverride = new List<MaterialOverride>();

        public List<EntityOverride> entityOverrides = new List<EntityOverride>();

        public List<MaterialOverride> layerMaterials = new List<MaterialOverride>();

        [Space(3), Header("Collision")]
        public string collisionMask; // LayerMask not working correctly for some reason

        #region PRIVATE

        private readonly Dictionary<string, Material> _materialOverrides = new Dictionary<string, Material>();
        private readonly Dictionary<string, GameObject> _entityOverrides = new Dictionary<string, GameObject>();

        #endregion

        public void Init() {
            List<MaterialOverride> materialOverrides = new List<MaterialOverride>(this.layerMaterials);
            materialOverrides.AddRange(this.materialOverride);

            this._materialOverrides.Clear();
            foreach (MaterialOverride mat in materialOverrides)
            {
                if (this._materialOverrides.ContainsKey(mat.key))
                {
                    Debug.LogWarning($"Duplicate material override key: {mat.key}");
                    continue;
                }

                this._materialOverrides.Add(mat.key, mat.val);
            }

            this._entityOverrides.Clear();
            foreach (EntityOverride ent in this.entityOverrides)
            {
                if (this._entityOverrides.ContainsKey(ent.key))
                {
                    Debug.LogWarning($"Duplicate entity override key: {ent.key}");
                    continue;
                }

                this._entityOverrides.Add(ent.key, ent.prefab);
            }
        }

        public Material GetMaterialOverride(string key) {
            return this._materialOverrides.GetValueOrDefault(key);
        }

        public bool HasMaterialOverride(string key) {
            return this._materialOverrides.TryGetValue(key, out Material mat) && mat;
        }

        public GameObject GetEntityOverride(string key) {
            return this._entityOverrides.GetValueOrDefault(key);
        }

        public Dictionary<string, GameObject> GetEntityOverrides() {
            return this._entityOverrides;
        }
    }
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
