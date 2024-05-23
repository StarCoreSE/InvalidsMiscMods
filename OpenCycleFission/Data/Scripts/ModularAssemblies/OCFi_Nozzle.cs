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
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public class OCFi_NozzleLogic : MyGameLogicComponent
    {
        private IMyThrust _nozzle;
        private OCFi_ReactorLogic _reactor;
        private float heatConsumption = 1f; // Heat consumed per tick
        private bool isFiring = false;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);
            _nozzle = (IMyThrust)Entity;
            NeedsUpdate |= MyEntityUpdateEnum.EACH_FRAME;

            MyAPIGateway.Utilities.ShowNotification($"OCFi Nozzle Initialized: {_nozzle.CustomName}", 1000 / 60);
            NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
        }

        public override void UpdateOnceBeforeFrame()
        {
            base.UpdateOnceBeforeFrame();
            FindReactor();
        }

        public override void UpdateAfterSimulation()
        {
            base.UpdateAfterSimulation();
            isFiring = _nozzle.CurrentThrust > 0;

            if (isFiring)
            {
                if (_reactor != null)
                {
                    if (_reactor.ConsumeHeat(heatConsumption))
                    {
                        // MyAPIGateway.Utilities.ShowNotification($"Nozzle consuming heat: {heatConsumption} K", 1000 / 60);
                    }
                    else
                    {
                        // MyAPIGateway.Utilities.ShowNotification("Not enough heat available!", 1000 / 60);
                    }
                }
            }
        }

        private void FindReactor()
        {
            _reactor = OCFiManager.I?.GetReactorForNozzle(_nozzle);
            if (_reactor != null)
            {
                MyAPIGateway.Utilities.ShowNotification($"Reactor found for nozzle", 1000 / 60);
            }
            else
            {
                MyAPIGateway.Utilities.ShowNotification("No reactor found for nozzle", 1000 / 60);
            }
        }
    }
}
