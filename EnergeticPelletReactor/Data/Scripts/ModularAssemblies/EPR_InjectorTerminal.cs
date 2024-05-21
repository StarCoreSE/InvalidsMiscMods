using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Game.Components;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;

namespace Scripts.ModularAssemblies
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Collector), false, "EPR_Injector")]
    public class EPR_Injector : MyGameLogicComponent
    {
        private bool controlsCreated = false;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            NeedsUpdate = MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
        }

        public override void UpdateOnceBeforeFrame()
        {
            if (!controlsCreated)
            {
                EPRInjectorTerminalControls.CreateControlsOnce(ModContext);
                controlsCreated = true;
            }

            // Add additional initialization code if needed
        }

        public override void Close()
        {
            base.Close();
            controlsCreated = false;
        }
    }

    public static class EPRInjectorTerminalControls
    {
        const string IdPrefix = "YourMod_";

        static bool controlsCreated = false;

        public static void CreateControlsOnce(IMyModContext context)
        {
            if (controlsCreated)
                return;

            controlsCreated = true;
            CreateControls();
            CreateActions(context);
        }

        static bool CustomVisibleCondition(IMyTerminalBlock block)
        {
            return block?.BlockDefinition.SubtypeId == "EPR_Injector";
        }

        static void CreateControls()
        {
            {
                var button = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyCollector>(IdPrefix + "Button");
                button.Title = MyStringId.GetOrCompute("Inject");
                button.Tooltip = MyStringId.GetOrCompute("Perform the injection action");
                button.Action = ButtonAction;
                button.Visible = CustomVisibleCondition;
                MyAPIGateway.TerminalControls.AddControl<IMyCollector>(button);
            }
        }

        static void ButtonAction(IMyTerminalBlock block)
        {
            MyAPIGateway.Utilities.ShowNotification("Injector Button pressed", 2000);
        }

        static void CreateActions(IMyModContext context)
        {
            // Implement actions if needed
        }
    }
}