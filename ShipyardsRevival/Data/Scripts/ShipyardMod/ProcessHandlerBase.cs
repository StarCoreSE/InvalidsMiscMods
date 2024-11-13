using System;
using Sandbox.ModAPI;
using ShipyardMod.Utility;

namespace ShipyardMod.ProcessHandlers
{
    public abstract class ProcessHandlerBase
    {
        protected DateTime _lastUpdate;

        public ProcessHandlerBase()
        {
            LastUpdate = DateTime.Now;
        }

        public DateTime LastUpdate
        {
            get { return _lastUpdate; }
            set { _lastUpdate = value; }
        }

        public virtual int GetUpdateResolution()
        {
            return 1000;
        }

        public virtual bool ServerOnly()
        {
            return true;
        }

        public virtual bool ClientOnly()
        {
            return false;
        }

        public bool CanRun()
        {
            if (DateTime.Now - LastUpdate > TimeSpan.FromMilliseconds(GetUpdateResolution()))
            {
                if (ServerOnly() && MyAPIGateway.Multiplayer.IsServer)
                {
                    //Logging.Instance.WriteDebug($"[ProcessHandler] {GetType().Name} can run on server");
                    return true;
                }
                if (ClientOnly() && MyAPIGateway.Session.Player != null)
                {
                    //Logging.Instance.WriteDebug($"[ProcessHandler] {GetType().Name} can run on client");
                    return true;
                }
            }
            return false;
        }

        public abstract void Handle();
    }
}