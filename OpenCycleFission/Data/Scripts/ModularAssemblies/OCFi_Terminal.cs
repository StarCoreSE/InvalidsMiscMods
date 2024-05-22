using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;
using System.Collections.Generic;

namespace Scripts.ModularAssemblies
{
    public static class EPRTerminalControls
    {
        private const string IdPrefix = "OCFi_";
        private static bool controlsCreated = false;

        public static void CreateControlsOnce()
        {
            if (controlsCreated)
                return;

            controlsCreated = true;
            CreateGeneratorControls();
            CreateInjectorControls();
            // Add calls to create controls for other blocks here
        }

        private static void CreateGeneratorControls()
        {
            MyAPIGateway.TerminalControls.CustomControlGetter += GeneratorCustomControlGetter;
        }

        private static void GeneratorCustomControlGetter(IMyTerminalBlock block, List<IMyTerminalControl> controls)
        {
            if (block?.BlockDefinition.SubtypeId == "OCFi_Reactor" && !ControlsAlreadyAdded(controls))
            {
                var button1 = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyBatteryBlock>(IdPrefix + "Button1");
                button1.Title = MyStringId.GetOrCompute("Button 1");
                button1.Tooltip = MyStringId.GetOrCompute("This is button 1");
                button1.Action = b => MyAPIGateway.Utilities.ShowNotification("Button 1 pressed", 2000);
                controls.Add(button1);

                var button2 = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyBatteryBlock>(IdPrefix + "Button2");
                button2.Title = MyStringId.GetOrCompute("Button 2");
                button2.Tooltip = MyStringId.GetOrCompute("This is button 2");
                button2.Action = b => MyAPIGateway.Utilities.ShowNotification("Button 2 pressed", 2000);
                controls.Add(button2);

                var button3 = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyBatteryBlock>(IdPrefix + "Button3");
                button3.Title = MyStringId.GetOrCompute("Button 3");
                button3.Tooltip = MyStringId.GetOrCompute("This is button 3");
                button3.Action = b => MyAPIGateway.Utilities.ShowNotification("Button 3 pressed", 2000);
                controls.Add(button3);
            }
        }

        private static void CreateInjectorControls()
        {
            MyAPIGateway.TerminalControls.CustomControlGetter += InjectorCustomControlGetter;
        }

        private static void InjectorCustomControlGetter(IMyTerminalBlock block, List<IMyTerminalControl> controls)
        {
            if (block?.BlockDefinition.SubtypeId == "OCFi_Nozzle" && !ControlsAlreadyAdded(controls))
            {
                var button = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyCollector>(IdPrefix + "Button");
                button.Title = MyStringId.GetOrCompute("Inject");
                button.Tooltip = MyStringId.GetOrCompute("Perform the injection action");
                button.Action = b => MyAPIGateway.Utilities.ShowNotification("Injector Button pressed", 2000);
                controls.Add(button);
            }
        }

        private static bool ControlsAlreadyAdded(List<IMyTerminalControl> controls)
        {
            foreach (var control in controls)
            {
                if (control.Id.StartsWith(IdPrefix))
                {
                    return true;
                }
            }
            return false;
        }

        // Add similar methods for other block types as needed
    }
}
