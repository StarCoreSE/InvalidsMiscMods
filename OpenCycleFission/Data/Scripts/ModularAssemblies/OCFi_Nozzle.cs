using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Game.ModAPI;
using Scripts.ModularAssemblies.Communication;

namespace Scripts.ModularAssemblies
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Thrust), false, "OCFi_Nozzle")]
    public class OCFi_NozzleLogic : MyGameLogicComponent
    {
        private IMyThrust _nozzle;
        private int _assemblyId;
        private OCFi_ReactorLogic _reactor;
        private float heatConsumption = 1000f; // Heat consumed per tick
        private bool isFiring = false;
        private bool reactorFound = false; // To cache the reactor status

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);
            _nozzle = (IMyThrust)Entity;
            NeedsUpdate |= MyEntityUpdateEnum.EACH_FRAME;

            MyAPIGateway.Utilities.ShowNotification($"OCFi Nozzle Initialized: {_nozzle.CustomName}", 1000 / 60);
        }

        public override void UpdateOnceBeforeFrame()
        {
            base.UpdateOnceBeforeFrame();
            FindAssembly();

        }

        public override void UpdateAfterSimulation()
        {
            base.UpdateAfterSimulation();
            isFiring = _nozzle.CurrentThrust > 0.01f; // Slightly above zero to ensure detection

            if (isFiring)
            {
                MyAPIGateway.Utilities.ShowNotification($"Nozzle is firing, checking reactor...", 1000 / 60);
                if (reactorFound)
                {
                    if (_reactor != null && _reactor.ConsumeHeat(heatConsumption))
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
            else
            {
                //MyAPIGateway.Utilities.ShowNotification("Nozzle not firing", 1000 / 60);
            }
        }

        private void FindAssembly()
        {
            ModularDefinitionApi modularApi = OCFiManager.I?.ModularApi;
            if (modularApi == null)
            {
                MyAPIGateway.Utilities.ShowNotification("Modular API not ready.", 1000 / 60);
                return;
            }

            _assemblyId = modularApi.GetContainingAssembly(_nozzle, "OCFi_Nozzle");
            MyAPIGateway.Utilities.ShowNotification($"Nozzle assembly ID: {_assemblyId}", 1000 / 60);

            if (_assemblyId != -1)
            {
                MyAPIGateway.Utilities.ShowNotification($"Assembly found: {_assemblyId}", 1000 / 60);
                FindReactor();
            }
            else
            {
                MyAPIGateway.Utilities.ShowNotification("No assembly found for nozzle.", 1000 / 60);
            }
        }

        private void FindReactor()
        {
            MyAPIGateway.Utilities.ShowNotification("Finding reactor for nozzle...", 1000 / 60);
            ModularDefinitionApi modularApi = OCFiManager.I?.ModularApi;
            if (modularApi == null)
            {
                MyAPIGateway.Utilities.ShowNotification("Modular API not ready.", 1000 / 60);
                return;
            }

            var parts = modularApi.GetMemberParts(_assemblyId);
            MyAPIGateway.Utilities.ShowNotification($"Total parts in assembly {_assemblyId}: {parts.Length}", 1000 / 60);

            foreach (var part in parts)
            {
                var reactor = part as IMyReactor;
                if (reactor != null)
                {
                    var reactorLogic = reactor.GameLogic.GetAs<OCFi_ReactorLogic>();
                    if (reactorLogic != null)
                    {
                        _reactor = reactorLogic;
                        reactorFound = true;
                        MyAPIGateway.Utilities.ShowNotification("Reactor found for nozzle", 1000 / 60);
                        return;
                    }
                }
                else
                {
                    MyAPIGateway.Utilities.ShowNotification($"Part is not a reactor: {part.DisplayNameText}", 1000 / 60);
                }
            }

            MyAPIGateway.Utilities.ShowNotification("No reactor found for nozzle", 1000 / 60);
        }
    }
}
