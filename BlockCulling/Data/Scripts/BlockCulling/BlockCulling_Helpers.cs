using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;

namespace Scripts.BlockCulling
{
    partial class BlockCulling
    {
        // TODO: Check for full mountpoints OR invisibility on neighboring blocks for culling.

        private void SetTransparency(IMySlimBlock slimBlock, bool recursive = true)
        {
            IMyCubeBlock block = slimBlock.FatBlock;
            if (!recursive && block == null) // Don't do a non-recursive scan on slimblocks
                return; // No logic would run so just return early

            var blockSlimNeighbors = new List<IMySlimBlock>();
            bool shouldCullBlock = BlockEligibleForCulling(slimBlock, blockSlimNeighbors);

            if (block != null) // Only set fatblock visiblity to false
            {
                if (!_unCulledGrids.Contains(block.CubeGrid)) // Only set blocks to be invisible if grid is being culled
                    block.Visible = !shouldCullBlock;

                if (shouldCullBlock)
                    _culledBlocks[block.CubeGrid].Add(block);
                else
                    _culledBlocks[block.CubeGrid].Remove(block);
            }

            if (recursive) // Do set nearby blocks visibility to false.
                foreach (var slimBlockN in blockSlimNeighbors.Where(slimBlockN => slimBlockN.FatBlock != null))
                    SetTransparency(slimBlockN, false);
        }

        private bool BlockEligibleForCulling(IMySlimBlock slimBlock, List<IMySlimBlock> blockSlimNeighbors = null)
        {
            IMyCubeBlock block = slimBlock?.FatBlock;
            if (block == null) return false;

            if (blockSlimNeighbors == null)
                blockSlimNeighbors = new List<IMySlimBlock>();

            foreach (var blockPos in GetSurfacePositions(slimBlock))
            {
                IMySlimBlock slimNeighbor = slimBlock.CubeGrid.GetCubeBlock(blockPos);
                if (slimNeighbor == null)
                    continue;
                if (slimNeighbor.FatBlock == null || !(slimNeighbor.FatBlock is IMyLightingBlock || slimNeighbor.BlockDefinition.Id.SubtypeName.Contains("Window"))) // Limit to slimblocks and fatblocks with physics (and not windows)
                    blockSlimNeighbors.Add(slimNeighbor);
            }

            return blockSlimNeighbors.Count == GetBlockFaceCount(block);
        }

        private int GetBlockFaceCount(IMyCubeBlock block)
        {
            Vector3I blockSize = Vector3I.Abs(block.Max - block.Min) + Vector3I.One;
            return 2 * (blockSize.X * blockSize.Y + blockSize.Y * blockSize.Z + blockSize.Z * blockSize.X);
        }

        private Vector3I[] GetSurfacePositions(IMySlimBlock block)
        {
            List<Vector3I> surfacePositions = new List<Vector3I>();
            Vector3I blockSize = Vector3I.Abs(block.Max - block.Min) + Vector3I.One;
            Vector3I min = block.Min;

            for (int x = -1; x <= blockSize.X; x++)
            {
                for (int y = -1; y <= blockSize.Y; y++)
                {
                    for (int z = -1; z <= blockSize.Z; z++)
                    {
                        // One x, y, z should be at the outside edge of the block. Otherwise don't add to array.
                        bool xLimit = (x == -1 || x == blockSize.X);
                        bool yLimit = (y == -1 || y == blockSize.Y);
                        bool zLimit = (z == -1 || z == blockSize.Z);
                        if (TernaryXor(xLimit, yLimit, zLimit)) // Avoid checking positions inside the block.
                            surfacePositions.Add(min + new Vector3I(x, y, z));
                    }
                }
            }

            return surfacePositions.ToArray();
        }

        public static bool TernaryXor(bool a, bool b, bool c)
        {
            return (!a && (b ^ c)) || (a && !(b || c));
        }
    }
}
