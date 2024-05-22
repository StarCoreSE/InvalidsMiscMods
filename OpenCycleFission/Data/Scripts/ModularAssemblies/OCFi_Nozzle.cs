using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using System.Collections.Generic;
using VRage.Game.ModAPI;

namespace Scripts.ModularAssemblies
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Thrust), false, "OCFi_Nozzle")]
    public class OCFi_NozzleLogic : MyGameLogicComponent
    {
        private IMyThrust _nozzle;
        private OCFi_ReactorLogic _reactor;
        private float heatConsumption = 1000f; // Heat consumed per tick
        private bool isFiring = false;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);
            _nozzle = (IMyThrust)Entity;
            NeedsUpdate |= MyEntityUpdateEnum.EACH_FRAME;
            MyAPIGateway.Utilities.ShowNotification($"OCFi Nozzle Initialized: {_nozzle.CustomName}", 1000 / 60);
            FindReactor();
        }

        public override void UpdateAfterSimulation()
        {
            base.UpdateAfterSimulation();
            isFiring = _nozzle.CurrentThrust > 0;

            if (isFiring)
            {
                MyAPIGateway.Utilities.ShowNotification($"Nozzle is firing, checking reactor...", 1000 / 60);
                if (_reactor != null)
                {
                    if (_reactor.ConsumeHeat(heatConsumption))
                    {
                        MyAPIGateway.Utilities.ShowNotification($"Nozzle consuming heat: {heatConsumption} K", 1000 / 60);
                    }
                    else
                    {
                        MyAPIGateway.Utilities.ShowNotification("Not enough heat available!", 1000 / 60);
                    }
                }
                else
                {
                    FindReactor();
                }
            }
        }

        private void FindReactor()
        {
            MyAPIGateway.Utilities.ShowNotification("Finding reactor for nozzle...", 1000 / 60);
            var grid = _nozzle.CubeGrid;
            var reactors = new List<IMySlimBlock>();
            grid.GetBlocks(reactors, block => block.FatBlock is IMyReactor);

            foreach (var reactor in reactors)
            {
                MyAPIGateway.Utilities.ShowNotification($"Checking reactor: {reactor.FatBlock.DisplayNameText}", 1000 / 60);
                var reactorLogic = reactor.FatBlock.GameLogic.GetAs<OCFi_ReactorLogic>();
                if (reactorLogic != null)
                {
                    _reactor = reactorLogic;
                    MyAPIGateway.Utilities.ShowNotification("Reactor found for nozzle", 1000 / 60);
                    return;
                }
            }

            MyAPIGateway.Utilities.ShowNotification("No reactor found for nozzle", 1000 / 60);
        }
    }
}
