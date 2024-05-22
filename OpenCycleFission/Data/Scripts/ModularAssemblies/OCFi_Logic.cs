using Scripts.ModularAssemblies;
using Scripts.ModularAssemblies.Communication;
using VRage.Game.Components;

namespace Scripts.ModularAssemblies
{
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    internal class SessionLogic : MySessionComponentBase
    {
        public ModularDefinitionApi ModularApi = new ModularDefinitionApi();

        public override void LoadData()
        {
            ModularApi.Init(ModContext); // ModContext is available to all session and game logic components.
        }

        public override void UpdateAfterSimulation()
        {
            if (!ModularApi.IsReady)
                return;

            // Add session-wide logic here
        }
    }
}