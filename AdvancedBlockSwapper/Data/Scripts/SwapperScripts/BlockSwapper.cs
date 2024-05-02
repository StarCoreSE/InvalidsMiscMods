using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
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

        public void Log(string line)
        {
            logger.WriteLine(line);
            logger.Flush();
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
            logger = MyAPIGateway.Utilities.WriteFileInLocalStorage("log.txt", typeof(BlockSwapper_Session));

            var defs = MyDefinitionManager.Static.GetAllDefinitions();
            foreach (var def in defs.OfType<MyCubeBlockDefinition>())
            {
                validSubtypes.Add(def.Id.SubtypeName);
                Log(def.Id.SubtypeName);
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
            logger?.Close();
            Instance = null;
        }

        private void HandleMessage(ulong sender, string messageText, ref bool sendToOthers)
        {
            if (!messageText.StartsWith(commandPrefix)) { return; }
            var infostring = $"sender {sender}: {messageText}";
            Log(infostring);

            sendToOthers = false;

            var args = messageText.Substring(commandPrefix.Length).Split(' ');
            
            if (args.Length != 3) { return; }

            var command = args[0];
            var targetSubtype = args[1];
            var replacementSubtype = args[2];

            if (!validSubtypes.Contains(targetSubtype))
            {
                MyAPIGateway.Utilities.ShowNotification($"unknown subtypeId '{targetSubtype}'");
                return;
            }
            if (!validSubtypes.Contains(replacementSubtype))
            {
                MyAPIGateway.Utilities.ShowNotification($"unknown subtypeId '{replacementSubtype}'");
                return;
            }

            infostring = $"command {command}, target {targetSubtype}, replacement {replacementSubtype}";
            Log(infostring);

            var grid = RaycastGridFromCamera();
            infostring = grid?.ToString() ?? "No grid hit by Raycast";
            Log(infostring);
            if (grid == null) {
                MyAPIGateway.Utilities.ShowNotification(infostring);
                return;
            }

            if (!MyAPIGateway.Session.IsServer)
            {
                var replacement = new Packet(grid.EntityId, targetSubtype, replacementSubtype);
                var serialized = MyAPIGateway.Utilities.SerializeToBinary(replacement);
                MyAPIGateway.Multiplayer.SendMessageToServer(NetworkId, serialized, true);
                MyAPIGateway.Utilities.ShowNotification("Sent packet to server");
            }
            else
            {
                var result = DoBlockReplacement(grid, targetSubtype, replacementSubtype);

                MyAPIGateway.Utilities.ShowNotification($"Replaced {result} blocks");
                Log($"Replaced {result} blocks");
            }
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
            // try MyCubeGrid.BuildBlockRequestInternal(); ?
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
            foreach ( var hit in hits )
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
                        DoBlockReplacementServer(packet);
                    }
                }
                else
                {
                    Log("Server received invalid packet");
                }
            }
            catch (Exception ex)
            {
                Log(ex.ToString());
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

        private Order() {}

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

        private Packet() {}
        public Packet(long entityId, string targetSubtype, string replacementSubtype)
        {
            EntityId = entityId;
            TargetSubtype = targetSubtype;
            ReplacementSubtype = replacementSubtype;
        }
    }
}