﻿using System;
using ProtoBuf;
using CGP.ShareTrack.HeartNetworking.Custom;

namespace CGP.ShareTrack.HeartNetworking
{
    [ProtoInclude(91, typeof(TrackingSyncPacket))]
    [ProtoInclude(92, typeof(SyncRequestPacket))]
    [ProtoContract(UseProtoMembersOnly = true)]
    public abstract class PacketBase
    {
        public static Type[] Types =
        {
            typeof(PacketBase),
            typeof(TrackingSyncPacket),
            typeof(SyncRequestPacket),
        };

        /// <summary>
        ///     Called whenever your packet is recieved.
        /// </summary>
        /// <param name="SenderSteamId"></param>
        public abstract void Received(ulong SenderSteamId);
    }
}