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
    private const float MinAcceleration = 0.1f;
    private const float MaxRange = 1000f;
    private const float ConeAngle = 30f;
    private const int NotificationInterval = 60;
    private const float AccelerationSmoothing = 5f;
    private const float VelocityWeight = 0.7f;
    private const float AccelerationWeight = 0.3f;
    private const float CollisionAlertTime = 3f;
    private const float CollisionWarningTime = 10f;

    private int updateCounter = 0;
    private Random random = new Random();
    private Vector3D? lastVelocity = null;
    private DateTime lastUpdateTime = DateTime.Now;
    private Vector3D accelerationMemory = Vector3D.Zero;
    private Vector3D smoothedAcceleration = Vector3D.Zero;

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

        Vector3D currentVelocity = grid.Physics.LinearVelocity;
        var currentTime = DateTime.Now;
        var deltaTime = (currentTime - lastUpdateTime).TotalSeconds;

        // Calculate and smooth acceleration
        Vector3D currentAcceleration = Vector3D.Zero;
        if (lastVelocity.HasValue && deltaTime > 0)
        {
            currentAcceleration = (currentVelocity - lastVelocity.Value) / deltaTime;

            // Update acceleration memory
            if (currentAcceleration.LengthSquared() > MinAcceleration * MinAcceleration)
            {
                accelerationMemory = Vector3D.Lerp(accelerationMemory * 0.9, currentAcceleration, 0.5);
            }
            else
            {
                accelerationMemory *= 0.9;
            }

            // Smooth acceleration
            float sf = (float)Math.Min(deltaTime * AccelerationSmoothing, 1);
            smoothedAcceleration = Vector3D.Lerp(smoothedAcceleration, currentAcceleration, sf);
        }

        // Update for next frame
        lastVelocity = currentVelocity;
        lastUpdateTime = currentTime;

        Vector3D gridCenter = grid.Physics.CenterOfMassWorld;
        double currentSpeed = currentVelocity.Length();

        // Debug visualizations
        if (!Vector3D.IsZero(currentVelocity))
            DrawLine(gridCenter, gridCenter + Vector3D.Normalize(currentVelocity) * 50, Color.Blue);
        if (!Vector3D.IsZero(accelerationMemory))
            DrawLine(gridCenter, gridCenter + Vector3D.Normalize(accelerationMemory) * 50, Color.Yellow);

        // Collision detection
        const int rayCount = 100; // Adjust this number as needed
        double closestCollisionTime = double.MaxValue;
        Vector3D? collisionPoint = null;
        IMyEntity collisionEntity = null;

        for (int i = 0; i < rayCount; i++)
        {
            Vector3D rayDirection = GetRandomDirectionInSphere();
            Vector3D rayEnd = gridCenter + rayDirection * MaxRange;

            // Debug visualization of each raycast
            DrawLine(gridCenter, rayEnd, Color.Gray);

            IHitInfo hitInfo;
            if (MyAPIGateway.Physics.CastLongRay(gridCenter, rayEnd, out hitInfo, false))
            {
                if (hitInfo.HitEntity == null)
                {
                    // Debug visualization of voxel hit
                    DrawLine(gridCenter, hitInfo.Position, Color.White);
                    continue;
                }

                var hitGrid = hitInfo.HitEntity as IMyCubeGrid;
                if (hitGrid != null && hitGrid.GetGridGroup(GridLinkTypeEnum.Mechanical) == grid.GetGridGroup(GridLinkTypeEnum.Mechanical))
                    continue;

                // Debug visualization of entity hit
                DrawLine(gridCenter, hitInfo.Position, Color.Yellow);

                Vector3D? targetVelocity = (hitGrid != null) ? (Vector3D?)hitGrid.Physics.LinearVelocity : null;
                double? timeToCollision = CalculateTimeToCollision(
                    gridCenter, currentVelocity, smoothedAcceleration,
                    hitInfo.Position, targetVelocity);

                if (!timeToCollision.HasValue)
                    continue;

                if (timeToCollision.Value < closestCollisionTime)
                {
                    closestCollisionTime = timeToCollision.Value;
                    collisionPoint = hitInfo.Position;
                    collisionEntity = hitInfo.HitEntity;
                }
            }
        }

        // Always show debug info
        if (updateCounter % NotificationInterval == 0)
        {
            string debugInfo = $"Debug: Speed={currentSpeed:F1}m/s\n" +
                              $"AccelMem={accelerationMemory.Length():F1}m/s²\n" +
                              $"SmoothedAccel={smoothedAcceleration.Length():F1}m/s²";

            if (collisionPoint.HasValue)
            {
                debugInfo += $"\nNearest collision: {closestCollisionTime:F1}s";
            }

            MyAPIGateway.Utilities.ShowNotification(debugInfo, 2000, MyFontEnum.White);
        }

        // Draw collision warning
        if (collisionPoint.HasValue)
        {
            Color warningColor;
            string warningMessage = "";

            if (closestCollisionTime < CollisionAlertTime)
            {
                warningColor = Color.Red;
                warningMessage = "IMMINENT COLLISION";
            }
            else if (closestCollisionTime < CollisionWarningTime)
            {
                warningColor = GetWarningColor(closestCollisionTime);
                warningMessage = "Collision Warning";
            }
            else
            {
                warningColor = Color.Green;
            }

            DrawThickLine(gridCenter, collisionPoint.Value, warningColor);

            if (!string.IsNullOrEmpty(warningMessage) && updateCounter % NotificationInterval == 0)
            {
                string entityName = collisionEntity?.DisplayName ?? "Unknown Entity";
                MyAPIGateway.Utilities.ShowNotification(
                    $"{warningMessage}: {entityName}\nTime: {closestCollisionTime:F1}s",
                    2000, warningColor == Color.Red ? MyFontEnum.Red : MyFontEnum.White);
            }
        }
    }

    private Vector3D GetRandomDirectionInSphere()
    {
        double theta = random.NextDouble() * 2 * Math.PI;
        double phi = Math.Acos(2 * random.NextDouble() - 1);

        double x = Math.Sin(phi) * Math.Cos(theta);
        double y = Math.Sin(phi) * Math.Sin(theta);
        double z = Math.Cos(phi);

        return new Vector3D(x, y, z);
    }

    private double? CalculateTimeToCollision(Vector3D myPosition, Vector3D myVelocity, Vector3D myAcceleration,
        Vector3D targetPosition, Vector3D? targetVelocity)
    {
        Vector3D relativePosition = targetPosition - myPosition;
        Vector3D relativeVelocity = targetVelocity.HasValue ?
            myVelocity - targetVelocity.Value : myVelocity;

        double a = 0.5 * myAcceleration.LengthSquared();
        double b = Vector3D.Dot(relativeVelocity, myAcceleration);
        double c = relativePosition.LengthSquared();

        double discriminant = b * b - 4 * a * c;
        if (discriminant < 0) return null;

        double t1 = (-b + Math.Sqrt(discriminant)) / (2 * a);
        double t2 = (-b - Math.Sqrt(discriminant)) / (2 * a);

        if (t1 < 0 && t2 < 0) return null;
        if (t1 < 0) return t2;
        if (t2 < 0) return t1;
        return Math.Min(t1, t2);
    }

    private Color GetWarningColor(double timeToCollision)
    {
        float t = (float)Math.Min(Math.Max((timeToCollision - CollisionAlertTime) /
                                         (CollisionWarningTime - CollisionAlertTime), 0), 1);
        return Color.Lerp(Color.Red, Color.Yellow, t);
    }

    private bool IsOnCollisionCourse(Vector3D currentPosition, Vector3 velocity, Vector3D obstaclePosition)
    {
        Vector3D predictedPosition = currentPosition + velocity;
        return Vector3D.Distance(predictedPosition, obstaclePosition) < 10;
    }

    private void DrawLine(Vector3D start, Vector3D end, Color color)
    {
        Vector4 colorVector = color.ToVector4();
        MySimpleObjectDraw.DrawLine(start, end, MyStringId.GetOrCompute("Square"), ref colorVector, 0.5f);
    }

    private void DrawThickLine(Vector3D start, Vector3D end, Color color)
    {
        Vector4 colorVector = color.ToVector4();
        MySimpleObjectDraw.DrawLine(start, end, MyStringId.GetOrCompute("Square"), ref colorVector, 1.5f);
    }

    protected override void UnloadData() { }
}