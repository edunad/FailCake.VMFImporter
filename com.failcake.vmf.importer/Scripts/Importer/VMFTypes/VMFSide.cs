#region

using System.Collections.Generic;
using UnityEngine;

#endregion

namespace FailCake.VMF
{
    internal class VMFSide
    {
        public string material;

        public int materialIndex;
        public int textureIndex;

        public bool isDisplacement;

        public List<Vertex> vertices = new List<Vertex>();

        public void AddVertice(Vector3 pos, Vector3 normal, Vector2 uv) {
            this.vertices.Add(new Vertex(pos, normal, uv));
        }

        public List<int> GetTriangles() {
            List<int> triangles = new List<int>();

            if (this.vertices.Count % 6 == 0 && this.isDisplacement)
                for (int i = 0; i < this.vertices.Count; i += 3)
                {
                    triangles.Add(i + 2);
                    triangles.Add(i + 1);
                    triangles.Add(i);
                }
            else
                for (int i = 1; i < this.vertices.Count - 1; i++)
                {
                    triangles.Add(0);
                    triangles.Add(i + 1);
                    triangles.Add(i);
                }

            return triangles;
        }
    }
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