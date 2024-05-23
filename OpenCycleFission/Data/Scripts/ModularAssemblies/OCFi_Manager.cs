using System.Collections.Generic;
using System.Linq;
using Sandbox.ModAPI;
using Scripts.ModularAssemblies.Communication;
using VRage.Game.Components;
using VRage.Game.ModAPI;

namespace Scripts.ModularAssemblies
{
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    internal class OCFiManager : MySessionComponentBase
    {
        public static OCFiManager I;
        public ModularDefinitionApi ModularApi = new ModularDefinitionApi();
        public Dictionary<int, OCFi_ReactorLogic> Reactors = new Dictionary<int, OCFi_ReactorLogic>();

        private bool _didRegisterAssemblyClose = false;

        public override void LoadData()
        {
            ModularApi.Init(ModContext);
            I = this;
            MyAPIGateway.Utilities.ShowNotification("OCFiManager loaded", 1000 / 60);
        }

        protected override void UnloadData()
        {
            I = null;
            ModularApi.UnloadData();
            MyAPIGateway.Utilities.ShowNotification("OCFiManager unloaded", 1000 / 60);
        }

        public override void UpdateAfterSimulation()
        {
            if (!ModularApi.IsReady)
            {
                MyAPIGateway.Utilities.ShowNotification("Modular API not ready", 1000 / 60);
                return;
            }

            if (!_didRegisterAssemblyClose)
            {
                ModularApi.AddOnAssemblyClose(assemblyId => Reactors.Remove(assemblyId));
                _didRegisterAssemblyClose = true;
                MyAPIGateway.Utilities.ShowNotification("Registered assembly close handler", 1000 / 60);
            }

            foreach (var reactor in Reactors.Values)
                reactor.UpdateAfterSimulation();

            var systems = ModularApi.GetAllAssemblies();
            foreach (var reactor in Reactors.Values.ToList())
                if (!systems.Contains(reactor.PhysicalAssemblyId))
                    Reactors.Remove(reactor.PhysicalAssemblyId);
        }

        public void OnPartAdd(int PhysicalAssemblyId, IMyCubeBlock NewBlockEntity, bool IsBaseBlock)
        {
            MyAPIGateway.Utilities.ShowNotification($"OnPartAdd called: Assembly {PhysicalAssemblyId}, Block {NewBlockEntity.DisplayNameText}, IsBaseBlock {IsBaseBlock}", 1000 / 60);

            if (!Reactors.ContainsKey(PhysicalAssemblyId))
            {
                Reactors.Add(PhysicalAssemblyId, new OCFi_ReactorLogic(PhysicalAssemblyId, NewBlockEntity));
                MyAPIGateway.Utilities.ShowNotification($"New reactor logic created for assembly {PhysicalAssemblyId}", 1000 / 60);
            }

            Reactors[PhysicalAssemblyId].AddPart(NewBlockEntity);
        }

        public void OnPartRemove(int PhysicalAssemblyId, IMyCubeBlock BlockEntity, bool IsBaseBlock)
        {
            MyAPIGateway.Utilities.ShowNotification($"OnPartRemove called: Assembly {PhysicalAssemblyId}, Block {BlockEntity.DisplayNameText}, IsBaseBlock {IsBaseBlock}", 1000 / 60);

            if (!Reactors.ContainsKey(PhysicalAssemblyId))
                return;

            Reactors[PhysicalAssemblyId].RemovePart(BlockEntity);
        }

        public OCFi_ReactorLogic GetReactorForNozzle(IMyCubeBlock nozzle)
        {
            foreach (var reactor in Reactors.Values)
            {
                if (reactor.ContainsPart(nozzle))
                {
                    return reactor;
                }
            }
            return null;
        }
    }
}
