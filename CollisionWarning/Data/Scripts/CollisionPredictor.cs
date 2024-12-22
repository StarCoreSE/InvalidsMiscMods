using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRageMath;
using VRage.ModAPI;
using System.Collections.Generic;
using VRage.Game;
using IMyCockpit = Sandbox.ModAPI.Ingame.IMyCockpit;
using System;
using VRage.Utils;

[MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
public class CollisionPredictor : MySessionComponentBase
{
    private const float MinSpeed = 20f;
    private const float MaxRange = 1000f;
    private const float ConeAngle = 30f;
    private int updateCounter = 0;
    private const int NotificationInterval = 60;
    private Random random = new Random();

    public override void UpdateBeforeSimulation()
    {
        updateCounter++;

        var player = MyAPIGateway.Session?.Player;
        if (player == null)
        {
            if (updateCounter % NotificationInterval == 0)
                MyAPIGateway.Utilities.ShowNotification("Debug: No player found", 1000, MyFontEnum.Red);
            return;
        }

        var controlledEntity = player.Controller.ControlledEntity as IMyCockpit;
        if (controlledEntity == null)
        {
            if (updateCounter % NotificationInterval == 0)
                MyAPIGateway.Utilities.ShowNotification("Debug: Player not in cockpit", 1000, MyFontEnum.Red);
            return;
        }

        var grid = controlledEntity.CubeGrid as IMyCubeGrid;
        if (grid == null)
        {
            if (updateCounter % NotificationInterval == 0)
                MyAPIGateway.Utilities.ShowNotification("Debug: No grid found", 1000, MyFontEnum.Red);
            return;
        }

        var velocity = grid.Physics.LinearVelocity;
        var speed = velocity.Length();

        if (updateCounter % NotificationInterval == 0)
            MyAPIGateway.Utilities.ShowNotification($"Debug: Current speed: {speed:F1} m/s", 1000,
                speed >= MinSpeed ? MyFontEnum.Green : MyFontEnum.White);

        if (speed < MinSpeed) return;

        var position = grid.Physics.CenterOfMassWorld;
        var mainDirection = Vector3D.Normalize(velocity);

        // Generate a single random direction within the cone
        Vector3D rayDirection = GetRandomDirectionInCone(mainDirection);
        Vector3D rayEnd = position + rayDirection * MaxRange;

        // Draw the ray
        DrawLine(position, rayEnd, Color.Blue);

        IHitInfo hitInfo;
        if (MyAPIGateway.Physics.CastLongRay(position, rayEnd, out hitInfo, false))  // false for closest hit
        {
            if (hitInfo.HitEntity != null)
            {
                string entityName = hitInfo.HitEntity.DisplayName ?? "Unknown Entity";
                MyAPIGateway.Utilities.ShowNotification($"Hit: {entityName}", 1000, MyFontEnum.White);
                DrawThickLine(position, hitInfo.Position, Color.Yellow);
            }
            else
            {
                MyAPIGateway.Utilities.ShowNotification("Hit: Voxel", 1000, MyFontEnum.White);
                Color voxelColor = IsOnCollisionCourse(position, velocity, hitInfo.Position) ? Color.Red : Color.White;
                DrawThickLine(position, hitInfo.Position, voxelColor);
            }
        }
    }

    private Vector3D GetRandomDirectionInCone(Vector3D mainDirection)
    {
        double angleRad = MathHelper.ToRadians(ConeAngle);
        double randomAngle = random.NextDouble() * angleRad;
        double randomAzimuth = random.NextDouble() * 2 * Math.PI;

        Vector3D perp1 = Vector3D.CalculatePerpendicularVector(mainDirection);
        Vector3D perp2 = Vector3D.Cross(mainDirection, perp1);

        double x = Math.Sin(randomAngle) * Math.Cos(randomAzimuth);
        double y = Math.Sin(randomAngle) * Math.Sin(randomAzimuth);
        double z = Math.Cos(randomAngle);

        return Vector3D.Normalize(z * mainDirection + x * perp1 + y * perp2);
    }

    private bool IsOnCollisionCourse(Vector3D currentPosition, Vector3 velocity, Vector3D obstaclePosition)
    {
        Vector3D predictedPosition = currentPosition + velocity;
        return Vector3D.Distance(predictedPosition, obstaclePosition) < 10;
    }

    private void DrawLine(Vector3D start, Vector3D end, Color color)
    {
        Vector4 colorVector = color.ToVector4();
        MySimpleObjectDraw.DrawLine(start, end, MyStringId.GetOrCompute("Square"), ref colorVector, 1f);
    }

    private void DrawThickLine(Vector3D start, Vector3D end, Color color)
    {
        Vector4 colorVector = color.ToVector4();
        MySimpleObjectDraw.DrawLine(start, end, MyStringId.GetOrCompute("Square"), ref colorVector, 2f);
    }

    protected override void UnloadData() { }
}