using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sandbox.Game.Entities.Cube;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Block_Culling_Test.Data.Scripts.BlockCulling
{
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    public class BlockCullingSession : MySessionComponentBase
    {
        #region Base Methods

        public override void LoadData()
        {
            if (MyAPIGateway.Utilities.IsDedicated)
                return;

            MyAPIGateway.Entities.OnEntityAdd += OnEntityAdd;
            MyAPIGateway.Utilities.ShowMessage("Block Culler", "Loaded.");
        }

        protected override void UnloadData()
        {
            if (MyAPIGateway.Utilities.IsDedicated)
                return;

            MyAPIGateway.Entities.OnEntityAdd -= OnEntityAdd;
        }

        #endregion

        #region Actions

        private void OnEntityAdd(IMyEntity entity)
        {
            var grid = entity as IMyCubeGrid;
            if (grid?.Physics == null)
                return;

            grid.OnBlockAdded += OnBlockPlace;
            grid.OnBlockRemoved += OnBlockRemove;

            foreach (var block in grid.GetFatBlocks<IMyCubeBlock>())
                SetTransparency(block.SlimBlock);
        }

        private void OnBlockPlace(IMySlimBlock slimBlock)
        {
            SetTransparency(slimBlock);
        }

        private void OnBlockRemove(IMySlimBlock slimBlock)
        {
            foreach (var blockPos in GetSurfacePositions(slimBlock))
            {
                IMySlimBlock slimNeighbor = slimBlock.CubeGrid.GetCubeBlock(blockPos);
                if (slimNeighbor?.FatBlock != null)
                    slimNeighbor.FatBlock.Visible = true;
            }
        }

        #endregion

        #region Private Methods

        private void SetTransparency(IMySlimBlock slimBlock, bool recursive = true)
        {
            IMyCubeBlock block = slimBlock.FatBlock;
            if (!recursive && block == null)
                return; // No logic would run so just return early

            var blockSlimNeighbors = new List<IMySlimBlock>();

            foreach (var blockPos in GetSurfacePositions(slimBlock))
            {
                IMySlimBlock slimNeighbor = slimBlock.CubeGrid.GetCubeBlock(blockPos);
                if (slimNeighbor == null)
                    continue;
                if (slimNeighbor.FatBlock == null || !(slimNeighbor.FatBlock is IMyLightingBlock)) // Limit to slimblocks and fatblocks with physics
                    blockSlimNeighbors.Add(slimNeighbor);
            }

            if (block != null) // Only set fatblock visiblity to false
                block.Visible = blockSlimNeighbors.Count != GetBlockFaceCount(block);

            if (recursive) // Do set nearby blocks visibility to false.
                foreach (var slimBlockN in blockSlimNeighbors.Where(slimBlockN => slimBlockN.FatBlock != null))
                    SetTransparency(slimBlockN, false);
        }

        private int GetBlockFaceCount(IMyCubeBlock block)
        {
            Vector3I blockSize = Vector3I.Abs(block.Max - block.Min) + Vector3I.One;
            return 2 * (blockSize.X * blockSize.Y + blockSize.Y*blockSize.Z + blockSize.Z*blockSize.X);
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

        #endregion
        }
}
