using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.Game;
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
        private readonly List<IMyCubeBlock> _queuedBlockUnculls = new List<IMyCubeBlock>();
        private readonly TaskScheduler _taskScheduler = new TaskScheduler();
        private readonly PerformanceMonitor _performanceMonitor = new PerformanceMonitor();
        private const int MaxBlocksCulledPerTick = 100;
        public static event Action<IMyCubeGrid> OnGridCullingStarted;
        public static event Action<IMyCubeGrid> OnGridCullingCompleted;
        private int _reportCounter = 0;
        private const int REPORT_INTERVAL = 600;
        private ModConfig modConfig;
        public bool ModEnabled => modConfig?.ModEnabled ?? false;

        public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
        {
            base.Init(sessionComponent);
            MyAPIGateway.Utilities.MessageEntered += OnMessageEntered;
        }

        private void OnMessageEntered(string messageText, ref bool sendToOthers)
        {
            if (messageText.StartsWith("/bcdebug"))
            {
                ThreadSafeLog.EnableDebugLogging = !ThreadSafeLog.EnableDebugLogging;
                if (ThreadSafeLog.EnableDebugLogging)
                {
                    ThreadSafeLog.EnqueueMessage($"Debug logging enabled at {DateTime.Now}");
                }
                MyAPIGateway.Utilities.ShowMessage("Block Culling", $"Debug logging {(ThreadSafeLog.EnableDebugLogging ? "enabled" : "disabled")}");
                sendToOthers = false;
            }
            else if (messageText.StartsWith("/bcenable"))
            {
                modConfig.ModEnabled = true;
                modConfig.Save();
                MyAPIGateway.Utilities.ShowMessage("BlockCulling", "Block culling enabled!");
                sendToOthers = false;
            }
            else if (messageText.StartsWith("/bcdisable"))
            {
                modConfig.ModEnabled = false;
                modConfig.Save();
                MyAPIGateway.Utilities.ShowMessage("BlockCulling", "Block culling disabled!");
                sendToOthers = false;
            }
            else if (messageText.StartsWith("/bcstatus"))
            {
                string status = modConfig.ModEnabled ? "enabled" : "disabled";
                MyAPIGateway.Utilities.ShowMessage("BlockCulling", $"Block culling is {status}");
                sendToOthers = false;
            }
        }

        private bool _isClient = false;  // Add as a class field

        public override void LoadData()
        {
            if (MyAPIGateway.Session.IsServer && MyAPIGateway.Utilities.IsDedicated) return;
            _isClient = true;  // HERE: Set a flag that we are, in fact, a client.

            Instance = this;
            MyAPIGateway.Entities.OnEntityAdd += OnEntityAdd;
            modConfig = ModConfig.Load();
            ThreadSafeLog.EnqueueMessage($"Block Culling mod loaded. Enabled: {modConfig.ModEnabled}");
        }

        protected override void UnloadData()
        {
            if (MyAPIGateway.Session.IsServer && MyAPIGateway.Utilities.IsDedicated) return;
            MyAPIGateway.Utilities.MessageEntered -= OnMessageEntered;
            MyAPIGateway.Entities.OnEntityAdd -= OnEntityAdd;
            ThreadSafeLog.Close();
            Instance = null;
        }

        public override void UpdateBeforeSimulation()
        {

            if (modConfig == null || !modConfig.ModEnabled) return;

            try
            {
                var stopwatch = Stopwatch.StartNew();
                _taskScheduler?.ProcessTasks();
                if (ThreadSafeLog.EnableDebugLogging)
                {
                    ThreadSafeLog.ProcessLogQueue();
                }
                stopwatch.Stop();
                _performanceMonitor?.RecordSyncOperation(stopwatch.ElapsedMilliseconds);
                _performanceMonitor?.Update();
                if (ThreadSafeLog.EnableDebugLogging)
                {
                    _reportCounter++;
                    if (_reportCounter >= REPORT_INTERVAL)
                    {
                        GenerateCulledBlocksReport();
                        _reportCounter = 0;
                    }
                }
            }
            catch (Exception e)
            {
                ThreadSafeLog.EnqueueMessage($"Exception in UpdateBeforeSimulation: {e}");
            }
        }

        public override void UpdateAfterSimulation()
        {
            if (modConfig == null)
            {
                ThreadSafeLog.EnqueueMessage("modConfig is null in UpdateAfterSimulation.");
                return;
            }

            if (!modConfig.ModEnabled) return;

            var stopwatch = Stopwatch.StartNew();

            // Check if MainThreadDispatcher is null and log a message if it is
            try
            {
                MainThreadDispatcher.Update();
            }
            catch (Exception e)
            {
                ThreadSafeLog.EnqueueMessage($"Exception in MainThreadDispatcher.Update: {e}");
            }

            // Check and handle exceptions in ProcessQueuedBlockCulls
            try
            {
                ProcessQueuedBlockCulls();
            }
            catch (Exception e)
            {
                ThreadSafeLog.EnqueueMessage($"Exception in ProcessQueuedBlockCulls: {e}");
            }

            // Check and handle exceptions in UpdateGridCulling
            try
            {
                UpdateGridCulling();
            }
            catch (Exception e)
            {
                ThreadSafeLog.EnqueueMessage($"Exception in UpdateGridCulling: {e}");
            }

            stopwatch.Stop();

            // Check if _performanceMonitor is null and log a message if it is
            if (_performanceMonitor == null)
            {
                ThreadSafeLog.EnqueueMessage("_performanceMonitor is null in UpdateAfterSimulation.");
            }
            else
            {
                _performanceMonitor.RecordSyncOperation(stopwatch.ElapsedMilliseconds);
            }
        }

        private void ProcessQueuedBlockCulls()
        {
            int maxProcessPerTick = MaxBlocksCulledPerTick;
            int cullCount = Math.Min(_queuedBlockCulls.Count, maxProcessPerTick / 2);
            for (int i = 0; i < cullCount; i++)
            {
                _queuedBlockCulls[i].Visible = false;
            }
            _queuedBlockCulls.RemoveRange(0, cullCount);
            int uncullCount = Math.Min(_queuedBlockUnculls.Count, maxProcessPerTick - cullCount);
            for (int i = 0; i < uncullCount; i++)
            {
                _queuedBlockUnculls[i].Visible = true;
            }
            _queuedBlockUnculls.RemoveRange(0, uncullCount);
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
                else
                {
                    if (_unCulledGrids.Add(grid))
                    {
                        foreach (var block in _culledBlocks[grid])
                        {
                            if (!block.Visible) _queuedBlockUnculls.Add(block);
                        }
                    }
                }
            }
        }

        private void GenerateCulledBlocksReport()
        {
            if (!ThreadSafeLog.EnableDebugLogging) return;

            int totalCulledBlocks = 0;
            int totalGrids = 0;
            StringBuilder report = new StringBuilder("BLOCK CULLING REPORT:\n");
            foreach (var gridBlocks in _culledBlocks)
            {
                if (gridBlocks.Key?.EntityId != 0)
                {
                    int culledCount = gridBlocks.Value.Count;
                    if (culledCount > 0)
                    {
                        totalGrids++;
                        totalCulledBlocks += culledCount;
                        report.AppendFormat("• Grid {0}: {1} blocks culled\n", gridBlocks.Key.EntityId, culledCount);
                    }
                }
            }
            report.AppendFormat("TOTAL: {0} blocks across {1} grids", totalCulledBlocks, totalGrids);
            ThreadSafeLog.EnqueueMessageDebug(report.ToString());
        }

        private void OnEntityAdd(IMyEntity entity)
        {
            var grid = entity as IMyCubeGrid;
            if (grid?.Physics == null) return;
            if (_culledBlocks.ContainsKey(grid))
            {
                ThreadSafeLog.EnqueueMessageDebug($"Grid {grid.EntityId} already exists. Re-initializing...");
                _culledBlocks[grid].Clear();
            }
            else
            {
                _culledBlocks.Add(grid, new HashSet<IMyCubeBlock>());
            }
            grid.OnBlockAdded += OnBlockPlace;
            grid.OnBlockRemoved += OnBlockRemove;
            grid.OnClose += OnGridRemove;
            OnGridCullingStarted?.Invoke(grid);
            foreach (var block in grid.GetFatBlocks<IMyCubeBlock>())
            {
                SetTransparencyAsync(new SafeSlimBlockRef(block.SlimBlock), recursive: true, deepRecurse: true);
            }
            ThreadSafeLog.EnqueueMessageDebug($"Started culling for Grid {grid.EntityId} ({grid.GetFatBlocks<IMyCubeBlock>().Count()} fat blocks)");
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
            ThreadSafeLog.EnqueueMessageDebug($"Grid {grid.EntityId} removed, stopped culling");
        }

        private void SetTransparencyAsync(SafeSlimBlockRef slimBlockRef, bool recursive = true, bool deepRecurse = true)
        {
            _taskScheduler.EnqueueTask(() =>
            {
                var stopwatch = Stopwatch.StartNew();
                try
                {
                    IMySlimBlock slimBlock;
                    if (!slimBlockRef.TryGetSlimBlock(out slimBlock)) return;
                    IMyCubeBlock block = slimBlock.FatBlock;
                    if (!recursive && block == null) return;
                    var blockSlimNeighbors = new HashSet<IMySlimBlock>();
                    bool shouldCullBlock = BlockEligibleForCulling(slimBlock, ref blockSlimNeighbors);
                    MainThreadDispatcher.Enqueue(() =>
                    {
                        if (block?.CubeGrid != null)
                        {
                            if (!_unCulledGrids.Contains(block.CubeGrid)) block.Visible = !shouldCullBlock;
                            if (shouldCullBlock) _culledBlocks[block.CubeGrid].Add(block);
                            else _culledBlocks[block.CubeGrid].Remove(block);
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
            if (blockSlimNeighbors == null) blockSlimNeighbors = new HashSet<IMySlimBlock>();
            foreach (var blockPos in GetSurfacePositions(slimBlock))
            {
                IMySlimBlock neighbor = slimBlock.CubeGrid.GetCubeBlock(blockPos);
                if (neighbor != null) blockSlimNeighbors.Add(neighbor);
            }
            IMyCubeBlock block = slimBlock?.FatBlock;
            if (block == null) return false;
            List<IMySlimBlock> slimNeighborsContributor = new List<IMySlimBlock>();
            foreach (var slimNeighbor in blockSlimNeighbors)
            {
                int surroundingBlockCount = 0;
                foreach (Vector3I surfacePosition in GetSurfacePositions(slimNeighbor))
                    if (slimNeighbor.CubeGrid.CubeExists(surfacePosition)) surroundingBlockCount++;
                if (surroundingBlockCount != GetBlockFaceCount(slimNeighbor) && !ConnectsWithFullMountPoint(slimBlock, slimNeighbor)) return false;
                if (slimNeighbor.FatBlock == null || !(slimNeighbor.FatBlock is IMyLightingBlock || slimNeighbor.BlockDefinition.Id.SubtypeName.Contains("Window"))) slimNeighborsContributor.Add(slimNeighbor);
            }
            bool result = slimNeighborsContributor.Count == GetBlockFaceCount(block);
            return result;
        }

        private bool ConnectsWithFullMountPoint(IMySlimBlock thisBlock, IMySlimBlock slimNeighbor)
        {
            Quaternion slimNeighborRotation;
            slimNeighbor.Orientation.GetQuaternion(out slimNeighborRotation);
            foreach (var mountPoint in ((MyCubeBlockDefinition)slimNeighbor.BlockDefinition).MountPoints)
            {
                if (!Vector3I.BoxContains(thisBlock.Min, thisBlock.Max, (Vector3I)(slimNeighborRotation * mountPoint.Normal + slimNeighbor.Position))) continue;
                Vector3I mountSize = Vector3I.Abs(Vector3I.Round(mountPoint.End - mountPoint.Start));
                if (mountSize.X + mountSize.Y + mountSize.Z == 2) return true;
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
            if (_surfacePositions.Length != faceCount) _surfacePositions = new Vector3I[faceCount];
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
                        if ((!xLimit && yLimit ^ zLimit) || (xLimit && !(yLimit || zLimit))) _surfacePositions[idx++] = block.Min + new Vector3I(x, y, z);
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
