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

        private bool StepWeld(ShipyardItem shipyardItem)
        {
            Logging.Instance.WriteDebug($"[StepWeld] Starting weld cycle for yard {shipyardItem.EntityId}");
            Logging.Instance.WriteDebug($"[StepWeld] Current grid count: {shipyardItem.YardGrids.Count}");
            Logging.Instance.WriteDebug($"[StepWeld] Current target count: {shipyardItem.TargetBlocks.Count}");

            // Log grid details
            foreach (var grid in shipyardItem.YardGrids)
            {
                Logging.Instance.WriteDebug($"[StepWeld] Grid {grid.EntityId} status:");
                Logging.Instance.WriteDebug($"  - Has physics: {grid.Physics != null}");
                Logging.Instance.WriteDebug($"  - Closed: {grid.Closed}");
                Logging.Instance.WriteDebug($"  - MarkedForClose: {grid.MarkedForClose}");
                Logging.Instance.WriteDebug($"  - Is projection: {grid.Projector() != null}");
            }

            var targetsToRemove = new HashSet<BlockTarget>();
            var targetsToRedraw = new HashSet<BlockTarget>();

            float weldAmount = MyAPIGateway.Session.WelderSpeedMultiplier * shipyardItem.Settings.WeldMultiplier;
            float boneAmount = weldAmount * .1f;

            Logging.Instance.WriteDebug($"[StepWeld] Calculated weldAmount: {weldAmount}, boneAmount: {boneAmount}.");

            if (shipyardItem.TargetBlocks.Count == 0)
            {
                Logging.Instance.WriteDebug("[StepWeld] No target blocks, starting scan");
                var sortBlock = Profiler.Start(FullName, nameof(StepWeld), "Sort Targets");
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

                // Log total processing results
                int totalTargets = 0;
                foreach (KeyValuePair<long, List<BlockTarget>> entry in gridTargets)
                {
                    totalTargets += entry.Value.Count;
                    Logging.Instance.WriteDebug($"[StepWeld] Grid {entry.Key} final target count: {entry.Value.Count}");
                }

                // Sort and assign targets to tools
                foreach (IMyCubeBlock tool in shipyardItem.Tools)
                {
                    Logging.Instance.WriteDebug($"[StepWeld] Setting up tool {tool.EntityId}");
                    shipyardItem.ProxDict.Add(tool.EntityId, new List<BlockTarget>());

                    shipyardItem.YardGrids.Sort((a, b) =>
                        Vector3D.DistanceSquared(a.Center(), tool.GetPosition()).CompareTo(
                            Vector3D.DistanceSquared(b.Center(), tool.GetPosition())));

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

                sortBlock.End();
            }

            //nothing to do
            if (shipyardItem.TargetBlocks.Count == 0)
            {
                Logging.Instance.WriteDebug($"[StepWeld] Populated {shipyardItem.TargetBlocks.Count} target blocks.");
                return false;
            }

            Logging.Instance.WriteDebug("[StepWeld] Beginning welding loop.");
            //assign blocks to our welders
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
                            Logging.Instance.WriteDebug($"[StepWeld] Target at {target.GridPosition} already being processed");
                            continue;
                        }

                        if (target.Projector != null)
                        {
                            BuildCheckResult res = target.Projector.CanBuild(target.Block, false);
                            Logging.Instance.WriteDebug($"[StepWeld] Projection build check for {target.GridPosition}: {res}");

                            if (res == BuildCheckResult.AlreadyBuilt)
                            {
                                target.UpdateAfterBuild();
                            }
                            else if (res != BuildCheckResult.OK)
                            {
                                continue;
                            }
                            else
                            {
                                bool success = false;
                                Utilities.InvokeBlocking(() => success = BuildTarget(target, shipyardItem, welder));
                                if (!success)
                                {
                                    Logging.Instance.WriteDebug($"[StepWeld] Failed to build projection at {target.GridPosition}");
                                    continue;
                                }
                                target.UpdateAfterBuild();
                            }
                        }

                        if (target.Block.IsFullIntegrity && !target.Block.HasDeformation)
                        {
                            Logging.Instance.WriteDebug($"[StepWeld] Block at {target.GridPosition} is already complete");
                            toRemove.Add(target);
                            continue;
                        }

                        nextTarget = target;
                        Logging.Instance.WriteDebug($"[StepWeld] Selected target at {target.GridPosition} for beam {i}");
                        break;
                    }

                    if (nextTarget != null)
                    {
                        targetsToRedraw.Add(nextTarget);
                        shipyardItem.BlocksToProcess[welder.EntityId][i] = nextTarget;
                    }

                    foreach (BlockTarget removeTarget in toRemove)
                    {
                        shipyardItem.ProxDict[welder.EntityId].Remove(removeTarget);
                        shipyardItem.TargetBlocks.Remove(removeTarget);
                    }
                }
            }
            //update lasers
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

            if (shipyardItem.BlocksToProcess.All(e => e.Value.All(t => t == null)))
                return false;

            shipyardItem.UpdatePowerUse();
            targetsToRedraw.Clear();

            Utilities.InvokeBlocking(() =>
                                     {
                                         foreach (IMyCubeBlock welder in shipyardItem.Tools)
                                         {
                                             var tool = (IMyCollector)welder;
                                             MyInventory welderInventory = ((MyEntity)tool).GetInventory();
                                             int i = 0;
                                             foreach (BlockTarget target in shipyardItem.BlocksToProcess[tool.EntityId])
                                             {
                                                 if (target == null)
                                                     continue;
                                                 
                                                 if (target.CubeGrid.Physics == null || target.CubeGrid.Closed || target.CubeGrid.MarkedForClose)
                                                 {
                                                     targetsToRemove.Add(target);
                                                     continue;
                                                 }

                                                 //      if (MyAPIGateway.Session.CreativeMode)
                                                 //     {
                                                 /*
                                                  * Welding laser "efficiency" is a float between 0-1 where:
                                                  *   0.0 =>   0% of component stock used for construction (100% loss)
                                                  *   1.0 => 100% of component stock used for construction (0% loss)
                                                  * 
                                                  * Efficiency decay/distance formula is the same as above for grinder
                                                  */

                                                 // Set efficiency to 1 because wtf ew no, good ideas aren't allowed
                                                 //double efficiency = 1 - (target.ToolDist[tool.EntityId] / 200000);
                                                 double efficiency = 1;
                                                 //if (!shipyardItem.StaticYard)
                                                 //        efficiency /= 2;
                                                 if (efficiency < 0.1)
                                                     efficiency = 0.1;
                                                     //Logging.Instance.WriteDebug(String.Format("Welder[{0}]block[{1}] distance=[{2:F2}m] efficiency=[{3:F5}]", tool.DisplayNameText, i, Math.Sqrt(target.ToolDist[tool.EntityId]), efficiency));
                                                     /*
                                                      * We have to factor in our efficiency ratio before transferring to the block "construction stockpile",
                                                      * but that math isn't nearly as easy as it was with the grinder.
                                                      * 
                                                      * For each missing component type, we know how many items are "missing" from the construction model, M
                                                      * 
                                                      * The simplest approach would be to pull M items from the conveyor system (if enabled), then move
                                                      * (M*efficiency) items to the "construction stockpile" and vaporize the other (M*(1-efficiency)) items.
                                                      * However, this approach would leave the construction stockpile incomplete and require several iterations
                                                      * before M items have actually been copied.
                                                      * 
                                                      * A better solution is to pull enough "extra" items from the conveyors that the welder has enough to
                                                      * move M items to the construction stockpile even after losses due to distance inefficiency
                                                      *
                                                      * For example, if the target block is missing M=9 items and we are running at 0.9 (90%) efficiency,
                                                      * ideally that means we should pull 10 units, vaporize 1, and still have 9 for construction. However,
                                                      * if the conveyor system was only able to supply us with 2 components, we should not continue to blindly
                                                      * vaporize 1 unit.
                                                      * 
                                                      * Instead, we have to consult the post-conveyor-pull welder inventory to determine if it has at least
                                                      * the required number of components.  If it does, we vaporize M*(1-efficiency).  Otherwise we only
                                                      * vaporize current_count*(1-efficiency) and transfer the rest to the construction stockpile
                                                      */
                                                     var missingComponents = new Dictionary<string, int>();
                                                     target.Block.GetMissingComponents(missingComponents);

                                                     var wasteComponents = new Dictionary<string, int>();
                                                     foreach (KeyValuePair<string, int> entry in missingComponents)
                                                     {
                                                         var componentId = new MyDefinitionId(typeof(MyObjectBuilder_Component), entry.Key);
                                                         int missing = entry.Value;
                                                         MyFixedPoint totalRequired = (MyFixedPoint)(missing / efficiency);

                                                         MyFixedPoint currentStock = welderInventory.GetItemAmount(componentId);

                                                         //Logging.Instance.WriteDebug(String.Format("Welder[{0}]block[{1}] Component[{2}] missing[{3:F3}] inefficiency requires[{4:F3}] in_stock[{5:F3}]", tool.DisplayNameText, i, entry.Key, missing, totalRequired, currentStock));
                                                         if (currentStock < totalRequired && tool.UseConveyorSystem)
                                                         {
                                                             // Welder doesn't have totalRequired, so try to pull the difference from conveyors
                                                             welderInventory.PullAny(shipyardItem.ConnectedCargo, entry.Key, (int)Math.Ceiling((double)(totalRequired - currentStock)));
                                                             currentStock = welderInventory.GetItemAmount(componentId);
                                                             //Logging.Instance.WriteDebug(String.Format("Welder[{0}]block[{1}] Component[{2}] - after conveyor pull - in_stock[{3:F3}]", tool.DisplayNameText, i, entry.Key, currentStock));
                                                         }

                                                         // Now compute the number of components to delete
                                                         // MoveItemsToConstructionPile() below won't move anything if we have less than 1 unit,
                                                         // so don't bother "losing" anything due to ineffeciency
                                                         if (currentStock >= 1)
                                                         {
                                                             // The lesser of (missing, currentStock), times (1 minus) our efficiency fraction
                                                             MyFixedPoint toDelete = MyFixedPoint.Min(MyFixedPoint.Floor(currentStock), missing) * (MyFixedPoint)(1 - efficiency);
                                                             //Logging.Instance.WriteDebug(String.Format("Welder[{0}]block[{1}] Component[{2}] amount lost due to distance [{3:F3}]", tool.DisplayNameText, i, entry.Key, toDelete));
                                                             welderInventory.RemoveItemsOfType(toDelete, componentId);
                                                         }
                                                     }

                                                     target.Block.MoveItemsToConstructionStockpile(welderInventory);

                                                     missingComponents.Clear();
                                                     target.Block.GetMissingComponents(missingComponents);
                                                     
                                                     if (missingComponents.Any() && !target.Block.HasDeformation)
                                                     {
                                                         if (_stalledTargets.Add(target))
                                                             targetsToRedraw.Add(target);
                                                     }
                                                     else
                                                     {
                                                         if (_stalledTargets.Remove(target))
                                                             targetsToRedraw.Add(target);
                                                     }

                                                     target.Block.IncreaseMountLevel(weldAmount, 0, welderInventory, boneAmount, true);
                                                     if (target.Block.IsFullIntegrity && !target.Block.HasDeformation)
                                                         targetsToRemove.Add(target);
                                            //     }
                                                 i++;
                                             }
                                         }
                                     });
            
            shipyardItem.MissingComponentsDict.Clear();

            foreach (KeyValuePair<long, BlockTarget[]> entry in shipyardItem.BlocksToProcess)
            {
                for (int i = 0; i < entry.Value.Length; i++)
                {
                    BlockTarget target = entry.Value[i];

                    if (target == null)
                        continue;

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
                        var blockComponents = new Dictionary<string, int>();
                        target.Block.GetMissingComponents(blockComponents);

                        foreach (KeyValuePair<string, int> component in blockComponents)
                        {
                            if (shipyardItem.MissingComponentsDict.ContainsKey(component.Key))
                                shipyardItem.MissingComponentsDict[component.Key] += component.Value;
                            else
                                shipyardItem.MissingComponentsDict.Add(component.Key, component.Value);
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

            return true;
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