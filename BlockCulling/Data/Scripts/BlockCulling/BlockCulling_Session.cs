using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
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
        /// List of all temporarily un-culled grids, set by distance.
        /// </summary>
        private readonly HashSet<IMyCubeGrid> _unCulledGrids = new HashSet<IMyCubeGrid>();

        /// <summary>
        /// Making blocks invisible is expensive; this spreads it over several ticks.
        /// </summary>
        private readonly List<IMyCubeBlock> _queuedBlockCulls = new List<IMyCubeBlock>();

        private const int MaxBlocksCulledPerTick = 100;

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
        private Vector3D _cameraPosition;
        public override void Draw()
        {
            // Making blocks invisible is expensive; this spreads it over several ticks.
            if (_queuedBlockCulls.Count > 0)
            {
                int i = 0;
                for (; i < _queuedBlockCulls.Count && i < MaxBlocksCulledPerTick; i++)
                {
                    _queuedBlockCulls[i].Visible = false;
                }
                _queuedBlockCulls.RemoveRange(0, i);
            }

            _drawTicks++;
            if (MyAPIGateway.Utilities.IsDedicated || _drawTicks < 13) // Only check every 1/4 second
                return;
            _drawTicks = 0;

            _cameraPosition = MyAPIGateway.Session?.Camera?.Position ?? Vector3D.MaxValue;
            foreach (var grid in _culledBlocks.Keys)
            {
                // Re-cull blocks if within grid's WorldAABB
                if (grid.WorldAABB.Contains(_cameraPosition) != ContainmentType.Contains)
                {
                    if (_unCulledGrids.Remove(grid))
                        _queuedBlockCulls.AddRange(_culledBlocks[grid]);
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
            grid.OnClose += OnGridRemove;

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

        private void OnGridRemove(IMyEntity gridEntity)
        {
            _culledBlocks.Remove((IMyCubeGrid) gridEntity);
            _unCulledGrids.Remove((IMyCubeGrid) gridEntity);
        }

        #endregion
        }
}