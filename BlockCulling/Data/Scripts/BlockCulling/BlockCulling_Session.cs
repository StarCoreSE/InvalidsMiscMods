using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Scripts.BlockCulling
{
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    public partial class BlockCulling : MySessionComponentBase
    {
        /// <summary>
        /// Map of all culled blocks, used for temporary distance unculling.
        /// </summary>
        private readonly Dictionary<IMyCubeGrid, HashSet<IMyCubeBlock>> _culledBlocks = new Dictionary<IMyCubeGrid, HashSet<IMyCubeBlock>>();
        /// <summary>
        /// Map of all temporarily unculled grids, set by distance.
        /// </summary>
        private readonly HashSet<IMyCubeGrid> _unCulledGrids = new HashSet<IMyCubeGrid>();

        #region Base Methods

        public override void LoadData()
        {
            if (MyAPIGateway.Utilities.IsDedicated)
                return;

            MyAPIGateway.Entities.OnEntityAdd += OnEntityAdd;
        }

        protected override void UnloadData()
        {
            if (MyAPIGateway.Utilities.IsDedicated)
                return;

            MyAPIGateway.Entities.OnEntityAdd -= OnEntityAdd;
        }

        private int _drawTicks;
        public override void Draw()
        {
            _drawTicks++;
            if (MyAPIGateway.Utilities.IsDedicated || _drawTicks % 13 != 0) // Only check every 1/4 second
                return;

            Vector3D cameraPosition = MyAPIGateway.Session?.Camera?.Position ?? Vector3D.MaxValue;
            foreach (var grid in _culledBlocks.Keys) // TODO uncomment
            {
                // Recull blocks if within grid's WorldAABB
                if (grid.WorldAABB.Contains(cameraPosition) != ContainmentType.Contains)
                {
                    if (_unCulledGrids.Remove(grid))
                        foreach (var block in _culledBlocks[grid])
                            block.Visible = false;
                    continue;
                }
            
                // Set blocks to visible if not already done
                if (!_unCulledGrids.Add(grid))
                    continue;
                foreach (var block in _culledBlocks[grid])
                    block.Visible = true;
            }
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
            grid.OnClose += gridEntity =>
            {
                _culledBlocks.Remove(grid);
                _unCulledGrids.Remove(grid);
            };

            _culledBlocks.Add(grid, new HashSet<IMyCubeBlock>());

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
                {
                    SetTransparency(slimNeighbor, true, false);
                }
            }
        }

        #endregion
        }
}
