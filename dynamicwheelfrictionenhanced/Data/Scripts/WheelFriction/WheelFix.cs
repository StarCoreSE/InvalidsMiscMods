using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities.Cube;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRage.Game;
using VRage.Game.Components;
using VRage.Utils;
using VRageMath;

namespace WheelFix
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_MotorSuspension), false)]
    public class WheelFix : MyGameLogicComponent
    {
		IMyMotorSuspension _suspension;
		public override void OnAddedToContainer()
		{
			base.OnAddedToContainer();
			this.NeedsUpdate |= VRage.ModAPI.MyEntityUpdateEnum.EACH_FRAME;
			_suspension = (IMyMotorSuspension)this.Entity;
		}

		public const double ResistanceCoefficient = 1000d;
		public override void UpdateBeforeSimulation()
		{
			base.UpdateBeforeSimulation();
			var grid = _suspension?.TopGrid;
			if (grid?.Physics == null)
				return;

			var minpos = Vector3D.Transform(_suspension.DummyPosition, _suspension.CubeGrid.PositionComp.WorldMatrixRef);
			minpos += _suspension.Height * _suspension.PositionComp.WorldMatrixRef.Forward;

			var distance = grid.Physics.CenterOfMassWorld - minpos;
			distance = Vector3D.Reject(distance, _suspension.PositionComp.WorldMatrixRef.Up);

			var friction = _suspension.Friction / 100f;
			
			var str = _suspension.Strength / 100f;
			
			var num = 35.0d * ((double)(MyMath.FastTanH(6f * friction - 3f) / 2f) + 0.5); //keen base friction
			var num2 = (ResistanceCoefficient * str * distance.Length() * friction); //added friction based on resistive force

			grid.Physics.Friction = (float)(num + num2);

		}

		public override void UpdateAfterSimulation()
		{
			base.UpdateAfterSimulation();
		}
	}
}
