using Sandbox.ModAPI;
using System.Collections.Generic;
using System.Linq;
using VRage.Game;
using VRage.Game.Components;
using VRage.Utils;
using Sandbox.Definitions;
using Sandbox.Game.GameSystems;
using SpaceEngineers.Game.ModAPI;
using VRage.ObjectBuilders;

namespace FakeHydro
{
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class FakeHydroSession : MySessionComponentBase
    {
        public static bool IsServer;
        public static AlteredThrusters ThrusterTypes = new AlteredThrusters();
        private static SerializableDefinitionId _hydroId;
        private static float _energyDensity = 0.001556f;
        private static bool _idsLoaded = false;

        public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
        {
            IsServer = MyAPIGateway.Multiplayer.IsServer;
            
            // MyLog.Default.WriteLine($"##MOD: FakeHydro: is server {IsServer}");
        }

        public override void LoadData()
        {
            LoadThrusterDefinitions();
        }
        
        protected override void UnloadData()
        {
            Unload();
        }
        
        private void Unload()
        {
            var thrusterDefs = MyDefinitionManager.Static.GetDefinitionsOfType<MyThrustDefinition>();
            
            foreach (var thruster in thrusterDefs)
            {
                if (thruster == null) continue;
                
                if (!ThrusterTypes.Thrusters.Any(t => t.SubtypeId == thruster.Id.SubtypeName)) continue;
                
                // MyLog.Default.WriteLine($"##MOD: FakeHydro was hydro thruster: {thruster.Id.SubtypeName}");

                UnAlterDefinition(thruster);
            }

            _idsLoaded = false;
        }

        public static bool IsAlteredThruster(IMyThrust thrust)
        {
            if (thrust == null) return false;
            
            return ThrusterTypes.Thrusters.Any(t => t.SubtypeId == thrust.BlockDefinition.SubtypeId);
        }
        
        private static void LoadThrusterDefinitions()
        {
            var thrusterDefs = MyDefinitionManager.Static.GetDefinitionsOfType<MyThrustDefinition>();
            
            // MyLog.Default.WriteLine($"##MOD: FakeHydro: total thrusterDefs {thrusterDefs.Count}");
            
            foreach (var thruster in thrusterDefs)
            {
                if (thruster == null) continue;
                
                if (thruster.FuelConverter.FuelId.SubtypeName != "Hydrogen") continue;

                if (!_idsLoaded)
                {
                    _hydroId = thruster.FuelConverter.FuelId;
                    MyGasProperties fuelDef;
                    MyDefinitionManager.Static.TryGetDefinition(_hydroId, out fuelDef);

                    if (fuelDef != null)
                    {
                        _energyDensity = fuelDef.EnergyDensity;
                        _idsLoaded = true;
                    }
                }
                
                // MyLog.Default.WriteLine($"##MOD: FakeHydro this is hydro thruster: {thruster.Id.SubtypeName}");

                AlterDefinition(thruster);
            }
            
            // MyLog.Default.WriteLine($"##MOD: FakeHydro: _thrusterTypes.Thrusters {ThrusterTypes.Thrusters.Count}");
        }

        public static void AlterDefinition(MyThrustDefinition def)
        {
            if (def == null) return;

            if (!ThrusterTypes.Thrusters.Any(t => t.SubtypeId == def.Id.SubtypeName))
            {
                float efficiency = def.FuelConverter.Efficiency * _energyDensity;
                
                var newAlteredThruster = new AlteredThruster
                {
                    SubtypeId = def.Id.SubtypeName,
                    // calculate consumption in litres
                    MinConsumption = 0, //def.MinPowerConsumption / efficiency,
                    MaxConsumption = def.MaxPowerConsumption / efficiency,
                    
                    // store data for rollback
                    DefMinConsumption = def.MinPowerConsumption,
                    DefMaxConsumption = def.MaxPowerConsumption,
                    Efficiency = def.FuelConverter.Efficiency,
                };
                ThrusterTypes.Thrusters.Add(newAlteredThruster);
                
                // MyLog.Default.WriteLine($"##MOD: FakeHydro altered thruster: {def.Id.SubtypeName};{efficiency};{newAlteredThruster.MaxConsumption};{newAlteredThruster.MinConsumption}");
            }

            def.FuelConverter.FuelId = new SerializableDefinitionId();
            def.MaxPowerConsumption = 0.0000001f;
            def.MinPowerConsumption = 0.0000001f;
            
        }

        public static void UnAlterDefinition(MyThrustDefinition def)
        {
            if (def == null) return;

            var altered = ThrusterTypes.Thrusters.FirstOrDefault(t => t.SubtypeId == def.Id.SubtypeName);

            if (altered == null) return;
            
            def.FuelConverter.FuelId = _hydroId;
            def.MaxPowerConsumption = altered.DefMaxConsumption;
            def.MinPowerConsumption = altered.DefMinConsumption;
            def.FuelConverter.Efficiency = altered.Efficiency;

        }
        
    }

    public class AlteredThrusters
    {
        public List<AlteredThruster> Thrusters = new List<AlteredThruster>();
    }

    public class AlteredThruster
    {
        public string SubtypeId;
        public float MinConsumption;
        public float MaxConsumption;
        public float Efficiency;
        public float DefMinConsumption;
        public float DefMaxConsumption;
    }

    public class TankGroup
    {
        public List<IMyGasTank> Tanks = new List<IMyGasTank>();
        public List<IMyThrust> Thrusters = new List<IMyThrust>();
        public List<IMyOxygenFarm> HydrogenFarms = new List<IMyOxygenFarm>();
        public List<IMyGasGenerator> HydrogenGens = new List<IMyGasGenerator>();
        public float SinkValue;
    }
    
}