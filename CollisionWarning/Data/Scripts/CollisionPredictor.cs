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
    private const float MinSpeed = 2f;
    private const float MaxRange = 1000f;
    private const int NotificationInterval = 60;
    private const float BaseSearchAngle = 360f;
    private const float MinSearchAngle = 30f;
    private const float SpeedAngleReductionFactor = 0.5f;
    private const int TargetMemoryDuration = 180;
    private const float RaycastDensity = 0.1f; // Adjusts number of raycasts

    private Dictionary<long, StoredTarget> trackedTargets = new Dictionary<long, StoredTarget>();
    private int updateCounter = 0;
    private Random random = new Random();

    private class CollisionTarget
    {
        public IMyEntity Entity;
        public Vector3D Position;
        public Vector3D? Velocity;
        public double TimeToCollision;
        public double ThreatLevel;
    }

    private class StoredTarget
    {
        public IMyEntity Entity;
        public Vector3D LastPosition;
        public Vector3D? LastVelocity;
        public int LastSeenCounter;
        public double LastTimeToCollision;
        public double ThreatLevel;
        public bool IsCurrentThreat;
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

        // Calculate search angle based on speed
        float currentSearchAngle = Math.Max(
            MinSearchAngle,
            BaseSearchAngle - (float)(mySpeed * SpeedAngleReductionFactor)
        );

        // Calculate acceleration direction and adjust search angle
        Vector3D accelerationDirection = Vector3D.Zero;
        if (controlledEntity.MoveIndicator != Vector3.Zero)
        {
            accelerationDirection = Vector3D.Transform(controlledEntity.MoveIndicator, controlledEntity.WorldMatrix);
            float angleToAccel = (float)Vector3D.Angle(myVelocity, accelerationDirection);
            currentSearchAngle = Math.Min(BaseSearchAngle, currentSearchAngle + angleToAccel);
        }

        UpdateTrackedTargets();

        var mainDirection = Vector3D.Normalize(myVelocity);
        var gridGroup = grid.GetGridGroup(GridLinkTypeEnum.Mechanical);
        var gridCenter = grid.Physics.CenterOfMassWorld;

        CollisionTarget closestCollisionTarget = ScanForCollisions(grid, gridGroup, gridCenter, myVelocity, mainDirection, currentSearchAngle);

        if (closestCollisionTarget != null)
        {
            UpdateTargetTracking(closestCollisionTarget);
            DisplayWarnings(closestCollisionTarget, mySpeed, gridCenter);
        }
    }

    private CollisionTarget ScanForCollisions(IMyCubeGrid grid, IMyGridGroupData gridGroup, Vector3D gridCenter,
        Vector3D myVelocity, Vector3D mainDirection, float searchAngle)
    {
        CollisionTarget closestCollisionTarget = null;
        double closestCollisionTime = double.MaxValue;

        // First check previously tracked targets
        foreach (var storedTarget in trackedTargets.Values)
        {
            if (storedTarget.Entity?.Physics != null)
            {
                Vector3D currentTargetPos = storedTarget.Entity.Physics.CenterOfMassWorld;
                Vector3D currentTargetVel = storedTarget.Entity.Physics.LinearVelocity;

                double? timeToCollision = CalculateTimeToCollision(
                    gridCenter, myVelocity,
                    currentTargetPos, currentTargetVel);

                if (timeToCollision.HasValue && timeToCollision.Value < closestCollisionTime)
                {
                    closestCollisionTime = timeToCollision.Value;
                    double threatLevel = CalculateThreatLevel(timeToCollision.Value, myVelocity, currentTargetVel);

                    closestCollisionTarget = new CollisionTarget
                    {
                        Entity = storedTarget.Entity,
                        Position = currentTargetPos,
                        Velocity = currentTargetVel,
                        TimeToCollision = timeToCollision.Value,
                        ThreatLevel = threatLevel
                    };
                }
            }
        }

        // Perform new raycasts
        BoundingBoxD box = grid.WorldAABB;
        Vector3D[] corners = new Vector3D[8];
        box.GetCorners(corners);

        int raycastCount = Math.Max(1, (int)(searchAngle * RaycastDensity));

        foreach (Vector3D corner in corners)
        {
            for (int i = 0; i < raycastCount; i++)
            {
                Vector3D rayDirection = GetRandomDirectionInCone(mainDirection, searchAngle);
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
                        double threatLevel = CalculateThreatLevel(timeToCollision.Value, myVelocity, targetVelocity);

                        closestCollisionTarget = new CollisionTarget
                        {
                            Entity = hitInfo.HitEntity,
                            Position = targetCenter,
                            Velocity = targetVelocity,
                            TimeToCollision = timeToCollision.Value,
                            ThreatLevel = threatLevel
                        };
                    }
                }
            }
        }

        return closestCollisionTarget;
    }

    private void UpdateTrackedTargets()
    {
        List<long> targetsToRemove = new List<long>();
        foreach (var kvp in trackedTargets)
        {
            kvp.Value.LastSeenCounter++;
            kvp.Value.IsCurrentThreat = false;

            if (kvp.Value.LastSeenCounter > TargetMemoryDuration)
            {
                targetsToRemove.Add(kvp.Key);
            }
        }

        foreach (var targetId in targetsToRemove)
        {
            trackedTargets.Remove(targetId);
        }
    }

    private void UpdateTargetTracking(CollisionTarget newTarget)
    {
        long entityId = newTarget.Entity.EntityId;

        if (!trackedTargets.ContainsKey(entityId))
        {
            trackedTargets[entityId] = new StoredTarget();
        }

        var storedTarget = trackedTargets[entityId];
        storedTarget.Entity = newTarget.Entity;
        storedTarget.LastPosition = newTarget.Position;
        storedTarget.LastVelocity = newTarget.Velocity;
        storedTarget.LastSeenCounter = 0;
        storedTarget.LastTimeToCollision = newTarget.TimeToCollision;
        storedTarget.ThreatLevel = newTarget.ThreatLevel;
        storedTarget.IsCurrentThreat = true;
    }

    private void DisplayWarnings(CollisionTarget target, double mySpeed, Vector3D gridCenter)
    {
        if (target.TimeToCollision < MaxRange / mySpeed)
        {
            Color warningColor = GetWarningColor(target.TimeToCollision * mySpeed, target.ThreatLevel);
            DrawLine(gridCenter, target.Position, warningColor);

            if (updateCounter % NotificationInterval == 0)
            {
                string entityName = target.Entity?.DisplayName ?? "Unknown Entity";
                string relativeSpeed = target.Velocity.HasValue ?
                    $", Relative Speed: {(target.Velocity.Value).Length():F1} m/s" : "";
                string threatLevel = $", Threat Level: {target.ThreatLevel:F1}";

                MyAPIGateway.Utilities.ShowNotification(
                    $"Warning: Potential collision with {entityName} in {target.TimeToCollision:F1}s{relativeSpeed}{threatLevel}",
                    1000, MyFontEnum.Red);
            }
        }
    }

    private double CalculateThreatLevel(double timeToCollision, Vector3D myVelocity, Vector3D? targetVelocity)
    {
        double relativeSpeed = targetVelocity.HasValue ?
            (myVelocity - targetVelocity.Value).Length() : myVelocity.Length();

        return (relativeSpeed * relativeSpeed) / (timeToCollision + 1.0); // Adding 1.0 to avoid division by zero
    }

    private double? CalculateTimeToCollision(Vector3D myPosition, Vector3D myVelocity,
        Vector3D targetPosition, Vector3D? targetVelocity)
    {
        Vector3D relativePosition = targetPosition - myPosition;
        Vector3D relativeVelocity = targetVelocity.HasValue ?
            myVelocity - targetVelocity.Value : myVelocity;

        double relativeSpeed = relativeVelocity.Length();
        if (relativeSpeed < 1)
            return null;

        double dot = Vector3D.Dot(relativePosition, relativeVelocity);
        if (dot < 0)
            return null;

        return dot / (relativeSpeed * relativeSpeed);
    }

    private Color GetWarningColor(double distance, double threatLevel)
    {
        float normalizedDistance = (float)(Math.Min(Math.Max(distance, 0), MaxRange) / MaxRange);
        float normalizedThreat = (float)Math.Min(threatLevel / 100.0, 1.0);

        return new Color(
            (byte)(255 * (1 - normalizedDistance) * normalizedThreat),
            (byte)(255 * normalizedDistance),
            0);
    }

    private Vector3D GetRandomDirectionInCone(Vector3D mainDirection, float coneAngle)
    {
        double angleRad = MathHelper.ToRadians(coneAngle);
        double randomAngle = random.NextDouble() * angleRad;
        double randomAzimuth = random.NextDouble() * 2 * Math.PI;

        Vector3D perp1 = Vector3D.CalculatePerpendicularVector(mainDirection);
        Vector3D perp2 = Vector3D.Cross(mainDirection, perp1);

        double x = Math.Sin(randomAngle) * Math.Cos(randomAzimuth);
        double y = Math.Sin(randomAngle) * Math.Sin(randomAzimuth);
        double z = Math.Cos(randomAngle);

        return Vector3D.Normalize(z * mainDirection + x * perp1 + y * perp2);
    }

    private void DrawLine(Vector3D start, Vector3D end, Color color)
    {
        Vector4 colorVector = color.ToVector4();
        MySimpleObjectDraw.DrawLine(start, end, MyStringId.GetOrCompute("Square"), ref colorVector, 0.2f);
    }

    protected override void UnloadData() { }
}