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
        private const float maxHeatConsumption = 1f; // Maximum heat consumed per tick
        private const float minTemperatureThreshold = 2000f; // Minimum temperature threshold for optimal thrust
        private bool isFiring = false;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);
            _nozzle = (IMyThrust)Entity;
            NeedsUpdate |= MyEntityUpdateEnum.EACH_FRAME;
            // MyAPIGateway.Utilities.ShowNotification($"OCFi Nozzle Initialized: {_nozzle.CustomName}", 1000 / 60);
        }

        public override void UpdateAfterSimulation()
        {
            base.UpdateAfterSimulation();
            isFiring = _nozzle.CurrentThrust > 0.01f; // Slightly above zero to ensure detection

            if (isFiring)
            {
                foreach (var reactor in OCFi_ReactorLogic.Reactors)
                {
                    float currentHeatConsumption = maxHeatConsumption;

                    // Reduce heat consumption if reactor temperature is below threshold
                    if (reactor.Temperature < minTemperatureThreshold)
                    {
                        float efficiency = reactor.Temperature / minTemperatureThreshold;
                        currentHeatConsumption *= efficiency;
                        _nozzle.ThrustMultiplier = efficiency;
                    }
                    else
                    {
                        _nozzle.ThrustMultiplier = 1.0f; // Full thrust efficiency
                    }

                    if (reactor.ConsumeHeat(currentHeatConsumption))
                    {
                        // MyAPIGateway.Utilities.ShowNotification($"Nozzle consuming heat: {currentHeatConsumption} K", 1000 / 60);
                        break; // Stop after finding the first reactor with enough heat
                    }
                }
            }
        }
    }
}
