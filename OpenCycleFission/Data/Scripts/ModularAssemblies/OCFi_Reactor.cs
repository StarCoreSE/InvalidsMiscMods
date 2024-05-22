using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Utils;

namespace Scripts.ModularAssemblies
{
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
        }

        public override void UpdateAfterSimulation()
        {
            UpdateTemperature();
            ShowTemperatureNotification();
        }

        public void AddPart(IMyCubeBlock block)
        {
            _parts.Add(block);
        }

        public void RemovePart(IMyCubeBlock block)
        {
            _parts.Remove(block);
        }

        private void UpdateTemperature()
        {
            if (temperature < maxTemperature)
            {
                temperature += temperatureIncrement;
            }
        }

        private void ShowTemperatureNotification()
        {
            MyAPIGateway.Utilities.ShowNotification($"Reactor Temperature: {temperature} K", 1000 / 60);
        }
    }
}