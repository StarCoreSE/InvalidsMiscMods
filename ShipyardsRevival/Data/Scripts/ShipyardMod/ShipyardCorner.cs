﻿using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using Sandbox.ModAPI.Interfaces.Terminal;
using ShipyardMod.ItemClasses;
using ShipyardMod.Settings;
using ShipyardMod.Utility;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageRender.Import;
using Sandbox.Engine.Physics;
using System.Text.RegularExpressions;

namespace ShipyardMod
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Collector), false, "ShipyardCorner_Large", "ShipyardCorner_Small")]
    public class ShipyardCorner : MyGameLogicComponent
    {
        private static bool _init;
        private static readonly MyDefinitionId PowerDef = MyResourceDistributorComponent.ElectricityId;
        private static readonly List<IMyTerminalControl> Controls = new List<IMyTerminalControl>();
        private IMyCollector _block;
        private float _maxpower;
        private float _power;
        private string _info = String.Empty;

        private MyResourceSinkComponent _sink = new MyResourceSinkComponent();

        public ShipyardItem Shipyard = null;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            _block = (IMyCollector)Container.Entity;
            _block.Components.TryGet(out _sink);
            //_block.NeedsUpdate = MyEntityUpdateEnum.NONE;
            NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
            NeedsUpdate |= MyEntityUpdateEnum.EACH_FRAME;
            NeedsUpdate |= MyEntityUpdateEnum.EACH_10TH_FRAME;
            _block.OnClosing += OnClosing;
            _block.AppendingCustomInfo += AppendingCustomInfo;
        }

        private void OnClosing(IMyEntity obj)
        {
            _block.OnClosing -= OnClosing;
            _block.AppendingCustomInfo -= AppendingCustomInfo;
            NeedsUpdate = MyEntityUpdateEnum.NONE;
        }

        public override void Close()
        {
            NeedsUpdate = MyEntityUpdateEnum.NONE;
        }

        public override void UpdateAfterSimulation()
        {
            if (_block?.CubeGrid?.Physics == null)
            {
                return;
            }

            var pos = _block.GetPosition();
            var offset = 2.5f;
            var mat = _block.WorldMatrix;
            float width = 0.125f;
            pos += -mat.Forward * offset;
            pos += -mat.Left * offset;
            pos += -mat.Up * offset;

            Vector3D[] directions = new Vector3D[] {
                mat.Forward,
                mat.Left,
                mat.Up
            };

            var lineColor = (VRageMath.Vector4)Color.Red;

            var red = (VRageMath.Vector4)Color.Red;
            var green = (VRageMath.Vector4)Color.Green;
            var blue = (VRageMath.Vector4)Color.Blue;
            var yellow = (VRageMath.Vector4)Color.Yellow;

            var material = MyStringId.GetOrCompute("Square");
            var blend = VRageRender.MyBillboard.BlendTypeEnum.PostPP;

            string subtypeShipyardCorner = _block.BlockDefinition.SubtypeId.ToString();
            float maxLength = 1000.0f; // let me guess, you need more?
            var cells = new List<Vector3I>();
            foreach (var direction in directions)
            {
                var endPoint = pos + direction * maxLength;
                cells.Clear();
                _block.CubeGrid.RayCastCells(pos, endPoint, cells);

                // path statuses:
                // red     = blocked by invalid block
                // yellow  = is only blocked by valid blocks
                // green   = completely valid segment
                // blue    = is unbounded by opposing shipyard corner
                
                int indexOfOpposingShipyardCorner = -1;
                for (int i = 0; i < cells.Count; i++)
                {
                    var block = _block.CubeGrid.GetCubeBlock(cells[i]);
                    if (block != null)
                    {
                        var subtypeOther = block.BlockDefinition.Id.SubtypeId.ToString();
                        if (subtypeOther == subtypeShipyardCorner && block.SlimId() != _block.SlimBlock.SlimId())
                        {
                            indexOfOpposingShipyardCorner = i;
                            break;
                        }
                    }
                }
                if (indexOfOpposingShipyardCorner > -1)
                {
                    // the segment is bounded by two shipyard corners

                    // clamp endpoint to position of opposing shipyard corner
                    endPoint = pos + direction * (indexOfOpposingShipyardCorner + 4.5);
                    var blockedByInvalid = false;
                    var pathHasEmptyCells = false;
                    for (int i = 3; i < indexOfOpposingShipyardCorner-1; i++)
                    {
                        // iterate the closed segment to determine status
                        var block = _block.CubeGrid.GetCubeBlock(cells[i]);
                        if (block != null)
                        {
                            var subtypeOther = block.BlockDefinition.Id.SubtypeId.ToString();
                            //encountered non-shipyard corner block
                            if (!(subtypeOther == "ShipyardConveyor_Large" || subtypeOther == "ShipyardConveyorMount_Large"))
                            {
                                // condition red; path is blocked by invalid block
                                blockedByInvalid = true;
                                break;
                            }
                        }
                        else
                        {
                            pathHasEmptyCells = true;
                        }
                    }
                    if (blockedByInvalid)
                    {
                        MySimpleObjectDraw.DrawLine(pos, endPoint, material, ref red, width, blend);
                        continue;
                    }
                    else if (pathHasEmptyCells)
                    {
                        MySimpleObjectDraw.DrawLine(pos, endPoint, material, ref yellow, width, blend);
                        continue;
                    }
                    else
                    {
                        MySimpleObjectDraw.DrawLine(pos, endPoint, material, ref green, width, blend);
                        continue;
                    }
                }
                else
                {
                    MySimpleObjectDraw.DrawLine(pos, endPoint, material, ref blue, width, blend);
                    continue;
                }
                
            }
        }

        public override void UpdateOnceBeforeFrame()
        {
            if (_init)
                return;

            _init = true;
            _block = Entity as IMyCollector;

            if (_block == null)
                return;

            //create terminal controls
            IMyTerminalControlSeparator sep = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSeparator, IMyCollector>(string.Empty);
            sep.Visible = b => b.BlockDefinition.SubtypeId.Contains("ShipyardCorner");
            MyAPIGateway.TerminalControls.AddControl<IMyCollector>(sep);

            IMyTerminalControlOnOffSwitch guideSwitch = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlOnOffSwitch, IMyCollector>("Shipyard_GuideSwitch");
            guideSwitch.Title = MyStringId.GetOrCompute("Guide Boxes");
            guideSwitch.Tooltip = MyStringId.GetOrCompute("Toggles the guide boxes drawn around grids in the shipyard.");
            guideSwitch.OnText = MyStringId.GetOrCompute("On");
            guideSwitch.OffText = MyStringId.GetOrCompute("Off");
            guideSwitch.Visible = b => b.BlockDefinition.SubtypeId.Contains("ShipyardCorner");
            guideSwitch.Enabled = b => b.BlockDefinition.SubtypeId.Contains("ShipyardCorner") && GetYard(b) != null;
            guideSwitch.SupportsMultipleBlocks = true;
            guideSwitch.Getter = GetGuideEnabled;
            guideSwitch.Setter = SetGuideEnabled;
            MyAPIGateway.TerminalControls.AddControl<IMyCollector>(guideSwitch);
            Controls.Add(guideSwitch);

            var lockSwitch = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlOnOffSwitch, IMyCollector>("Shipyard_LockSwitch");
            lockSwitch.Title = MyStringId.GetOrCompute("Advanced Locking");
            lockSwitch.Tooltip = MyStringId.GetOrCompute("Toggles locking grids in the shipyard when grinding or welding while moving.");
            lockSwitch.OnText = MyStringId.GetOrCompute("On");
            lockSwitch.OffText = MyStringId.GetOrCompute("Off");
            lockSwitch.Visible = b => b.BlockDefinition.SubtypeId.Equals("ShipyardCorner_Small");
            lockSwitch.Enabled = b => b.BlockDefinition.SubtypeId.Equals("ShipyardCorner_Small") && GetYard(b) != null;
            lockSwitch.SupportsMultipleBlocks = true;
            lockSwitch.Getter = GetLockEnabled;
            lockSwitch.Setter = SetLockEnabled;
            MyAPIGateway.TerminalControls.AddControl<IMyCollector>(lockSwitch);
            Controls.Add(lockSwitch);

            IMyTerminalControlButton grindButton = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyCollector>("Shipyard_GrindButton");
            IMyTerminalControlButton weldButton = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyCollector>("Shipyard_WeldButton");
            IMyTerminalControlButton stopButton = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyCollector>("Shipyard_StopButton");

            grindButton.Title = MyStringId.GetOrCompute("Grind");
            grindButton.Tooltip = MyStringId.GetOrCompute("Begins grinding ships in the yard.");
            grindButton.Enabled = b => b.BlockDefinition.SubtypeId.Contains("ShipyardCorner") && GetYard(b)?.YardType == ShipyardType.Disabled;
            grindButton.Visible = b => b.BlockDefinition.SubtypeId.Contains("ShipyardCorner");
            grindButton.SupportsMultipleBlocks = true;
            grindButton.Action = b => Communication.SendYardCommand(b.CubeGrid.EntityId, ShipyardType.Grind);
            MyAPIGateway.TerminalControls.AddControl<IMyCollector>(grindButton);
            Controls.Add(grindButton);

            weldButton.Title = MyStringId.GetOrCompute("Weld");
            weldButton.Tooltip = MyStringId.GetOrCompute("Begins welding ships in the yard.");
            weldButton.Enabled = b => b.BlockDefinition.SubtypeId.Contains("ShipyardCorner") && GetYard(b)?.YardType == ShipyardType.Disabled;
            weldButton.Visible = b => b.BlockDefinition.SubtypeId.Contains("ShipyardCorner");
            weldButton.SupportsMultipleBlocks = true;
            weldButton.Action = b => Communication.SendYardCommand(b.CubeGrid.EntityId, ShipyardType.Weld);
            MyAPIGateway.TerminalControls.AddControl<IMyCollector>(weldButton);
            Controls.Add(weldButton);

            stopButton.Title = MyStringId.GetOrCompute("Stop");
            stopButton.Tooltip = MyStringId.GetOrCompute("Stops the shipyard.");
            stopButton.Enabled = b =>
                                 {
                                     if (!b.BlockDefinition.SubtypeId.Contains("ShipyardCorner"))
                                         return false;

                                     ShipyardItem yard = GetYard(b);

                                     return yard?.YardType == ShipyardType.Weld || yard?.YardType == ShipyardType.Grind;
                                 };
            stopButton.Visible = b => b.BlockDefinition.SubtypeId.Contains("ShipyardCorner");
            stopButton.SupportsMultipleBlocks = true;
            stopButton.Action = b => Communication.SendYardCommand(b.CubeGrid.EntityId, ShipyardType.Disabled);
            MyAPIGateway.TerminalControls.AddControl<IMyCollector>(stopButton);
            Controls.Add(stopButton);
            
            IMyTerminalControlSlider beamCountSlider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyCollector>("Shipyard_BeamCount");
            beamCountSlider.Title = MyStringId.GetOrCompute("Beam Count");

            beamCountSlider.Tooltip = MyStringId.GetOrCompute("Number of beams this shipyard can use per corner.");
            beamCountSlider.SetLimits(1, 3);
            beamCountSlider.Writer = (b, result) => result.Append(GetBeamCount(b));
            beamCountSlider.Visible = b => b.BlockDefinition.SubtypeId.Contains("ShipyardCorner");
            beamCountSlider.Enabled = b => b.BlockDefinition.SubtypeId.Contains("ShipyardCorner") && GetYard(b) != null;
            beamCountSlider.Getter = b => GetBeamCount(b);
            beamCountSlider.Setter = (b, v) =>
                                     {
                                         SetBeamCount(b, (int)Math.Round(v, 0, MidpointRounding.ToEven));
                                         beamCountSlider.UpdateVisual();
                                     };
            beamCountSlider.SupportsMultipleBlocks = true;
            MyAPIGateway.TerminalControls.AddControl<IMyCollector>(beamCountSlider);
            Controls.Add(beamCountSlider);

            IMyTerminalControlSlider grindSpeedSlider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyCollector>("Shipyard_GrindSpeed");
            grindSpeedSlider.Title = MyStringId.GetOrCompute("Grind Speed");

            grindSpeedSlider.Tooltip = MyStringId.GetOrCompute("How fast this shipyard grinds grids.");
            grindSpeedSlider.SetLimits(0.01f, 2);
            grindSpeedSlider.Writer = (b, result) => result.Append(GetGrindSpeed(b));
            grindSpeedSlider.Visible = b => b.BlockDefinition.SubtypeId.Contains("ShipyardCorner");
            grindSpeedSlider.Enabled = b => b.BlockDefinition.SubtypeId.Contains("ShipyardCorner") && GetYard(b) != null;
            grindSpeedSlider.Getter = GetGrindSpeed;
            grindSpeedSlider.Setter = (b, v) =>
                                      {
                                          SetGrindSpeed(b, (float)Math.Round(v, 2, MidpointRounding.ToEven));
                                          grindSpeedSlider.UpdateVisual();
                                      };
            grindSpeedSlider.SupportsMultipleBlocks = true;
            MyAPIGateway.TerminalControls.AddControl<IMyCollector>(grindSpeedSlider);
            Controls.Add(grindSpeedSlider);

            IMyTerminalControlSlider weldSpeedSlider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyCollector>("Shipyard_WeldSpeed");
            weldSpeedSlider.Title = MyStringId.GetOrCompute("Weld Speed");

            weldSpeedSlider.Tooltip = MyStringId.GetOrCompute("How fast this shipyard welds grids.");
            weldSpeedSlider.SetLimits(0.01f, 2);
            weldSpeedSlider.Writer = (b, result) => result.Append(GetWeldSpeed(b));
            weldSpeedSlider.Visible = b => b.BlockDefinition.SubtypeId.Contains("ShipyardCorner");
            weldSpeedSlider.Enabled = b => b.BlockDefinition.SubtypeId.Contains("ShipyardCorner") && GetYard(b) != null;
            weldSpeedSlider.Getter = GetWeldSpeed;
            weldSpeedSlider.Setter = (b, v) =>
                                     {
                                         SetWeldSpeed(b, (float)Math.Round(v, 2, MidpointRounding.ToEven));
                                         weldSpeedSlider.UpdateVisual();
                                     };
            weldSpeedSlider.SupportsMultipleBlocks = true;
            MyAPIGateway.TerminalControls.AddControl<IMyCollector>(weldSpeedSlider);
            Controls.Add(weldSpeedSlider);

            IMyTerminalAction grindAction = MyAPIGateway.TerminalControls.CreateAction<IMyCollector>("Shipyard_GrindAction");
            grindAction.Enabled = b => b.BlockDefinition.SubtypeId.Contains("ShipyardCorner");
            grindAction.Name = new StringBuilder("Grind");
            grindAction.Icon = @"Textures\GUI\Icons\Actions\Start.dds";
            grindAction.Action = b => Communication.SendYardCommand(b.CubeGrid.EntityId, ShipyardType.Grind);
            MyAPIGateway.TerminalControls.AddAction<IMyCollector>(grindAction);

            IMyTerminalAction weldAction = MyAPIGateway.TerminalControls.CreateAction<IMyCollector>("Shipyard_WeldAction");
            weldAction.Enabled = b => b.BlockDefinition.SubtypeId.Contains("ShipyardCorner");
            weldAction.Name = new StringBuilder("Weld");
            weldAction.Icon = @"Textures\GUI\Icons\Actions\Start.dds";
            weldAction.Action = b => Communication.SendYardCommand(b.CubeGrid.EntityId, ShipyardType.Weld);
            MyAPIGateway.TerminalControls.AddAction<IMyCollector>(weldAction);
            
            IMyTerminalAction stopAction = MyAPIGateway.TerminalControls.CreateAction<IMyCollector>("Shipyard_StopAction");
            stopAction.Enabled = b => b.BlockDefinition.SubtypeId.Contains("ShipyardCorner");
            stopAction.Name = new StringBuilder("Stop");
            stopAction.Icon = @"Textures\GUI\Icons\Actions\Reset.dds";
            stopAction.Action = b => Communication.SendYardCommand(b.CubeGrid.EntityId, ShipyardType.Disabled);
            MyAPIGateway.TerminalControls.AddAction<IMyCollector>(stopAction);
        }

        private void AppendingCustomInfo(IMyTerminalBlock b, StringBuilder arg2)
        {
            try
            {
                float power = _power;
                float maxpower = _maxpower;
                if (GetYard(b) != null)
                {
                    maxpower *= Math.Max(b.GetValueFloat("Shipyard_GrindSpeed"), b.GetValueFloat("Shipyard_WeldSpeed"));
                    maxpower *= GetBeamCount(b);
                }
                var sb = new StringBuilder();
                sb.Append("Required Input: ");
                MyValueFormatter.AppendWorkInBestUnit(power, sb);
                sb.AppendLine();
                sb.Append("Max required input: ");
                MyValueFormatter.AppendWorkInBestUnit(maxpower, sb);
                sb.AppendLine();
                sb.Append(_info);
                sb.AppendLine();

                arg2.Append(sb);
            }
            catch (Exception)
            {
                //don't really care, just don't crash
            }
        }

        private int GetBeamCount(IMyCubeBlock b)
        {
            if (GetYard(b) == null)
                return 3;

            return ShipyardSettings.Instance.GetYardSettings(b.CubeGrid.EntityId).BeamCount;
        }

        private void SetBeamCount(IMyCubeBlock b, int value)
        {
            if (GetYard(b) == null)
                return;

            //this value check stops infinite loops of sending the setting to server and immediately getting the same value back
            if (value == GetBeamCount(b))
                return;

            YardSettingsStruct settings = ShipyardSettings.Instance.GetYardSettings(b.CubeGrid.EntityId);
            settings.BeamCount = value;

            ShipyardSettings.Instance.SetYardSettings(b.CubeGrid.EntityId, settings);

            Communication.SendShipyardSettings(b.CubeGrid.EntityId, settings);
        }

        private bool GetGuideEnabled(IMyCubeBlock b)
        {
            if (GetYard(b) == null)
                return true;

            return ShipyardSettings.Instance.GetYardSettings(b.CubeGrid.EntityId).GuideEnabled;
        }

        private void SetGuideEnabled(IMyCubeBlock b, bool value)
        {
            if (GetYard(b) == null)
                return;

            if (value == GetGuideEnabled(b))
                return;

            YardSettingsStruct settings = ShipyardSettings.Instance.GetYardSettings(b.CubeGrid.EntityId);
            settings.GuideEnabled = value;

            ShipyardSettings.Instance.SetYardSettings(b.CubeGrid.EntityId, settings);

            Communication.SendShipyardSettings(b.CubeGrid.EntityId, settings);
        }

        private bool GetLockEnabled(IMyCubeBlock b)
        {
            if (GetYard(b) == null)
                return false;

            return ShipyardSettings.Instance.GetYardSettings(b.CubeGrid.EntityId).AdvancedLocking;
        }

        private void SetLockEnabled(IMyCubeBlock b, bool value)
        {
            if (GetYard(b) == null)
                return;

            if (value == GetLockEnabled(b))
                return;

            YardSettingsStruct settings = ShipyardSettings.Instance.GetYardSettings(b.CubeGrid.EntityId);
            settings.AdvancedLocking = value;

            ShipyardSettings.Instance.SetYardSettings(b.CubeGrid.EntityId, settings);

            Communication.SendShipyardSettings(b.CubeGrid.EntityId, settings);
        }

        private float GetGrindSpeed(IMyCubeBlock b)
        {
            if (GetYard(b) == null)
                return 0.1f;

            return ShipyardSettings.Instance.GetYardSettings(b.CubeGrid.EntityId).GrindMultiplier;
        }

        private void SetGrindSpeed(IMyCubeBlock b, float value)
        {
            if (GetYard(b) == null)
                return;

            if (value == GetGrindSpeed(b))
                return;

            YardSettingsStruct settings = ShipyardSettings.Instance.GetYardSettings(b.CubeGrid.EntityId);
            settings.GrindMultiplier = value;

            ShipyardSettings.Instance.SetYardSettings(b.CubeGrid.EntityId, settings);

            Communication.SendShipyardSettings(b.CubeGrid.EntityId, settings);
        }

        private float GetWeldSpeed(IMyCubeBlock b)
        {
            if (GetYard(b) == null)
                return 0.1f;

            return ShipyardSettings.Instance.GetYardSettings(b.CubeGrid.EntityId).WeldMultiplier;
        }

        private void SetWeldSpeed(IMyCubeBlock b, float value)
        {
            if (GetYard(b) == null)
                return;

            if (value == GetWeldSpeed(b))
                return;

            YardSettingsStruct settings = ShipyardSettings.Instance.GetYardSettings(b.CubeGrid.EntityId);
            settings.WeldMultiplier = value;

            ShipyardSettings.Instance.SetYardSettings(b.CubeGrid.EntityId, settings);

            Communication.SendShipyardSettings(b.CubeGrid.EntityId, settings);
        }

        private ShipyardItem GetYard(IMyCubeBlock b)
        {
            return b.GameLogic.GetAs<ShipyardCorner>()?.Shipyard;
        }

        public void SetPowerUse(float req)
        {
            _power = req;
        }

        public void SetMaxPower(float req)
        {
            _maxpower = req;
        }

        public void SetInfo(string info)
        {
            _info = info;
        }

        public void UpdateVisuals()
        {
            foreach (IMyTerminalControl control in Controls)
                control.UpdateVisual();
        }

        public override void UpdateBeforeSimulation()
        {
            if (!((IMyCollector)Container.Entity).Enabled)
                _power = 0f;
            _sink.SetMaxRequiredInputByType(PowerDef, _power);
            _sink.SetRequiredInputByType(PowerDef, _power);
            //sink.Update();
        }

        public override void UpdateBeforeSimulation10()
        {
            ((IMyTerminalBlock)Container.Entity).RefreshCustomInfo();
        }

        public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = false)
        {
            return Entity.GetObjectBuilder(copy);
        }
    }
}