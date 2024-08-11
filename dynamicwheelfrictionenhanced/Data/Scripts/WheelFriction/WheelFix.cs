using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities.Cube;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;
using Draygo.API;
using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;
using System.Text;

namespace WheelFix
{
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public class WheelFixSession : MySessionComponentBase
    {
        public static bool IsEnabled = true;
        private static bool _isHandlerRegistered = false;
        private static List<WheelFix> _activeWheels = new List<WheelFix>();
        private static int _updateCounter = 0;

        // HUD API
        private static HudAPIv2 _hudApi;
        private static bool _isHudApiReady = false;
        private static HudAPIv2.HUDMessage _debugHudMessage;
        private static StringBuilder _debugMessageBuilder = new StringBuilder();

        private const string FontName = "DefaultFont";
        private static readonly Vector2D HudPosition = new Vector2D(0.0, 0.0); // Adjust as needed
        private const float HudScale = 0.7f;

        public override void LoadData()
        {
            if (!_isHandlerRegistered)
            {
                MyAPIGateway.Utilities.MessageEntered += OnMessageEntered;
                _isHandlerRegistered = true;
            }

            if (!_isHudApiReady && MyAPIGateway.Session?.Player != null)
            {
                _hudApi = new HudAPIv2();
                _isHudApiReady = true;
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

        public override void UpdateAfterSimulation()
        {
            base.UpdateAfterSimulation();

            if (!IsEnabled || !_isHudApiReady || ++_updateCounter % 60 != 0) return;

            var player = MyAPIGateway.Session?.Player;
            if (player == null || player.Character == null) return;

            var playerPosition = player.Character.PositionComp.GetPosition();

            var nearestWheels = _activeWheels
                .OrderBy(w => Vector3D.DistanceSquared(w.GetPosition(), playerPosition))
                .Take(4)
                .ToList();

            _debugMessageBuilder.Clear();
            _debugMessageBuilder.AppendLine("WheelFix Debug Info:");

            for (int i = 0; i < nearestWheels.Count; i++)
            {
                var wheel = nearestWheels[i];
                var distance = Vector3D.Distance(wheel.GetPosition(), playerPosition);
                _debugMessageBuilder.AppendLine($"Wheel {i + 1}: {wheel.GetName()} - Dist: {distance:F2}m");
                wheel.AppendDebugInfo(_debugMessageBuilder);
            }

            if (_debugHudMessage == null)
            {
                _debugHudMessage = new HudAPIv2.HUDMessage(
                    Message: _debugMessageBuilder,
                    origin: HudPosition,
                    offset: Vector2D.Zero,
                    timeToLive: -1,
                    scale: HudScale,
                    hideHud: false,
                    shadowing: true,
                    shadowColor: Color.Black,
                    blend: BlendTypeEnum.PostPP,
                    font: FontName
                );
            }
            else
            {
                _debugHudMessage.Message = _debugMessageBuilder;
            }
        }

        protected override void UnloadData()
        {
            if (_isHandlerRegistered)
            {
                MyAPIGateway.Utilities.MessageEntered -= OnMessageEntered;
                _isHandlerRegistered = false;
            }

            if (_debugHudMessage != null)
            {
                _debugHudMessage.Visible = false;
                _debugHudMessage.DeleteMessage();
                _debugHudMessage = null;
            }
        }

        public static void RegisterWheel(WheelFix wheel)
        {
            if (!_activeWheels.Contains(wheel))
            {
                _activeWheels.Add(wheel);
            }
        }

        public static void UnregisterWheel(WheelFix wheel)
        {
            _activeWheels.Remove(wheel);
        }
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_MotorSuspension), false)]
    public class WheelFix : MyGameLogicComponent
    {
        private IMyMotorSuspension _suspension;

        public override void OnAddedToContainer()
        {
            base.OnAddedToContainer();
            this.NeedsUpdate |= VRage.ModAPI.MyEntityUpdateEnum.EACH_FRAME;
            _suspension = (IMyMotorSuspension)this.Entity;
            WheelFixSession.RegisterWheel(this);
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
        }

        public void AppendDebugInfo(StringBuilder sb)
        {
            var grid = _suspension?.TopGrid;
            if (grid?.Physics == null) return;

            sb.AppendLine($"  Friction: {grid.Physics.Friction:F2}");
            sb.AppendLine($"  Smoothing: {TerrainSmoothingFactor:F2}");
        }

        public Vector3D GetPosition()
        {
            return _suspension.GetPosition();
        }

        public string GetName()
        {
            return _suspension.CustomName;
        }

        public override void Close()
        {
            WheelFixSession.UnregisterWheel(this);
            base.Close();
        }
    }
}