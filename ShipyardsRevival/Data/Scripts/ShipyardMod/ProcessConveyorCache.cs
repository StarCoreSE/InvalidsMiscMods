using System.Collections.Generic;
using System.Linq;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.GameSystems;
using Sandbox.ModAPI;
using ShipyardMod.ItemClasses;
using ShipyardMod.Utility;
using VRage.Game.Entity;
using VRage.Game.ModAPI;

namespace ShipyardMod.ProcessHandlers
{
    public class ProcessConveyorCache : ProcessHandlerBase
    {
        public override int GetUpdateResolution()
        {
            Logging.Instance.WriteDebug($"ProcessConveyorCache.GetUpdateResolution called, returning 5000 ms.");
            return 5000;
        }

        public override bool ServerOnly()
        {
            return true;
        }

        private int _currentShipyardIndex = 0;

        public override void Handle()
        {
            if (ProcessShipyardDetection.ShipyardsList.Count == 0)
            {
                Logging.Instance.WriteDebug("No shipyards in ShipyardsList. Exiting Handle.");
                return;
            }

            int index = _currentShipyardIndex % ProcessShipyardDetection.ShipyardsList.Count;
            ShipyardItem currentItem = ProcessShipyardDetection.ShipyardsList.ElementAtOrDefault(index);
            if (currentItem == null)
            {
                Logging.Instance.WriteDebug($"No shipyard found at index {index}. Incrementing _currentShipyardIndex.");
                _currentShipyardIndex++;
                return;
            }

            var grid = (IMyCubeGrid)currentItem.YardEntity;

            if (grid.Physics == null || grid.Closed || currentItem.YardType == ShipyardType.Invalid)
            {
                Logging.Instance.WriteDebug($"Invalid or closed grid for shipyard {currentItem.EntityId}. Clearing ConnectedCargo.");
                currentItem.ConnectedCargo.Clear();
                _currentShipyardIndex++;
                return;
            }

            Logging.Instance.WriteDebug($"Processing shipyard {currentItem.EntityId} at index {index}.");

            var gts = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid);
            var blocks = new List<IMyTerminalBlock>();
            gts.GetBlocks(blocks);

            var cornerInventory = (IMyInventory)((MyEntity)currentItem.Tools[0]).GetInventory();
            var disconnectedInventories = new HashSet<IMyTerminalBlock>();

            foreach (var block in currentItem.ConnectedCargo)
            {
                if (block.Closed || !blocks.Contains(block))
                    disconnectedInventories.Add(block);
            }

            Logging.Instance.WriteDebug($"Disconnected inventories identified: {disconnectedInventories.Count} for shipyard {currentItem.EntityId}.");

            foreach (var dis in disconnectedInventories)
            {
                currentItem.ConnectedCargo.Remove(dis);
                Logging.Instance.WriteDebug($"Removed disconnected block {dis.CustomName} from ConnectedCargo.");
            }

            var newConnections = new HashSet<IMyTerminalBlock>();
            Utilities.InvokeBlocking(() =>
            {
                Logging.Instance.WriteDebug("InvokeBlocking started for connectivity check.");

                foreach (IMyTerminalBlock cargo in currentItem.ConnectedCargo)
                {
                    if (cornerInventory == null)
                    {
                        Logging.Instance.WriteDebug("Corner inventory is null, aborting connectivity check.");
                        return;
                    }

                    if (!cornerInventory.IsConnectedTo(((MyEntity)cargo).GetInventory()))
                        disconnectedInventories.Add(cargo);
                }

                foreach (var block in blocks)
                {
                    if (disconnectedInventories.Contains(block) || currentItem.ConnectedCargo.Contains(block))
                        continue;

                    if (block.BlockDefinition.SubtypeName.Contains("ShipyardCorner") || block is IMyReactor || block is IMyGasGenerator || block is IMyGasTank)
                        continue;

                    if (((MyEntity)block).HasInventory && cornerInventory.IsConnectedTo(((MyEntity)block).GetInventory()))
                    {
                        newConnections.Add(block);
                        Logging.Instance.WriteDebug($"Added new connection: {block.CustomName}");
                    }
                }

                Logging.Instance.WriteDebug("InvokeBlocking completed for connectivity check.");
            });

            foreach (IMyTerminalBlock removeBlock in disconnectedInventories)
            {
                currentItem.ConnectedCargo.Remove(removeBlock);
                Logging.Instance.WriteDebug($"Disconnected block removed: {removeBlock.CustomName}");
            }

            foreach (IMyTerminalBlock newBlock in newConnections)
            {
                currentItem.ConnectedCargo.Add(newBlock);
                Logging.Instance.WriteDebug($"Newly connected block added: {newBlock.CustomName}");
            }

            _currentShipyardIndex++;
            Logging.Instance.WriteDebug($"Completed processing for shipyard {currentItem.EntityId}. Moving to next index {_currentShipyardIndex}.");
        }
    }
}
