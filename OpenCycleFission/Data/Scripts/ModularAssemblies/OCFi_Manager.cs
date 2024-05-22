using System.Collections.Generic;
using System.Linq;
using Scripts.ModularAssemblies.Communication;
using VRage.Game.Components;
using VRage.Game.ModAPI;

namespace Scripts.ModularAssemblies
{
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    internal class OCFiManager : MySessionComponentBase
    {
        public ModularDefinitionApi ModularApi = new ModularDefinitionApi();
        public Dictionary<int, OCFi_ReactorLogic> Reactors = new Dictionary<int, OCFi_ReactorLogic>();

        private bool _didRegisterAssemblyClose = false;

        public override void LoadData()
        {
            ModularApi.Init(ModContext);
        }

        public override void UpdateAfterSimulation()
        {
            if (!ModularApi.IsReady)
                return;

            if (!_didRegisterAssemblyClose)
            {
                ModularApi.AddOnAssemblyClose(assemblyId => Reactors.Remove(assemblyId));
                _didRegisterAssemblyClose = true;
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
            if (!Reactors.ContainsKey(PhysicalAssemblyId))
                Reactors.Add(PhysicalAssemblyId, new OCFi_ReactorLogic(PhysicalAssemblyId));

            Reactors[PhysicalAssemblyId].AddPart(NewBlockEntity);
        }

        public void OnPartRemove(int PhysicalAssemblyId, IMyCubeBlock BlockEntity, bool IsBaseBlock)
        {
            if (!Reactors.ContainsKey(PhysicalAssemblyId))
                return;

            if (!IsBaseBlock)
                Reactors[PhysicalAssemblyId].RemovePart(BlockEntity);
        }
    }
}