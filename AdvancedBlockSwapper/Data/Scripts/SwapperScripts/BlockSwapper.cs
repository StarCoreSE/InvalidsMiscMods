using System;
using System.Collections.Generic;
using ProtoBuf;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
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

        private List<MyBillboard> PersistBillboard = new List<MyBillboard>();

        public override void LoadData()
        {
            Instance = this;
            NetworkId = 38271;
            if (MyAPIGateway.Utilities.IsDedicated)
            {
                MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(NetworkId, ReceivedPacket);
            }
            else
            {
                MyAPIGateway.Utilities.MessageEnteredSender += HandleMessage;
            }
        }

        protected override void UnloadData()
        {
            if (MyAPIGateway.Utilities.IsDedicated)
            {
                MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(NetworkId, ReceivedPacket);
            }
            else
            {
                MyAPIGateway.Utilities.MessageEnteredSender -= HandleMessage;
            }
            Instance = null;
        }

        private void HandleMessage(ulong sender, string messageText, ref bool sendToOthers)
        {
            if (!messageText.StartsWith(commandPrefix)) { return; }

            sendToOthers = false;

            var args = messageText.Substring(commandPrefix.Length).Split(' ');
            if (args.Length != 3) { return; }

            string command = args[0];
            string targetSubtype = args[1];
            string replacementSubtype = args[2];

            var grid = RaycastGridFromCamera();
            if (grid == null) { return; }

            if (MyAPIGateway.Utilities.IsDedicated)
            {
                var replacement = new Replacement(grid.EntityId, targetSubtype, replacementSubtype);
                var serialized = MyAPIGateway.Utilities.SerializeToBinary(replacement);
                MyAPIGateway.Multiplayer.SendMessageToServer(NetworkId, serialized, true);
            }
            else
            {
                DoBlockReplacement(grid, targetSubtype, replacementSubtype);
            }
        }

        private void DoBlockReplacement(IMyCubeGrid grid, string target, string replacement)
        {
            if (grid != null)
            {
                var objectBuilder = new MyObjectBuilder_CubeBlock
                {
                    SubtypeName = replacement
                };

                var blocks = new List<IMySlimBlock>();
                grid.GetBlocks(blocks);
                foreach (var block in blocks)
                {
                    if (block.BlockDefinition.Id.SubtypeId.ToString() == target)
                    {
                        var cell = grid.WorldToGridInteger(block.Position);
                        grid.RazeBlock(cell);
                        
                        objectBuilder.BlockOrientation = block.Orientation;

                        var blockAdded = grid.AddBlock(objectBuilder, true);
                        if (blockAdded != null)
                        {
                            MyAPIGateway.Utilities.ShowNotification($"Replaced block at ${cell}");
                        }
                        else
                        {
                            grid.AddBlock(block.GetObjectBuilder(), true);
                            MyAPIGateway.Utilities.ShowNotification($"Failed to replace block at ${cell}");
                        }
                    }
                }
            }
        }
        
        private IMyCubeGrid RaycastGridFromCamera()
        {
            var cameraMatrix = MyAPIGateway.Session.Camera.WorldMatrix;
            var hits = new List<IHitInfo>();
            MyAPIGateway.Physics.CastRay(cameraMatrix.Translation, cameraMatrix.Translation + cameraMatrix.Forward * rangeLimit, hits);
            foreach ( var hit in hits )
            {
                var grid = hit.HitEntity as IMyCubeGrid;
                if (grid?.Physics != null)
                {
                    if (Debug)
                    {
                        Color color = Color.GreenYellow;
                        var refcolor = color.ToVector4();
                        MyTransparentGeometry.AddPointBillboard(MyStringId.GetOrCompute("WhiteDot"), refcolor, hit.Position, 1f, 0f, -1, BlendTypeEnum.SDR, PersistBillboard);
                    }
                    return grid;
                }
            }
            return null;
        }

        private void ReceivedPacket(ushort channelId, byte[] serialized, ulong senderSteamId, bool isSenderServer)
        {
            try
            {
                Replacement r = MyAPIGateway.Utilities.SerializeFromBinary<Replacement>(serialized);
                // entityId -> grid
                // DoBlockReplacement(grid, r.targetSubtype, r.replacementSubtype);
            }
            catch (Exception ex)
            {

            }
        }
    }

    [ProtoContract]
    internal class Replacement
    {
        [ProtoMember(1)] public readonly long entityId;
        [ProtoMember(2)] public readonly string targetSubtype;
        [ProtoMember(3)] public readonly string replacementSubtype;

        public Replacement(long entityId, string targetSubtype, string replacementSubtype)
        {
            this.entityId = entityId;
            this.targetSubtype = targetSubtype;
            this.replacementSubtype = replacementSubtype;
        }
    }
}