#region

using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Scripting;

#endregion

namespace FailCake.VMF
{
    [Serializable, Preserve]
    public enum VMFMaterial : byte
    {
        TILE = 0,
        METAL,
        CONCRETE,
        WOOD,
        STONE,
        GRASS,
        SAND,
        CARPET
    }

    public class VMFMaterials : MonoBehaviour
    {
        // Maps array index -> texture index -> material type
        public SerializedDictionary<byte, SerializedDictionary<byte, VMFMaterial>> materialDictionary =
            new SerializedDictionary<byte, SerializedDictionary<byte, VMFMaterial>>();

        #if UNITY_EDITOR
        [ReadOnly]
        #endif
        public MeshFilter meshFilter;

        public static VMFMaterial GetMaterialTexture(string texName) {
            VMFMaterial type;

            if (texName.Contains("sand", StringComparison.OrdinalIgnoreCase))
                type = VMFMaterial.SAND;
            else if (texName.Contains("concrete", StringComparison.OrdinalIgnoreCase))
                type = VMFMaterial.CONCRETE;
            else if (texName.Contains("metal", StringComparison.OrdinalIgnoreCase) ||
                     texName.Contains("fence", StringComparison.OrdinalIgnoreCase))
                type = VMFMaterial.METAL;
            else if (texName.Contains("wood", StringComparison.OrdinalIgnoreCase) ||
                     texName.Contains("planks", StringComparison.OrdinalIgnoreCase))
                type = VMFMaterial.WOOD;
            else if (texName.Contains("stone", StringComparison.OrdinalIgnoreCase) ||
                     texName.Contains("rock", StringComparison.OrdinalIgnoreCase))
                type = VMFMaterial.STONE;
            else if (texName.Contains("grass", StringComparison.OrdinalIgnoreCase))
                type = VMFMaterial.GRASS;
            else if (texName.Contains("carpet", StringComparison.OrdinalIgnoreCase) ||
                     texName.Contains("rug", StringComparison.OrdinalIgnoreCase))
                type = VMFMaterial.CARPET;
            else
                type = VMFMaterial.TILE;

            return type;
        }
    }
}