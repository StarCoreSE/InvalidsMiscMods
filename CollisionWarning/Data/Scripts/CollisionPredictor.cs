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
    private const int NotificationInterval = 60;

    private int updateCounter = 0;
    private Random random = new Random();
    private IMyEntity currentCollisionTarget = null;
    private Vector3D currentCollisionPoint;
    private double currentTimeToCollision;

    private class CollisionTarget
    {
        public IMyEntity Entity;
        public Vector3D Position;
        public Vector3D? Velocity;
        public double TimeToCollision;
    }

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

        var myVelocity = grid.Physics.LinearVelocity;
        var mySpeed = myVelocity.Length();

        if (mySpeed < MinSpeed) return;

        var mainDirection = Vector3D.Normalize(myVelocity);
        var gridGroup = grid.GetGridGroup(GridLinkTypeEnum.Mechanical);
        var gridCenter = grid.Physics.CenterOfMassWorld;

        BoundingBoxD box = grid.WorldAABB;
        Vector3D[] corners = new Vector3D[8];
        box.GetCorners(corners);

        CollisionTarget closestCollisionTarget = null;
        double closestCollisionTime = double.MaxValue;

        foreach (Vector3D corner in corners)
        {
            Vector3D rayDirection = GetRandomDirectionInCone(mainDirection);
            Vector3D rayEnd = corner + rayDirection * MaxRange;

            IHitInfo hitInfo;
            if (MyAPIGateway.Physics.CastLongRay(corner, rayEnd, out hitInfo, false))
            {
                if (hitInfo.HitEntity == null) continue;

                var hitGrid = hitInfo.HitEntity as IMyCubeGrid;
                if (hitGrid != null && hitGrid.GetGridGroup(GridLinkTypeEnum.Mechanical) == gridGroup)
                    continue;

                Vector3D? targetVelocity = (hitGrid != null) ? hitGrid.Physics.LinearVelocity : (Vector3D?)null;
                Vector3D targetCenter = (hitGrid != null) ? hitGrid.Physics.CenterOfMassWorld : hitInfo.Position;

                double? timeToCollision = CalculateTimeToCollision(
                    gridCenter, myVelocity,
                    targetCenter, targetVelocity);

                if (!timeToCollision.HasValue) continue;

                if (timeToCollision.Value < closestCollisionTime)
                {
                    closestCollisionTime = timeToCollision.Value;
                    closestCollisionTarget = new CollisionTarget
                    {
                        Entity = hitInfo.HitEntity,
                        Position = targetCenter,
                        Velocity = targetVelocity,
                        TimeToCollision = timeToCollision.Value
                    };
                }
            }
        }

        // Draw collision warning if we found a potential collision
        if (closestCollisionTarget != null && closestCollisionTarget.TimeToCollision < MaxRange / mySpeed)
        {
            Color warningColor = GetWarningColor(closestCollisionTarget.TimeToCollision * mySpeed);
            DrawThickLine(gridCenter, closestCollisionTarget.Position, warningColor);

            if (updateCounter % NotificationInterval == 0)
            {
                string entityName = closestCollisionTarget.Entity?.DisplayName ?? "Unknown Entity";
                string relativeSpeed = closestCollisionTarget.Velocity.HasValue ?
                    $", Relative Speed: {(myVelocity - closestCollisionTarget.Velocity.Value).Length():F1} m/s" : "";

                MyAPIGateway.Utilities.ShowNotification(
                    $"Warning: Potential collision with {entityName} in {closestCollisionTarget.TimeToCollision:F1}s{relativeSpeed}",
                    1000, MyFontEnum.Red);
            }
        }
    }

    private double? CalculateTimeToCollision(Vector3D myPosition, Vector3D myVelocity,
        Vector3D targetPosition, Vector3D? targetVelocity)
    {
        Vector3D relativePosition = targetPosition - myPosition;
        Vector3D relativeVelocity = targetVelocity.HasValue ?
            myVelocity - targetVelocity.Value : myVelocity;

        double relativeSpeed = relativeVelocity.Length();
        if (relativeSpeed < 1) // Effectively stationary relative to each other
            return null;

        // Project relative position onto relative velocity
        double dot = Vector3D.Dot(relativePosition, relativeVelocity);
        if (dot < 0) // Moving away from each other
            return null;

        return dot / (relativeSpeed * relativeSpeed);
    }

    private Color GetWarningColor(double distance)
    {
        float normalizedDistance = (float)(Math.Min(Math.Max(distance, 0), MaxRange) / MaxRange);
        return new Color(
            (byte)(255 * (1 - normalizedDistance)),
            (byte)(255 * normalizedDistance),
            0);
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