using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Game;
using Sandbox.Game.EntityComponents;
using SpaceEngineers.Game.ModAPI;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;
using IMyTerminalBlock = Sandbox.ModAPI.IMyTerminalBlock;

namespace SurvivalStats
{
    public class Utils
    {
        
        public static List<IMyTerminalBlock> GetTanksGensFarmsThrusters(IMyTerminalBlock startBlock, bool onlyOne = false)
        {
            // filter only specific blocks and get the first one, we don't need more
            return GetConveyorConnectedBlocks(startBlock,
                block => block is IMyGasTank || block is IMyThrust || block is IMyGasGenerator ||
                         block is IMyOxygenFarm, onlyOne);
        }
        
        public static List<IMyTerminalBlock> GetGensFarmsThrusters(IMyTerminalBlock startBlock, bool onlyOne = false)
        {
            // filter only specific blocks and get the first one, we don't need more
            return GetConveyorConnectedBlocks(startBlock,
                block => block is IMyThrust || block is IMyGasGenerator || block is IMyOxygenFarm, onlyOne);
        }
        
        public static List<IMyTerminalBlock> GetFarmsThrusters(IMyTerminalBlock startBlock, bool onlyOne = false)
        {
            // filter only specific blocks and get the first one, we don't need more
            return GetConveyorConnectedBlocks(startBlock, block => block is IMyThrust || block is IMyOxygenFarm,
                onlyOne);
        }
        
        public static List<IMyTerminalBlock> GetConveyorConnectedBlocks(IMyTerminalBlock startBlock, Func<IMyTerminalBlock, bool> filter = null, bool onlyOne = false)
        {
            // Cache all blocks on the same grid
            var allBlocksOnGrid = GetBlocksOnSameGrid(startBlock.CubeGrid);
        
            var connectedBlocks = new List<IMyTerminalBlock>();
        
            // Iterate through all blocks on the same grid and check connectivity
            foreach (var block in allBlocksOnGrid)
            {
                if (block == startBlock)
                    continue;
        
                // Check if the block is conveyor-connected to the startBlock
                var isConnected = MyVisualScriptLogicProvider.IsConveyorConnected(startBlock.Name, block.Name);
        
                if (isConnected)
                {
                    // Apply the filter, if provided
                    if (filter == null || filter(block))
                    {
                        connectedBlocks.Add(block);
        
                        // If onlyOne is true, return immediately with the first matching block
                        if (onlyOne)
                            return connectedBlocks;
                    }
                }
            }
        
            return connectedBlocks;
        }
        
        public static List<IMyTerminalBlock> GetBlocksOnSameGrid(IMyCubeGrid cubeGrid)
        {
            if (cubeGrid == null)
                return new List<IMyTerminalBlock>();

            // Collect slim blocks on the grid
            var blocksOnGrid = new List<IMySlimBlock>();
            cubeGrid.GetBlocks(blocksOnGrid, b => b != null);

            // Filter to return only terminal blocks
            var terminalBlocks = new List<IMyTerminalBlock>();
            foreach (var slimBlock in blocksOnGrid)
            {
                var fatBlock = slimBlock.FatBlock;
                if (fatBlock is IMyTerminalBlock)
                {
                    terminalBlocks.Add((IMyTerminalBlock)fatBlock);
                }
            }

            return terminalBlocks;
        }

        public static bool IsHydrogenTank(IMyGasTank gasTank)
        {
            var sinkComp = gasTank.Components.Get<MyResourceSinkComponent>();

            if (sinkComp == null) return false;

            return sinkComp.AcceptedResources.Contains(MyResourceDistributorComponent.HydrogenId);
        }

        public static bool IsHydrogenGen(IMyGasGenerator gasGen)
        {
            var sourceComp = gasGen.Components.Get<MyResourceSourceComponent>();

            if (sourceComp == null) return false;
            
            return sourceComp.ResourceTypes.Contains(MyResourceDistributorComponent.HydrogenId);
        }

        public static bool IsHydrogenFarm(IMyOxygenFarm gasFarm)
        {
            var sourceComp = gasFarm.Components.Get<MyResourceSourceComponent>();

            if (sourceComp == null) return false;
            
            return sourceComp.ResourceTypes.Contains(MyResourceDistributorComponent.HydrogenId);
        }
        
        public static Vector3I GetThrusterDirection(IMyThrust thruster)
        {
            return Vector3I.Round(Vector3D.TransformNormal(thruster.WorldMatrix.Forward, thruster.CubeGrid.WorldMatrixNormalizedInv));
        }

    }
}