using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;
using IMyCockpit = Sandbox.ModAPI.Ingame.IMyCockpit;

[MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
public class CollisionPredictor : MySessionComponentBase
{
    private const float MinSpeed = 2f;
    private const float MaxRange = 1000f;
    private const double VoxelRayRange = 20000; // Fixed view distance in meters, adjust as needed
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

    private Dictionary<long, StoredTarget> trackedTargets = new Dictionary<long, StoredTarget>();
    private int updateCounter = 0;
    private Random random = new Random();

    private const double VoxelScanSpread = 1.0; // Reduced to 1 degree spread
    private const int VoxelRayCount = 3; // Reduced ray count since we're more focused
    private const int VoxelScanInterval = 30; // Can scan more often with fewer rays
    private Dictionary<Vector3D, double> voxelHazards = new Dictionary<Vector3D, double>(); // Store detected voxel collision points
    private int voxelScanCounter = 0;

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
        if (MyAPIGateway.Utilities.IsDedicated && MyAPIGateway.Session.IsServer)
            return;

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

        var gridCenter = grid.Physics.CenterOfMassWorld;

        UpdateTrackedTargets();

        ScanForCollisions(grid, gridCenter, myVelocity);

        ScanForVoxelHazards(gridCenter, myVelocity, mySpeed);

        DisplayWarnings(gridCenter, mySpeed);
    }
    private void ScanForCollisions(IMyCubeGrid myGrid, Vector3D gridCenter, Vector3D myVelocity)
    {
        var visibleEntities = new HashSet<IMyEntity>();
        MyAPIGateway.Entities.GetEntities(null, (entity) =>
        {
            if (entity is IMyCubeGrid && entity != myGrid)
            {
                visibleEntities.Add(entity);
            }
            return false;
        });

        foreach (var entity in visibleEntities)
        {
            var targetGrid = entity as IMyCubeGrid;
            if (targetGrid == null) continue;

            Vector3D targetCenter = targetGrid.Physics.CenterOfMassWorld;
            Vector3D targetVelocity = targetGrid.Physics.LinearVelocity;

            // Check if the target is within a reasonable range
            double distanceToTarget = Vector3D.Distance(gridCenter, targetCenter);
            if (distanceToTarget > MaxRange) continue;

            // Check if we're on a collision course
            Vector3D relativePosition = targetCenter - gridCenter;
            Vector3D relativeVelocity = targetVelocity - myVelocity;

            // Calculate the time of closest approach
            double timeToClosestApproach = -Vector3D.Dot(relativePosition, relativeVelocity) / relativeVelocity.LengthSquared();

            // If the time is negative, we've already passed the closest point
            if (timeToClosestApproach < 0) continue;

            // Calculate the distance at closest approach
            Vector3D positionAtClosestApproach = relativePosition + relativeVelocity * timeToClosestApproach;
            double distanceAtClosestApproach = positionAtClosestApproach.Length();

            // If the distance at closest approach is greater than the sum of the grids' bounding spheres, no collision
            double collisionThreshold = myGrid.WorldVolume.Radius + targetGrid.WorldVolume.Radius;
            if (distanceAtClosestApproach > collisionThreshold) continue;

            // If we've made it this far, calculate the actual time to collision
            double? timeToCollision = CalculateTimeToCollision(gridCenter, myVelocity, targetCenter, targetVelocity);

            if (timeToCollision.HasValue)
            {
                double threatLevel = CalculateThreatLevel(timeToCollision.Value, myVelocity, targetVelocity);

                UpdateTargetTracking(new CollisionTarget
                {
                    Entity = targetGrid,
                    Position = targetCenter,
                    Velocity = targetVelocity,
                    TimeToCollision = timeToCollision.Value,
                    ThreatLevel = threatLevel
                });
            }
        }
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

            if (target.LastSeenCounter > TargetMemoryDuration ||
                target.ThreatLevel < MinimumThreatLevelToTrack ||
                Vector3D.Distance(target.LastPosition, MyAPIGateway.Session.Player.GetPosition()) > MaxRange)
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
    private void DisplayWarnings(Vector3D gridCenter, double mySpeed)  // Added mySpeed parameter
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

        // Display voxel hazards
        foreach (var hazard in voxelHazards)
        {
            Color hazardColor = GetWarningColor(hazard.Value, hazard.Value * mySpeed);
            DrawLine(gridCenter, hazard.Key, hazardColor);

            if (ShowDebugSpheres)
            {
                DrawDebugSphere(hazard.Key, DebugSphereSize * 2f, hazardColor); // Larger sphere for voxel impacts
            }
        }

        // Text notification
        if (updateCounter % NotificationInterval == 0)
        {
            // Check for closest voxel hazard
            var closestVoxelHazard = voxelHazards.OrderBy(h => h.Value).FirstOrDefault();
            var highestThreat = sortedThreats.FirstOrDefault();

            // Determine which warning to show based on which is closer
            if (closestVoxelHazard.Value > 0 &&
                (highestThreat == null || closestVoxelHazard.Value < highestThreat.LastTimeToCollision))
            {
                string message = $"COLLISION WARNING [Asteroid]: {closestVoxelHazard.Value:F1}s";
                if (trackedTargets.Count > 0)
                {
                    message += $" | +{trackedTargets.Count} threats";
                }
                MyAPIGateway.Utilities.ShowNotification(message, 950, MyFontEnum.Red);
            }
            else if (highestThreat != null)
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

                MyAPIGateway.Utilities.ShowNotification(message, 950, MyFontEnum.Red);
            }
        }
    }
    private void ScanForVoxelHazards(Vector3D gridCenter, Vector3D velocityDirection, double speed)
    {
        voxelScanCounter++;
        if (voxelScanCounter % VoxelScanInterval != 0) return;

        voxelHazards.Clear();
        Vector3D mainDirection = Vector3D.Normalize(velocityDirection);

        // Create a cone of rays around the velocity vector
        for (int i = 0; i < VoxelRayCount; i++)
        {
            Vector3D rayDirection = GetRandomDirectionInCone(mainDirection, (float)VoxelScanSpread);
            Vector3D rayEnd = gridCenter + (rayDirection * VoxelRayRange);
            LineD ray = new LineD(gridCenter, rayEnd);

            List<MyLineSegmentOverlapResult<MyVoxelBase>> voxelHits = new List<MyLineSegmentOverlapResult<MyVoxelBase>>();
            MyGamePruningStructure.GetVoxelMapsOverlappingRay(ref ray, voxelHits);

            foreach (var voxelHit in voxelHits)
            {
                Vector3D? intersection;
                voxelHit.Element.GetIntersectionWithLine(ref ray, out intersection, false);

                if (intersection.HasValue)
                {
                    double distance = Vector3D.Distance(gridCenter, intersection.Value);
                    double timeToCollision = distance / Math.Max(speed, 0.1); // Avoid division by zero

                    // Only store if it's a potential threat
                    if (timeToCollision < VoxelRayRange / speed)
                    {
                        voxelHazards[intersection.Value] = timeToCollision;
                    }
                }
            }
        }
    }
    private double CalculateThreatLevel(double timeToCollision, Vector3D myVelocity, Vector3D? targetVelocity)
    {
        // Relative speed squared, as kinetic energy is proportional to v^2
        double relativeSpeed = targetVelocity.HasValue
            ? (myVelocity - targetVelocity.Value).LengthSquared()
            : myVelocity.LengthSquared();

        // Apply weighting for time to collision to scale the threat level logarithmically
        double timeFactor = 1.0 / (timeToCollision + 0.5); // Add offset to avoid division by zero
        return relativeSpeed * timeFactor;
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
    private Color GetWarningColor(double timeToCollision, double threatLevel)
    {
        // Normalize time-to-collision (nearer threats are more red)
        float normalizedTime = 1f - (float)Math.Min(timeToCollision / MaxRange, 1.0);

        // Normalize threat level with an exponential factor for more gradation
        float normalizedThreat = (float)Math.Min(Math.Pow(threatLevel / 100.0, 0.5), 1.0);

        return new Color(
            (byte)(255 * normalizedThreat),  // Red increases with threat
            (byte)(255 * (1.0 - normalizedThreat) * normalizedTime),  // Green diminishes closer to impact
            0); // Blue remains 0 for pure warning colors
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
