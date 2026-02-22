#region

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

#endregion

namespace FailCake.VMF
{
#if UNITY_EDITOR
    internal class VMFSolid
    {
        public string ID;
        public List<VMFSide> sides = new();

        public bool AddSides(VMFDataBlock solidDataBlock, bool generateTextures)
        {
            if (solidDataBlock == null)
            {
                Debug.LogWarning($"Null solid data block provided for solid {ID}");
                return false;
            }

            var dataSides = solidDataBlock.GetAllBlocks("side");
            if (dataSides.Length == 0)
            {
                Debug.LogWarning($"No sides found in solid {ID}");
                return false;
            }

            // DISPLACEMENT
            var hasDisplacement = dataSides.Any(side => side.GetSingle("dispinfo") != null);
            // ------------

            foreach (var sideBlock in dataSides) ProcessSide(sideBlock, hasDisplacement, generateTextures);

            return sides.Count > 0;
        }

        private void ProcessSide(VMFDataBlock sideBlock, bool hasDisplacement, bool generateTextures)
        {
            try
            {
                var isDisplacementSide = sideBlock.GetSingle("dispinfo") != null;
                if (hasDisplacement && !isDisplacementSide) return;

                var material = sideBlock.GetSingle("material") as string;
                if (string.IsNullOrEmpty(material))
                {
                    Debug.LogWarning($"Material missing on side in solid {ID}");
                    return;
                }

                if (VMFImporter.Settings.removeToolTextures && material.ToLower().StartsWith("tools/"))
                    return;

                Texture2D texture = null;
                if (generateTextures) texture = VMFTextures.GetVMFTextureByName(material);

                var newSide = new VMFSide
                {
                    isDisplacement = isDisplacementSide,
                    material = material
                };

                var uaxisStr = sideBlock.GetSingle("uaxis") as string;
                var vaxisStr = sideBlock.GetSingle("vaxis") as string;

                if (string.IsNullOrEmpty(uaxisStr) || string.IsNullOrEmpty(vaxisStr))
                {
                    Debug.LogWarning($"UV axes missing for side in solid {ID}");
                    return;
                }

                var uAxis = new ParsedAxis(uaxisStr);
                var vAxis = new ParsedAxis(vaxisStr);

                var textureSize = new Vector2(texture?.width ?? 1, texture?.height ?? 1);

                var vertices = GetVerticesFromSide(sideBlock);
                if (vertices == null || vertices.Length < 3)
                {
                    Debug.LogWarning($"Insufficient vertices for side in solid {ID}");
                    return;
                }

                if (isDisplacementSide)
                {
                    var displacementData = sideBlock.GetSingle("dispinfo") as VMFDataBlock;
                    ParseDisplacement(displacementData, ref newSide, vertices, uAxis, vAxis, textureSize);
                }
                else
                {
                    GenerateBlockSide(ref newSide, vertices, uAxis, vAxis, textureSize);
                }

                if (newSide.vertices?.Count > 0)
                {
                    sides.Add(newSide);
                    return;
                }

                Debug.LogWarning($"No vertices generated for side in solid {ID}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error processing side in solid {ID}: {ex.Message}");
            }
        }

        private string[] GetVerticesFromSide(VMFDataBlock side)
        {
            if (side == null) throw new ArgumentNullException(nameof(side), $"Side data block is null for solid {ID}");

            var vertDataBlock = side.GetSingle("vertices_plus") as VMFDataBlock;
            if (vertDataBlock == null)
                throw new InvalidOperationException($"Malformed side in solid {ID}: vertices_plus block is missing");

            var vertices = vertDataBlock.GetAllString("v");
            if (vertices == null || vertices.Length == 0)
                throw new InvalidOperationException(
                    $"Malformed side in solid {ID}: no vertex data found in vertices_plus block");

            return vertices;
        }

        private void GenerateBlockSide(ref VMFSide newSide, string[] vertices, in ParsedAxis uAxis, in ParsedAxis vAxis,
            Vector2 textureSize)
        {
            if (newSide == null || vertices == null || vertices.Length < 3)
            {
                Debug.LogError($"Invalid parameters for side generation in solid {ID}");
                return;
            }

            try
            {
                switch (vertices.Length)
                {
                    case 3:
                        GenerateTriangle(ref newSide, vertices, uAxis, vAxis, textureSize);
                        break;

                    case 4:
                        GenerateQuad(ref newSide, vertices, uAxis, vAxis, textureSize);
                        break;

                    default:
                        if (vertices.Length >= 5)
                            GeneratePolygon(ref newSide, vertices, uAxis, vAxis, textureSize);
                        else
                            Debug.LogError($"Invalid vertex count ({vertices.Length}) for side in solid {ID}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error generating block side for solid {ID}: {ex.Message}");
            }
        }

        private void GenerateTriangle(ref VMFSide newSide, string[] vertices, in ParsedAxis uAxis, in ParsedAxis vAxis,
            Vector2 textureSize)
        {
            try
            {
                var a = VMFUtils.ParseVector(vertices[0]);
                var b = VMFUtils.ParseVector(vertices[1]);
                var c = VMFUtils.ParseVector(vertices[2]);

                var aUV = CalculateUV(a, uAxis, vAxis, textureSize);
                var bUV = CalculateUV(b, uAxis, vAxis, textureSize);
                var cUV = CalculateUV(c, uAxis, vAxis, textureSize);

                var cross = Vector3.Cross(c - a, b - a);
                var normal = cross.sqrMagnitude > 0.000001f ? cross.normalized : Vector3.up;

                newSide.AddVertice(a, normal, aUV);
                newSide.AddVertice(b, normal, bUV);
                newSide.AddVertice(c, normal, cUV);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error generating triangle for solid {ID}: {ex.Message}");
            }
        }

        private void GenerateQuad(ref VMFSide newSide, string[] vertices, in ParsedAxis uAxis, in ParsedAxis vAxis,
            Vector2 textureSize)
        {
            try
            {
                var a = VMFUtils.ParseVector(vertices[0]);
                var b = VMFUtils.ParseVector(vertices[1]);
                var c = VMFUtils.ParseVector(vertices[2]);
                var d = VMFUtils.ParseVector(vertices[3]);

                var aUV = CalculateUV(a, uAxis, vAxis, textureSize);
                var bUV = CalculateUV(b, uAxis, vAxis, textureSize);
                var cUV = CalculateUV(c, uAxis, vAxis, textureSize);
                var dUV = CalculateUV(d, uAxis, vAxis, textureSize);

                var cross = Vector3.Cross(d - a, b - a);
                Vector3 normal;
                if (cross.sqrMagnitude > 0.000001f)
                {
                    normal = cross.normalized;
                }
                else
                {
                    cross = Vector3.Cross(c - a, b - a);
                    normal = cross.sqrMagnitude > 0.000001f ? cross.normalized : Vector3.up;
                }

                newSide.AddVertice(a, normal, aUV);
                newSide.AddVertice(b, normal, bUV);
                newSide.AddVertice(c, normal, cUV);

                newSide.AddVertice(a, normal, aUV);
                newSide.AddVertice(c, normal, cUV);
                newSide.AddVertice(d, normal, dUV);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error generating quad for solid {ID}: {ex.Message}");
            }
        }

        private void GeneratePolygon(ref VMFSide newSide, string[] vertices, in ParsedAxis uAxis, in ParsedAxis vAxis,
            Vector2 textureSize)
        {
            try
            {
                if (vertices.Length < 5) return;

                var firstVertex = VMFUtils.ParseVector(vertices[0]);
                var firstUV = CalculateUV(firstVertex, uAxis, vAxis, textureSize);

                var a = firstVertex;
                var b = VMFUtils.ParseVector(vertices[1]);
                var c = VMFUtils.ParseVector(vertices[2]);

                var cross = Vector3.Cross(c - a, b - a);
                Vector3 normal;

                if (cross.sqrMagnitude > 0.000001f)
                {
                    normal = cross.normalized;
                }
                else
                {
                    if (vertices.Length >= 4)
                    {
                        var d = VMFUtils.ParseVector(vertices[3]);
                        cross = Vector3.Cross(d - a, c - a);
                    }

                    normal = cross.sqrMagnitude > 0.000001f ? cross.normalized : Vector3.up;
                }

                for (var i = 1; i < vertices.Length - 1; i++)
                    try
                    {
                        var vertB = VMFUtils.ParseVector(vertices[i]);
                        var vertC = VMFUtils.ParseVector(vertices[i + 1]);

                        var bUV = CalculateUV(vertB, uAxis, vAxis, textureSize);
                        var cUV = CalculateUV(vertC, uAxis, vAxis, textureSize);

                        newSide.AddVertice(firstVertex, normal, firstUV);
                        newSide.AddVertice(vertB, normal, bUV);
                        newSide.AddVertice(vertC, normal, cUV);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"Error processing triangle {i} in polygon for solid {ID}: {ex.Message}");
                    }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error generating polygon for solid {ID}: {ex.Message}");
            }
        }

        private void ParseDisplacement(VMFDataBlock displacementData, ref VMFSide newSide, string[] rawVerts,
            in ParsedAxis uAxis, in ParsedAxis vAxis, Vector2 textureSize)
        {
            if (rawVerts.Length != 4)
            {
                Debug.LogError($"Malformed side in solid {ID}, displacement requires exactly 4 vertices");
                return;
            }

            if (!int.TryParse(displacementData.GetSingle("power") as string, out var power) || power < 2 || power > 4)
            {
                Debug.LogError($"Invalid displacement power in solid {ID}");
                return;
            }

            var resolution = (1 << power) + 1;

            var startPosition = Vector3.zero;
            if (displacementData.GetSingle("startposition") is string startPosStr)
                startPosition = VMFUtils.ParseVector(startPosStr);

            var elevation = 0f;
            if (displacementData.GetSingle("elevation") is string elevStr) float.TryParse(elevStr, out elevation);

            var subdiv = false;
            if (displacementData.GetSingle("subdiv") is string subdivStr) subdiv = subdivStr == "1";

            var normals = StringArrayToFloatParts(
                (displacementData.GetSingle("normals") as VMFDataBlock)?.GetAll<string>());

            var distances = StringArrayToFloatParts(
                (displacementData.GetSingle("distances") as VMFDataBlock)?.GetAll<string>());

            var offsets = StringArrayToFloatParts(
                (displacementData.GetSingle("offsets") as VMFDataBlock)?.GetAll<string>());

            float[][] offsetNormals = null;
            if (displacementData.GetSingle("offset_normals") is VMFDataBlock offsetNormalsBlock)
                offsetNormals = StringArrayToFloatParts(offsetNormalsBlock.GetAll<string>());

            if (normals.Length != resolution || distances.Length != resolution || offsets.Length != resolution)
            {
                Debug.LogError($"Displacement data in solid {ID} does not match expected resolution {resolution}");
                return;
            }

            var topLeft = VMFUtils.ParseVector(rawVerts[0]);
            var topRight = VMFUtils.ParseVector(rawVerts[1]);
            var bottomRight = VMFUtils.ParseVector(rawVerts[2]);
            var bottomLeft = VMFUtils.ParseVector(rawVerts[3]);

            if (startPosition != Vector3.zero)
            {
                Vector3[] corners = { bottomLeft, bottomRight, topRight, topLeft };
                var minDist = float.MaxValue;
                var startCornerIndex = 0;

                for (var i = 0; i < 4; i++)
                {
                    var dist = Vector3.SqrMagnitude(corners[i] - startPosition);
                    if (!(dist < minDist)) continue;

                    minDist = dist;
                    startCornerIndex = i;
                }

                if (startCornerIndex != 0)
                {
                    var reordered = new Vector3[4];
                    for (var i = 0; i < 4; i++) reordered[i] = corners[(startCornerIndex + i) % 4];

                    bottomLeft = reordered[0];
                    bottomRight = reordered[1];
                    topRight = reordered[2];
                    topLeft = reordered[3];
                }
            }

            var faceNormal = Vector3.Cross(bottomLeft - topLeft, topRight - topLeft).normalized;

            var grid = new Vector3[resolution, resolution];
            var uvGrid = new Vector2[resolution, resolution];

            for (var y = 0; y < resolution; y++)
            for (var x = 0; x < resolution; x++)
            {
                var u = x / (float)(resolution - 1);
                var v = y / (float)(resolution - 1);

                var origin = GetLerpedPoint(topLeft, topRight, bottomLeft, bottomRight, u, v);
                var dispDirection = faceNormal;

                if (y < normals.Length && x * 3 + 2 < normals[y].Length)
                {
                    dispDirection = new Vector3(
                        normals[y][x * 3],
                        normals[y][x * 3 + 1],
                        normals[y][x * 3 + 2]
                    );

                    if (dispDirection.sqrMagnitude > 0.0001f) dispDirection.Normalize();
                }

                var distance = 0f;
                if (y < distances.Length && x < distances[y].Length) distance = distances[y][x];

                var offset = Vector3.zero;
                if (y < offsets.Length && x * 3 + 2 < offsets[y].Length)
                    offset = new Vector3(
                        offsets[y][x * 3],
                        offsets[y][x * 3 + 1],
                        offsets[y][x * 3 + 2]
                    );

                if (offsetNormals != null && y < offsetNormals.Length && x * 3 + 2 < offsetNormals[y].Length)
                {
                    var offsetNormal = new Vector3(
                        offsetNormals[y][x * 3],
                        offsetNormals[y][x * 3 + 1],
                        offsetNormals[y][x * 3 + 2]
                    );
                    offset += offsetNormal;
                }

                var displacedVertex = origin + dispDirection * (distance + elevation) + offset;
                var uv = CalculateUV(displacedVertex, uAxis, vAxis, textureSize);

                grid[y, x] = displacedVertex;
                uvGrid[y, x] = uv;
            }

            var renderNormals = new Vector3[resolution, resolution];
            for (var y = 0; y < resolution; y++)
            for (var x = 0; x < resolution; x++)
                renderNormals[y, x] = faceNormal;


            for (var y = 0; y < resolution - 1; y++)
            for (var x = 0; x < resolution - 1; x++)
            {
                var v00 = grid[y, x];
                var v10 = grid[y + 1, x];
                var v01 = grid[y, x + 1];
                var v11 = grid[y + 1, x + 1];

                var n1 = Vector3.Cross(v01 - v00, v10 - v00).normalized;
                var n2 = Vector3.Cross(v10 - v11, v01 - v11).normalized;

                if (Vector3.Dot(n1, faceNormal) < 0) n1 = -n1;
                if (Vector3.Dot(n2, faceNormal) < 0) n2 = -n2;

                var avgNormal = (n1 + n2).normalized;
                renderNormals[y, x] += n1;
                renderNormals[y + 1, x] += avgNormal;
                renderNormals[y, x + 1] += avgNormal;
                renderNormals[y + 1, x + 1] += n2;
            }

            // NORMALS
            for (var y = 0; y < resolution; y++)
            for (var x = 0; x < resolution; x++)
                if (renderNormals[y, x].sqrMagnitude > 0.0001f)
                    renderNormals[y, x].Normalize();
                else
                    renderNormals[y, x] = faceNormal;

            // TRIANGLE GEN
            for (var y = 0; y < resolution - 1; y++)
            for (var x = 0; x < resolution - 1; x++)
            {
                var v00 = grid[y, x];
                var v01 = grid[y, x + 1];
                var v10 = grid[y + 1, x];
                var v11 = grid[y + 1, x + 1];

                var n00 = renderNormals[y, x];
                var n01 = renderNormals[y, x + 1];
                var n10 = renderNormals[y + 1, x];
                var n11 = renderNormals[y + 1, x + 1];

                if (subdiv) // Four
                {
                    var center = (v00 + v01 + v10 + v11) / 4f;
                    var centerNormal = (n00 + n01 + n10 + n11).normalized;
                    var centerUV = (uvGrid[y, x] + uvGrid[y, x + 1] +
                                    uvGrid[y + 1, x] + uvGrid[y + 1, x + 1]) / 4f;

                    newSide.AddVertice(v00, n00, uvGrid[y, x]);
                    newSide.AddVertice(v10, n10, uvGrid[y + 1, x]);
                    newSide.AddVertice(center, centerNormal, centerUV);

                    newSide.AddVertice(v10, n10, uvGrid[y + 1, x]);
                    newSide.AddVertice(v11, n11, uvGrid[y + 1, x + 1]);
                    newSide.AddVertice(center, centerNormal, centerUV);

                    newSide.AddVertice(v11, n11, uvGrid[y + 1, x + 1]);
                    newSide.AddVertice(v01, n01, uvGrid[y, x + 1]);
                    newSide.AddVertice(center, centerNormal, centerUV);

                    newSide.AddVertice(v01, n01, uvGrid[y, x + 1]);
                    newSide.AddVertice(v00, n00, uvGrid[y, x]);
                    newSide.AddVertice(center, centerNormal, centerUV);
                }
                else // Two
                {
                    newSide.AddVertice(v00, n00, uvGrid[y, x]);
                    newSide.AddVertice(v10, n10, uvGrid[y + 1, x]);
                    newSide.AddVertice(v01, n01, uvGrid[y, x + 1]);

                    newSide.AddVertice(v01, n01, uvGrid[y, x + 1]);
                    newSide.AddVertice(v10, n10, uvGrid[y + 1, x]);
                    newSide.AddVertice(v11, n11, uvGrid[y + 1, x + 1]);
                }
            }
        }

        private Vector3 GetLerpedPoint(Vector3 topLeft, Vector3 topRight, Vector3 bottomLeft,
            Vector3 bottomRight, float u, float v)
        {
            var topEdge = Vector3.Lerp(topLeft, topRight, u);
            var bottomEdge = Vector3.Lerp(bottomLeft, bottomRight, u);

            return Vector3.Lerp(bottomEdge, topEdge, v);
        }

        private float[][] StringArrayToFloatParts(string[] rows)
        {
            if (rows == null || rows.Length == 0) return Array.Empty<float[]>();

            return rows
                .Select(row => row.Split(' ')
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(s =>
                        float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
                            ? result
                            : 0f)
                    .ToArray())
                .ToArray();
        }

        private struct ParsedAxis
        {
            public readonly Vector3 direction;
            public readonly float translation;
            public readonly float scale;

            public ParsedAxis(string axisString)
            {
                var data = VMFUtils.ParseAxis(axisString);
                direction = new Vector3(data[0], data[1], data[2]);
                translation = data[3];
                scale = data[4];
            }
        }

        private static Vector2 CalculateUV(
            Vector3 vertex,
            in ParsedAxis uAxis,
            in ParsedAxis vAxis,
            Vector2 textureSize
        )
        {
            var u = Vector3.Dot(vertex, uAxis.direction) / uAxis.scale + uAxis.translation;
            var v = Vector3.Dot(vertex, vAxis.direction) / vAxis.scale + vAxis.translation;

            return new Vector2(u / textureSize.x, v / textureSize.y);
        }
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