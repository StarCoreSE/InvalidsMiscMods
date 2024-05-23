using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Game.ModAPI;

namespace Scripts.ModularAssemblies
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Thrust), false, "OCFi_Nozzle")]
    public class OCFi_NozzleLogic : MyGameLogicComponent
    {
        private IMyThrust _nozzle;
        private float heatConsumption = 1f; // Heat consumed per tick
        private bool isFiring = false;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);
            _nozzle = (IMyThrust)Entity;
            NeedsUpdate |= MyEntityUpdateEnum.EACH_FRAME;
            MyAPIGateway.Utilities.ShowNotification($"OCFi Nozzle Initialized: {_nozzle.CustomName}", 1000 / 60);
        }

        public override void UpdateAfterSimulation()
        {
            base.UpdateAfterSimulation();
            isFiring = _nozzle.CurrentThrust > 0.01f; // Slightly above zero to ensure detection

            if (isFiring)
            {
                foreach (var reactor in OCFi_ReactorLogic.Reactors)
                {
                    if (reactor.ConsumeHeat(heatConsumption))
                    {
                        //MyAPIGateway.Utilities.ShowNotification($"Nozzle consuming heat: {heatConsumption} K", 1000 / 60);
                        break; // Stop after finding the first reactor with enough heat
                    }
                }
            }
        }
    }
}