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
    private const float MinSpeed = 20f; // 20 m/s minimum speed
    private const float MaxRange = 1000f; // 1 km max range
    private const float ConeAngle = 30f; // 30 degree cone angle
    private const int RayCount = 8; // Number of rays to cast
    private int updateCounter = 0;
    private const int NotificationInterval = 60; // Show debug info every 60 ticks

    // Array of colors for different rays
    private readonly Color[] rayColors = new Color[]
    {
        Color.Blue,
        Color.Green,
        Color.Yellow,
        Color.Orange,
        Color.Purple,
        Color.Cyan,
        Color.Magenta,
        Color.White
    };

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
        var direction = Vector3D.Normalize(velocity);

        // Generate ray directions
        List<Vector3D> rayDirections = GenerateRayDirections(direction, ConeAngle, RayCount);

        int raysCast = 0;
        int hitsDetected = 0;

        for (int i = 0; i < rayDirections.Count; i++)
        {
            raysCast++;
            Vector3D rayEnd = position + rayDirections[i] * MaxRange;

            // Debug draw the ray
            DrawLine(position, rayEnd, rayColors[i % rayColors.Length]);

            List<IHitInfo> hits = new List<IHitInfo>();
            MyAPIGateway.Physics.CastRay(position, rayEnd, hits);

            foreach (var hit in hits)
            {
                if (hit?.HitEntity?.GetTopMostParent()?.EntityId == grid.EntityId) continue; // Skip self

                if (hit.HitEntity == null) // Hit voxel
                {
                    hitsDetected++;
                    Color lineColor = IsOnCollisionCourse(position, velocity, hit.Position) ? Color.Red : Color.White;

                    // Draw a thicker line to the hit point
                    DrawThickLine(position, hit.Position, lineColor);
                    break; // Only draw the first voxel hit per ray
                }
            }
        }

        if (updateCounter % NotificationInterval == 0)
            MyAPIGateway.Utilities.ShowNotification($"Debug: Rays cast: {raysCast}, Hits detected: {hitsDetected}",
                1000, hitsDetected > 0 ? MyFontEnum.Red : MyFontEnum.White);
    }

    private List<Vector3D> GenerateRayDirections(Vector3D mainDirection, float coneAngle, int rayCount)
    {
        List<Vector3D> directions = new List<Vector3D>();
        directions.Add(mainDirection); // Central ray

        Vector3D perpendicular1 = Vector3D.CalculatePerpendicularVector(mainDirection);
        Vector3D perpendicular2 = Vector3D.Cross(mainDirection, perpendicular1);

        float angleStep = 360f / (rayCount - 1);
        float radianAngle = MathHelper.ToRadians(coneAngle);

        for (int i = 0; i < rayCount - 1; i++)
        {
            float angle = MathHelper.ToRadians(angleStep * i);
            Vector3D rayDirection = Math.Cos(angle) * perpendicular1 +
                                    Math.Sin(angle) * perpendicular2;
            rayDirection = Vector3D.Normalize(mainDirection + Math.Tan(radianAngle) * rayDirection);
            directions.Add(rayDirection);
        }

        return directions;
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