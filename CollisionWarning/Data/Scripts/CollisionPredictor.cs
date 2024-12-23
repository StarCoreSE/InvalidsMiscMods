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
using System.Linq;
using static VRageRender.MyBillboard;

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

    private const int MaxTrackedTargets = 100; // Maximum number of targets to track and display
    private const float MinimumThreatLevelToTrack = 0.1f; // Minimum threat level to consider tracking a target
    private const float ThreatLevelDecayRate = 0.9f; // How quickly threat level decays per update
    private const bool ShowDebugSpheres = true; // Toggle for showing prediction spheres
    private const float DebugSphereSize = 2f; // Size of the debug spheres

    private string currentWarningMessage = null;
    private int warningStartTime = 0;
    private const int WarningDuration = 60; // 60 ticks = 1 second at 60 fps

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

        float currentSearchAngle = Math.Max(
            MinSearchAngle,
            BaseSearchAngle - (float)(mySpeed * SpeedAngleReductionFactor)
        );

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
        }

        DisplayWarnings(gridCenter);
    }

    private CollisionTarget ScanForCollisions(IMyCubeGrid grid, IMyGridGroupData gridGroup, Vector3D gridCenter,
        Vector3D myVelocity, Vector3D mainDirection, float searchAngle)
    {
        CollisionTarget closestCollisionTarget = null;
        double closestCollisionTime = double.MaxValue;

        // Check previously tracked targets
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

    private void UpdateTopThreats()
    {
        var sortedTargets = trackedTargets.Values
            .Where(t => t.IsCurrentThreat && t.ThreatLevel >= MinimumThreatLevelToTrack)
            .OrderByDescending(t => t.ThreatLevel)
            .Take(MaxTrackedTargets)
            .ToList();

        var targetIdsToKeep = sortedTargets.Select(t => t.Entity.EntityId).ToHashSet();
        var idsToRemove = trackedTargets.Keys
            .Where(id => !targetIdsToKeep.Contains(id))
            .ToList();

        foreach (var id in idsToRemove)
        {
            trackedTargets.Remove(id);
        }
    }

    private void UpdateTrackedTargets()
    {
        List<long> targetsToRemove = new List<long>();
        foreach (var kvp in trackedTargets)
        {
            var target = kvp.Value;
            target.LastSeenCounter++;
            target.IsCurrentThreat = false;

            target.ThreatLevel *= ThreatLevelDecayRate;

            if (target.LastSeenCounter > TargetMemoryDuration || target.ThreatLevel < MinimumThreatLevelToTrack)
            {
                targetsToRemove.Add(kvp.Key);
            }
        }

        foreach (var targetId in targetsToRemove)
        {
            trackedTargets.Remove(targetId);
        }

        UpdateTopThreats();
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

    private void DisplayWarnings(Vector3D gridCenter)
    {
        var sortedThreats = trackedTargets.Values
            .Where(t => t.IsCurrentThreat && t.ThreatLevel >= MinimumThreatLevelToTrack)
            .OrderByDescending(t => t.ThreatLevel)
            .Take(MaxTrackedTargets);

        // Visual warnings (lines and spheres)
        foreach (var target in sortedThreats)
        {
            Color warningColor = GetWarningColor(target.LastTimeToCollision, target.ThreatLevel);
            DrawLine(gridCenter, target.LastPosition, warningColor);

            if (ShowDebugSpheres)
            {
                DrawDebugSphere(target.LastPosition, DebugSphereSize, warningColor);

                if (target.LastVelocity.HasValue)
                {
                    Vector3D predictedPos = target.LastPosition +
                                            target.LastVelocity.Value * target.LastTimeToCollision;
                    DrawDebugSphere(predictedPos, DebugSphereSize * 0.5f, Color.Yellow);
                }
            }
        }

        // Text notification
        if (updateCounter % NotificationInterval == 0)
        {
            var highestThreat = sortedThreats.FirstOrDefault();
            if (highestThreat != null)
            {
                string gridName = highestThreat.Entity?.DisplayName ?? "Unknown";
                int additionalThreatsCount = trackedTargets.Count - 1;
                string message;

                if (additionalThreatsCount > 0)
                {
                    message = $"COLLISION WARNING [{gridName}]: {highestThreat.LastTimeToCollision:F1}s | +{additionalThreatsCount} threats";
                }
                else
                {
                    message = $"COLLISION WARNING [{gridName}]: {highestThreat.LastTimeToCollision:F1}s";
                }

                MyAPIGateway.Utilities.ShowNotification(message, 1000, MyFontEnum.Red);
            }
        }
    }

    private double CalculateThreatLevel(double timeToCollision, Vector3D myVelocity, Vector3D? targetVelocity)
    {
        double relativeSpeed = targetVelocity.HasValue ?
            (myVelocity - targetVelocity.Value).Length() : myVelocity.Length();

        return (relativeSpeed * relativeSpeed) / (timeToCollision + 1.0);
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
        var color1 = color.ToVector4();
        MySimpleObjectDraw.DrawLine(start, end, MyStringId.GetOrCompute("Square"), ref color1, 0.2f);
    }

    private void DrawDebugSphere(Vector3D position, float radius, Color color)
    {
        var matrix = MatrixD.CreateTranslation(position);
        Color color1 = color.ToVector4();
        MySimpleObjectDraw.DrawTransparentSphere(ref matrix, radius, ref color1, (MySimpleObjectRasterizer)0.5f, 32, MyStringId.GetOrCompute("Square"));
    }


    protected override void UnloadData() { }
}
