﻿using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities.Cube;
using Sandbox.ModAPI;
using System;
using VRage.Game;
using VRage.Game.Components;
using VRage.Utils;
using VRageMath;

namespace WheelFix
{
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    public class WheelFixSession : MySessionComponentBase
    {
        public static bool IsEnabled = true;
        private static bool _isHandlerRegistered = false;

        public override void LoadData()
        {
            if (!_isHandlerRegistered)
            {
                MyAPIGateway.Utilities.MessageEntered += OnMessageEntered;
                _isHandlerRegistered = true;
            }
        }

        private void OnMessageEntered(string messageText, ref bool sendToOthers)
        {
            if (messageText.Equals("/toggleWheelFix", StringComparison.OrdinalIgnoreCase))
            {
                IsEnabled = !IsEnabled;
                MyAPIGateway.Utilities.ShowNotification($"WheelFix is now {(IsEnabled ? "enabled" : "disabled")}", 2000);
                sendToOthers = false;
            }
        }

        protected override void UnloadData()
        {
            if (_isHandlerRegistered)
            {
                MyAPIGateway.Utilities.MessageEntered -= OnMessageEntered;
                _isHandlerRegistered = false;
            }
        }
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_MotorSuspension), false)]
    public class WheelFix : MyGameLogicComponent
    {
        private IMyMotorSuspension _suspension;
        private int _debugCounter = 0;
        public override void OnAddedToContainer()
        {
            base.OnAddedToContainer();
            this.NeedsUpdate |= VRage.ModAPI.MyEntityUpdateEnum.EACH_FRAME;
            _suspension = (IMyMotorSuspension)this.Entity;
        }

        public const double ResistanceCoefficient = 100d;
        public const float MaxFriction = 0.95f;
        public const float MaxStrength = 0.95f;
        public const float TerrainSmoothingFactor = 100f;

        public override void UpdateBeforeSimulation()
        {
            if (!WheelFixSession.IsEnabled) return;

            base.UpdateBeforeSimulation();
            var grid = _suspension?.TopGrid;
            if (grid?.Physics == null) return;

            var minpos = Vector3D.Transform(_suspension.DummyPosition, _suspension.CubeGrid.PositionComp.WorldMatrixRef);
            minpos += _suspension.Height * _suspension.PositionComp.WorldMatrixRef.Forward;

            var distance = grid.Physics.CenterOfMassWorld - minpos;
            distance = Vector3D.Reject(distance, _suspension.PositionComp.WorldMatrixRef.Up);

            var friction = MyMath.Clamp(_suspension.Friction / 100f, 0f, MaxFriction);
            var str = MyMath.Clamp(_suspension.Strength / 100f, 0f, MaxStrength);
            var num = 35.0d * ((double)(MyMath.FastTanH(6f * friction - 3f) / 2f) + 0.5);
            var num2 = ResistanceCoefficient * str * distance.Length() * friction;

            grid.Physics.Friction = (float)(num + num2);

            var smoothingForce = TerrainSmoothingFactor * _suspension.PositionComp.WorldMatrixRef.Up;
            grid.Physics.AddForce(MyPhysicsForceType.APPLY_WORLD_IMPULSE_AND_WORLD_ANGULAR_IMPULSE, smoothingForce, null, null);

            if (_debugCounter++ % 60 == 0)
            {
                MyAPIGateway.Utilities.ShowNotification($"Suspension: {_suspension.CustomName}", 1000);
                MyAPIGateway.Utilities.ShowNotification($"Base Friction: {num:F2}", 1000);
                MyAPIGateway.Utilities.ShowNotification($"Added Friction: {num2:F2}", 1000);
                MyAPIGateway.Utilities.ShowNotification($"Total Friction: {grid.Physics.Friction:F2}", 1000);
                MyAPIGateway.Utilities.ShowNotification($"Smoothing Force: {smoothingForce.Length():F2}", 1000);
            }
        }

        public override void UpdateAfterSimulation()
        {
            base.UpdateAfterSimulation();
        }
    }
}
