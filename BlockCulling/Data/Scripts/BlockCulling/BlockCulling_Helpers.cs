using System.Collections.Generic;
using System.Linq;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;

namespace Scripts.BlockCulling
{
    partial class BlockCulling
    {
        private void SetTransparency(IMySlimBlock slimBlock, bool recursive = true, bool deepRecurse = true)
        {
            IMyCubeBlock block = slimBlock.FatBlock;
            if (!recursive && block == null) // Don't do a non-recursive scan on slimblocks
                return; // No logic would run so just return early

            var blockSlimNeighbors = new List<IMySlimBlock>();
            bool shouldCullBlock = BlockEligibleForCulling(slimBlock, ref blockSlimNeighbors);

            // Add to cache for making blocks invisible when inside grid's WorldAABB
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
                foreach (var slimBlockN in blockSlimNeighbors)
                    SetTransparency(slimBlockN, deepRecurse, false);
        }

        private bool BlockEligibleForCulling(IMySlimBlock slimBlock, ref List<IMySlimBlock> blockSlimNeighbors)
        {
            if (blockSlimNeighbors == null)
                blockSlimNeighbors = new List<IMySlimBlock>();
            foreach (var blockPos in GetSurfacePositions(slimBlock))
            {
                IMySlimBlock neighbor = slimBlock.CubeGrid.GetCubeBlock(blockPos);
                if (neighbor != null)
                    blockSlimNeighbors.Add(neighbor);
            }

            IMyCubeBlock block = slimBlock?.FatBlock;
            if (block == null) return false;

            List<IMySlimBlock> slimNeighborsContributor = blockSlimNeighbors.ToList();
            blockSlimNeighbors.Clear();

            foreach (var slimNeighbor in blockSlimNeighbors)
            {
                if (IsBlockExposed(slimNeighbor) && !ConnectsWithFullMountPoint(slimBlock, slimNeighbor)) // If any neighbor block is exposed, check if this block is completely occluded.
                    continue;

                if (slimNeighbor.FatBlock == null || !(slimNeighbor.FatBlock is IMyLightingBlock || slimNeighbor.BlockDefinition.Id.SubtypeName.Contains("Window"))) // Limit to slimblocks and fatblocks with physics (and not windows)
                    slimNeighborsContributor.Add(slimNeighbor);
            }

            return slimNeighborsContributor.Count == GetBlockFaceCount(block);
        }

        private bool IsBlockExposed(IMySlimBlock block)
        {
            int surroundingBlockCount = 0;
            foreach (Vector3I surfacePosition in GetSurfacePositions(block))
                if (block.CubeGrid.CubeExists(surfacePosition))
                    surroundingBlockCount++;

            return surroundingBlockCount != GetBlockFaceCount(block);
        }

        private bool ConnectsWithFullMountPoint(IMySlimBlock thisBlock, IMySlimBlock slimNeighbor)
        {
            Quaternion slimNeighborRotation;
            slimNeighbor.Orientation.GetQuaternion(out slimNeighborRotation);

            foreach (var mountPoint in ((MyCubeBlockDefinition)slimNeighbor.BlockDefinition).MountPoints)
            {
                if (!Vector3I.BoxContains(thisBlock.Min, thisBlock.Max, (Vector3I)(slimNeighborRotation * mountPoint.Normal + slimNeighbor.Position)))
                    continue;

                Vector3I mountSize = Vector3I.Abs(Vector3I.Round(mountPoint.End - mountPoint.Start));
                if (mountSize.X + mountSize.Y + mountSize.Z == 2)
                    return true;
            }

            return false;
        }

        private int GetBlockFaceCount(IMySlimBlock block)
        {
            Vector3I blockSize = Vector3I.Abs(block.Max - block.Min) + Vector3I.One;
            return 2 * (blockSize.X * blockSize.Y + blockSize.Y * blockSize.Z + blockSize.Z * blockSize.X);
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
                            surfacePositions.Add(block.Min + new Vector3I(x, y, z));
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
