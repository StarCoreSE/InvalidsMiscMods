using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using SurvivalStats;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace FakeHydro
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_CubeGrid), false)]
    public class FakeHydroGrid : MyGameLogicComponent
    {
        private MyCubeGrid _cubeGrid;
        private List<IMySlimBlock> _cubeBlocks;
        private bool _blockScanPending;
        private List<TankGroup> _tankGroups = new List<TankGroup>();
        private DateTime _lastBlockScanTime = DateTime.MinValue;
        private readonly TimeSpan _debounceInterval = TimeSpan.FromSeconds(3);
        private List<IMyThrust> _reducedThrusters = new List<IMyThrust>();
        private int _tick;
        private bool _noThrusters;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            _cubeGrid = Entity as MyCubeGrid;

            if (_cubeGrid == null) return;

            if (FakeHydroSession.IsServer)
            {
                NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME;

                _cubeGrid.OnFatBlockAdded += FatBlockAdded;
                _cubeGrid.OnFatBlockRemoved += FatBlockRemoved;
                _cubeGrid.OnGridSplit += GridSplit;

                ScheduleBlockScan();
            }
            else
            {
                NeedsUpdate = MyEntityUpdateEnum.NONE;
            }
        }
        
        public override void OnBeforeRemovedFromContainer()
        {
            if (_cubeGrid != null)
            {
                if (FakeHydroSession.IsServer)
                {
                    _cubeGrid.OnFatBlockAdded -= FatBlockAdded;
                    _cubeGrid.OnFatBlockRemoved -= FatBlockRemoved;
                    _cubeGrid.OnGridSplit -= GridSplit;
                }
            }
        }

        private void GridSplit(MyCubeGrid oldGrid, MyCubeGrid newGrid)
        {
            ScheduleBlockScan();
        }

        private void FatBlockAdded(MyCubeBlock myCubeBlock)
        {
            ScheduleBlockScan();
        }

        private void FatBlockRemoved(MyCubeBlock myCubeBlock)
        {
            ScheduleBlockScan();
        }

        public override void UpdateBeforeSimulation()
        {
            try
            {
                if (_cubeGrid == null)
                    return;

                // do not run at all over static grids, meaning no hydro usage from thrusters on static grids
                if (_cubeGrid.IsStatic)
                    return;

                if (_tick++ >= 120)
                {
                    UpdateBeforeSimulation120();
                    _tick = 0;
                }

                if (_blockScanPending)
                    return;

                if (_noThrusters)
                    return;

                // process hydro sinks on all tanks in all tank groups
                ProcessTankGroupsSink();
            }
            catch (Exception exc)
            {
                MyLog.Default.WriteLineAndConsole($"##MOD: FakeHydroGrid UpdateBeforeSimulation, ERROR: {exc}");
            }
        }

        private void UpdateBeforeSimulation120()
        {
            try
            {
                if (_cubeGrid == null)
                    return;

                // do not run at all over static grids, meaning no hydro usage from thrusters on static grids
                if (_cubeGrid.IsStatic)
                    return;

                if (_blockScanPending && DateTime.UtcNow - _lastBlockScanTime >= _debounceInterval)
                {
                    RunBlockScan();
                    _blockScanPending = false; // Reset the flag
                }

                ProcessTankGroups();

                // MyLog.Default.WriteLineAndConsole($"##MOD: FakeHydroGrid totalHydrogenUsage: {totalHydrogenUsage}");
            }
            catch (Exception exc)
            {
                MyLog.Default.WriteLineAndConsole($"##MOD: FakeHydroGrid UpdateBeforeSimulation60, ERROR: {exc}");
            }
        }

        private void ProcessTankGroupsSink()
        {
            foreach (var tankGroup in _tankGroups)
            {
                if (tankGroup.SinkValue == 0)
                {
                    ResetProducersInTankGroup(tankGroup);
                    continue;
                }

                var totalSinkValue = tankGroup.SinkValue;
                var resourceId = MyResourceDistributorComponent.HydrogenId;

                foreach (var tank in tankGroup.Tanks)
                {
                    if (totalSinkValue <= 0) break;

                    if (tank == null) continue;

                    if (!tank.IsFunctional || !tank.Enabled || tank.Stockpile) continue;

                    var sourceComp = tank.Components.Get<MyResourceSourceComponent>();

                    if (sourceComp == null) continue;

                    double currentVolume = tank.FilledRatio * tank.Capacity;
                    var output = sourceComp.CurrentOutputByType(resourceId);
                    var maxOutput = sourceComp.MaxOutputByType(resourceId);

                    var availableOutput = maxOutput - output > totalSinkValue ? totalSinkValue : maxOutput - output;
                    var realAvailableOutput = currentVolume > availableOutput ? availableOutput : currentVolume;

                    if (realAvailableOutput > 0)
                    {
                        sourceComp.SetOutputByType(resourceId, (float)realAvailableOutput);
                        totalSinkValue -= (float)realAvailableOutput;
                    }
                }

                if (totalSinkValue <= 0) break;

                foreach (var gasGen in tankGroup.HydrogenGens)
                {
                    if (totalSinkValue <= 0) break;

                    if (gasGen == null) continue;

                    if (!gasGen.IsFunctional || !gasGen.Enabled) continue;

                    var sourceComp = gasGen.Components.Get<MyResourceSourceComponent>();

                    if (sourceComp == null) continue;

                    var output = sourceComp.CurrentOutputByType(resourceId);
                    var maxOutput = sourceComp.MaxOutputByType(resourceId);

                    var availableOutput = maxOutput - output > totalSinkValue ? totalSinkValue : maxOutput - output;

                    if (availableOutput > 0)
                    {
                        sourceComp.SetOutputByType(resourceId, availableOutput);
                        totalSinkValue -= availableOutput;
                    }
                }

                if (totalSinkValue <= 0) break;

                // add available from gas farms
                foreach (var gasFarm in tankGroup.HydrogenFarms)
                {
                    if (totalSinkValue <= 0) break;

                    if (gasFarm == null) continue;

                    if (!gasFarm.IsFunctional || !gasFarm.Enabled) continue;

                    var sourceComp = gasFarm.Components.Get<MyResourceSourceComponent>();

                    if (sourceComp == null) continue;

                    var output = sourceComp.CurrentOutputByType(resourceId);
                    var maxOutput = sourceComp.MaxOutputByType(resourceId);

                    var availableOutput = maxOutput - output > totalSinkValue ? totalSinkValue : maxOutput - output;

                    if (availableOutput > 0)
                    {
                        sourceComp.SetOutputByType(resourceId, availableOutput);
                        totalSinkValue -= availableOutput;
                    }
                }
            }
        }

        private void ResetProducersInTankGroup(TankGroup tankGroup)
        {
            var resourceId = MyResourceDistributorComponent.HydrogenId;

            foreach (var tank in tankGroup.Tanks)
            {
                if (tank == null) continue;

                if (!tank.IsFunctional || !tank.Enabled || tank.Stockpile) continue;

                var sourceComp = tank.Components.Get<MyResourceSourceComponent>();

                if (sourceComp == null) continue;

                var output = sourceComp.CurrentOutputByType(resourceId);

                if (output > 0)
                {
                    sourceComp.SetOutputByType(resourceId, 0);
                }
            }

            foreach (var gasGen in tankGroup.HydrogenGens)
            {
                if (gasGen == null) continue;

                if (!gasGen.IsFunctional || !gasGen.Enabled) continue;

                var sourceComp = gasGen.Components.Get<MyResourceSourceComponent>();

                if (sourceComp == null) continue;

                var output = sourceComp.CurrentOutputByType(resourceId);

                if (output > 0)
                {
                    sourceComp.SetOutputByType(resourceId, 0);
                }
            }

            // add available from gas farms
            foreach (var gasFarm in tankGroup.HydrogenFarms)
            {
                if (gasFarm == null) continue;

                if (!gasFarm.IsFunctional || !gasFarm.Enabled) continue;

                var sourceComp = gasFarm.Components.Get<MyResourceSourceComponent>();

                if (sourceComp == null) continue;

                var output = sourceComp.CurrentOutputByType(resourceId);

                if (output > 0)
                {
                    sourceComp.SetOutputByType(resourceId, 0);
                }
            }
        }

        private void ProcessTankGroups()
        {
            if (!_tankGroups.Any())
            {
                GenerateTankGroupWithoutTanks();
                return;
            }

            foreach (var tankGroup in _tankGroups)
            {
                var hydrogenAddition = CalculateHydrogenConsumption(tankGroup);
                var hydroUsageInGroup = hydrogenAddition;

                // remove hydro from tanks in this group
                RemoveHydrogenFromTanks(tankGroup, hydroUsageInGroup);
            }
        }

        private void RemoveHydrogenFromTanks(TankGroup tankGroup, float usage)
        {
            var hasGas = false;
            var resourceId = MyResourceDistributorComponent.HydrogenId;
            tankGroup.SinkValue = 0;

            // add available from gas gens
            foreach (var gasGen in tankGroup.HydrogenGens)
            {
                if (gasGen == null) continue;

                if (!gasGen.IsFunctional || !gasGen.Enabled) continue;

                var sourceComp = gasGen.Components.Get<MyResourceSourceComponent>();

                if (sourceComp == null) continue;

                var output = sourceComp.CurrentOutputByType(resourceId);
                var maxOutput = sourceComp.MaxOutputByType(resourceId);
                var availableOutput = maxOutput - output > usage - tankGroup.SinkValue
                    ? usage - tankGroup.SinkValue
                    : maxOutput - output;

                if (maxOutput - output > 0)
                    hasGas = true;

                if (availableOutput > 0)
                {
                    tankGroup.SinkValue += availableOutput;
                }
            }

            // add available from gas farms
            foreach (var gasFarm in tankGroup.HydrogenFarms)
            {
                if (gasFarm == null) continue;

                if (!gasFarm.IsFunctional || !gasFarm.Enabled) continue;

                var sourceComp = gasFarm.Components.Get<MyResourceSourceComponent>();

                if (sourceComp == null) continue;

                var output = sourceComp.CurrentOutputByType(resourceId);
                var maxOutput = sourceComp.MaxOutputByType(resourceId);
                var availableOutput = maxOutput - output > usage - tankGroup.SinkValue
                    ? usage - tankGroup.SinkValue
                    : maxOutput - output;

                if (maxOutput - output > 0)
                    hasGas = true;

                if (availableOutput > 0)
                {
                    tankGroup.SinkValue += availableOutput;
                }
            }

            // add available from gas tanks
            foreach (var tank in tankGroup.Tanks)
            {
                if (tank == null) continue;

                if (!tank.IsFunctional || !tank.Enabled || tank.Stockpile) continue;

                var sinkComp = tank.Components.Get<MyResourceSinkComponent>();

                if (sinkComp == null) continue;

                double currentVolume = tank.FilledRatio * tank.Capacity;
                var availableOutput = currentVolume > usage - tankGroup.SinkValue
                    ? usage - tankGroup.SinkValue
                    : currentVolume;

                if (currentVolume > 0)
                    hasGas = true;

                if (availableOutput > 0)
                {
                    tankGroup.SinkValue += (float)availableOutput;
                }
            }

            // limit thrusters if usage is not fully satisfied
            float multiplier = tankGroup.SinkValue >= usage ? 1 : tankGroup.SinkValue / usage;
            if (tankGroup.SinkValue == 0 && usage == 0 && multiplier == 1 && !hasGas)
                multiplier = 0;

            // MyLog.Default.WriteLineAndConsole($"##MOD: FakeHydroGrid multiplier: {multiplier};{removedHydro};{usage};{tanksFunctional}");

            // multiplier represents how much hydrogen was fulfilled from the total demanded usage
            // multiplier goes from 0.0 -> 1.0 and we have to limit the thrusters in the tank group somehow somehow
            // if we don't have all the required hydrogen, and un-limit them when we do have enough hydrogen

            foreach (var thruster in tankGroup.Thrusters)
            {
                if (thruster == null) continue;

                if (multiplier == 0)
                {
                    if (thruster.ThrustMultiplier == 0 && !thruster.Enabled) continue;

                    if (_reducedThrusters.Contains(thruster))
                        _reducedThrusters.Remove(thruster);

                    thruster.ThrustMultiplier = 0;
                    thruster.Enabled = false;

                    _reducedThrusters.Add(thruster);
                }
                else
                {
                    if (thruster.ThrustMultiplier > multiplier)
                    {
                        thruster.ThrustMultiplier = multiplier; // Scale thrust

                        // track which thrusters we actually limited
                        if (_reducedThrusters.Contains(thruster))
                        {
                            _reducedThrusters.Remove(thruster);

                            if (!thruster.Enabled)
                                thruster.Enabled = true;
                        }

                        _reducedThrusters.Add(thruster);
                    }
                    else
                    {
                        // de-limit the thruster, but only if we were the ones to limit it in the first place
                        // this should prevent this code to interfere with other mods affecting ThrustMultiplier
                        if (_reducedThrusters.Contains(thruster))
                        {
                            thruster.ThrustMultiplier = multiplier;

                            _reducedThrusters.Remove(thruster);

                            if (!thruster.Enabled)
                                thruster.Enabled = true;

                            if (multiplier < 1)
                                _reducedThrusters.Add(thruster);
                        }
                    }
                }
            }
        }

        private float CalculateHydrogenConsumption(TankGroup tankGroup)
        {
            float consumption = 0;

            foreach (var thruster in tankGroup.Thrusters)
            {
                if (thruster == null) continue;

                if (!thruster.IsWorking || !thruster.IsFunctional) continue;

                var alteredThruster = FakeHydroSession.ThrusterTypes.Thrusters
                    .FirstOrDefault(t => t.SubtypeId == thruster.BlockDefinition.SubtypeId);

                if (alteredThruster != null)
                {
                    float finalConsumption = 0;

                    var thrustPercentage = thruster.CurrentThrust / thruster.MaxEffectiveThrust;

                    if (thrustPercentage == 0)
                    {
                        finalConsumption = alteredThruster.MinConsumption;
                    }
                    else
                    {
                        finalConsumption = alteredThruster.MaxConsumption * thrustPercentage;

                        if (finalConsumption < alteredThruster.MinConsumption)
                            finalConsumption = alteredThruster.MinConsumption;
                    }

                    consumption += finalConsumption;
                }
            }

            return consumption;
        }

        private void ScheduleBlockScan()
        {
            // If within the debounce interval, update the time but don't scan immediately
            if (DateTime.UtcNow - _lastBlockScanTime < _debounceInterval)
                return;

            _lastBlockScanTime = DateTime.UtcNow;

            // Defer the actual scan to be executed in the update loop
            _blockScanPending = true;
        }

        private void RunBlockScan()
        {
            _tankGroups.Clear();
            _noThrusters = true;

            // scan hydrogen tanks
            var terminalBlocks = Utils.GetBlocksOnSameGrid(_cubeGrid);

            // generate tank group for each tank
            foreach (var block in terminalBlocks)
            {
                var gasTank = block as IMyGasTank;

                // not gas tank
                if (gasTank == null) continue;

                // not hydro tank
                if (!Utils.IsHydrogenTank(gasTank)) continue;

                // if tank is in an existing group, skip
                if (_tankGroups.Any(t => t.Tanks.Contains(gasTank))) continue;

                // start new tank group
                var tankGroup = new TankGroup();
                tankGroup.Tanks.Add(gasTank);

                // generate connection to thrusters and other hydro tanks
                var connected = Utils.GetTanksGensFarmsThrusters(gasTank);

                // MyLog.Default.WriteLineAndConsole($"##MOD: FakeHydroGrid connected stuff: {connected.Count}");

                foreach (var terminalBlock in connected)
                {
                    var tank = terminalBlock as IMyGasTank;

                    // is another tank
                    if (tank != null)
                    {
                        // is hydro tank
                        if (!Utils.IsHydrogenTank(tank)) continue;

                        // not in any group
                        if (_tankGroups.Any(t => t.Tanks.Contains(tank))) continue;

                        tankGroup.Tanks.Add(tank);
                        continue;
                    }

                    var thruster = terminalBlock as IMyThrust;

                    // is thruster
                    if (thruster != null)
                    {
                        // is replaced thruster
                        if (!FakeHydroSession.IsAlteredThruster(thruster)) continue;

                        // not in any group
                        if (_tankGroups.Any(t => t.Thrusters.Contains(thruster))) continue;

                        tankGroup.Thrusters.Add(thruster);
                    }

                    var gasGen = terminalBlock as IMyGasGenerator;

                    // is gas generator
                    if (gasGen != null)
                    {
                        if (!Utils.IsHydrogenGen(gasGen)) continue;

                        // not in any group
                        if (_tankGroups.Any(t => t.HydrogenGens.Contains(gasGen))) continue;

                        tankGroup.HydrogenGens.Add(gasGen);
                    }

                    var gasFarm = terminalBlock as IMyOxygenFarm;

                    if (gasFarm != null)
                    {
                        if (!Utils.IsHydrogenFarm(gasFarm)) continue;

                        // not in any group
                        if (_tankGroups.Any(t => t.HydrogenFarms.Contains(gasFarm))) continue;

                        tankGroup.HydrogenFarms.Add(gasFarm);
                    }
                }

                _tankGroups.Add(tankGroup);
            }

            // now go through all gas gens and generate tank groups for each gas gens to cover the case that the tank group has no tank
            foreach (var block in terminalBlocks)
            {
                var gasGen = block as IMyGasGenerator;

                // not gas tank
                if (gasGen == null) continue;

                if (!Utils.IsHydrogenGen(gasGen)) continue;

                // not in any group
                if (_tankGroups.Any(t => t.HydrogenGens.Contains(gasGen))) continue;

                // start new tank group
                var tankGroup = new TankGroup();
                tankGroup.HydrogenGens.Add(gasGen);

                // we don't have to check tanks, we already know there aren't any
                var connected = Utils.GetGensFarmsThrusters(gasGen);
                foreach (var terminalBlock in connected)
                {
                    var thruster = terminalBlock as IMyThrust;

                    // is thruster
                    if (thruster != null)
                    {
                        // is replaced thruster
                        if (!FakeHydroSession.IsAlteredThruster(thruster)) continue;

                        // not in any group
                        if (_tankGroups.Any(t => t.Thrusters.Contains(thruster))) continue;

                        tankGroup.Thrusters.Add(thruster);
                    }

                    var otherGasGen = terminalBlock as IMyGasGenerator;

                    // is gas generator
                    if (otherGasGen != null)
                    {
                        if (!Utils.IsHydrogenGen(otherGasGen)) continue;

                        // not in any group
                        if (_tankGroups.Any(t => t.HydrogenGens.Contains(otherGasGen))) continue;

                        tankGroup.HydrogenGens.Add(otherGasGen);
                    }

                    var gasFarm = terminalBlock as IMyOxygenFarm;

                    if (gasFarm != null)
                    {
                        if (!Utils.IsHydrogenFarm(gasFarm)) continue;

                        // not in any group
                        if (_tankGroups.Any(t => t.HydrogenFarms.Contains(gasFarm))) continue;

                        tankGroup.HydrogenFarms.Add(gasFarm);
                    }
                }

                _tankGroups.Add(tankGroup);
            }

            // now go through all hydrogen farms and generate tank groups for each hydrogen farm to cover the case that the tank group has no tank
            foreach (var block in terminalBlocks)
            {
                var farmBlock = block as IMyOxygenFarm;

                // not gas tank
                if (farmBlock == null) continue;

                if (!Utils.IsHydrogenFarm(farmBlock)) continue;

                // not in any group
                if (_tankGroups.Any(t => t.HydrogenFarms.Contains(farmBlock))) continue;

                // start new tank group
                var tankGroup = new TankGroup();
                tankGroup.HydrogenFarms.Add(farmBlock);

                // we don't have to check tanks and gas gens, we already know there aren't any
                var connected = Utils.GetFarmsThrusters(farmBlock);
                foreach (var terminalBlock in connected)
                {
                    var thruster = terminalBlock as IMyThrust;

                    // is thruster
                    if (thruster != null)
                    {
                        // is replaced thruster
                        if (!FakeHydroSession.IsAlteredThruster(thruster)) continue;

                        // not in any group
                        if (_tankGroups.Any(t => t.Thrusters.Contains(thruster))) continue;

                        tankGroup.Thrusters.Add(thruster);
                    }

                    var gasFarm = terminalBlock as IMyOxygenFarm;

                    if (gasFarm != null)
                    {
                        if (!Utils.IsHydrogenFarm(gasFarm)) continue;

                        // not in any group
                        if (_tankGroups.Any(t => t.HydrogenFarms.Contains(gasFarm))) continue;

                        tankGroup.HydrogenFarms.Add(gasFarm);
                    }
                }

                _tankGroups.Add(tankGroup);
            }

            foreach (var group in _tankGroups)
            {
                if (group.Thrusters.Any())
                {
                    _noThrusters = false;
                    break;
                }
            }
        }

        private void GenerateTankGroupWithoutTanks()
        {
            var terminalBlocks = Utils.GetBlocksOnSameGrid(_cubeGrid);
            _noThrusters = true;

            // start new tank group
            var tankGroup = new TankGroup();

            foreach (var block in terminalBlocks)
            {
                var thruster = block as IMyThrust;

                if (thruster != null)
                {
                    // is replaced thruster
                    if (!FakeHydroSession.IsAlteredThruster(thruster)) continue;

                    // not in any group
                    if (_tankGroups.Any(t => t.Thrusters.Contains(thruster))) continue;

                    tankGroup.Thrusters.Add(thruster);
                    
                    if (_noThrusters)
                        _noThrusters = false;
                }

                // MyLog.Default.WriteLine($"##MOD: FakeHydro tankGroup created: {tankGroup.Tanks.Count};{tankGroup.Thrusters.Count}");
            }

            _tankGroups.Add(tankGroup);
        }
    }
}