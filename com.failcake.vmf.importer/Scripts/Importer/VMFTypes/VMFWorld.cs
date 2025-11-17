#region

using System;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

#endregion

namespace FailCake.VMF
{
    internal class VMFWorld
    {
        #region PRIVATE

        private readonly VMFDataBlock _worldBlock;

        private readonly List<VMFDataBlock> _root;
        private readonly List<VMFDataBlock> _solids;
        private readonly List<VMFDataBlock> _entitySolids;
        private readonly List<VMFDataBlock> _entities;

        #endregion

        public VMFWorld(List<VMFDataBlock> root) {
            this._root = root ?? throw new UnityException("Root VMF data cannot be null");

            try
            {
                this._worldBlock = this._root.Where(data => data.ID == "world").FirstOrDefault();
                if (this._worldBlock == null) throw new UnityException("World block not found in VMF data - invalid VMF format");

                this._solids = this._worldBlock.GetAllBlocks("solid").ToList();
                this._entities = this._root.Where(data => data.ID == "entity").ToList();
                this._entitySolids = this.ProcessEntitySolids();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error initializing VMF world: {ex.Message}");
                throw;
            }
        }

        private List<VMFDataBlock> ProcessEntitySolids() {
            List<VMFDataBlock> entitySolids = new List<VMFDataBlock>();

            foreach (VMFDataBlock entity in this._entities)
                try
                {
                    string classname = entity.GetSingle("classname") as string ?? "unknown_entity";
                    entity.ID = classname;

                    VMFDataBlock[] solids = entity.GetAllBlocks("solid");
                    foreach (VMFDataBlock solid in solids)
                        if (solid != null)
                        {
                            solid.ID = classname;
                            entitySolids.Add(solid);
                        }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Error processing entity solids: {ex.Message}");
                }

            return entitySolids;
        }

        public List<VMFDataBlock> GetSolids() {
            return this._solids;
        }

        public List<VMFDataBlock> GetEntitySolids() {
            return this._entitySolids;
        }

        public List<VMFDataBlock> GetEntities() {
            return this._entities;
        }

        public void LoadWorldTextures(bool includeFuncEntities) {
            try
            {
                List<VMFDataBlock> solidsToProcess = new List<VMFDataBlock>(this.GetSolids());
                if (includeFuncEntities && this._entitySolids?.Count > 0) solidsToProcess.AddRange(this._entitySolids);

                HashSet<string> materialNames = this.ExtractMaterialNames(solidsToProcess);
                VMFTextures.PreloadTextures(materialNames);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error loading world textures: {ex.Message}");
            }
        }

        private HashSet<string> ExtractMaterialNames(List<VMFDataBlock> solids) {
            HashSet<string> materialNames = new HashSet<string>();

            foreach (VMFDataBlock solid in solids)
            {
                if (solid == null) continue;

                try
                {
                    List<VMFDataBlock> sides = this.GetSolidSides(solid);
                    foreach (VMFDataBlock side in sides)
                    {
                        if (side == null) continue;

                        string material = side.GetSingle("material") as string;
                        if (!string.IsNullOrEmpty(material)) materialNames.Add(material);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Error extracting materials from solid: {ex.Message}");
                }
            }

            return materialNames;
        }

        #region PRIVATE

        private List<VMFDataBlock> GetSolidSides(VMFDataBlock solid) {
            if (solid == null) return new List<VMFDataBlock>();

            try
            {
                return solid.GetAllBlocks("side").ToList();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error getting sides from solid: {ex.Message}");
                return new List<VMFDataBlock>();
            }
        }

        #endregion
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