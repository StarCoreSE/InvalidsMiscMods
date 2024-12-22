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
        if (player == null || player.Controller?.ControlledEntity == null)
            return;

        var controlledEntity = player.Controller.ControlledEntity as IMyCockpit;
        if (controlledEntity == null)
            return;

        var grid = controlledEntity.CubeGrid as IMyCubeGrid;
        if (grid == null)
            return;

        var velocity = grid.Physics.LinearVelocity;
        var speed = velocity.Length();

        if (updateCounter % NotificationInterval == 0)
            MyAPIGateway.Utilities.ShowNotification($"Debug: Current speed: {speed:F1} m/s", 1000,
                speed >= MinSpeed ? MyFontEnum.Green : MyFontEnum.White);

        if (speed < MinSpeed) return;

        var mainDirection = Vector3D.Normalize(velocity);
        var gridGroup = grid.GetGridGroup(GridLinkTypeEnum.Mechanical);

        // Get bounding box corners
        BoundingBoxD box = grid.WorldAABB;
        Vector3D[] corners = new Vector3D[8];
        box.GetCorners(corners);

        foreach (Vector3D corner in corners)
        {
            Vector3D rayDirection = GetRandomDirectionInCone(mainDirection);
            Vector3D rayEnd = corner + rayDirection * MaxRange;

            // Draw the ray
            DrawLine(corner, rayEnd, Color.Blue);

            IHitInfo hitInfo;
            if (MyAPIGateway.Physics.CastLongRay(corner, rayEnd, out hitInfo, false))
            {
                if (hitInfo.HitEntity != null)
                {
                    // Check if the hit entity is part of our grid group
                    var hitGrid = hitInfo.HitEntity as IMyCubeGrid;
                    if (hitGrid != null && hitGrid.GetGridGroup(GridLinkTypeEnum.Mechanical) == gridGroup)
                        continue;

                    string entityName = hitInfo.HitEntity.DisplayName ?? "Unknown Entity";
                    if (updateCounter % NotificationInterval == 0)
                        MyAPIGateway.Utilities.ShowNotification($"Hit: {entityName}", 1000, MyFontEnum.White);
                    DrawThickLine(corner, hitInfo.Position, Color.Yellow);
                }
                else
                {
                    if (updateCounter % NotificationInterval == 0)
                        MyAPIGateway.Utilities.ShowNotification("Hit: Voxel", 1000, MyFontEnum.White);
                    Color voxelColor = IsOnCollisionCourse(corner, velocity, hitInfo.Position) ? Color.Red : Color.White;
                    DrawThickLine(corner, hitInfo.Position, voxelColor);
                }
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