using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace Scripts.BlockCulling
{
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation | MyUpdateOrder.AfterSimulation)]
    public class BlockCulling : MySessionComponentBase
    {
        private static BlockCulling Instance;

        private readonly Dictionary<IMyCubeGrid, HashSet<IMyCubeBlock>> _culledBlocks = new Dictionary<IMyCubeGrid, HashSet<IMyCubeBlock>>();
        private readonly HashSet<IMyCubeGrid> _unCulledGrids = new HashSet<IMyCubeGrid>();
        private readonly List<IMyCubeBlock> _queuedBlockCulls = new List<IMyCubeBlock>();
        private readonly TaskScheduler _taskScheduler = new TaskScheduler();
        private readonly PerformanceMonitor _performanceMonitor = new PerformanceMonitor();

        private const int MaxBlocksCulledPerTick = 100;

        public static event Action<IMyCubeGrid> OnGridCullingStarted;
        public static event Action<IMyCubeGrid> OnGridCullingCompleted;

        public override void LoadData()
        {
            if (MyAPIGateway.Utilities.IsDedicated)
                return;

            Instance = this;
            MyAPIGateway.Entities.OnEntityAdd += OnEntityAdd;
        }

        protected override void UnloadData()
        {
            if (MyAPIGateway.Utilities.IsDedicated)
                return;

            MyAPIGateway.Entities.OnEntityAdd -= OnEntityAdd;
            Instance = null;
        }

        public override void UpdateBeforeSimulation()
        {
            var stopwatch = Stopwatch.StartNew();

            _taskScheduler.ProcessTasks();
            ThreadSafeLog.ProcessLogQueue();

            stopwatch.Stop();
            _performanceMonitor.RecordSyncOperation(stopwatch.ElapsedMilliseconds);
            _performanceMonitor.Update();
        }

        public override void UpdateAfterSimulation()
        {
            var stopwatch = Stopwatch.StartNew();

            MainThreadDispatcher.Update();
            ProcessQueuedBlockCulls();
            UpdateGridCulling();

            stopwatch.Stop();
            _performanceMonitor.RecordSyncOperation(stopwatch.ElapsedMilliseconds);
        }

        private void ProcessQueuedBlockCulls()
        {
            int count = Math.Min(_queuedBlockCulls.Count, MaxBlocksCulledPerTick);
            for (int i = 0; i < count; i++)
            {
                _queuedBlockCulls[i].Visible = false;
            }
            _queuedBlockCulls.RemoveRange(0, count);
        }

        private void UpdateGridCulling()
        {
            Vector3D cameraPosition = MyAPIGateway.Session?.Camera?.Position ?? Vector3D.MaxValue;
            foreach (var grid in _culledBlocks.Keys)
            {
                if (grid.WorldAABB.Contains(cameraPosition) != ContainmentType.Contains)
                {
                    if (_unCulledGrids.Remove(grid))
                    {
                        _queuedBlockCulls.AddRange(_culledBlocks[grid]);
                    }
                }
                else if (_unCulledGrids.Add(grid))
                {
                    foreach (var block in _culledBlocks[grid])
                    {
                        block.Visible = true;
                    }
                }
            }
        }

        private void OnEntityAdd(IMyEntity entity)
        {
            var grid = entity as IMyCubeGrid;
            if (grid?.Physics == null)
                return;

            grid.OnBlockAdded += OnBlockPlace;
            grid.OnBlockRemoved += OnBlockRemove;
            grid.OnClose += OnGridRemove;

            _culledBlocks.Add(grid, new HashSet<IMyCubeBlock>());

            OnGridCullingStarted?.Invoke(grid);

            foreach (var block in grid.GetFatBlocks<IMyCubeBlock>())
            {
                SetTransparencyAsync(new SafeSlimBlockRef(block.SlimBlock));
            }
        }

        private void OnBlockPlace(IMySlimBlock slimBlock)
        {
            SetTransparencyAsync(new SafeSlimBlockRef(slimBlock));
        }

        private void OnBlockRemove(IMySlimBlock slimBlock)
        {
            foreach (var blockPos in GetSurfacePositions(slimBlock))
            {
                IMySlimBlock slimNeighbor = slimBlock.CubeGrid.GetCubeBlock(blockPos);
                if (slimNeighbor?.FatBlock != null)
                {
                    SetTransparencyAsync(new SafeSlimBlockRef(slimNeighbor), recursive: true, deepRecurse: false);
                }
            }
        }

        private void OnGridRemove(IMyEntity gridEntity)
        {
            var grid = gridEntity as IMyCubeGrid;
            if (grid != null)
            {
                _culledBlocks.Remove(grid);
                _unCulledGrids.Remove(grid);
                OnGridCullingCompleted?.Invoke(grid);
            }
        }

        private void SetTransparencyAsync(SafeSlimBlockRef slimBlockRef, bool recursive = true, bool deepRecurse = true)
        {
            _taskScheduler.EnqueueTask(() =>
            {
                var stopwatch = Stopwatch.StartNew();

                try
                {
                    IMySlimBlock slimBlock;
                    if (!slimBlockRef.TryGetSlimBlock(out slimBlock))
                        return;

                    IMyCubeBlock block = slimBlock.FatBlock;
                    if (!recursive && block == null)
                        return;

                    var blockSlimNeighbors = new HashSet<IMySlimBlock>();
                    bool shouldCullBlock = BlockEligibleForCulling(slimBlock, ref blockSlimNeighbors);

                    MainThreadDispatcher.Enqueue(() =>
                    {
                        if (block?.CubeGrid != null)
                        {
                            if (!_unCulledGrids.Contains(block.CubeGrid))
                                block.Visible = !shouldCullBlock;

                            if (shouldCullBlock)
                                _culledBlocks[block.CubeGrid].Add(block);
                            else
                                _culledBlocks[block.CubeGrid].Remove(block);
                        }
                    });

                    if (recursive)
                    {
                        foreach (var slimBlockN in blockSlimNeighbors)
                        {
                            SetTransparencyAsync(new SafeSlimBlockRef(slimBlockN), deepRecurse, false);
                        }
                    }
                }
                catch (Exception ex)
                {
                    ThreadSafeLog.EnqueueMessage($"Exception in SetTransparencyAsync: {ex}");
                }

                stopwatch.Stop();
                _performanceMonitor.RecordAsyncOperation(stopwatch.ElapsedMilliseconds);
            });
        }

        private bool BlockEligibleForCulling(IMySlimBlock slimBlock, ref HashSet<IMySlimBlock> blockSlimNeighbors)
        {
            if (blockSlimNeighbors == null)
                blockSlimNeighbors = new HashSet<IMySlimBlock>();

            foreach (var blockPos in GetSurfacePositions(slimBlock))
            {
                IMySlimBlock neighbor = slimBlock.CubeGrid.GetCubeBlock(blockPos);
                if (neighbor != null)
                    blockSlimNeighbors.Add(neighbor);
            }

            IMyCubeBlock block = slimBlock?.FatBlock;
            if (block == null) return false;

            List<IMySlimBlock> slimNeighborsContributor = new List<IMySlimBlock>();

            foreach (var slimNeighbor in blockSlimNeighbors)
            {
                int surroundingBlockCount = 0;
                foreach (Vector3I surfacePosition in GetSurfacePositions(slimNeighbor))
                    if (slimNeighbor.CubeGrid.CubeExists(surfacePosition))
                        surroundingBlockCount++;

                if (surroundingBlockCount != GetBlockFaceCount(slimNeighbor) && !ConnectsWithFullMountPoint(slimBlock, slimNeighbor))
                    return false;

                if (slimNeighbor.FatBlock == null || !(slimNeighbor.FatBlock is IMyLightingBlock || slimNeighbor.BlockDefinition.Id.SubtypeName.Contains("Window")))
                    slimNeighborsContributor.Add(slimNeighbor);
            }

            return slimNeighborsContributor.Count == GetBlockFaceCount(block);
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

        private Vector3I[] _surfacePositions = new Vector3I[6];
        private Vector3I[] GetSurfacePositions(IMySlimBlock block)
        {
            Vector3I blockSize = Vector3I.Abs(block.Max - block.Min) + Vector3I.One;

            int faceCount = 2 * (blockSize.X * blockSize.Y + blockSize.Y * blockSize.Z + blockSize.Z * blockSize.X);
            if (_surfacePositions.Length != faceCount)
                _surfacePositions = new Vector3I[faceCount];

            int idx = 0;

            for (int x = -1; x <= blockSize.X; x++)
            {
                for (int y = -1; y <= blockSize.Y; y++)
                {
                    for (int z = -1; z <= blockSize.Z; z++)
                    {
                        bool xLimit = (x == -1 || x == blockSize.X);
                        bool yLimit = (y == -1 || y == blockSize.Y);
                        bool zLimit = (z == -1 || z == blockSize.Z);
                        if ((!xLimit && yLimit ^ zLimit) || (xLimit && !(yLimit || zLimit)))
                            _surfacePositions[idx++] = block.Min + new Vector3I(x, y, z);
                    }
                }
            }

            return _surfacePositions;
        }
    }
    public class SafeSlimBlockRef
    {
        private long _entityId;
        private Vector3I _position;

        public SafeSlimBlockRef(IMySlimBlock slimBlock)
        {
            SetSlimBlock(slimBlock);
        }

        public void SetSlimBlock(IMySlimBlock slimBlock)
        {
            if (slimBlock != null)
            {
                Interlocked.Exchange(ref _entityId, slimBlock.CubeGrid.EntityId);
                _position = slimBlock.Position;
            }
            else
            {
                Interlocked.Exchange(ref _entityId, 0);
                _position = Vector3I.Zero;
            }
        }

        public bool TryGetSlimBlock(out IMySlimBlock slimBlock)
        {
            slimBlock = null;
            IMyCubeGrid grid = MyAPIGateway.Entities.GetEntityById(Interlocked.Read(ref _entityId)) as IMyCubeGrid;
            if (grid != null)
            {
                slimBlock = grid.GetCubeBlock(_position);
            }
            return slimBlock != null;
        }
    }

}