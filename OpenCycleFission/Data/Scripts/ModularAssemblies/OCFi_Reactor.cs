using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Utils;

namespace Scripts.ModularAssemblies
{
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public class OCFi_ReactorLogic : MySessionComponentBase
    {
        public int PhysicalAssemblyId { get; private set; }
        private List<IMyCubeBlock> _parts = new List<IMyCubeBlock>();

        private float temperature = 0f;
        private const float maxTemperature = 5000f;
        private const float temperatureIncrement = 50f;

        public OCFi_ReactorLogic(int physicalAssemblyId)
        {
            PhysicalAssemblyId = physicalAssemblyId;
            MyAPIGateway.Utilities.ShowNotification($"Reactor {physicalAssemblyId} created", 1000 / 60);
        }

        public override void UpdateAfterSimulation()
        {
            UpdateTemperature();
            ShowTemperatureNotification();
        }

        public void AddPart(IMyCubeBlock block)
        {
            _parts.Add(block);
            MyAPIGateway.Utilities.ShowNotification($"Part added to reactor {PhysicalAssemblyId}: {block.DisplayNameText}", 1000 / 60);
        }

        public void RemovePart(IMyCubeBlock block)
        {
            _parts.Remove(block);
            MyAPIGateway.Utilities.ShowNotification($"Part removed from reactor {PhysicalAssemblyId}: {block.DisplayNameText}", 1000 / 60);
        }

        private void UpdateTemperature()
        {
            if (temperature < maxTemperature)
            {
                temperature += temperatureIncrement;
            }
        }

        public bool ConsumeHeat(float amount)
        {
            if (temperature >= amount)
            {
                temperature -= amount;
                MyAPIGateway.Utilities.ShowNotification($"Heat consumed: {amount} K, remaining: {temperature} K", 1000 / 60);
                return true;
            }
            return false;
        }

        private void ShowTemperatureNotification()
        {
            MyAPIGateway.Utilities.ShowNotification($"Reactor Temperature: {temperature} K", 1000 / 60);
        }
    }
}