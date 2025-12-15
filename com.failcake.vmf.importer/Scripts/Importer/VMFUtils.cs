#region

using System;
using System.Globalization;
using UnityEngine;

#endregion

namespace FailCake.VMF
{
    public static class VMFUtils
    {
        /// <summary>
        /// Parses VMF texture axis format: "[x y z translation] scale"
        /// Example: "[1 0 0 0] 0.25"
        /// </summary>
        /// <param name="axis">The axis string to parse</param>
        /// <returns>Array containing [x, y, z, translation, scale]</returns>
        public static float[] ParseAxis(string axis)
        {
            if (string.IsNullOrEmpty(axis))
            {
                throw new ArgumentNullException(nameof(axis), "Axis string cannot be null or empty");
            }

            try
            {
                string[] parts = axis.Split(new[] { '[', ']', ' ' }, StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length < 5)
                {
                    throw new FormatException($"Invalid axis format - expected 5 components, got {parts.Length}: {axis}");
                }

                float x = float.Parse(parts[0], CultureInfo.InvariantCulture);
                float y = float.Parse(parts[1], CultureInfo.InvariantCulture);
                float z = float.Parse(parts[2], CultureInfo.InvariantCulture);
                float translation = float.Parse(parts[3], CultureInfo.InvariantCulture);
                float scale = float.Parse(parts[4], CultureInfo.InvariantCulture);

                // Validate scale to prevent division by zero
                if (Mathf.Approximately(scale, 0f))
                {
                    Debug.LogWarning($"Zero scale found in axis '{axis}', using 1.0 instead");
                    scale = 1.0f;
                }

                return new[] { x, y, z, translation, scale };
            }
            catch (System.Exception ex) when (!(ex is FormatException))
            {
                throw new FormatException($"Error parsing axis '{axis}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Parses VMF vector format: "[x y z]" or "x y z"
        /// Example: "[1024 512 256]" or "1024 512 256"
        /// </summary>
        /// <param name="vectorString">The vector string to parse</param>
        /// <returns>Parsed Vector3, or Vector3.zero if parsing fails</returns>
        public static Vector3 ParseVector(string vectorString)
        {
            if (string.IsNullOrEmpty(vectorString))
            {
                Debug.LogWarning("Empty vector string provided, returning Vector3.zero");
                return Vector3.zero;
            }

            try
            {
                // Clean the string - remove quotes, brackets, and extra spaces
                string cleanedString = vectorString.Trim('"', ' ', '\t')
                    .Replace("[", "")
                    .Replace("]", "")
                    .Trim();

                string[] parts = cleanedString.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length < 3)
                {
                    Debug.LogError($"Vector string must have at least 3 components, got {parts.Length}: {vectorString}");
                    return Vector3.zero;
                }

                if (float.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out float x) &&
                    float.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out float y) &&
                    float.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out float z))
                {
                    return new Vector3(x, y, z);
                }

                Debug.LogError($"Failed to parse numeric values from vector string: {vectorString}");
                return Vector3.zero;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Exception parsing vector '{vectorString}': {ex.Message}");
                return Vector3.zero;
            }
        }

        /// <summary>
        /// Extracts layer information from material name containing "LAYER_TEXTURE_N"
        /// </summary>
        /// <param name="material">Material name to parse</param>
        /// <returns>VMFLayer enum value, or LAYER_0 if parsing fails</returns>
        public static VMFLayer ExtractLayerMaterial(string material)
        {
            if (string.IsNullOrEmpty(material))
            {
                return VMFLayer.LAYER_0;
            }

            try
            {
                string[] parts = material.Split(new[] { '_', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                {
                    return VMFLayer.LAYER_0;
                }

                // Look for numeric layer identifier at the end
                string lastPart = parts[^1];
                if (int.TryParse(lastPart, out int layer))
                {
                    // Clamp to valid layer range
                    int clampedLayer = Mathf.Clamp(layer, (int)VMFLayer.LAYER_0, (int)VMFLayer.COUNT - 1);
                    return (VMFLayer)clampedLayer;
                }

                return VMFLayer.LAYER_0;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Error extracting layer from material '{material}': {ex.Message}");
                return VMFLayer.LAYER_0;
            }
        }
    }
}