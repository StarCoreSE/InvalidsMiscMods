using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Weapons;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using ShipyardMod.ItemClasses;
using ShipyardMod.Utility;
using VRage;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace ShipyardMod.ProcessHandlers
{
    public class ProcessShipyardAction : ProcessHandlerBase
    {
        private static readonly string FullName = typeof(ProcessShipyardAction).FullName;

        private readonly HashSet<BlockTarget> _stalledTargets = new HashSet<BlockTarget>();

        private readonly MyInventory _tmpInventory = new MyInventory();

        public override int GetUpdateResolution()
        {
            return 5000;
        }

        public override bool ServerOnly()
        {
            return true;
        }

        public override void Handle()
        {
            if (ProcessShipyardDetection.ShipyardsList.Count == 0)
            {
                Logging.Instance.WriteDebug("[ProcessConveyorCache] No shipyards to process");
                return;
            }
            foreach (ShipyardItem item in ProcessShipyardDetection.ShipyardsList)
            {
                item.ProcessDisable();
                var startBlock = Profiler.Start(FullName, nameof(Handle), "Target Detection");
                //see if the shipyard has been deleted
                //just disable the yard and skip it, the ShipyardHandler will take care of deleting the item
                if (item.YardEntity.Closed || item.YardEntity.MarkedForClose)
                {
                    item.Disable();
                    item.ProcessDisable();
                    continue;
                }

                item.UpdatePowerUse();

                if (item.Tools.Any(x => !((IMyFunctionalBlock)x).Enabled))
                {
                    item.Disable();
                    item.ProcessDisable();
                    continue;
                }

                //look for grids inside the shipyard
                if (item.YardType == ShipyardType.Disabled)
                {
                    var allEntities = new HashSet<IMyEntity>();
                    var grids = new HashSet<IMyCubeGrid>();

                    MyAPIGateway.Entities.GetEntities(allEntities);
                    if (allEntities.Count == 0)
                    {
                        //this can happen when we pull the entitiy list outside of the game thread
                        Logging.Instance.WriteLine("Failed to get entity list in ShipyardAction");
                        continue;
                    }

                    //do a fast check to get entities near the shipyard
                    //we check against the OBB later, but it takes more time than this
                    double centerDist = item.ShipyardBox.HalfExtent.LengthSquared();

                    foreach (IMyEntity entity in allEntities)
                    {
                        if (!(entity is IMyCubeGrid) || entity.EntityId == item.EntityId)
                            continue;

                        if (entity.Closed || entity.MarkedForClose)
                            continue;

                        //use the center of the ship's bounding box. GetPosition() returns the pivot point, which can be far away from grid center
                        if (Vector3D.DistanceSquared(entity.Center(), item.ShipyardBox.Center) <= centerDist)
                            grids.Add(entity as IMyCubeGrid);
                    }

                    if (grids.Count == 0)
                        continue;

                    foreach (IMyCubeGrid grid in grids)
                    {
                        //create a bounding box around the ship
                        MyOrientedBoundingBoxD gridBox = MathUtility.CreateOrientedBoundingBox(grid);

                        //check if the ship is completely inside the shipyard
                        if (item.ShipyardBox.Contains(ref gridBox) == ContainmentType.Contains)
                            item.ContainsGrids.Add(grid);
                        else
                        //in case it was previously inside, but moved
                            item.ContainsGrids.Remove(grid);
                    }

                    continue;
                }

                var toRemove = new List<IMyCubeGrid>();

                foreach (IMyCubeGrid yardGrid in item.YardGrids)
                {
                    if (yardGrid.Closed || yardGrid.MarkedForClose)
                    {
                        toRemove.Add(yardGrid);
                        continue;
                    }

                    //check if the target has left the shipyard
                    MyOrientedBoundingBoxD gridBox = MathUtility.CreateOrientedBoundingBox(yardGrid);
                    if (item.ShipyardBox.Contains(ref gridBox) != ContainmentType.Contains)
                    {
                        Logging.Instance.WriteLine("Target grid left yard");
                        toRemove.Add(yardGrid);
                    }
                }

                foreach (IMyCubeGrid removeGrid in toRemove)
                {
                    item.YardGrids.Remove(removeGrid);
                    ((MyCubeGrid)removeGrid).OnGridSplit -= item.OnGridSplit;
                    var blocks = new List<IMySlimBlock>();
                    removeGrid.GetBlocks(blocks);
                    var targetsToRemove = new HashSet<BlockTarget>();
                    foreach (BlockTarget target in item.TargetBlocks.Where(x => blocks.Contains(x.Block)))
                    {
                        targetsToRemove.Add(target);

                        foreach (KeyValuePair<long, List<BlockTarget>> entry in item.ProxDict)
                            entry.Value.Remove(target);
                        foreach (KeyValuePair<long, BlockTarget[]> procEntry in item.BlocksToProcess)
                        {
                            for (int i = 0; i < procEntry.Value.Length; i++)
                            {
                                if (procEntry.Value[i] == target)
                                {
                                    procEntry.Value[i] = null;
                                    Communication.ClearLine(procEntry.Key, i);
                                }
                            }
                        }
                    }
                    foreach (BlockTarget remove in targetsToRemove)
                        item.TargetBlocks.Remove(remove);
                }

                if (item.YardGrids.Count == 0 || item.YardGrids.All(g => g.Physics == null && g.Projector() != null))
                {
                    Logging.Instance.WriteLine("Disabling shipyard; no more targets");
                    //clear out and disable the shipyard
                    item.Disable();
                }

                startBlock.End();
                if (item.YardType == ShipyardType.Grind)
                {
                    var grindBlock = Profiler.Start(FullName, "StepGrind");
                    StepGrind(item);
                    grindBlock.End();
                }
                else if (item.YardType == ShipyardType.Weld)
                {
                    var weldBlock = Profiler.Start(FullName, "StepWeld");
                    if (!StepWeld(item))
                        item.Disable();
                    weldBlock.End();
                }

                item.UpdatePowerUse();
                item.ProcessDisable();
            }
        }

        private void StepGrind(ShipyardItem shipyardItem)
        {
            try
            {
                Logging.Instance.WriteDebug($"[StepGrind] Starting grind cycle for yard {shipyardItem.EntityId}");
                var targetsToRedraw = new HashSet<BlockTarget>();
                //we need to multiply this by MyShipGrinderConstants.GRINDER_AMOUNT_PER_SECOND / 2... which evaluates to 1
                float grindAmount = MyAPIGateway.Session.GrinderSpeedMultiplier * shipyardItem.Settings.GrindMultiplier;

                if (shipyardItem.TargetBlocks.Count == 0)
                {
                    var targetblock = Profiler.Start(FullName, nameof(StepGrind), "Populate target blocks");
                    var blocks = new List<IMySlimBlock>();
                    Logging.Instance.WriteLine(shipyardItem.YardGrids.Count.ToString());
                    foreach (IMyCubeGrid yardGrid in shipyardItem.YardGrids.Where(g => g.Physics != null)) //don't process projections
                    {
                        if (yardGrid.Closed || yardGrid.MarkedForClose)
                        {
                            Logging.Instance.WriteDebug($"[StepGrind] Skipping closed grid {yardGrid.EntityId}");
                            continue;
                        }

                        var tmpBlocks = new List<IMySlimBlock>();
                        yardGrid.GetBlocks(tmpBlocks);
                        blocks.AddRange(tmpBlocks);
                    }

                    foreach (IMySlimBlock tmpBlock in blocks.Where(b => b != null && !b.IsFullyDismounted))
                    {
                        shipyardItem.TargetBlocks.Add(new BlockTarget(tmpBlock, shipyardItem));
                    }
                    targetblock.End();
                }

                List<BlockTarget> allBlocks = shipyardItem.TargetBlocks.ToList();
                if (allBlocks.Count == 0)
                    return;

                if (!shipyardItem.ProxDict.Any())
                {
                    //sort blocks by distance to each tool
                    foreach (IMyCubeBlock tool in shipyardItem.Tools)
                    {
                        var targetSortBlock = Profiler.Start(FullName, nameof(StepGrind), "Sort Targets");
                        List<BlockTarget> sortTargets = allBlocks.ToList();
                        sortTargets.Sort((a, b) => a.ToolDist[tool.EntityId].CompareTo(b.ToolDist[tool.EntityId]));
                        shipyardItem.ProxDict[tool.EntityId] = sortTargets;
                        targetSortBlock.End();
                    }
                }

                var targetFindBlock = Profiler.Start(FullName, nameof(StepGrind), "Find Targets");
                foreach (IMyCubeBlock tool in shipyardItem.Tools)
                {
                    if (tool?.GetInventory() == null || tool.Closed || tool.MarkedForClose)
                    {
                        //this is bad
                        shipyardItem.Disable();
                        return;
                    }

                    BlockTarget[] blockArray = shipyardItem.BlocksToProcess[tool.EntityId];

                    //find the next target for each grinder, if it needs one
                    for (int i = 0; i < shipyardItem.Settings.BeamCount; i++)
                    {
                        var toRemove = new HashSet<BlockTarget>();
                        if (blockArray[i] != null)
                            continue;

                        BlockTarget nextTarget = null;

                        for (int b = 0; b < shipyardItem.ProxDict[tool.EntityId].Count; b++)
                        {
                            nextTarget = shipyardItem.ProxDict[tool.EntityId][b];

                            if (nextTarget?.CubeGrid == null || nextTarget.CubeGrid.Closed || nextTarget.CubeGrid.MarkedForClose)
                                continue;

                            //one grinder per block, please
                            bool found = false;
                            foreach (KeyValuePair<long, BlockTarget[]> entry in shipyardItem.BlocksToProcess)
                            {
                                if (entry.Value.Contains(nextTarget))
                                {
                                    found = true;
                                    break;
                                }
                            }

                            if (found)
                            {
                                toRemove.Add(nextTarget);
                                continue;
                            }

                            targetsToRedraw.Add(nextTarget);
                            break;
                        }

                        foreach (BlockTarget removeTarget in toRemove)
                        {
                            shipyardItem.ProxDict[tool.EntityId].Remove(removeTarget);
                            shipyardItem.TargetBlocks.Remove(removeTarget);
                        }

                        //we found a block to pair with our grinder, add it to the dictionary and carry on with destruction
                        if (nextTarget != null)
                        {
                            shipyardItem.BlocksToProcess[tool.EntityId][i] = nextTarget;
                        }
                    }
                }
                targetFindBlock.End();

                shipyardItem.UpdatePowerUse();
                var grindActionBlock = Profiler.Start(FullName, nameof(StepGrind), "Grind action");
                var removeTargets = new List<BlockTarget>();

                //do the grinding
                Utilities.InvokeBlocking(() =>
                {
                    try
                    {
                        foreach (IMyCubeBlock tool in shipyardItem.Tools)
                        {
                            for (int b = 0; b < shipyardItem.BlocksToProcess[tool.EntityId].Length; b++)
                            {
                                BlockTarget target = shipyardItem.BlocksToProcess[tool.EntityId][b];
                                if (target?.Block == null)
                                    continue;

                                if (target.CubeGrid.Closed || target.CubeGrid.MarkedForClose)
                                {
                                    Logging.Instance.WriteDebug("Error in grind action: Target closed");
                                    removeTargets.Add(target);
                                    continue;
                                }

                                if (targetsToRedraw.Contains(target))
                                {
                                    var toolLine = new Communication.ToolLineStruct
                                    {
                                        ToolId = tool.EntityId,
                                        GridId = target.CubeGrid.EntityId,
                                        BlockPos = target.GridPosition,
                                        PackedColor = Color.OrangeRed.PackedValue,
                                        Pulse = false,
                                        EmitterIndex = (byte)b
                                    };

                                    Communication.SendLine(toolLine, shipyardItem.ShipyardBox.Center);
                                }

                                /*
                                 * Grinding laser "efficiency" is a float between 0-1 where:
                                 *   0.0 =>   0% of components recovered
                                 *   1.0 => 100% of components recovered
                                 * 
                                 * Efficiency decays exponentially as distance to the target (length of the "laser") increases
                                 *     0m => 1.0000
                                 *    10m => 0.9995
                                 *    25m => 0.9969
                                 *    50m => 0.9875
                                 *   100m => 0.9500
                                 *   150m => 0.8875
                                 *   250m => 0.6875
                                 *   400m => 0.2000
                                 *    inf => 0.1000
                                 * We impose a minimum efficiency of 0.1 (10%), which happens at distances > ~450m
                                 */

                                // edited, same thing as welders
                                double efficiency = 1;
                                if (efficiency < 0.1)
                                    efficiency = 0.1;

                                if (!shipyardItem.YardGrids.Contains(target.CubeGrid))
                                {
                                    //we missed this grid or its split at some point, so add it to the list and register the split event
                                    shipyardItem.YardGrids.Add(target.CubeGrid);
                                    ((MyCubeGrid)target.CubeGrid).OnGridSplit += shipyardItem.OnGridSplit;
                                }

                                MyInventory grinderInventory = ((MyEntity)tool).GetInventory();
                                if (grinderInventory == null)
                                    continue;

                                if (!target.Block.IsFullyDismounted)
                                {
                                    var decreaseBlock = Profiler.Start(FullName, nameof(StepGrind), "DecreaseMountLevel");

                                    // Add safety check before grinding
                                    if (target.Block.Integrity > 0 && target.Block.BuildPercent() > 0)
                                    {
                                        try
                                        {
                                            target.Block.DecreaseMountLevel(grindAmount, grinderInventory);
                                        }
                                        catch
                                        {
                                            continue;
                                        }
                                    }

                                    decreaseBlock.End();

                                    var inventoryBlock = Profiler.Start(FullName, nameof(StepGrind), "Grind Inventory");
                                    // First move everything into _tmpInventory
                                    target.Block.MoveItemsFromConstructionStockpile(_tmpInventory);

                                    // Then move items into grinder inventory, factoring in our efficiency ratio
                                    foreach (MyPhysicalInventoryItem item in _tmpInventory.GetItems())
                                    {
                                        grinderInventory.Add(item, (MyFixedPoint)Math.Round((double)item.Amount * efficiency));
                                    }

                                    // Then clear out everything left in _tmpInventory
                                    _tmpInventory.Clear();
                                    inventoryBlock.End();
                                }

                                // This isn't an <else> clause because target.Block may have become FullyDismounted above,
                                // in which case we need to run both code blocks
                                if (target.Block.IsFullyDismounted)
                                {
                                    var dismountBlock = Profiler.Start(FullName, nameof(StepGrind), "FullyDismounted");
                                    var tmpItemList = new List<MyPhysicalInventoryItem>();
                                    var blockEntity = target.Block.FatBlock as MyEntity;
                                    if (blockEntity != null && blockEntity.HasInventory)
                                    {
                                        var dismountInventory = Profiler.Start(FullName, nameof(StepGrind), "DismountInventory");
                                        for (int i = 0; i < blockEntity.InventoryCount; ++i)
                                        {
                                            MyInventory blockInventory = blockEntity.GetInventory(i);
                                            if (blockInventory == null || blockInventory.Empty())
                                                continue;

                                            tmpItemList.Clear();
                                            tmpItemList.AddRange(blockInventory.GetItems());

                                            foreach (MyPhysicalInventoryItem item in tmpItemList)
                                            {
                                                blockInventory.Remove(item, item.Amount);
                                                grinderInventory.Add(item, (MyFixedPoint)Math.Round((double)item.Amount * efficiency));
                                            }
                                        }
                                        dismountInventory.End();
                                    }
                                    target.Block.SpawnConstructionStockpile();
                                    target.CubeGrid.RazeBlock(target.GridPosition);
                                    removeTargets.Add(target);
                                    shipyardItem.TargetBlocks.Remove(target);
                                    dismountBlock.End();
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logging.Instance.WriteDebug($"[StepGrind] Error in grind process: {ex.Message}");
                    }
                });

                foreach (KeyValuePair<long, List<BlockTarget>> entry in shipyardItem.ProxDict)
                {
                    foreach (BlockTarget removeBlock in removeTargets)
                        entry.Value.Remove(removeBlock);
                }

                foreach (BlockTarget removeBlock in removeTargets)
                    shipyardItem.TargetBlocks.Remove(removeBlock);

                //clear lines for any destroyed blocks and update our target count
                foreach (KeyValuePair<long, BlockTarget[]> entry in shipyardItem.BlocksToProcess)
                {
                    for (int i = 0; i < entry.Value.Length; i++)
                    {
                        BlockTarget removeBlock = entry.Value[i];
                        if (removeTargets.Contains(removeBlock))
                        {
                            Communication.ClearLine(entry.Key, i);
                            entry.Value[i] = null;
                        }
                    }
                }

                grindActionBlock.End();
            }
            catch (Exception ex)
            {
                Logging.Instance.WriteLine($"[StepGrind] Unhandled error: {ex}");
            }
        }

        private const int BATCH_SIZE = 100; // Process blocks in smaller batches

        private bool StepWeld(ShipyardItem shipyardItem)
        {
            Logging.Instance.WriteDebug($"[StepWeld] Starting weld cycle for yard {shipyardItem.EntityId}");
            LogGridStatus(shipyardItem);

            var targetsToRemove = new HashSet<BlockTarget>();
            var targetsToRedraw = new HashSet<BlockTarget>();

            // Initialize targets if needed
            if (shipyardItem.TargetBlocks.Count == 0)
            {
                if (!InitializeWeldTargets(shipyardItem))
                {
                    return false;
                }
            }

            // Validate before proceeding
            if (!ValidateWeldOperation(shipyardItem))
            {
                return false;
            }

            // Assign blocks to welders
            foreach (IMyCubeBlock welder in shipyardItem.Tools)
            {
                Logging.Instance.WriteDebug($"[StepWeld] Processing welder {welder.EntityId}");
                for (int i = 0; i < shipyardItem.Settings.BeamCount; i++)
                {
                    if (shipyardItem.BlocksToProcess[welder.EntityId][i] != null)
                    {
                        Logging.Instance.WriteDebug($"[StepWeld] Beam {i} already has assigned target, skipping");
                        continue;
                    }

                    BlockTarget nextTarget = FindNextValidTarget(shipyardItem, welder);
                    if (nextTarget != null)
                    {
                        targetsToRedraw.Add(nextTarget);
                        shipyardItem.BlocksToProcess[welder.EntityId][i] = nextTarget;
                    }
                }
            }

            // Update visuals
            ProcessBeamVisuals(shipyardItem, targetsToRedraw);

            // Process welding operations
            if (!ProcessWelding(shipyardItem, targetsToRemove, targetsToRedraw))
            {
                return false;
            }

            return true;
        }

        private bool InitializeWeldTargets(ShipyardItem shipyardItem)
        {
            var sortBlock = Profiler.Start(FullName, nameof(InitializeWeldTargets));
            try
            {
                Logging.Instance.WriteDebug("[StepWeld] No target blocks, starting scan");
                shipyardItem.TargetBlocks.Clear();
                shipyardItem.ProxDict.Clear();
                var gridTargets = new Dictionary<long, List<BlockTarget>>();

                foreach (IMyCubeGrid targetGrid in shipyardItem.YardGrids)
                {
                    if (targetGrid.Closed || targetGrid.MarkedForClose)
                    {
                        Logging.Instance.WriteDebug($"[StepWeld] Skipping closed/marked grid {targetGrid.EntityId}");
                        continue;
                    }

                    var tmpBlocks = new List<IMySlimBlock>();
                    targetGrid.GetBlocks(tmpBlocks);
                    Logging.Instance.WriteDebug($"[StepWeld] Found {tmpBlocks.Count} total blocks in grid {targetGrid.EntityId}");

                    gridTargets.Add(targetGrid.EntityId, new List<BlockTarget>(tmpBlocks.Count));
                    int skippedBlocks = 0;
                    int addedBlocks = 0;

                    foreach (IMySlimBlock block in tmpBlocks.ToArray())
                    {
                        if (block == null)
                        {
                            skippedBlocks++;
                            continue;
                        }

                        if (targetGrid.Physics != null && block.IsFullIntegrity && !block.HasDeformation)
                        {
                            skippedBlocks++;
                            continue;
                        }

                        var target = new BlockTarget(block, shipyardItem);
                        shipyardItem.TargetBlocks.Add(target);
                        gridTargets[targetGrid.EntityId].Add(target);
                        addedBlocks++;
                    }

                    Logging.Instance.WriteDebug($"[StepWeld] Grid {targetGrid.EntityId} processing results:");
                    Logging.Instance.WriteDebug($"  - Skipped blocks: {skippedBlocks}");
                    Logging.Instance.WriteDebug($"  - Added targets: {addedBlocks}");
                }

                int totalTargets = 0;
                foreach (KeyValuePair<long, List<BlockTarget>> entry in gridTargets)
                {
                    totalTargets += entry.Value.Count;
                    Logging.Instance.WriteDebug($"[StepWeld] Grid {entry.Key} final target count: {entry.Value.Count}");
                }

                foreach (IMyCubeBlock tool in shipyardItem.Tools)
                {
                    Logging.Instance.WriteDebug($"[StepWeld] Setting up tool {tool.EntityId}");
                    shipyardItem.ProxDict.Add(tool.EntityId, new List<BlockTarget>());

                    shipyardItem.YardGrids.Sort((a, b) =>
                        Vector3D.DistanceSquared(a.Center(), tool.GetPosition())
                            .CompareTo(Vector3D.DistanceSquared(b.Center(), tool.GetPosition())));

                    foreach (IMyCubeGrid grid in shipyardItem.YardGrids)
                    {
                        if (!gridTargets.ContainsKey(grid.EntityId))
                        {
                            Logging.Instance.WriteDebug($"[StepWeld] Warning: Grid {grid.EntityId} missing from gridTargets");
                            continue;
                        }

                        List<BlockTarget> list = gridTargets[grid.EntityId];
                        list.Sort((a, b) => a.CenterDist.CompareTo(b.CenterDist));
                        shipyardItem.ProxDict[tool.EntityId].AddRange(list);
                    }

                    Logging.Instance.WriteDebug($"[StepWeld] Tool {tool.EntityId} assigned {shipyardItem.ProxDict[tool.EntityId].Count} targets");
                }

                return shipyardItem.TargetBlocks.Count > 0;
            }
            finally
            {
                sortBlock.End();
            }
        }

        private void LogGridStatus(ShipyardItem shipyardItem)
        {
            Logging.Instance.WriteDebug($"[StepWeld] Current grid count: {shipyardItem.YardGrids.Count}");
            Logging.Instance.WriteDebug($"[StepWeld] Current target count: {shipyardItem.TargetBlocks.Count}");

            foreach (var grid in shipyardItem.YardGrids)
            {
                Logging.Instance.WriteDebug($"[StepWeld] Grid {grid.EntityId} status:");
                Logging.Instance.WriteDebug($"  - Has physics: {grid.Physics != null}");
                Logging.Instance.WriteDebug($"  - Closed: {grid.Closed}");
                Logging.Instance.WriteDebug($"  - MarkedForClose: {grid.MarkedForClose}");
                Logging.Instance.WriteDebug($"  - Is projection: {grid.Projector() != null}");
            }
        }

        private void ProcessBeamVisuals(ShipyardItem shipyardItem, HashSet<BlockTarget> targetsToRedraw)
        {
            foreach (KeyValuePair<long, BlockTarget[]> entry in shipyardItem.BlocksToProcess)
            {
                for (int i = 0; i < entry.Value.Length; i++)
                {
                    BlockTarget targetBlock = entry.Value[i];
                    if (targetBlock == null || _stalledTargets.Contains(targetBlock))
                        continue;

                    if (targetsToRedraw.Contains(targetBlock))
                    {
                        var toolLine = new Communication.ToolLineStruct
                        {
                            ToolId = entry.Key,
                            GridId = targetBlock.CubeGrid.EntityId,
                            BlockPos = targetBlock.GridPosition,
                            PackedColor = Color.DarkCyan.PackedValue,
                            Pulse = false,
                            EmitterIndex = (byte)i
                        };
                        Communication.SendLine(toolLine, shipyardItem.ShipyardBox.Center);
                    }
                }
            }
        }

        private bool ValidateWeldOperation(ShipyardItem shipyardItem)
        {
            if (shipyardItem.BlocksToProcess.All(e => e.Value.All(t => t == null)))
            {
                shipyardItem.Disable(true, "No more blocks to weld");
                return false;
            }

            if (shipyardItem.TargetBlocks.Count == 0)
            {
                Logging.Instance.WriteDebug($"[StepWeld] Populated {shipyardItem.TargetBlocks.Count} target blocks.");
                shipyardItem.Disable(true, "No blocks require welding");
                return false;
            }

            shipyardItem.UpdatePowerUse();
            return true;
        }

        private bool ProcessWelding(ShipyardItem shipyardItem, HashSet<BlockTarget> targetsToRemove, HashSet<BlockTarget> targetsToRedraw)
        {
            ProcessWeldingActions(shipyardItem, targetsToRemove, targetsToRedraw);
            ProcessStalledTargets(shipyardItem, targetsToRemove, targetsToRedraw);
            CleanupRemovedTargets(shipyardItem, targetsToRemove);

            return true;
        }

        private void ProcessWeldingActions(ShipyardItem shipyardItem, HashSet<BlockTarget> targetsToRemove, HashSet<BlockTarget> targetsToRedraw)
        {
            Utilities.InvokeBlocking(() => {
                float weldAmount = MyAPIGateway.Session.WelderSpeedMultiplier * shipyardItem.Settings.WeldMultiplier;
                float boneAmount = weldAmount * 0.1f;

                foreach (IMyCubeBlock welder in shipyardItem.Tools)
                {
                    var tool = (IMyCollector)welder;
                    MyInventory welderInventory = ((MyEntity)tool).GetInventory();

                    ProcessToolTargets(shipyardItem, tool, welderInventory, weldAmount, boneAmount, targetsToRemove, targetsToRedraw);
                }
            });
        }

        private void ProcessToolTargets(ShipyardItem shipyardItem, IMyCollector tool, MyInventory welderInventory,
                                        float weldAmount, float boneAmount, HashSet<BlockTarget> targetsToRemove,
                                        HashSet<BlockTarget> targetsToRedraw)
        {
            int i = 0;
            foreach (BlockTarget target in shipyardItem.BlocksToProcess[tool.EntityId])
            {
                if (target == null) continue;

                if (target.CubeGrid.Physics == null || target.CubeGrid.Closed || target.CubeGrid.MarkedForClose)
                {
                    targetsToRemove.Add(target);
                    continue;
                }

                double efficiency = 1;
                if (efficiency < 0.1) efficiency = 0.1;

                ProcessMissingComponents(target, welderInventory, tool, shipyardItem, efficiency);
                target.Block.MoveItemsToConstructionStockpile(welderInventory);

                var missingComponents = new Dictionary<string, int>();
                target.Block.GetMissingComponents(missingComponents);

                if (missingComponents.Any() && !target.Block.HasDeformation)
                {
                    if (_stalledTargets.Add(target)) targetsToRedraw.Add(target);
                }
                else
                {
                    if (_stalledTargets.Remove(target)) targetsToRedraw.Add(target);
                }

                target.Block.IncreaseMountLevel(weldAmount, 0, welderInventory, boneAmount, true);

                if (target.Block.IsFullIntegrity && !target.Block.HasDeformation)
                    targetsToRemove.Add(target);

                i++;
            }
        }

        private void ProcessMissingComponents(BlockTarget target, MyInventory welderInventory, IMyCollector tool,
                                              ShipyardItem shipyardItem, double efficiency)
        {
            var missingComponents = new Dictionary<string, int>();
            target.Block.GetMissingComponents(missingComponents);

            foreach (KeyValuePair<string, int> entry in missingComponents)
            {
                var componentId = new MyDefinitionId(typeof(MyObjectBuilder_Component), entry.Key);
                int missing = entry.Value;
                MyFixedPoint totalRequired = (MyFixedPoint)(missing / efficiency);
                MyFixedPoint currentStock = welderInventory.GetItemAmount(componentId);

                if (currentStock < totalRequired && tool.UseConveyorSystem)
                {
                    welderInventory.PullAny(shipyardItem.ConnectedCargo, entry.Key,
                                             (int)Math.Ceiling((double)(totalRequired - currentStock)));
                    currentStock = welderInventory.GetItemAmount(componentId);
                }

                if (currentStock >= 1)
                {
                    MyFixedPoint toDelete = MyFixedPoint.Min(MyFixedPoint.Floor(currentStock), missing) *
                                            (MyFixedPoint)(1 - efficiency);
                    welderInventory.RemoveItemsOfType(toDelete, componentId);
                }
            }
        }

        private void ProcessStalledTargets(ShipyardItem shipyardItem, HashSet<BlockTarget> targetsToRemove,
                                           HashSet<BlockTarget> targetsToRedraw)
        {
            shipyardItem.MissingComponentsDict.Clear();

            foreach (KeyValuePair<long, BlockTarget[]> entry in shipyardItem.BlocksToProcess)
            {
                for (int i = 0; i < entry.Value.Length; i++)
                {
                    BlockTarget target = entry.Value[i];
                    if (target == null) continue;

                    if (targetsToRemove.Contains(target))
                    {
                        Communication.ClearLine(entry.Key, i);
                        _stalledTargets.Remove(target);
                        shipyardItem.TargetBlocks.Remove(target);
                        entry.Value[i] = null;
                        continue;
                    }

                    if (_stalledTargets.Contains(target))
                    {
                        AggregateComponentRequirements(target, shipyardItem);

                        if (shipyardItem.MissingComponentsDict.Any())
                        {
                            shipyardItem.Disable(true, "Insufficient components to continue welding");
                            return;
                        }

                        var toolLine = new Communication.ToolLineStruct
                        {
                            ToolId = entry.Key,
                            GridId = target.CubeGrid.EntityId,
                            BlockPos = target.GridPosition,
                            PackedColor = Color.Purple.PackedValue,
                            Pulse = true,
                            EmitterIndex = (byte)i
                        };

                        if (targetsToRedraw.Contains(target))
                        {
                            Communication.ClearLine(entry.Key, i);
                            Communication.SendLine(toolLine, shipyardItem.ShipyardBox.Center);
                        }
                        continue;
                    }
                }
            }
        }

        private void AggregateComponentRequirements(BlockTarget target, ShipyardItem shipyardItem)
        {
            var blockComponents = new Dictionary<string, int>();
            target.Block.GetMissingComponents(blockComponents);

            foreach (KeyValuePair<string, int> component in blockComponents)
            {
                if (shipyardItem.MissingComponentsDict.ContainsKey(component.Key))
                    shipyardItem.MissingComponentsDict[component.Key] += component.Value;
                else
                    shipyardItem.MissingComponentsDict.Add(component.Key, component.Value);
            }
        }

        private void CleanupRemovedTargets(ShipyardItem shipyardItem, HashSet<BlockTarget> targetsToRemove)
        {
            foreach (KeyValuePair<long, List<BlockTarget>> entry in shipyardItem.ProxDict)
            {
                foreach (BlockTarget removeBlock in targetsToRemove)
                    entry.Value.Remove(removeBlock);
            }

            foreach (BlockTarget removeBlock in targetsToRemove)
            {
                shipyardItem.TargetBlocks.Remove(removeBlock);
            }

            foreach (KeyValuePair<long, BlockTarget[]> entry in shipyardItem.BlocksToProcess)
            {
                for (int i = 0; i < entry.Value.Length; i++)
                {
                    BlockTarget removeBlock = entry.Value[i];
                    if (targetsToRemove.Contains(removeBlock))
                    {
                        Communication.ClearLine(entry.Key, i);
                        entry.Value[i] = null;
                    }
                }
            }
        }

        private BlockTarget FindNextValidTarget(ShipyardItem shipyardItem, IMyCubeBlock welder)
        {
            var toRemove = new List<BlockTarget>();
            BlockTarget nextTarget = null;

            foreach (BlockTarget target in shipyardItem.ProxDict[welder.EntityId])
            {
                bool found = false;
                foreach (KeyValuePair<long, BlockTarget[]> entry in shipyardItem.BlocksToProcess)
                {
                    if (entry.Value.Contains(target))
                    {
                        found = true;
                        break;
                    }
                }

                if (found)
                {
                    toRemove.Add(target);
                    continue;
                }

                if (target.Projector != null)
                {
                    if (!HandleProjection(target)) continue;
                }

                nextTarget = target;
                break;
            }

            // Cleanup
            foreach (BlockTarget removeTarget in toRemove)
            {
                shipyardItem.ProxDict[welder.EntityId].Remove(removeTarget);
                shipyardItem.TargetBlocks.Remove(removeTarget);
            }

            return nextTarget;
        }

        private bool HandleProjection(BlockTarget target)
        {
            BuildCheckResult res = target.Projector.CanBuild(target.Block, false);
            Logging.Instance.WriteDebug($"[StepWeld] Projection build check for {target.GridPosition}: {res}");

            if (res == BuildCheckResult.AlreadyBuilt)
            {
                target.UpdateAfterBuild();
                return false;
            }

            if (res != BuildCheckResult.OK)
            {
                return false;
            }

            bool success = false;
            Utilities.InvokeBlocking(() => success = BuildTarget(target));

            if (!success)
            {
                Logging.Instance.WriteDebug($"[StepWeld] Failed to build projection at {target.GridPosition}");
                return false;
            }

            target.UpdateAfterBuild();
            return true;
        }

        private bool BuildTarget(BlockTarget target)
        {
            IMyProjector projector = target.Projector;
            IMySlimBlock block = target.Block;

            if (projector == null || block == null) return false;
            if (projector.CanBuild(block, false) != BuildCheckResult.OK) return false;

            if (MyAPIGateway.Session.CreativeMode)
            {
                try
                {
                    projector.Build(block, projector.OwnerId, projector.EntityId, false, projector.OwnerId);
                }
                catch (NullReferenceException ex)
                {
                    Logging.Instance.WriteDebug($"Build failed due to missing DLC reference: {ex.Message}");
                    return false;
                }
                return projector.CanBuild(block, true) != BuildCheckResult.OK;
            }

            var blockDefinition = block.BlockDefinition as MyCubeBlockDefinition;
            string componentName = blockDefinition.Components[0].Definition.Id.SubtypeName;

            // This _tmpInventory is assumed to be a class-level MyInventory
            if (_tmpInventory.PullAny(new HashSet<IMyTerminalBlock>(), componentName, 1))
            {
                _tmpInventory.Clear();
                try
                {
                    projector.Build(block, projector.OwnerId, projector.EntityId, false, projector.OwnerId);
                }
                catch (NullReferenceException ex)
                {
                    Logging.Instance.WriteDebug($"Build failed due to missing DLC reference: {ex.Message}");
                    return false;
                }
                return projector.CanBuild(block, true) != BuildCheckResult.OK;
            }

            return false;
        }

        private bool BuildTarget(BlockTarget target, ShipyardItem item, IMyCubeBlock tool)
        {
            IMyProjector projector = target.Projector;
            IMySlimBlock block = target.Block;

            if (projector == null || block == null)
                return false;

            // Check if the projector is allowed to build the block
            if (projector.CanBuild(block, false) != BuildCheckResult.OK)
                return false;

            // If in Creative Mode, build the block immediately
            if (MyAPIGateway.Session.CreativeMode)
            {
                try
                {
                    projector.Build(block, projector.OwnerId, projector.EntityId, false, projector.OwnerId);
                }
                catch (NullReferenceException ex)
                {
                    // Log and ignore the exception to prevent a crash
                    Logging.Instance.WriteDebug($"Build failed due to missing DLC reference: {ex.Message}");
                    return false;
                }
                return projector.CanBuild(block, true) != BuildCheckResult.OK;
            }

            // Attempt to pull required components for building in survival mode
            var blockDefinition = block.BlockDefinition as MyCubeBlockDefinition;
            string componentName = blockDefinition.Components[0].Definition.Id.SubtypeName;
            if (_tmpInventory.PullAny(item.ConnectedCargo, componentName, 1))
            {
                _tmpInventory.Clear();
                try
                {
                    projector.Build(block, projector.OwnerId, projector.EntityId, false, projector.OwnerId);
                }
                catch (NullReferenceException ex)
                {
                    // Log and ignore the exception to prevent a crash
                    Logging.Instance.WriteDebug($"Build failed due to missing DLC reference: {ex.Message}");
                    return false;
                }
                return projector.CanBuild(block, true) != BuildCheckResult.OK;
            }

            return false; // Return false if the block cannot be built
        }

    }
}