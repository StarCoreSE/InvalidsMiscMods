using System;
using System.Collections.Generic;
using System.Diagnostics;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;
using System.Linq;

[MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
public class BlockDebuggerComponent : MySessionComponentBase
{
    private const ushort NET_ID = 9972; // Unique network ID
    private const string COMMAND = "/blockdebug";
    private const string TempGridDisplayName = "BlockDebugger_TemporaryGrid";
    private List<long> spawnTimes = new List<long>();
    private long minSpawnTime = long.MaxValue;
    private long maxSpawnTime = long.MinValue;

    public override void LoadData()
    {
        MyAPIGateway.Utilities.MessageEntered += OnMessageEntered;
        MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(NET_ID, NetworkHandler);
    }

    protected override void UnloadData()
    {
        MyAPIGateway.Utilities.MessageEntered -= OnMessageEntered;
        MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(NET_ID, NetworkHandler);
    }

    private void OnMessageEntered(string messageText, ref bool sendToOthers)
    {
        if (!messageText.Equals(COMMAND, StringComparison.InvariantCultureIgnoreCase)) return;

        sendToOthers = false;  // Don't send this command to chat

        if (!MyAPIGateway.Multiplayer.IsServer)
        {
            MyAPIGateway.Utilities.ShowNotification("BlockDebug: Client detected. Sending request to server!", 5000);
            MyAPIGateway.Multiplayer.SendMessageToServer(NET_ID, new byte[1]);
        }
        else
        {
            MyAPIGateway.Utilities.ShowNotification("BlockDebug: Server detected. Executing locally!", 5000);
            ExecuteDebugSpawn();
        }
    }

    private void NetworkHandler(ushort handler, byte[] data, ulong steamId, bool isFromServer)
    {
        if (MyAPIGateway.Multiplayer.IsServer)
        {
            MyAPIGateway.Utilities.ShowNotification($"BlockDebug: Request received from {steamId}. Executing on server!", 5000);
            ExecuteDebugSpawn();
        }
        else
        {
            MyAPIGateway.Utilities.ShowNotification("BlockDebug: Network message received by client. This shouldn't happen!", 5000, "Red");
        }
    }

    private void ExecuteDebugSpawn()
    {
        MyAPIGateway.Utilities.ShowNotification("BlockDebug: Starting debug spawn process...", 5000);

        try
        {
            MatrixD camMatrix = MyAPIGateway.Session.Camera.WorldMatrix;
            Vector3D playerPosition = camMatrix.Translation;

            var blockDefinitions = MyDefinitionManager.Static.GetAllDefinitions().OfType<MyCubeBlockDefinition>().Where(def => def.Public).ToList();
            int totalBlocks = blockDefinitions.Count;

            if (totalBlocks == 0)
            {
                MyAPIGateway.Utilities.ShowNotification("BlockDebug: No public block definitions found.", 5000, "Red");
                return;
            }

            // Calculate the size of the 3D grid
            int gridSize = (int)Math.Ceiling(Math.Pow(totalBlocks, 1.0 / 3.0));
            double spacing = 10.0; // Adjust the spacing as needed

            int index = 0;
            foreach (var blockDef in blockDefinitions)
            {
                int x = index % gridSize;
                int y = (index / gridSize) % gridSize;
                int z = index / (gridSize * gridSize);

                Vector3D offset = new Vector3D(x * spacing, y * spacing, z * spacing);
                Vector3D spawnPosition = playerPosition + offset;

                // Create and spawn a new grid for this block
                var tempGridSpawn = new TempGridSpawn(blockDef, spawnPosition, TempGridDisplayName, OnGridSpawned);
                index++;
            }
        }
        catch (Exception e)
        {
            MyAPIGateway.Utilities.ShowNotification($"BlockDebug: Exception during grid spawn - {e.Message}", 10000, "Red");
        }
    }

    private void OnGridSpawned(IMySlimBlock block, long spawnTime)
    {
        if (block != null)
        {
            // Update min and max spawn times
            spawnTimes.Add(spawnTime);
            minSpawnTime = Math.Min(minSpawnTime, spawnTime);

            // Update the maxSpawnTime to be the average of the top three times
            var topThreeTimes = spawnTimes.OrderByDescending(t => t).Take(3).ToList();
            if (topThreeTimes.Count > 0)
            {
                maxSpawnTime = (long)topThreeTimes.Average();
            }

            // Calculate the color based on normalized time
            Vector3 colorHSV = GetColorBasedOnNormalizedTime(spawnTime, minSpawnTime, maxSpawnTime);
            IMyCubeGrid grid = block.CubeGrid;

            // Color the blocks in the grid
            var blocks = new List<IMySlimBlock>();
            grid.GetBlocks(blocks, null); // Get all blocks

            foreach (var slimBlock in blocks)
            {
                Vector3I minPos = slimBlock.Min;
                Vector3I maxPos = slimBlock.Min;
                grid.ColorBlocks(minPos, maxPos, colorHSV);
            }

            MyAPIGateway.Utilities.ShowNotification($"BlockDebug: Spawned grid with block {block.BlockDefinition.Id} at {block.CubeGrid.GetPosition()} in {spawnTime} ms", 5000);
        }
        else
        {
            MyAPIGateway.Utilities.ShowNotification("BlockDebug: Failed to spawn block!", 5000, "Red");
        }
    }

    private class TempGridSpawn
    {
        private readonly Action<IMySlimBlock, long> Callback;
        private readonly Stopwatch stopwatch;

        public TempGridSpawn(MyCubeBlockDefinition blockDef, Vector3D playerPosition, string gridName, Action<IMySlimBlock, long> callback)
        {
            Callback = callback;
            stopwatch = new Stopwatch();
            stopwatch.Start(); // Start timing the spawn process

            var gridOB = MyObjectBuilderSerializer.CreateNewObject<MyObjectBuilder_CubeGrid>();
            gridOB.EntityId = 0;
            gridOB.DisplayName = gridName + "_" + blockDef.DisplayNameText;
            gridOB.CreatePhysics = true;
            gridOB.GridSizeEnum = blockDef.CubeSize;
            gridOB.PersistentFlags = MyPersistentEntityFlags2.InScene;
            gridOB.IsStatic = false;
            gridOB.Editable = true;
            gridOB.DestructibleBlocks = true;
            gridOB.IsRespawnGrid = false;

            var blockOB = MyObjectBuilderSerializer.CreateNewObject(blockDef.Id) as MyObjectBuilder_CubeBlock;
            if (blockOB != null)
            {
                blockOB.Min = Vector3I.Zero;  // Position the block at the grid's origin

                gridOB.PositionAndOrientation = new MyPositionAndOrientation(playerPosition, Vector3.Forward, Vector3.Up);
                gridOB.CubeBlocks.Add(blockOB);

                MyAPIGateway.Entities.CreateFromObjectBuilderParallel(gridOB, true, OnGridCreated);
            }
        }

        private void OnGridCreated(IMyEntity ent)
        {
            stopwatch.Stop(); // Stop timing the spawn process
            IMyCubeGrid grid = ent as IMyCubeGrid;
            if (grid == null)
            {
                MyAPIGateway.Utilities.ShowNotification("BlockDebug: Failed to create grid!", 5000, "Red");
                return;
            }

            var blocks = new List<IMySlimBlock>();
            grid.GetBlocks(blocks, null); // Fill the list with blocks

            if (blocks.Count > 0)
            {
                Callback?.Invoke(blocks[0], stopwatch.ElapsedMilliseconds); // Use the first block to represent the grid
            }
        }
    }

    private Vector3 GetColorBasedOnNormalizedTime(long spawnTime, long minTime, long maxTime)
    {
        if (maxTime == minTime)
        {
            // If all times are the same, return green as default
            return new Vector3(0.33f, 1f, 1f);
        }

        // Normalize the spawn time between 0 (minTime) and 1 (maxTime)
        float normalized = (float)(spawnTime - minTime) / (maxTime - minTime);

        // Map the normalized time to a hue (0 = red, 0.33 = green)
        float hue = MathHelper.Lerp(0f, 0.33f, 1f - normalized); // Invert so shorter times are greener
        return new Vector3(hue, 1f, 1f);  // Full saturation and value for bright color
    }

}
