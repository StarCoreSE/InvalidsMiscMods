using System;
using System.Collections.Generic;
using System.Linq;
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

[MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
public class BlockDebuggerComponent : MySessionComponentBase
{
    private const ushort NET_ID = 9972; // Use a unique ID. Random number between 10-60000.
    private const string COMMAND = "/blockdebug";

    public override void LoadData()
    {
        MyAPIGateway.Utilities.MessageEntered += OnMessageEntered;
        MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(NET_ID, NetworkHandler);
       // MyAPIGateway.Utilities.ShowNotification("[BlockDebugger] Loaded! '/blockdebug' to use.", 3000);
    }

    private void OnMessageEntered(string messageText, ref bool sendToOthers)
    {
        if (!messageText.Equals(COMMAND, StringComparison.InvariantCultureIgnoreCase)) return;

        sendToOthers = false;  // Don't send this command to chat

        // Send request to server
        if (!MyAPIGateway.Multiplayer.IsServer)
        {
            MyAPIGateway.Utilities.ShowNotification("BlockDebug: Client detected. Sending request to server!", 5000);
            MyAPIGateway.Multiplayer.SendMessageToServer(NET_ID, new byte[1]);  // Sending an empty byte array
        }
        else  // If we're on a local game, just execute
        {
            MyAPIGateway.Utilities.ShowNotification("BlockDebug: Server detected. Executing locally!", 5000);
            ExecuteDebugSpawn();
        }
    }

    private void NetworkHandler(ushort handler, byte[] data, ulong steamId, bool isFromServer)
    {
        if (MyAPIGateway.Multiplayer.IsServer)  // Only server should execute
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

        Vector3D spawnPos = MyAPIGateway.Session.Camera.WorldMatrix.Translation + MyAPIGateway.Session.Camera.WorldMatrix.Backward * 500;

        var gridOB = MyObjectBuilderSerializer.CreateNewObject<MyObjectBuilder_CubeGrid>();
        gridOB.CreatePhysics = false;
        gridOB.GridSizeEnum = MyCubeSize.Large;
        gridOB.PositionAndOrientation = new MyPositionAndOrientation(spawnPos, Vector3.Forward, Vector3.Up);

        int idx = 0;
        var blockList = new List<MyObjectBuilder_CubeBlock>();

        foreach (var def in MyDefinitionManager.Static.GetAllDefinitions())
        {
            MyCubeBlockDefinition blockDef = def as MyCubeBlockDefinition;
            if (blockDef != null && blockDef.Public)
            {
                var blockOB = MyObjectBuilderSerializer.CreateNewObject(blockDef.Id) as MyObjectBuilder_CubeBlock;
                blockOB.Min = new Vector3I(idx++, 0, 0);  // Line them up
                blockList.Add(blockOB);
            }
        }

        MyAPIGateway.Utilities.ShowNotification($"BlockDebug: Found {blockList.Count} public blocks. Now spawning...", 5000);

        gridOB.CubeBlocks = blockList;

        MyAPIGateway.Entities.CreateFromObjectBuilderParallel(gridOB, true,
            (IMyEntity ent) =>
            {
                if (ent == null)
                {
                    MyAPIGateway.Utilities.ShowNotification("BlockDebug: Entity spawn failed completely!", 5000, "Red");
                    return;
                }

                IMyCubeGrid grid = ent as IMyCubeGrid;
                if (grid == null)
                {
                    MyAPIGateway.Utilities.ShowNotification("BlockDebug: Spawned entity isn't a grid!", 5000, "Red");
                    return;
                }

                if (grid.Physics == null)
                {
                    MyAPIGateway.Utilities.ShowNotification("BlockDebug: Grid has no physics object!", 5000, "Red");
                    return;
                }

                try
                {
                    grid.Physics.Enabled = true;
                    grid.Physics.Clear();

                   // MyAPIGateway.Utilities.ShowNotification($"BlockDebug: Spawned blocks at {grid.GetPosition().ToString("0.0")}", 10000);
                }
                catch (Exception e)
                {
                    MyAPIGateway.Utilities.ShowNotification($"BlockDebug: {e.Message}", 10000, "Red");
                }
            });

        MyAPIGateway.Utilities.ShowNotification("BlockDebug: Spawn process completed.", 5000);
    }
}