#region

using System;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

#endregion

namespace FailCake.VMF
{
    #if UNITY_EDITOR
    internal class VMFSolid
    {
        public string ID;
        public List<VMFSide> sides = new List<VMFSide>();

        public bool AddSides(VMFDataBlock solidDataBlock, bool generateTextures) {
            if (solidDataBlock == null)
            {
                Debug.LogWarning($"Null solid data block provided for solid {this.ID}");
                return false;
            }

            var dataSides = solidDataBlock.GetAllBlocks("side");
            if (dataSides.Length == 0)
            {
                Debug.LogWarning($"No sides found in solid {this.ID}");
                return false;
            }

            // DISPLACEMENT
            bool hasDisplacement = dataSides.Any(side => side.GetSingle("dispinfo") != null);
            // ------------

            foreach (VMFDataBlock sideBlock in dataSides) this.ProcessSide(sideBlock, hasDisplacement, generateTextures);

            return this.sides.Count > 0;
        }

        private void ProcessSide(VMFDataBlock sideBlock, bool hasDisplacement, bool generateTextures) {
            try
            {
                bool isDisplacementSide = sideBlock.GetSingle("dispinfo") != null;
                if (hasDisplacement && !isDisplacementSide) return;

                string material = sideBlock.GetSingle("material") as string;
                if (string.IsNullOrEmpty(material))
                {
                    Debug.LogWarning($"Material missing on side in solid {this.ID}");
                    return;
                }

                if (VMFImporter.Settings.removeToolTextures && material.ToLower().StartsWith("tools/")) return; // Skip tool textures if filtering enabled

                Texture2D texture = null;
                if (generateTextures) texture = VMFTextures.GetVMFTextureByName(material);

                VMFSide newSide = new VMFSide {
                    isDisplacement = isDisplacementSide,
                    material = material
                };

                string uaxis = sideBlock.GetSingle("uaxis") as string;
                string vaxis = sideBlock.GetSingle("vaxis") as string;

                if (string.IsNullOrEmpty(uaxis) || string.IsNullOrEmpty(vaxis))
                {
                    Debug.LogWarning($"UV axes missing for side in solid {this.ID}");
                    return;
                }

                Vector2 textureSize = new Vector2(texture?.width ?? 1, texture?.height ?? 1);

                string[] vertices = this.GetVerticesFromSide(sideBlock);
                if (vertices == null || vertices.Length < 3)
                {
                    Debug.LogWarning($"Insufficient vertices for side in solid {this.ID}");
                    return;
                }

                if (isDisplacementSide)
                {
                    VMFDataBlock displacementData = sideBlock.GetSingle("dispinfo") as VMFDataBlock;
                    this.ParseDisplacement(displacementData, ref newSide, vertices, uaxis, vaxis, textureSize);
                }
                else
                    this.GenerateBlockSide(ref newSide, vertices, uaxis, vaxis, textureSize);

                if (newSide.vertices?.Count > 0)
                {
                    this.sides.Add(newSide);
                    return;
                }

                Debug.LogWarning($"No vertices generated for side in solid {this.ID}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error processing side in solid {this.ID}: {ex.Message}");
            }
        }

        private string[] GetVerticesFromSide(VMFDataBlock side) {
            if (side == null) throw new ArgumentNullException(nameof(side), $"Side data block is null for solid {this.ID}");

            VMFDataBlock vertDataBlock = side.GetSingle("vertices_plus") as VMFDataBlock;
            if (vertDataBlock == null) throw new InvalidOperationException($"Malformed side in solid {this.ID}: vertices_plus block is missing");

            string[] vertices = vertDataBlock.GetAllString("v");
            if (vertices == null || vertices.Length == 0) throw new InvalidOperationException($"Malformed side in solid {this.ID}: no vertex data found in vertices_plus block");

            return vertices;
        }

        private void GenerateBlockSide(ref VMFSide newSide, string[] vertices, string uaxis, string vaxis, Vector2 textureSize) {
            if (newSide == null || vertices == null || vertices.Length < 3)
            {
                Debug.LogError($"Invalid parameters for side generation in solid {this.ID}");
                return;
            }

            try
            {
                switch (vertices.Length)
                {
                    case 3:
                        this.GenerateTriangle(ref newSide, vertices, uaxis, vaxis, textureSize);
                        break;

                    case 4:
                        this.GenerateQuad(ref newSide, vertices, uaxis, vaxis, textureSize);
                        break;

                    default:
                        if (vertices.Length >= 5)
                            this.GeneratePolygon(ref newSide, vertices, uaxis, vaxis, textureSize);
                        else
                            Debug.LogError($"Invalid vertex count ({vertices.Length}) for side in solid {this.ID}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error generating block side for solid {this.ID}: {ex.Message}");
            }
        }

        private void GenerateTriangle(ref VMFSide newSide, string[] vertices, string uaxis, string vaxis, Vector2 textureSize) {
            try
            {
                Vector3 a = VMFUtils.ParseVector(vertices[0]);
                Vector3 b = VMFUtils.ParseVector(vertices[1]);
                Vector3 c = VMFUtils.ParseVector(vertices[2]);

                Vector2 aUV = this.CalculateUVsLocal(a, uaxis, vaxis, textureSize);
                Vector2 bUV = this.CalculateUVsLocal(b, uaxis, vaxis, textureSize);
                Vector2 cUV = this.CalculateUVsLocal(c, uaxis, vaxis, textureSize);

                Vector3 normal = Vector3.Cross(c - a, b - a).normalized;
                if (normal.magnitude < 0.001f) normal = Vector3.up;

                newSide.AddVertice(a, normal, aUV);
                newSide.AddVertice(b, normal, bUV);
                newSide.AddVertice(c, normal, cUV);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error generating triangle for solid {this.ID}: {ex.Message}");
            }
        }

        private void GenerateQuad(ref VMFSide newSide, string[] vertices, string uaxis, string vaxis, Vector2 textureSize) {
            try
            {
                Vector3 a = VMFUtils.ParseVector(vertices[0]);
                Vector3 b = VMFUtils.ParseVector(vertices[1]);
                Vector3 c = VMFUtils.ParseVector(vertices[2]);
                Vector3 d = VMFUtils.ParseVector(vertices[3]);

                Vector2 aUV = this.CalculateUVsLocal(a, uaxis, vaxis, textureSize);
                Vector2 bUV = this.CalculateUVsLocal(b, uaxis, vaxis, textureSize);
                Vector2 cUV = this.CalculateUVsLocal(c, uaxis, vaxis, textureSize);
                Vector2 dUV = this.CalculateUVsLocal(d, uaxis, vaxis, textureSize);

                Vector3 normal = Vector3.Cross(d - a, b - a).normalized;
                if (normal.magnitude < 0.001f)
                {
                    normal = Vector3.Cross(c - a, b - a).normalized;
                    if (normal.magnitude < 0.001f) normal = Vector3.up;
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
                Debug.LogError($"Error generating quad for solid {this.ID}: {ex.Message}");
            }
        }

        private void GeneratePolygon(ref VMFSide newSide, string[] vertices, string uaxis, string vaxis, Vector2 textureSize) {
            try
            {
                if (vertices.Length < 5) return;

                Vector3 firstVertex = VMFUtils.ParseVector(vertices[0]);
                Vector2 firstUV = this.CalculateUVsLocal(firstVertex, uaxis, vaxis, textureSize);

                Vector3 a = firstVertex;
                Vector3 b = VMFUtils.ParseVector(vertices[1]);
                Vector3 c = VMFUtils.ParseVector(vertices[2]);

                Vector3 normal = Vector3.Cross(c - a, b - a).normalized;

                if (normal.magnitude < 0.001f)
                {
                    if (vertices.Length >= 4)
                    {
                        Vector3 d = VMFUtils.ParseVector(vertices[3]);
                        normal = Vector3.Cross(d - a, c - a).normalized;
                    }

                    if (normal.magnitude < 0.001f) normal = Vector3.up;
                }

                for (int i = 1; i < vertices.Length - 1; i++)
                    try
                    {
                        Vector3 vertB = VMFUtils.ParseVector(vertices[i]);
                        Vector3 vertC = VMFUtils.ParseVector(vertices[i + 1]);

                        Vector2 bUV = this.CalculateUVsLocal(vertB, uaxis, vaxis, textureSize);
                        Vector2 cUV = this.CalculateUVsLocal(vertC, uaxis, vaxis, textureSize);

                        newSide.AddVertice(firstVertex, normal, firstUV);
                        newSide.AddVertice(vertB, normal, bUV);
                        newSide.AddVertice(vertC, normal, cUV);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"Error processing triangle {i} in polygon for solid {this.ID}: {ex.Message}");
                    }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error generating polygon for solid {this.ID}: {ex.Message}");
            }
        }

        private void ParseDisplacement(VMFDataBlock displacementData, ref VMFSide newSide, string[] rawVerts,
            string uaxis, string vaxis, Vector2 textureSize) {
            if (rawVerts.Length != 4)
            {
                Debug.LogError($"Malformed side in solid {this.ID}, displacement requires exactly 4 vertices");
                return;
            }

            if (!int.TryParse(displacementData.GetSingle("power") as string, out int power) || power < 2 || power > 4)
            {
                Debug.LogError($"Invalid displacement power in solid {this.ID}");
                return;
            }

            int resolution = (1 << power) + 1;

            Vector3 startPosition = Vector3.zero;
            if (displacementData.GetSingle("startposition") is string startPosStr) startPosition = VMFUtils.ParseVector(startPosStr);

            float elevation = 0f;
            if (displacementData.GetSingle("elevation") is string elevStr) float.TryParse(elevStr, out elevation);

            bool subdiv = false;
            if (displacementData.GetSingle("subdiv") is string subdivStr) subdiv = subdivStr == "1";

            float[][] normals = this.StringArrayToFloatParts(
                (displacementData.GetSingle("normals") as VMFDataBlock)?.GetAll<string>());

            float[][] distances = this.StringArrayToFloatParts(
                (displacementData.GetSingle("distances") as VMFDataBlock)?.GetAll<string>());

            float[][] offsets = this.StringArrayToFloatParts(
                (displacementData.GetSingle("offsets") as VMFDataBlock)?.GetAll<string>());

            float[][] offsetNormals = null;
            if (displacementData.GetSingle("offset_normals") is VMFDataBlock offsetNormalsBlock) offsetNormals = this.StringArrayToFloatParts(offsetNormalsBlock.GetAll<string>());

            if (normals.Length != resolution || distances.Length != resolution || offsets.Length != resolution)
            {
                Debug.LogError($"Displacement data in solid {this.ID} does not match expected resolution {resolution}");
                return;
            }

            Vector3 topLeft = VMFUtils.ParseVector(rawVerts[0]);
            Vector3 topRight = VMFUtils.ParseVector(rawVerts[1]);
            Vector3 bottomRight = VMFUtils.ParseVector(rawVerts[2]);
            Vector3 bottomLeft = VMFUtils.ParseVector(rawVerts[3]);

            if (startPosition != Vector3.zero)
            {
                Vector3[] corners = { bottomLeft, bottomRight, topRight, topLeft };
                float minDist = float.MaxValue;
                int startCornerIndex = 0;

                for (int i = 0; i < 4; i++)
                {
                    float dist = Vector3.SqrMagnitude(corners[i] - startPosition);
                    if (!(dist < minDist)) continue;

                    minDist = dist;
                    startCornerIndex = i;
                }

                if (startCornerIndex != 0)
                {
                    Vector3[] reordered = new Vector3[4];
                    for (int i = 0; i < 4; i++) reordered[i] = corners[(startCornerIndex + i) % 4];

                    bottomLeft = reordered[0];
                    bottomRight = reordered[1];
                    topRight = reordered[2];
                    topLeft = reordered[3];
                }
            }

            Vector3 faceNormal = Vector3.Cross(bottomLeft - topLeft, topRight - topLeft).normalized;

            Vector3[,] grid = new Vector3[resolution, resolution];
            Vector2[,] uvGrid = new Vector2[resolution, resolution];

            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    float u = x / (float)(resolution - 1);
                    float v = y / (float)(resolution - 1);

                    Vector3 origin = this.GetLerpedPoint(topLeft, topRight, bottomLeft, bottomRight, u, v);
                    Vector3 dispDirection = faceNormal;

                    if (y < normals.Length && x * 3 + 2 < normals[y].Length)
                    {
                        dispDirection = new Vector3(
                            normals[y][x * 3],
                            normals[y][x * 3 + 1],
                            normals[y][x * 3 + 2]
                        );

                        if (dispDirection.sqrMagnitude > 0.0001f) dispDirection.Normalize();
                    }

                    float distance = 0f;
                    if (y < distances.Length && x < distances[y].Length) distance = distances[y][x];

                    Vector3 offset = Vector3.zero;
                    if (y < offsets.Length && x * 3 + 2 < offsets[y].Length)
                        offset = new Vector3(
                            offsets[y][x * 3],
                            offsets[y][x * 3 + 1],
                            offsets[y][x * 3 + 2]
                        );

                    if (offsetNormals != null && y < offsetNormals.Length && x * 3 + 2 < offsetNormals[y].Length)
                    {
                        Vector3 offsetNormal = new Vector3(
                            offsetNormals[y][x * 3],
                            offsetNormals[y][x * 3 + 1],
                            offsetNormals[y][x * 3 + 2]
                        );
                        offset += offsetNormal;
                    }

                    Vector3 displacedVertex = origin + dispDirection * (distance + elevation) + offset;
                    Vector2 uv = this.CalculateUVsLocal(displacedVertex, uaxis, vaxis, textureSize);

                    grid[y, x] = displacedVertex;
                    uvGrid[y, x] = uv;
                }
            }

            Vector3[,] renderNormals = new Vector3[resolution, resolution];
            for (int y = 0; y < resolution; y++)
                for (int x = 0; x < resolution; x++)
                    renderNormals[y, x] = faceNormal;


            for (int y = 0; y < resolution - 1; y++)
            {
                for (int x = 0; x < resolution - 1; x++)
                {
                    Vector3 v00 = grid[y, x];
                    Vector3 v10 = grid[y + 1, x];
                    Vector3 v01 = grid[y, x + 1];
                    Vector3 v11 = grid[y + 1, x + 1];

                    Vector3 n1 = Vector3.Cross(v01 - v00, v10 - v00).normalized;
                    Vector3 n2 = Vector3.Cross(v10 - v11, v01 - v11).normalized;

                    if (Vector3.Dot(n1, faceNormal) < 0) n1 = -n1;
                    if (Vector3.Dot(n2, faceNormal) < 0) n2 = -n2;

                    Vector3 avgNormal = (n1 + n2).normalized;
                    renderNormals[y, x] += n1;
                    renderNormals[y + 1, x] += avgNormal;
                    renderNormals[y, x + 1] += avgNormal;
                    renderNormals[y + 1, x + 1] += n2;
                }
            }

            // NORMALS
            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                    if (renderNormals[y, x].sqrMagnitude > 0.0001f)
                        renderNormals[y, x].Normalize();
                    else
                        renderNormals[y, x] = faceNormal;
            }

            // TRIANGLE GEN
            for (int y = 0; y < resolution - 1; y++)
            {
                for (int x = 0; x < resolution - 1; x++)
                {
                    Vector3 v00 = grid[y, x];
                    Vector3 v01 = grid[y, x + 1];
                    Vector3 v10 = grid[y + 1, x];
                    Vector3 v11 = grid[y + 1, x + 1];

                    Vector3 n00 = renderNormals[y, x];
                    Vector3 n01 = renderNormals[y, x + 1];
                    Vector3 n10 = renderNormals[y + 1, x];
                    Vector3 n11 = renderNormals[y + 1, x + 1];

                    if (subdiv) // Four
                    {
                        Vector3 center = (v00 + v01 + v10 + v11) / 4f;
                        Vector3 centerNormal = (n00 + n01 + n10 + n11).normalized;
                        Vector2 centerUV = (uvGrid[y, x] + uvGrid[y, x + 1] +
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
        }

        private Vector3 GetLerpedPoint(Vector3 topLeft, Vector3 topRight, Vector3 bottomLeft,
            Vector3 bottomRight, float u, float v) {
            Vector3 topEdge = Vector3.Lerp(topLeft, topRight, u);
            Vector3 bottomEdge = Vector3.Lerp(bottomLeft, bottomRight, u);

            return Vector3.Lerp(bottomEdge, topEdge, v);
        }

        private float[][] StringArrayToFloatParts(string[] rows) {
            if (rows == null || rows.Length == 0) return Array.Empty<float[]>();

            return rows
                .Select(row => row.Split(' ')
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(s => float.TryParse(s, out float result) ? result : 0f)
                    .ToArray())
                .ToArray();
        }

        private Vector2 CalculateUVsLocal(
            Vector3 vertex,
            string uaxis,
            string vaxis,
            Vector2 textureSize
        ) {
            float[] uAxisData = VMFUtils.ParseAxis(uaxis);
            float[] vAxisData = VMFUtils.ParseAxis(vaxis);

            Vector3 uDirection = new Vector3(uAxisData[0], uAxisData[1], uAxisData[2]);
            float uTranslation = uAxisData[3];
            float uScale = uAxisData[4];

            Vector3 vDirection = new Vector3(vAxisData[0], vAxisData[1], vAxisData[2]);
            float vTranslation = vAxisData[3];
            float vScale = vAxisData[4];

            float u = Vector3.Dot(vertex, uDirection) / uScale + uTranslation;
            float v = Vector3.Dot(vertex, vDirection) / vScale + vTranslation;

            return new Vector2(u / textureSize.x, v / textureSize.y);
        }
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