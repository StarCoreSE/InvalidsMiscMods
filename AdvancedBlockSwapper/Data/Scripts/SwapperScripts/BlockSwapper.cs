using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using ProtoBuf;
using Sandbox.Definitions;
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
            if (MyAPIGateway.Utilities.IsDedicated)
            {
                MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(NetworkId, ReceivedPacket);
            }
            else
            {
                MyAPIGateway.Utilities.MessageEnteredSender += HandleMessage;
            }
            logger = MyAPIGateway.Utilities.WriteFileInLocalStorage("log.txt", typeof(BlockSwapper_Session));

            var defs = MyDefinitionManager.Static.GetAllDefinitions();
            foreach (var def in defs)
            {
                if (def as MyCubeBlockDefinition != null)
                {
                    validSubtypes.Add(def.Id.SubtypeName.ToString());
                    Log(def.Id.SubtypeName.ToString());
                }
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

            string command = args[0];
            string targetSubtype = args[1];
            string replacementSubtype = args[2];

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

            if (MyAPIGateway.Utilities.IsDedicated)
            {
                var replacement = new Replacement(grid.EntityId, targetSubtype, replacementSubtype);
                var serialized = MyAPIGateway.Utilities.SerializeToBinary(replacement);
                MyAPIGateway.Multiplayer.SendMessageToServer(NetworkId, serialized, true);
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
            int blocksSuccessfullyReplaced = 0;
            MyAPIGateway.Utilities.ShowNotification("Attempting block replacement");
            Log("Attempting block replacement");

            var objectBuilder = new MyObjectBuilder_CubeBlock
            {
                SubtypeName = replacement
            };

            var blocks = new List<IMySlimBlock>();
            grid?.GetBlocks(blocks);
            foreach (var block in blocks)
            {
                if (block.BlockDefinition.Id.SubtypeName.ToString() == target)
                {
                    var backupBuilder = block.GetObjectBuilder(true);
                    grid?.RazeBlock(block.Position);

                    // Delaying block placement by one frame to ensure the physics update
                    // if it has an inventory it'll be dropped and replaced lmao (fix this later)
                    MyAPIGateway.Utilities.InvokeOnGameThread(() => {
                        objectBuilder.BlockOrientation = block.Orientation;
                        var blockAdded = grid?.AddBlock(objectBuilder, false);
                        if (blockAdded != null)
                        {
                            Log($"Successfully replaced {target} with {replacement} at {block.Position}");
                            MyAPIGateway.Utilities.ShowNotification($"Replaced block at {block.Position}");
                            blocksSuccessfullyReplaced++;
                        }
                        else
                        {
                            // Attempt to undo the raze if replacement fails
                            grid?.AddBlock(backupBuilder, false);
                            Log($"Failed to replace block at {block.Position}");
                            MyAPIGateway.Utilities.ShowNotification($"Failed to replace block at {block.Position}", 5000, MyFontEnum.Red);
                        }
                    });

                    if (Debug)
                    {
                        Color color = Color.Yellow;
                        var refcolor = color.ToVector4();
                        var worldPos = grid.GridIntegerToWorld(block.Position);
                        MyTransparentGeometry.AddPointBillboard(MyStringId.GetOrCompute("WhiteDot"), refcolor, worldPos, 1f, 0f, -1, BlendTypeEnum.SDR, PersistBillboard);
                    }
                }
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
                var grid = MyAPIGateway.Entities.GetEntityById(r.entityId) as IMyCubeGrid;
                if (grid != null)
                {
                    DoBlockReplacement(grid, r.targetSubtype, r.replacementSubtype);
                }
                else
                {
                    Log($"Server could not find IMyCubeGrid entity with matching entityId {r.entityId}");
                }
            }
            catch (Exception ex)
            {
                Log(ex.ToString());
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