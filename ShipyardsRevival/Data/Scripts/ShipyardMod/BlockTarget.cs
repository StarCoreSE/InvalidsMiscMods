using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using ShipyardMod.Utility;
using VRage.Game.ModAPI;
using VRageMath;

namespace ShipyardMod.ItemClasses
{
    public class BlockTarget
    {
        public BlockTarget(IMySlimBlock block, ShipyardItem item)
        {
            Logging.Instance.WriteDebug($"[BlockTarget] Creating new target for block at position {block.Position}");

            Block = block;

            // Log physics and projector state
            if (CubeGrid.Physics == null)
                Logging.Instance.WriteDebug("[BlockTarget] Grid has no physics");
            if (Projector != null)
                Logging.Instance.WriteDebug("[BlockTarget] Block is from projector");

            // Calculate and log distances
            if (CubeGrid.Physics == null && Projector != null)
            {
                CenterDist = Vector3D.DistanceSquared(block.GetPosition(), Projector.GetPosition());
                Logging.Instance.WriteDebug($"[BlockTarget] Distance to projector: {Math.Sqrt(CenterDist):F2}m");
            }
            else
            {
                CenterDist = Vector3D.DistanceSquared(block.GetPosition(), block.CubeGrid.Center());
                Logging.Instance.WriteDebug($"[BlockTarget] Distance to grid center: {Math.Sqrt(CenterDist):F2}m");
            }

            // Calculate and log tool distances
            ToolDist = new Dictionary<long, double>();
            foreach (IMyCubeBlock tool in item.Tools)
            {
                double dist = Vector3D.DistanceSquared(block.GetPosition(), tool.GetPosition());
                ToolDist.Add(tool.EntityId, dist);
                Logging.Instance.WriteDebug($"[BlockTarget] Distance to tool {tool.EntityId}: {Math.Sqrt(dist):F2}m");
            }

            // Calculate build time
            var blockDef = (MyCubeBlockDefinition)block.BlockDefinition;
            BuildTime = blockDef.MaxIntegrity / blockDef.IntegrityPointsPerSec;
            Logging.Instance.WriteDebug($"[BlockTarget] Estimated build time: {BuildTime:F2}s");

            // Log initial block state
            var missingComponents = new Dictionary<string, int>();
            block.GetMissingComponents(missingComponents);
            if (missingComponents.Any())
            {
                Logging.Instance.WriteDebug("[BlockTarget] Initial missing components:");
                foreach (var component in missingComponents)
                    Logging.Instance.WriteDebug($"  {component.Key}: {component.Value}");
            }

            Logging.Instance.WriteDebug($"[BlockTarget] Initial integrity: {block.BuildIntegrity:F2}/{block.MaxIntegrity:F2}");
        }

        public IMyCubeGrid CubeGrid
        {
            get { return Block.CubeGrid; }
        }

        public IMyProjector Projector
        {
            get { return ((MyCubeGrid)Block.CubeGrid).Projector; }
        }

        public Vector3I GridPosition
        {
            get { return Block.Position; }
        }

        public bool CanBuild
        {
            get
            {
                if (CubeGrid.Physics != null)
                {
                    Logging.Instance.WriteDebug($"[BlockTarget] Block at {GridPosition} can build: has physics");
                    return true;
                }

                var result = Projector?.CanBuild(Block, false) == BuildCheckResult.OK;
                if (!result)
                {
                    var buildResult = Projector?.CanBuild(Block, false);
                    Logging.Instance.WriteDebug($"[BlockTarget] Block at {GridPosition} cannot build: {buildResult}");
                }
                return result;
            }
        }

        public IMySlimBlock Block { get; private set; }
        public float BuildTime { get; }
        public double CenterDist { get; }
        public Dictionary<long, double> ToolDist { get; }

        public void UpdateAfterBuild()
        {
            Logging.Instance.WriteDebug($"[BlockTarget] Updating block at {GridPosition} after build");

            Vector3D pos = Block.GetPosition();
            IMyCubeGrid grid = Projector.CubeGrid;
            Vector3I gridPos = grid.WorldToGridInteger(pos);
            IMySlimBlock newBlock = grid.GetCubeBlock(gridPos);

            if (newBlock != null)
            {
                Block = newBlock;
                Logging.Instance.WriteDebug($"[BlockTarget] Updated to new block. Integrity: {Block.BuildIntegrity:F2}/{Block.MaxIntegrity:F2}");

                // Log any remaining missing components
                var missingComponents = new Dictionary<string, int>();
                Block.GetMissingComponents(missingComponents);
                if (missingComponents.Any())
                {
                    Logging.Instance.WriteDebug("[BlockTarget] Post-build missing components:");
                    foreach (var component in missingComponents)
                        Logging.Instance.WriteDebug($"  {component.Key}: {component.Value}");
                }
            }
            else
            {
                Logging.Instance.WriteDebug("[BlockTarget] Failed to find new block after build");
            }
        }
    }
}