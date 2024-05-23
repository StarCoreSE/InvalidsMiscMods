using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.ModAPI;

namespace Scripts.ModularAssemblies
{
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]

    public class PIDController
    {
        private float _kP, _kI, _kD;
        private float _integral, _previousError;

        public PIDController(float kP, float kI, float kD)
        {
            _kP = kP;
            _kI = kI;
            _kD = kD;
        }

        public float Compute(float setpoint, float actual, float deltaTime)
        {
            float error = setpoint - actual;
            _integral += error * deltaTime;
            float derivative = (error - _previousError) / deltaTime;
            _previousError = error;
            return _kP * error + _kI * _integral + _kD * derivative;
        }
    }

    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public class OCFi_ReactorLogic : MySessionComponentBase
    {
        public static List<OCFi_ReactorLogic> Reactors = new List<OCFi_ReactorLogic>();
        public int PhysicalAssemblyId { get; private set; }
        public float Temperature { get; private set; }
        private List<IMyCubeBlock> _parts = new List<IMyCubeBlock>();

        private const float maxTemperature = 5000f;
        private const float targetTemperature = 3500f;
        private const float maxHeatGenerationRate = 500f; // Maximum heat generated per tick
        private PIDController _pidController;
        private float controlRodAdjustment = 0f; // Adjustment factor from PID

        public OCFi_ReactorLogic(int physicalAssemblyId)
        {
            PhysicalAssemblyId = physicalAssemblyId;
            _pidController = new PIDController(0.1f, 0.01f, 0.01f); // Tune these values as necessary
        }

        public override void LoadData()
        {
            Reactors.Add(this);
            MyAPIGateway.Utilities.ShowNotification("Reactor loaded", 1000 / 60);
        }

        protected override void UnloadData()
        {
            Reactors.Remove(this);
            MyAPIGateway.Utilities.ShowNotification("Reactor unloaded", 1000 / 60);
        }

        public override void UpdateAfterSimulation()
        {
            float deltaTime = 1.0f / 60.0f; // Assuming the simulation updates at 60 FPS
            UpdateTemperature(deltaTime);
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

        private void UpdateTemperature(float deltaTime)
        {
            // Compute control rod adjustment using PID controller
            controlRodAdjustment = _pidController.Compute(targetTemperature, Temperature, deltaTime);

            // Adjust temperature based on control rod adjustment and cap the heat generation rate
            float heatGenerated = controlRodAdjustment;
            if (heatGenerated > maxHeatGenerationRate)
            {
                heatGenerated = maxHeatGenerationRate;
            }

            Temperature += heatGenerated * deltaTime;

            // Ensure temperature remains within bounds
            if (Temperature > maxTemperature)
            {
                Temperature = maxTemperature;
                MyAPIGateway.Utilities.ShowNotification("Reactor overheated! Control rods at maximum!", 1000 / 60);
            }
            else if (Temperature < 0)
            {
                Temperature = 0;
            }
        }

        public bool ConsumeHeat(float amount)
        {
            if (Temperature >= amount)
            {
                Temperature -= amount;
                // MyAPIGateway.Utilities.ShowNotification($"Heat consumed: {amount} K, remaining: {Temperature} K", 1000 / 60);
                return true;
            }
            return false;
        }

        private void ShowTemperatureNotification()
        {
            MyAPIGateway.Utilities.ShowNotification($"Reactor Temperature: {Temperature} K", 1000 / 60);
        }
    }
}
