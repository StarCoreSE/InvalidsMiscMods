using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ProtoBuf;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;
using VRageRender;
using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;

namespace Munashe.BlockSwapper
{
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public class BlockSwapper_Session : MySessionComponentBase
    {
        public static BlockSwapper_Session Instance;
        private ushort NetworkId;
        public bool Debug = true;
        private readonly string commandPrefix = "/bs";
        private readonly float rangeLimit = 150.0f;
        private HashSet<string> validSubtypes = new HashSet<string>();

        private List<MyBillboard> PersistBillboard = new List<MyBillboard>();
        private TextWriter logger;
        private static readonly object logLock = new object();

        private Dictionary<string, string> usageDictionary = new Dictionary<string, string>
        {
            ["help"] = "",
            ["replace"] = "replace {targetSubtype} {replacementSubtype}",
            ["delete"] = "delete {targetSubtype}",
        };

        private TextWriter GetLogger()
        {
            if (logger == null)
            {
                lock (logLock)
                {
                    if (logger == null)
                    {
                        try
                        {
                            logger = MyAPIGateway.Utilities.WriteFileInLocalStorage("log.txt", typeof(BlockSwapper_Session));
                            logger.WriteLine($"Log initialized: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                            logger.Flush();
                        }
                        catch (Exception e)
                        {
                            MyLog.Default.WriteLineAndConsole($"BlockSwapper: Failed to initialize log: {e.Message}");
                        }
                    }
                }
            }
            return logger;
        }

        public void Log(string line)
        {
            lock (logLock)
            {
                try
                {
                    var logWriter = GetLogger();
                    logWriter.WriteLine(line);
                    logWriter.Flush();
                }
                catch (Exception e)
                {
                    MyLog.Default.WriteLineAndConsole($"BlockSwapper: Failed writing to log: {e.Message}");
                }
            }
        }

        public override void LoadData()
        {
            Instance = this;
            NetworkId = 38271;
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(NetworkId, ReceivedPacket);
            if (MyAPIGateway.Utilities.IsDedicated)
            {

            }
            else
            {
                MyAPIGateway.Utilities.MessageEnteredSender += HandleMessage;
            }

            var defs = MyDefinitionManager.Static.GetAllDefinitions();
            foreach (var def in defs.OfType<MyCubeBlockDefinition>())
            {
                validSubtypes.Add(def.Id.SubtypeName);
                Log($"Loaded subtype: {def.Id.SubtypeName}");
            }
        }

        protected override void UnloadData()
        {
            MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(NetworkId, ReceivedPacket);
            if (MyAPIGateway.Utilities.IsDedicated)
            {

            }
            else
            {
                MyAPIGateway.Utilities.MessageEnteredSender -= HandleMessage;
            }

            lock (logLock)
            {
                if (logger != null)
                {
                    logger.Flush();
                    logger.Close();
                    logger = null;
                }
            }

            Instance = null;
        }

        private void PrepareReplace(IMyCubeGrid grid, string targetSubtype, string replacementSubtype)
        {
            if (!validSubtypes.Contains(targetSubtype))
            {
                MyAPIGateway.Utilities.ShowNotification($"unknown subtypeId '{targetSubtype}'");
                Log($"Unknown target subtypeId '{targetSubtype}'");
                return;
            }
            if (!validSubtypes.Contains(replacementSubtype))
            {
                MyAPIGateway.Utilities.ShowNotification($"unknown subtypeId '{replacementSubtype}'");
                Log($"Unknown replacement subtypeId '{replacementSubtype}'");
                return;
            }

            var infostring = $"command: replace, target {targetSubtype}, replacement {replacementSubtype}";
            Log(infostring);

            if (!MyAPIGateway.Session.IsServer)
            {
                var replacement = new Packet(grid.EntityId, targetSubtype, replacementSubtype);
                var serialized = MyAPIGateway.Utilities.SerializeToBinary(replacement);
                MyAPIGateway.Multiplayer.SendMessageToServer(NetworkId, serialized, true);
                MyAPIGateway.Utilities.ShowNotification("Sent packet to server");
                Log("Sent packet to server");
            }
            else
            {
                var result = DoBlockReplacement(grid, targetSubtype, replacementSubtype);

                MyAPIGateway.Utilities.ShowNotification($"Replaced {result} blocks");
                Log($"Replaced {result} blocks");
            }
        }

        private void HandleMessage(ulong sender, string messageText, ref bool sendToOthers)
        {
            Log($"Received message: {messageText} from sender {sender}");
            if (!messageText.StartsWith(commandPrefix)) { return; }
            var infostring = $"sender {sender}: {messageText}";
            Log(infostring);

            sendToOthers = false;

            var args = messageText.Substring(commandPrefix.Length)
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (args.Length < 1)
            {
                MyAPIGateway.Utilities.ShowMessage("BlockSwapper", $"No command provided");
                args = new[] { "help" };
            }
            var command = args[0];

            var grid = RaycastGridFromCamera();
            infostring = grid?.ToString() ?? "No grid hit by Raycast";
            Log(infostring);
            if (grid == null)
            {
                MyAPIGateway.Utilities.ShowNotification(infostring);
                return;
            }

            switch (command)
            {
                case "help":
                    MyAPIGateway.Utilities.ShowMessage("BlockSwapper", $"Commands:");
                    foreach (var key in usageDictionary.Keys)
                    {
                        if (key == "help") continue;
                        MyAPIGateway.Utilities.ShowMessage("", $"\t{usageDictionary[key]}");
                    }
                    MyAPIGateway.Utilities.ShowMessage("BlockSwapper", $"Shortcuts: [r]eplace, [d]elete");
                    break;
                case "r":
                case "replace":
                    if (args.Length - 1 != 2)
                    {
                        MyAPIGateway.Utilities.ShowMessage("BlockSwapper", $"Usage:\n\t{commandPrefix} {usageDictionary["replace"]}");
                        return;
                    }
                    PrepareReplace(grid, args[1], args[2]); break;
                case "d":
                case "delete":
                    if (args.Length - 1 != 1)
                    {
                        MyAPIGateway.Utilities.ShowMessage("BlockSwapper", $"Usage:\n\t{commandPrefix} {usageDictionary["delete"]}");
                        return;
                    }
                    var result = DeleteSubtype(grid, args[1]);
                    MyAPIGateway.Utilities.ShowNotification($"BlockSwapper: deleted {result} block/s");
                    break;
                default:
                    MyAPIGateway.Utilities.ShowMessage("BlockSwapper", $"Unrecognized command '{command}'");
                    break;
            }
        }

        private int DeleteSubtype(IMyCubeGrid grid, string targetSubtype)
        {
            if (grid == null) return 0;
            var deleteCount = 0;
            var blocks = new List<IMySlimBlock>();
            grid.GetBlocks(blocks, block => block.BlockDefinition.Id.SubtypeId.ToString() == targetSubtype);
            deleteCount = blocks.Count;
            blocks.ForEach(block => grid.RazeBlock(block.Position));
            return deleteCount;
        }

        private int DoBlockReplacement(IMyCubeGrid grid, string target, string replacement)
        {
            var blocksSuccessfullyReplaced = 0;
            MyAPIGateway.Utilities.ShowNotification("Attempting block replacement");
            Log("Attempting block replacement");

            var blocks = new List<IMySlimBlock>();
            grid?.GetBlocks(blocks);
            foreach (var block in blocks)
            {
                if (block.BlockDefinition.Id.SubtypeName != target) continue;
                var savedBlockBuilder = block.GetObjectBuilder();
                var replacementBuilder = block.GetObjectBuilder(true);
                replacementBuilder.SubtypeName = replacement;
                grid?.RazeBlock(block.Position);

                // TODO: (munashe) Prevent block from dumping inventory into world on removal
                MyAPIGateway.Utilities.InvokeOnGameThread(() => {
                    var blockAdded = grid?.AddBlock(replacementBuilder, false);
                    if (blockAdded != null)
                    {
                        Log($"Successfully replaced {target} with {replacement} at {block.Position}");
                        MyAPIGateway.Utilities.ShowNotification($"Replaced block at {block.Position}");
                        blocksSuccessfullyReplaced++;
                    }
                    else
                    {
                        // Attempt to undo the raze if replacement fails
                        grid?.AddBlock(savedBlockBuilder, false);
                        Log($"Failed to replace block at {block.Position}");
                        MyAPIGateway.Utilities.ShowNotification($"Failed to replace block at {block.Position}", 5000, MyFontEnum.Red);
                    }
                });

                if (MyAPIGateway.Utilities.IsDedicated || !Debug || grid == null) continue;
                var color = Color.Yellow;
                var refcolor = color.ToVector4();
                var worldPos = grid.GridIntegerToWorld(block.Position);
                MyTransparentGeometry.AddPointBillboard(MyStringId.GetOrCompute("WhiteDot"), refcolor, worldPos, 1f, 0f, -1, BlendTypeEnum.SDR, PersistBillboard);
            }
            return blocksSuccessfullyReplaced;
        }

        // FatBlocks only
        private int DoBlockReplacementServer(Packet packet)
        {
            if (packet == null) return 0;
            var grid = MyAPIGateway.Entities.GetEntityById(packet.EntityId) as IMyCubeGrid;
            if (grid == null)
            {
                Log("grid must be non-null");
                return 0;
            }

            packet.Orders = new List<Order>();

            var blocksSuccessfullyReplaced = 0;
            Log("Attempting block packet on server");

            var blocks = new List<IMySlimBlock>();
            grid.GetBlocks(blocks);
            Log($"Iterating grid with {blocks.Count} blocks...");
            foreach (var block in blocks.Where(block => block.FatBlock != null && block.BlockDefinition.Id.SubtypeName == packet.TargetSubtype))
            {
                Log($"Found candidate for packet {block.FatBlock.DisplayName} @ {block.Position}");

                var savedBlockBuilder = block.GetObjectBuilder();
                var replacementBuilder = block.GetObjectBuilder(true);
                if (savedBlockBuilder == null)
                {
                    Log("savedBlockBuilder is null");
                    return 0;
                }
                if (replacementBuilder == null)
                {
                    Log("replacementBuilder is null");
                    return 0;
                }
                replacementBuilder.SubtypeName = packet.ReplacementSubtype;

                grid.RazeBlock(block.Position);

                Log("InvokeOnGameThread...");
                MyAPIGateway.Utilities.InvokeOnGameThread(() => {
                    var addedBlock = grid.AddBlock(replacementBuilder, false);

                    if (addedBlock != null)
                    {
                        Log($"Successfully replaced {packet.TargetSubtype} with {packet.ReplacementSubtype} at {block.Position}");
                        MyAPIGateway.Utilities.ShowNotification($"Replaced block at {block.Position}");
                        packet.Orders.Add(new Order(addedBlock));
                        blocksSuccessfullyReplaced++;
                    }
                    else
                    {
                        // Attempt to undo the removal if packet fails
                        grid.AddBlock(savedBlockBuilder, false);
                        Log($"Failed to replace block at {block.Position}");
                        MyAPIGateway.Utilities.ShowNotification($"Failed to replace block at {block.Position}", 5000, MyFontEnum.Red);
                    }
                });
            }
            Log($"blocksSuccessfullyReplaced {blocksSuccessfullyReplaced}");
            if (blocksSuccessfullyReplaced > 0)
            {
                var serialized = MyAPIGateway.Utilities.SerializeToBinary(packet);
                if (serialized == null)
                {
                    Log("failed to serialize packet");
                    return 0;
                }

                Log("Serialized packet");
                var players = new List<IMyPlayer>();
                MyAPIGateway.Multiplayer.Players.GetPlayers(players);
                Log($"Players {players.Count}");
                players.ForEach(p =>
                {
                    MyAPIGateway.Multiplayer.SendMessageTo(NetworkId, serialized, p.SteamUserId);
                    Log($"Forwarded packet to {p.SteamUserId}");
                });
                Log("Done ");
            }
            return blocksSuccessfullyReplaced;
        }

        private IMyCubeGrid RaycastGridFromCamera()
        {
            var cameraMatrix = MyAPIGateway.Session.Camera.WorldMatrix;
            var hits = new List<IHitInfo>();
            MyAPIGateway.Physics.CastRay(cameraMatrix.Translation, cameraMatrix.Translation + cameraMatrix.Forward * rangeLimit, hits);
            foreach (var hit in hits)
            {
                var grid = hit.HitEntity as IMyCubeGrid;
                if (grid?.Physics == null) continue;
                if (Debug)
                {
                    var color = Color.GreenYellow;
                    var refcolor = color.ToVector4();
                    MyTransparentGeometry.AddPointBillboard(MyStringId.GetOrCompute("WhiteDot"), refcolor, hit.Position, 1f, 0f, -1, BlendTypeEnum.SDR, PersistBillboard);
                }
                return grid;
            }
            return null;
        }

        private void ReceivedPacket(ushort channelId, byte[] serialized, ulong senderSteamId, bool isSenderServer)
        {
            Log("Entered ReceivedPacket");
            try
            {
                Log("packet received");
                var packet = MyAPIGateway.Utilities.SerializeFromBinary<Packet>(serialized);
                if (packet != null)
                {
                    Log("packet is not null");
                    var grid = MyAPIGateway.Entities.GetEntityById(packet.EntityId) as IMyCubeGrid;
                    if (grid == null)
                    {
                        Log("grid must be non-null");
                        return;
                    }
                    if (isSenderServer && packet.Orders != null)
                    {
                        Log("packet orders is non-null");
                        foreach (var order in packet.Orders)
                        {
                            var ob = MyObjectBuilderSerializer.CreateNewObject(order.Id);
                            if (!(ob is MyObjectBuilder_CubeBlock))
                            {
                                Log($"Failed to CreateNewObject({order.Id})");
                                continue;
                            }
                            var block = grid.AddBlock(ob as MyObjectBuilder_CubeBlock, false);
                            if (block == null)
                            {
                                Log($"grid.AddBlock failure @ {order.Position}");
                            }
                        }
                    }
                    else
                    {
                        Log("isSenderServer is false");
                        if (packet.Orders != null)
                        {
                            Log("Server received packet with non-null Orders");
                            return;
                        }
                        var replaceCount = DoBlockReplacementServer(packet);
                        Log($"replaced {replaceCount}");
                    }
                }
                else
                {
                    Log("Server received invalid packet");
                }
            }
            catch (Exception ex)
            {
                Log($"Exception in ReceivedPacket: {ex.Message}");
            }
        }
    }

    [ProtoContract]
    internal class Order
    {
        [ProtoMember(10)] public MyDefinitionId Id;
        [ProtoMember(12)] public MyBlockOrientation Orientation;
        [ProtoMember(13)] public Vector3I Position;
        [ProtoMember(14)] public Vector3 Paint;

        private Order() { }

        public Order(IMySlimBlock block)
        {
            Id = block.BlockDefinition.Id;
            Orientation = block.Orientation;
            Position = block.Position;
            Paint = block.GetColorMask();
        }
    }

    [ProtoContract]
    internal class Packet
    {
        [ProtoMember(1)] public long EntityId;
        [ProtoMember(2)] public string TargetSubtype;
        [ProtoMember(3)] public string ReplacementSubtype;
        [ProtoMember(4)] public List<Order> Orders;

        private Packet() { }
        public Packet(long entityId, string targetSubtype, string replacementSubtype)
        {
            EntityId = entityId;
            TargetSubtype = targetSubtype;
            ReplacementSubtype = replacementSubtype;
        }
    }
}
