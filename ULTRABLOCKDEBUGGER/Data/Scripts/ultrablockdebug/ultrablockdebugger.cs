using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
using VRage.Library.Utils;
using System.Text;

[MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
public class BlockDebuggerComponent : MySessionComponentBase
{
    private const ushort NET_ID = 9972;
    private const string DEBUG_COMMAND = "/blockdebug";
    private const string PROFILE_COMMAND = "/blockdebugprofile";
    private const string TempGridDisplayName = "BlockDebugger_TemporaryGrid";
    private const int PROFILE_ITERATIONS = 10;
    private const string LOG_FILE_NAME = "blockdebugprofile.log";  // Notice .cfg instead of .xml


    private List<long> spawnTimes = new List<long>();
    private long minSpawnTime = long.MaxValue;
    private long maxSpawnTime = long.MinValue;
    private Dictionary<string, long> blockProfileTimes = new Dictionary<string, long>();

    private readonly HashSet<string> excludedSubtypePrefixes = new HashSet<string>
    {
        "EntityCover",
        "Billboard",
    };

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
        if (messageText.Equals(DEBUG_COMMAND, StringComparison.InvariantCultureIgnoreCase))
        {
            sendToOthers = false;
            ExecuteCommand(ExecuteDebugSpawn);
        }
        else if (messageText.Equals(PROFILE_COMMAND, StringComparison.InvariantCultureIgnoreCase))
        {
            sendToOthers = false;
            ExecuteCommand(ExecuteProfileSpawn);
        }
    }

    private void ExecuteCommand(Action action)
    {
        if (!MyAPIGateway.Multiplayer.IsServer)
        {
            MyAPIGateway.Utilities.ShowNotification("BlockDebug: Client detected. Sending request to server!", 5000);
            MyAPIGateway.Multiplayer.SendMessageToServer(NET_ID, new byte[1]);
        }
        else
        {
            MyAPIGateway.Utilities.ShowNotification("BlockDebug: Server detected. Executing locally!", 5000);
            action();
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
        SpawnBlocks(false);
    }

    private void ExecuteProfileSpawn()
    {
        MyAPIGateway.Utilities.ShowNotification("BlockDebug: Starting profiling process...", 5000);
        blockProfileTimes.Clear();  // Reset profile times before each profile run

        // Do a first-pass to collect data
        SpawnBlocks(true);

        // Wait 2 seconds, then color & output
        MyAPIGateway.Utilities.InvokeOnGameThread(() =>
        {
            ColorProfiledBlocks();
            WriteProfileResultsToFile();
        }, "BlockDebugProfile", 2000);
    }

    private void SpawnBlocks(bool isProfileRun)
    {
        try
        {
            MatrixD camMatrix = MyAPIGateway.Session.Camera.WorldMatrix;
            Vector3D playerPosition = camMatrix.Translation;

            var blockDefinitions = MyDefinitionManager.Static.GetAllDefinitions()
                .OfType<MyCubeBlockDefinition>()
                .Where(def => def.Public && !ShouldExcludeSubtype(def.Id.SubtypeName))
                .ToList();
            int totalBlocks = blockDefinitions.Count;

            if (totalBlocks == 0)
            {
                MyAPIGateway.Utilities.ShowNotification("BlockDebug: No public block definitions found.", 5000, "Red");
                return;
            }

            int gridSize = (int)Math.Ceiling(Math.Pow(totalBlocks, 1.0 / 3.0));
            double spacing = 75.0;

            for (int index = 0; index < totalBlocks; index++)
            {
                var blockDef = blockDefinitions[index];
                int x = index % gridSize;
                int y = (index / gridSize) % gridSize;
                int z = index / (gridSize * gridSize);

                Vector3D offset = new Vector3D(
                    (x * spacing) + (MyRandom.Instance.NextDouble() * 10),
                    (y * spacing) + (MyRandom.Instance.NextDouble() * 10),
                    (z * spacing) + (MyRandom.Instance.NextDouble() * 10)
                );
                Vector3D spawnPosition = playerPosition + offset;

                if (isProfileRun)
                {
                    new TempGridSpawn(blockDef, spawnPosition, TempGridDisplayName + "_Profile", OnProfileGridSpawned);
                }
                else
                {
                    new TempGridSpawn(blockDef, spawnPosition, TempGridDisplayName, OnGridSpawned);
                }
            }

            if (!isProfileRun)
            {
                int excludedCount = MyDefinitionManager.Static.GetAllDefinitions()
                    .OfType<MyCubeBlockDefinition>()
                    .Count(def => def.Public && ShouldExcludeSubtype(def.Id.SubtypeName));

                MyAPIGateway.Utilities.ShowNotification($"BlockDebug: Attempted to spawn {totalBlocks} blocks. {excludedCount} blocks were excluded based on {excludedSubtypePrefixes.Count} prefixes.", 10000);
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
            spawnTimes.Add(spawnTime);
            minSpawnTime = Math.Min(minSpawnTime, spawnTime);
            var topThreeTimes = spawnTimes.OrderByDescending(t => t).Take(3).ToList();
            if (topThreeTimes.Count > 0)
            {
                maxSpawnTime = (long)topThreeTimes.Average();
            }

            Vector3 colorHSV = GetColorBasedOnNormalizedTime(spawnTime, minSpawnTime, maxSpawnTime);
            IMyCubeGrid grid = block.CubeGrid;

            var blocks = new List<IMySlimBlock>();
            grid.GetBlocks(blocks, null);

            foreach (var slimBlock in blocks)
            {
                grid.ColorBlocks(slimBlock.Min, slimBlock.Max, colorHSV);
            }

            MyAPIGateway.Utilities.ShowNotification($"BlockDebug: Spawned grid with block {block.BlockDefinition.Id} at {block.CubeGrid.GetPosition()} in {spawnTime} ms", 5000);
        }
        else
        {
            MyAPIGateway.Utilities.ShowNotification("BlockDebug: Failed to spawn block!", 5000, "Red");
        }
    }

    private void OnProfileGridSpawned(IMySlimBlock block, long spawnTime)
    {
        if (block != null)
        {
            string key = block.BlockDefinition.Id.ToString();
            if (!blockProfileTimes.ContainsKey(key))
            {
                blockProfileTimes[key] = spawnTime;
            }
            else
            {
                blockProfileTimes[key] = Math.Min(blockProfileTimes[key], spawnTime);
            }
        }
    }

    private void ColorProfiledBlocks()
    {
        if (blockProfileTimes.Count == 0)
        {
            MyAPIGateway.Utilities.ShowNotification("BlockDebug: No profile data available to color blocks.", 10000, "Red");
            return;
        }

        var minTime = blockProfileTimes.Values.Min();
        var maxTime = blockProfileTimes.Values.Max();

        foreach (var entity in MyEntities.GetEntities())
        {
            var grid = entity as IMyCubeGrid;
            if (grid != null && grid.DisplayName.StartsWith(TempGridDisplayName + "_Profile"))
            {
                var blocks = new List<IMySlimBlock>();
                grid.GetBlocks(blocks, null);
                if (blocks.Count > 0)
                {
                    var block = blocks[0];
                    string key = block.BlockDefinition.Id.ToString();
                    long time;
                    if (blockProfileTimes.TryGetValue(key, out time))
                    {
                        Vector3 colorHSV = GetColorBasedOnNormalizedTime(time, minTime, maxTime);
                        grid.ColorBlocks(block.Min, block.Max, colorHSV);
                    }
                }
            }
        }

        MyAPIGateway.Utilities.ShowNotification("BlockDebug: Profiling complete. Blocks colored based on spawn time.", 10000);
    }

    private void WriteProfileResultsToFile()
    {
        try
        {
            using (TextWriter writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(LOG_FILE_NAME, typeof(BlockDebuggerComponent)))
            {
                if (writer == null)
                {
                    MyAPIGateway.Utilities.ShowNotification("BlockDebug: Can't create output! The writer is null!", 10000, "Red");
                    return;
                }

                writer.WriteLine($"// Block Debug Profile: {DateTime.Now}");
                writer.WriteLine($"// Total Blocks Profiled: {blockProfileTimes.Count}");
                writer.WriteLine($"// Fastest: {blockProfileTimes.Values.Min()}ms   |   Slowest: {blockProfileTimes.Values.Max()}ms");
                writer.WriteLine($"// Average: {blockProfileTimes.Values.Average():0.00}ms");
                writer.WriteLine("// ====================================");

                foreach (var report in blockProfileTimes.OrderBy(x => x.Value))
                {
                    writer.WriteLine($"{report.Key}: {report.Value:0.00}ms");
                }
            }

            MyAPIGateway.Utilities.ShowNotification($"BlockDebug: Profiler log written to local storage", 10000, "White");
        }
        catch (Exception e)
        {
            MyAPIGateway.Utilities.ShowNotification($"Profile write failed: {e.Message}", 10000, "Red");
        }
    }

    private bool ShouldExcludeSubtype(string subtypeName)
    {
        return excludedSubtypePrefixes.Any(prefix => subtypeName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private Vector3 GetColorBasedOnNormalizedTime(long spawnTime, long minTime, long maxTime)
    {
        if (maxTime == minTime)
        {
            return new Vector3(0.33f, 1f, 1f);
        }

        float normalized = (float)(spawnTime - minTime) / (maxTime - minTime);
        float hue = MathHelper.Lerp(0.33f, 0f, normalized);
        return new Vector3(hue, 1f, 1f);
    }

    private class TempGridSpawn
    {
        private readonly Action<IMySlimBlock, long> Callback;
        private readonly Stopwatch stopwatch;

        public TempGridSpawn(MyCubeBlockDefinition blockDef, Vector3D playerPosition, string gridName, Action<IMySlimBlock, long> callback)
        {
            Callback = callback;
            stopwatch = new Stopwatch();
            stopwatch.Start();

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
                blockOB.Min = Vector3I.Zero;
                gridOB.PositionAndOrientation = new MyPositionAndOrientation(playerPosition, Vector3.Forward, Vector3.Up);
                gridOB.CubeBlocks.Add(blockOB);

                MyAPIGateway.Entities.CreateFromObjectBuilderParallel(gridOB, true, OnGridCreated);
            }
        }

        private void OnGridCreated(IMyEntity ent)
        {
            stopwatch.Stop();
            IMyCubeGrid grid = ent as IMyCubeGrid;
            if (grid == null)
            {
                MyAPIGateway.Utilities.ShowNotification("BlockDebug: Failed to create grid!", 5000, "Red");
                return;
            }

            var blocks = new List<IMySlimBlock>();
            grid.GetBlocks(blocks, null);

            if (blocks.Count > 0)
            {
                Callback?.Invoke(blocks[0], stopwatch.ElapsedMilliseconds);
            }
        }
    }
}