using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Game.Components;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace Scripts.ModularAssemblies
{
    public static class EPRGeneratorTerminalControls
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
            return block?.BlockDefinition.SubtypeId == "EPR_Generator";
        }

        static void CreateControls()
        {
            {
                var button1 = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyBatteryBlock>(IdPrefix + "Button1");
                button1.Title = MyStringId.GetOrCompute("Button 1");
                button1.Tooltip = MyStringId.GetOrCompute("This is button 1");
                button1.Action = Button1Action;
                button1.Visible = CustomVisibleCondition;
                MyAPIGateway.TerminalControls.AddControl<IMyBatteryBlock>(button1);
            }

            {
                var button2 = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyBatteryBlock>(IdPrefix + "Button2");
                button2.Title = MyStringId.GetOrCompute("Button 2");
                button2.Tooltip = MyStringId.GetOrCompute("This is button 2");
                button2.Action = Button2Action;
                button2.Visible = CustomVisibleCondition;
                MyAPIGateway.TerminalControls.AddControl<IMyBatteryBlock>(button2);
            }

            {
                var button3 = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyBatteryBlock>(IdPrefix + "Button3");
                button3.Title = MyStringId.GetOrCompute("Button 3");
                button3.Tooltip = MyStringId.GetOrCompute("This is button 3");
                button3.Action = Button3Action;
                button3.Visible = CustomVisibleCondition;
                MyAPIGateway.TerminalControls.AddControl<IMyBatteryBlock>(button3);
            }
        }

        static void Button1Action(IMyTerminalBlock block)
        {
            MyAPIGateway.Utilities.ShowNotification("Button 1 pressed", 2000);
        }

        static void Button2Action(IMyTerminalBlock block)
        {
            MyAPIGateway.Utilities.ShowNotification("Button 2 pressed", 2000);
        }

        static void Button3Action(IMyTerminalBlock block)
        {
            MyAPIGateway.Utilities.ShowNotification("Button 3 pressed", 2000);
        }

        static void CreateActions(IMyModContext context)
        {
            // Implement actions if needed
        }
    }


    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_BatteryBlock), false, "EPR_Generator")]
    public class EPR_Generator : MyGameLogicComponent
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
                EPRGeneratorTerminalControls.CreateControlsOnce(ModContext);
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
}
