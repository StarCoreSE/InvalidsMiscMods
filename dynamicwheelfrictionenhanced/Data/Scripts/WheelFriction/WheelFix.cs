using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities.Cube;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRage.Game;
using VRage.Game.Components;
using VRage.Utils;
using VRageMath;

namespace WheelFix
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_MotorSuspension), false)]
    public class WheelFix : MyGameLogicComponent
    {
        IMyMotorSuspension _suspension;
        private int _debugCounter = 0;

        public override void OnAddedToContainer()
        {
            base.OnAddedToContainer();
            this.NeedsUpdate |= VRage.ModAPI.MyEntityUpdateEnum.EACH_FRAME;
            _suspension = (IMyMotorSuspension)this.Entity;
        }

        public const double ResistanceCoefficient = 1000d;
        public const float MaxFriction = 0.8f;
        public const float MaxStrength = 0.8f;
        public const float TerrainSmoothingFactor = 0.1f;

        public override void UpdateBeforeSimulation()
        {
            base.UpdateBeforeSimulation();
            var grid = _suspension?.TopGrid;
            if (grid?.Physics == null) return;

            var minpos = Vector3D.Transform(_suspension.DummyPosition, _suspension.CubeGrid.PositionComp.WorldMatrixRef);
            minpos += _suspension.Height * _suspension.PositionComp.WorldMatrixRef.Forward;

            var distance = grid.Physics.CenterOfMassWorld - minpos;
            distance = Vector3D.Reject(distance, _suspension.PositionComp.WorldMatrixRef.Up);

            var friction = Math.Min(_suspension.Friction / 100f, MaxFriction);
            var str = Math.Min(_suspension.Strength / 100f, MaxStrength);

            var num = 35.0d * ((double)(MyMath.FastTanH(6f * friction - 3f) / 2f) + 0.5);
            var num2 = (ResistanceCoefficient * str * distance.Length() * friction);

            grid.Physics.Friction = (float)(num + num2);

            // Smooth out the funny terrain impacts by applying a small upward force
            var smoothingForce = TerrainSmoothingFactor * _suspension.PositionComp.WorldMatrixRef.Up;
            grid.Physics.AddForce(MyPhysicsForceType.APPLY_WORLD_FORCE, smoothingForce, null, null);

            // Debug notifications (every 60 frames to reduce spam)
            if (_debugCounter++ % 60 == 0)
            {
                MyAPIGateway.Utilities.ShowNotification($"Suspension: {_suspension.CustomName}", 1000);
                MyAPIGateway.Utilities.ShowNotification($"Base Friction: {num:F2}", 1000);
                MyAPIGateway.Utilities.ShowNotification($"Added Friction: {num2:F2}", 1000);
                MyAPIGateway.Utilities.ShowNotification($"Total Friction: {grid.Physics.Friction:F2}", 1000);
                MyAPIGateway.Utilities.ShowNotification($"Smoothing Force: {smoothingForce.Length():F2}", 1000);
                MyAPIGateway.Utilities.ShowNotification($"Smoothing Direction: {smoothingForce.Normalize():F2}", 1000);
            }
        }

        public override void UpdateAfterSimulation()
        {
            base.UpdateAfterSimulation();
        }
    }
}