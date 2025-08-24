using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;

namespace CGP.ShareTrack.API
{
    public class RtsApi
    {
        private const long ChannelId = 2772681332;
        private Func<IMyCubeGrid, float[]> _GetAcceleration;
        private Func<IMyCubeGrid, float[]> _GetAccelerationByDirection;
        private Func<IMyCubeGrid, float[]> _GetBoost;
        private Func<IMyCubeGrid, float> _GetCruiseSpeed;
        private Func<IMyCubeGrid, float> _GetMaxSpeed;
        private Func<IMyCubeGrid, float> _GetNegativeInfluence;
        private Func<IMyCubeGrid, float> _GetReducedAngularSpeed;

        private bool isRegistered;

        private Action ReadyCallback;
        public bool IsReady { get; private set; }

        public void Load(Action readyCallback = null)
        {
            if (isRegistered)
                throw new Exception($"{GetType().Name}.Load() should not be called multiple times!");

            isRegistered = true;
            ReadyCallback = readyCallback;
            MyAPIGateway.Utilities.RegisterMessageHandler(ChannelId, HandleMessage);
            MyAPIGateway.Utilities.SendModMessage(ChannelId, "ApiEndpointRequest");
        }

        public void Unload()
        {
            MyAPIGateway.Utilities.UnregisterMessageHandler(ChannelId, HandleMessage);
            IsReady = false;
            isRegistered = false;
        }

        private void HandleMessage(object obj)
        {
            if (obj is string) // the sent "ApiEndpointRequest" will also be received here, explicitly ignoring that
                return;

            var dict = obj as IReadOnlyDictionary<string, Delegate>;

            if (dict == null)
                return;

            TryAssignMethod(dict, "GetCruiseSpeed", ref _GetCruiseSpeed);
            TryAssignMethod(dict, "GetMaxSpeed", ref _GetMaxSpeed);
            TryAssignMethod(dict, "GetBoost", ref _GetBoost);
            TryAssignMethod(dict, "GetAcceleration", ref _GetAcceleration);
            TryAssignMethod(dict, "GetAccelerationByDirection", ref _GetAccelerationByDirection);
            TryAssignMethod(dict, "GetNegativeInfluence", ref _GetNegativeInfluence);
            TryAssignMethod(dict, "GetReducedAngularSpeed", ref _GetReducedAngularSpeed);

            IsReady = true;
            ReadyCallback?.Invoke();
        }

        private void TryAssignMethod<T>(IReadOnlyDictionary<string, Delegate> delegates, string name, ref T field)
            where T : class
        {
            if (delegates == null)
            {
                field = null;
                return;
            }

            Delegate del;
            if (!delegates.TryGetValue(name, out del))
            {
                field = null;
                return;
            }

            field = del as T;
        }

        /// <summary>
        ///     Returns the cruising speed of the grid.
        /// </summary>
        public float GetCruiseSpeed(IMyCubeGrid grid)
        {
            return _GetCruiseSpeed != null ? _GetCruiseSpeed.Invoke(grid) : 0f;
        }

        /// <summary>
        ///     Gets the maximum possible speed (cruise speed + max boost)
        /// </summary>
        public float GetMaxSpeed(IMyCubeGrid grid)
        {
            return _GetMaxSpeed != null ? _GetMaxSpeed.Invoke(grid) : 0f;
        }

        /// <summary>
        ///     Returns 4 values: forward boost, min, average, max
        /// </summary>
        public float[] GetBoost(IMyCubeGrid grid)
        {
            return _GetBoost != null ? _GetBoost.Invoke(grid) : new float[0];
        }

        /// <summary>
        ///     Returns 4 values: forward accel, min, average, max
        /// </summary>
        public float[] GetAcceleration(IMyCubeGrid grid)
        {
            return _GetAcceleration != null ? _GetAcceleration.Invoke(grid) : new float[0];
        }

        /// <summary>
        ///     Uses Base6Directions.Direction
        ///     forward = reverse accel
        ///     backward = forward accel
        ///     left = right accel
        ///     ...
        /// </summary>
        public float[] GetAccelerationByDirection(IMyCubeGrid grid)
        {
            return _GetAccelerationByDirection != null ? _GetAccelerationByDirection.Invoke(grid) : new float[0];
        }

        /// <summary>
        ///     Returns the negative influence for the specified grid.
        /// </summary>
        public float GetNegativeInfluence(IMyCubeGrid grid)
        {
            return _GetNegativeInfluence != null ? _GetNegativeInfluence.Invoke(grid) : 0f;
        }

        /// <summary>
        ///     Returns the reduced angular speed for the specified grid.
        /// </summary>
        public float GetReducedAngularSpeed(IMyCubeGrid grid)
        {
            return _GetReducedAngularSpeed != null ? _GetReducedAngularSpeed.Invoke(grid) : 0f;
        }
    }
}
