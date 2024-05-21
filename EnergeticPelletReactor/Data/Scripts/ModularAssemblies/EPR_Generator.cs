using Scripts.ModularAssemblies.Communication;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRage.Game.Components;

namespace Scripts.ModularAssemblies
{
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    internal class SessionLogic : MySessionComponentBase
    {
        public ModularDefinitionApi ModularApi = new ModularDefinitionApi();

        public override void LoadData()
        {
            ModularApi.Init(ModContext); // ModContext is available to all session and gamelogic components.
        }

        public override void UpdateAfterSimulation()
        {
            if (!ModularApi.IsReady)
                return;

            // do stuff
        }
    }
}
