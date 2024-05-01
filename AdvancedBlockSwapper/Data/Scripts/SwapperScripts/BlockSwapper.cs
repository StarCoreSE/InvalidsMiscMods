using System;
using System.Collections.Generic;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Weapons;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;
using VRageRender;
using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum; // required for MyTransparentGeometry/MySimpleObjectDraw to be able to set blend type.

namespace Munashe.BlockSwapper
{
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public class BlockSwapper_Session : MySessionComponentBase
    {
        public static BlockSwapper_Session Instance;

        public bool Debug = true;
        private readonly string commandPrefix = "/bs";
        private readonly float rangeLimit = 150.0f;

        private List<MyBillboard> PersistBillboard = new List<MyBillboard>();

        public override void BeforeStart()
        {
            if (MyAPIGateway.Utilities.IsDedicated)
            {
                // script is running on a real server
            }
            else
            {
                MyAPIGateway.Utilities.MessageEnteredSender += HandleMessage;
            }
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
                // forward the request to the server, which will DoBlockReplacement on our behalf
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
                var blocks = new List<IMySlimBlock>();
                grid.GetBlocks(blocks);
                foreach (var block in blocks)
                {
                    if (block.BlockDefinition.Id.SubtypeId.ToString() == target)
                    {
                        // TODO(munashe): replace blocks if the replacement will fit
                        grid.RazeBlock(grid.WorldToGridInteger(block.Position));
                        
                        // remove the block from the grid
                        // try placing the replacement
                        // if it fails to place, notify caller (with coords of block that couldn't be replaced)
                        //      and put the old block back where it was removed from
                        // else continue
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

        public override void LoadData()
        {
            Instance = this;
        }

        protected override void UnloadData()
        {
            if (!MyAPIGateway.Utilities.IsDedicated)
            {
                MyAPIGateway.Utilities.MessageEnteredSender -= HandleMessage;
            }
            Instance = null;
        }

    }
}