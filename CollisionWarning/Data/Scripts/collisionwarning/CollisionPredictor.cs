using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;
using IMyCockpit = Sandbox.ModAPI.Ingame.IMyCockpit;

namespace CollisionPredictor
{

    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class CollisionPredictor : MySessionComponentBase
    {



        private const int MaxTrackedTargets = 100; // Maximum number of targets to track and display
        private const float MinimumThreatLevelToTrack = 0.1f; // Minimum threat level to consider tracking a target

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

        #region config

        public class CollisionConfig
        {
            public bool Debug { get; set; } = false;
            public float MinSpeed { get; set; } = 2f;
            public float MaxGridRange { get; set; } = 1000f;
            public double VoxelRayRange { get; set; } = 20000;
            public int NotificationInterval { get; set; } = 60;
            public int MaxTrackedTargets { get; set; } = 100;
            public float MinimumThreatLevelToTrack { get; set; } = 0.1f;
            public bool ShowDebugSpheres { get; set; } = true;
            public float DebugSphereSize { get; set; } = 2f;
            public int TargetMemoryDuration { get; set; } = 180;
            public float ThreatLevelDecayRate { get; set; } = 0.9f;
        }

        private CollisionConfig config;
        private const string CONFIG_FILE = "CollisionPredictorConfig.xml";

        private void LoadConfig()
        {
            try
            {
                if (MyAPIGateway.Utilities.FileExistsInGlobalStorage(CONFIG_FILE))
                {
                    var textReader = MyAPIGateway.Utilities.ReadFileInGlobalStorage(CONFIG_FILE);
                    var configText = textReader.ReadToEnd();
                    config = MyAPIGateway.Utilities.SerializeFromXML<CollisionConfig>(configText);
                    MyLog.Default.WriteLine($"CollisionPredictor: Loaded config from file");
                }
                else
                {
                    config = new CollisionConfig();
                    SaveConfig();
                    MyLog.Default.WriteLine($"CollisionPredictor: Created new config file");
                }
            }
            catch (Exception e)
            {
                MyLog.Default.WriteLine($"CollisionPredictor: Error loading config: {e}");
                config = new CollisionConfig();
            }
        }

        private void SaveConfig()
        {
            try
            {
                string xml = MyAPIGateway.Utilities.SerializeToXML(config);
                var textWriter = MyAPIGateway.Utilities.WriteFileInGlobalStorage(CONFIG_FILE);
                textWriter.Write(xml);
                textWriter.Flush();
                MyLog.Default.WriteLine($"CollisionPredictor: Saved config to file");
            }
            catch (Exception e)
            {
                MyLog.Default.WriteLine($"CollisionPredictor: Error saving config: {e}");
            }
        }

        #endregion

        #region commands
        private void HandleMessage(string messageText, ref bool sendToOthers)
        {
            string[] args = messageText.Split(' ');
            if (args[0].Equals("/collwarn", StringComparison.OrdinalIgnoreCase))
            {
                HandleCommand(args);
                sendToOthers = false;
            }
        }

        private void HandleCommand(string[] args)
        {
            if (args.Length < 2)
            {
                DisplayHelp();
                return;
            }

            switch (args[1].ToLower())
            {
                case "help":
                    DisplayHelp();
                    break;
                case "debug":
                    ToggleDebug();
                    break;
                case "minspeed":
                    float minSpeed;
                    if (args.Length > 2 && float.TryParse(args[2], out minSpeed))
                    {
                        SetMinSpeed(minSpeed);
                    }
                    break;
                case "maxrange":
                    float maxRange;
                    if (args.Length > 2 && float.TryParse(args[2], out maxRange))
                    {
                        SetMaxGridRange(maxRange);
                    }
                    break;
                case "voxelrange":
                    double voxelRange;
                    if (args.Length > 2 && double.TryParse(args[2], out voxelRange))
                    {
                        SetVoxelRayRange(voxelRange);
                    }
                    break;
                case "notifyinterval":
                    int notifyInterval;
                    if (args.Length > 2 && int.TryParse(args[2], out notifyInterval))
                    {
                        SetNotificationInterval(notifyInterval);
                    }
                    break;
                case "maxtargets":
                    int maxTargets;
                    if (args.Length > 2 && int.TryParse(args[2], out maxTargets))
                    {
                        SetMaxTrackedTargets(maxTargets);
                    }
                    break;
                case "minthreatlevel":
                    float minThreatLevel;
                    if (args.Length > 2 && float.TryParse(args[2], out minThreatLevel))
                    {
                        SetMinimumThreatLevelToTrack(minThreatLevel);
                    }
                    break;
                case "debugspheres":
                    ToggleDebugSpheres();
                    break;
                case "spheresize":
                    float sphereSize;
                    if (args.Length > 2 && float.TryParse(args[2], out sphereSize))
                    {
                        SetDebugSphereSize(sphereSize);
                    }
                    break;
                case "memoryduration":
                    int memoryDuration;
                    if (args.Length > 2 && int.TryParse(args[2], out memoryDuration))
                    {
                        SetTargetMemoryDuration(memoryDuration);
                    }
                    break;
                case "decayrate":
                    float decayRate;
                    if (args.Length > 2 && float.TryParse(args[2], out decayRate))
                    {
                        SetThreatLevelDecayRate(decayRate);
                    }
                    break;
                default:
                    MyAPIGateway.Utilities.ShowMessage("Collision Warning", "Unknown command. Available commands: debug, minspeed, maxrange, voxelrange, notifyinterval, maxtargets, minthreatlevel, debugspheres, spheresize, memoryduration, decayrate");
                    break;
            }
        }

        private void DisplayHelp()
        {
            StringBuilder helpText = new StringBuilder();
            helpText.AppendLine("Collision Warning Commands:");
            helpText.AppendLine("/collwarn debug - Toggle debug mode");
            helpText.AppendLine("/collwarn minspeed [value] - Set minimum speed");
            helpText.AppendLine("/collwarn maxrange [value] - Set maximum grid range");
            helpText.AppendLine("/collwarn voxelrange [value] - Set voxel ray range");
            helpText.AppendLine("/collwarn notifyinterval [value] - Set notification interval");
            helpText.AppendLine("/collwarn maxtargets [value] - Set max tracked targets");
            helpText.AppendLine("/collwarn minthreatlevel [value] - Set minimum threat level to track");
            helpText.AppendLine("/collwarn debugspheres - Toggle debug spheres");
            helpText.AppendLine("/collwarn spheresize [value] - Set debug sphere size");
            helpText.AppendLine("/collwarn memoryduration [value] - Set target memory duration");
            helpText.AppendLine("/collwarn decayrate [value] - Set threat level decay rate");

            MyAPIGateway.Utilities.ShowMessage("Collision Warning", helpText.ToString());
        }

        private void ToggleDebug()
        {
            config.Debug = !config.Debug;
            SaveConfig();
            MyAPIGateway.Utilities.ShowMessage("Collision Warning", $"Debug mode: {(config.Debug ? "ON" : "OFF")}");
        }

        private void SetMinSpeed(float speed)
        {
            config.MinSpeed = speed;
            SaveConfig();
            MyAPIGateway.Utilities.ShowMessage("Collision Warning", $"Minimum speed set to: {speed}");
        }

        private void SetMaxGridRange(float range)
        {
            config.MaxGridRange = range;
            SaveConfig();
            MyAPIGateway.Utilities.ShowMessage("Collision Warning", $"Maximum grid range set to: {range}");
        }

        private void SetVoxelRayRange(double range)
        {
            config.VoxelRayRange = range;
            SaveConfig();
            MyAPIGateway.Utilities.ShowMessage("Collision Warning", $"Voxel ray range set to: {range}");
        }

        private void SetNotificationInterval(int interval)
        {
            config.NotificationInterval = interval;
            SaveConfig();
            MyAPIGateway.Utilities.ShowMessage("Collision Warning", $"Notification interval set to: {interval}");
        }

        private void SetMaxTrackedTargets(int maxTargets)
        {
            config.MaxTrackedTargets = maxTargets;
            SaveConfig();
            MyAPIGateway.Utilities.ShowMessage("Collision Warning", $"Max tracked targets set to: {maxTargets}");
        }

        private void SetMinimumThreatLevelToTrack(float threatLevel)
        {
            config.MinimumThreatLevelToTrack = threatLevel;
            SaveConfig();
            MyAPIGateway.Utilities.ShowMessage("Collision Warning", $"Minimum threat level to track set to: {threatLevel}");
        }

        private void ToggleDebugSpheres()
        {
            config.ShowDebugSpheres = !config.ShowDebugSpheres;
            SaveConfig();
            MyAPIGateway.Utilities.ShowMessage("Collision Warning", $"Debug spheres: {(config.ShowDebugSpheres ? "ON" : "OFF")}");
        }

        private void SetDebugSphereSize(float size)
        {
            config.DebugSphereSize = size;
            SaveConfig();
            MyAPIGateway.Utilities.ShowMessage("Collision Warning", $"Debug sphere size set to: {size}");
        }

        private void SetTargetMemoryDuration(int duration)
        {
            config.TargetMemoryDuration = duration;
            SaveConfig();
            MyAPIGateway.Utilities.ShowMessage("Collision Warning", $"Target memory duration set to: {duration}");
        }

        private void SetThreatLevelDecayRate(float rate)
        {
            config.ThreatLevelDecayRate = rate;
            SaveConfig();
            MyAPIGateway.Utilities.ShowMessage("Collision Warning", $"Threat level decay rate set to: {rate}");
        }
        #endregion

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

            if (mySpeed < config.MinSpeed) return;

            var gridCenter = grid.Physics.CenterOfMassWorld;

            UpdateTrackedTargets();

            ScanForCollisions(grid, gridCenter, myVelocity);

            ScanForVoxelHazards(gridCenter, myVelocity, mySpeed);

            DisplayWarnings(gridCenter, mySpeed);
        }
        private void ScanForCollisions(IMyCubeGrid myGrid, Vector3D gridCenter, Vector3D myVelocity)
        {
            try
            {
                if (config.Debug)
                {
                    MyLog.Default.WriteLine($"CollisionPredictor: Starting collision scan for grid {myGrid.DisplayName}");
                }

                var visibleEntities = new HashSet<IMyEntity>();
                MyAPIGateway.Entities.GetEntities(null, (entity) =>
                {
                    if (entity is IMyCubeGrid && entity != myGrid)
                    {
                        visibleEntities.Add(entity);
                    }
                    return false;
                });

                if (config.Debug)
                {
                    MyLog.Default.WriteLine($"CollisionPredictor: Found {visibleEntities.Count} potential targets");
                }

                foreach (var entity in visibleEntities)
                {
                    try
                    {
                        if (entity == null)
                        {
                            if (config.Debug)
                            {
                                MyLog.Default.WriteLine("CollisionPredictor: Encountered null entity");
                            }

                            continue;
                        }

                        var targetGrid = entity as IMyCubeGrid;
                        if (targetGrid == null)
                        {
                            if (config.Debug)
                            {
                                MyLog.Default.WriteLine($"CollisionPredictor: Entity {entity.EntityId} is not a grid");
                            }
                            continue;
                        }

                        if (targetGrid.Physics == null)
                        {

                            MyLog.Default.WriteLine($"CollisionPredictor: Grid {targetGrid.EntityId} has null physics");
                            continue;
                        }

                        // Safely get target properties
                        Vector3D targetCenter;
                        Vector3D targetVelocity;

                        try
                        {
                            targetCenter = targetGrid.Physics.CenterOfMassWorld;
                            targetVelocity = targetGrid.Physics.LinearVelocity;
                        }
                        catch (Exception e)
                        {
                            MyLog.Default.WriteLine($"CollisionPredictor: Error getting physics properties for grid {targetGrid.EntityId}: {e.Message}");
                            continue;
                        }

                        // Check if the target is within a reasonable range
                        double distanceToTarget = Vector3D.Distance(gridCenter, targetCenter);
                        if (distanceToTarget > config.MaxGridRange)
                        {
                            MyLog.Default.WriteLine($"CollisionPredictor: Grid {targetGrid.EntityId} is out of range");
                            continue;
                        }

                        // Rest of your collision detection logic
                        Vector3D relativePosition = targetCenter - gridCenter;
                        Vector3D relativeVelocity = targetVelocity - myVelocity;

                        // Safely check for zero velocity
                        if (relativeVelocity.LengthSquared() < 0.0001)
                        {
                            MyLog.Default.WriteLine($"CollisionPredictor: Grid {targetGrid.EntityId} has near-zero relative velocity");
                            continue;
                        }

                        double timeToClosestApproach = -Vector3D.Dot(relativePosition, relativeVelocity) / relativeVelocity.LengthSquared();

                        if (timeToClosestApproach < 0)
                        {
                            MyLog.Default.WriteLine($"CollisionPredictor: Grid {targetGrid.EntityId} has negative time to closest approach");
                            continue;
                        }

                        Vector3D positionAtClosestApproach = relativePosition + relativeVelocity * timeToClosestApproach;
                        double distanceAtClosestApproach = positionAtClosestApproach.Length();

                        // Safely get bounding spheres
                        double myRadius = 0;
                        double targetRadius = 0;

                        try
                        {
                            myRadius = myGrid.WorldVolume.Radius;
                            targetRadius = targetGrid.WorldVolume.Radius;
                        }
                        catch (Exception e)
                        {
                            MyLog.Default.WriteLine($"CollisionPredictor: Error getting world volume for grid {targetGrid.EntityId}: {e.Message}");
                            myRadius = 10;
                            targetRadius = 10;
                        }

                        double collisionThreshold = myRadius + targetRadius;
                        if (distanceAtClosestApproach > collisionThreshold)
                        {
                            MyLog.Default.WriteLine($"CollisionPredictor: Grid {targetGrid.EntityId} is not on collision course");
                            continue;
                        }

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

                            MyLog.Default.WriteLine($"CollisionPredictor: Added grid {targetGrid.EntityId} to tracked targets");
                        }
                        else
                        {
                            MyLog.Default.WriteLine($"CollisionPredictor: Could not calculate time to collision for grid {targetGrid.EntityId}");
                        }
                    }
                    catch (Exception entityException)
                    {
                        MyLog.Default.WriteLine($"CollisionPredictor: Error processing entity {entity?.EntityId}: {entityException.Message}");
                    }
                }
            }
            catch (Exception e)
            {
                MyLog.Default.WriteLine($"CollisionPredictor: Error in ScanForCollisions: {e.Message}");
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

                target.ThreatLevel *= config.ThreatLevelDecayRate;

                if (target.LastSeenCounter > config.TargetMemoryDuration ||
                    target.ThreatLevel < config.MinimumThreatLevelToTrack ||
                    Vector3D.Distance(target.LastPosition, MyAPIGateway.Session.Player.GetPosition()) > config.MaxGridRange)
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
        private void DisplayWarnings(Vector3D gridCenter, double mySpeed)
        {
            var sortedThreats = trackedTargets.Values
                .Where(t => t.IsCurrentThreat && t.ThreatLevel >= config.MinimumThreatLevelToTrack)
                .OrderByDescending(t => t.ThreatLevel)
                .Take(config.MaxTrackedTargets);

            foreach (var target in sortedThreats)
            {
                Color warningColor = GetWarningColor(target.LastTimeToCollision, target.ThreatLevel);
                DrawLine(gridCenter, target.LastPosition, warningColor);

                if (config.ShowDebugSpheres)
                {
                    DrawDebugSphere(target.LastPosition, config.DebugSphereSize, warningColor);

                    if (target.LastVelocity.HasValue)
                    {
                        Vector3D predictedPos = target.LastPosition +
                                                target.LastVelocity.Value * target.LastTimeToCollision;
                        DrawDebugSphere(predictedPos, config.DebugSphereSize * 0.5f, Color.Yellow);
                    }
                }
            }

            foreach (var hazard in voxelHazards)
            {
                Color hazardColor = GetWarningColor(hazard.Value, hazard.Value * mySpeed);
                DrawLine(gridCenter, hazard.Key, hazardColor);

                if (config.ShowDebugSpheres)
                {
                    DrawDebugSphere(hazard.Key, config.DebugSphereSize * 2f, hazardColor);
                }
            }

            if (updateCounter % config.NotificationInterval == 0)
            {
                var closestVoxelHazard = voxelHazards.OrderBy(h => h.Value).FirstOrDefault();
                var highestThreat = sortedThreats.FirstOrDefault();

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

            if (config.Debug)
            {
                MyLog.Default.WriteLine($"CollisionPredictor: Starting voxel hazard scan from {gridCenter} with speed {speed}");
            }

            for (int i = 0; i < VoxelRayCount; i++)
            {
                Vector3D rayDirection = GetRandomDirectionInCone(mainDirection, (float)VoxelScanSpread);
                Vector3D rayEnd = gridCenter + (rayDirection * config.VoxelRayRange);
                LineD ray = new LineD(gridCenter, rayEnd);

                if (config.Debug)
                {
                    MyLog.Default.WriteLine($"CollisionPredictor: Scanning ray {i} from {gridCenter} to {rayEnd}");
                }

                List<MyLineSegmentOverlapResult<MyVoxelBase>> voxelHits = new List<MyLineSegmentOverlapResult<MyVoxelBase>>();
                MyGamePruningStructure.GetVoxelMapsOverlappingRay(ref ray, voxelHits);

                foreach (var voxelHit in voxelHits)
                {
                    Vector3D? intersection;
                    voxelHit.Element.GetIntersectionWithLine(ref ray, out intersection, false);

                    if (intersection.HasValue)
                    {
                        double distance = Vector3D.Distance(gridCenter, intersection.Value);
                        double timeToCollision = distance / Math.Max(speed, 0.1);

                        if (config.Debug)
                        {
                            MyLog.Default.WriteLine($"CollisionPredictor: Detected voxel hazard at {intersection.Value} with time to collision {timeToCollision}");
                        }

                        if (timeToCollision < config.VoxelRayRange / speed)
                        {
                            voxelHazards[intersection.Value] = timeToCollision;
                        }
                    }
                }
            }
        }
        private double CalculateThreatLevel(double timeToCollision, Vector3D myVelocity, Vector3D? targetVelocity)
        {
            double relativeSpeed = targetVelocity.HasValue
                ? (myVelocity - targetVelocity.Value).LengthSquared()
                : myVelocity.LengthSquared();

            double timeFactor = 1.0 / (timeToCollision + 0.5); // Add offset to avoid division by zero
            double threatLevel = relativeSpeed * timeFactor;

            if (config.Debug)
            {
                MyLog.Default.WriteLine($"CollisionPredictor: Calculated threat level {threatLevel} for time to collision {timeToCollision}");
            }

            return threatLevel;
        }
        private double? CalculateTimeToCollision(Vector3D myPosition, Vector3D myVelocity,
            Vector3D targetPosition, Vector3D? targetVelocity)
        {
            Vector3D relativePosition = targetPosition - myPosition;
            Vector3D relativeVelocity = targetVelocity.HasValue ?
                myVelocity - targetVelocity.Value : myVelocity;

            double relativeSpeed = relativeVelocity.Length();
            if (relativeSpeed < 1)
            {
                if (config.Debug)
                {
                    MyLog.Default.WriteLine($"CollisionPredictor: Relative speed {relativeSpeed} is too low for collision calculation");
                }
                return null;
            }

            double dot = Vector3D.Dot(relativePosition, relativeVelocity);
            if (dot < 0)
            {
                if (config.Debug)
                {
                    MyLog.Default.WriteLine($"CollisionPredictor: Negative dot product {dot}, no collision expected");
                }
                return null;
            }

            double timeToCollision = dot / (relativeSpeed * relativeSpeed);

            if (config.Debug)
            {
                MyLog.Default.WriteLine($"CollisionPredictor: Calculated time to collision {timeToCollision}");
            }

            return timeToCollision;
        }
        private Color GetWarningColor(double timeToCollision, double threatLevel)
        {
            float normalizedTime = 1f - (float)Math.Min(timeToCollision / config.MaxGridRange, 1.0);
            float normalizedThreat = (float)Math.Min(Math.Pow(threatLevel / 100.0, 0.5), 1.0);

            Color color = new Color(
                (byte)(255 * normalizedThreat),  // Red increases with threat
                (byte)(255 * (1.0 - normalizedThreat) * normalizedTime),  // Green diminishes closer to impact
                0); // Blue remains 0 for pure warning colors

            if (config.Debug)
            {
                MyLog.Default.WriteLine($"CollisionPredictor: Generated warning color {color} for time to collision {timeToCollision} and threat level {threatLevel}");
            }

            return color;
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

            Vector3D direction = Vector3D.Normalize(z * mainDirection + x * perp1 + y * perp2);

            if (config.Debug)
            {
                MyLog.Default.WriteLine($"CollisionPredictor: Generated random direction {direction} in cone around {mainDirection} with angle {coneAngle}");
            }

            return direction;
        }
        private void DrawLine(Vector3D start, Vector3D end, Color color)
        {
            var color1 = color.ToVector4();
            MySimpleObjectDraw.DrawLine(start, end, MyStringId.GetOrCompute("Square"), ref color1, 0.2f);

            if (config.Debug)
            {
                MyLog.Default.WriteLine($"CollisionPredictor: Drew line from {start} to {end} with color {color}");
            }
        }
        private void DrawDebugSphere(Vector3D position, float radius, Color color)
        {
            var matrix = MatrixD.CreateTranslation(position);
            Color color1 = color.ToVector4();
            MySimpleObjectDraw.DrawTransparentSphere(ref matrix, radius, ref color1, (MySimpleObjectRasterizer)0.5f, 32, MyStringId.GetOrCompute("Square"));

            if (config.Debug)
            {
                MyLog.Default.WriteLine($"CollisionPredictor: Drew debug sphere at {position} with radius {radius} and color {color}");
            }
        }

        public override void LoadData()
        {
            LoadConfig();
            MyAPIGateway.Utilities.MessageEntered += HandleMessage;
        }

        protected override void UnloadData()
        {
            try
            {
                SaveConfig();
                MyAPIGateway.Utilities.MessageEntered -= HandleMessage;
            }
            catch (Exception e)
            {
                MyLog.Default.WriteLine($"CollisionPredictor: Error in UnloadData: {e}");
            }
        }

    }

}