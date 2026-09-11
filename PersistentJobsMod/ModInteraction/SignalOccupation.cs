using PersistentJobsMod.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityModManagerNet;
using static PersistentJobsMod.Utilities.ReflectionUtilities;

namespace PersistentJobsMod.ModInteraction
{
    public static class SignalOccupation
    {
        public static bool initialized = false;

        private static Type TrackChecker;
        private static MethodInfo MarkTrackAsOccupied;
        private static MethodInfo ClearTrack;

        private static readonly List<TokenizedFlagHolder<string, RailTrack>> holders = [];

        public static void Initialize()
        {
            if (initialized || UnityModManager.modEntries.FirstOrDefault(m => m.Info.Id == "DVSignals" && m.Enabled && m.Active && !m.ErrorOnLoading) is null) return;
            try
            {
                TrackChecker = CompatAccess.Type("Signals.Game.Railway.TrackChecker");
                MarkTrackAsOccupied = CompatAccess.Method(TrackChecker, "MarkTrackAsOccupied", [typeof(RailTrack)]);
                ClearTrack = CompatAccess.Method(TrackChecker, "ClearTrack", [typeof(RailTrack)]);

                initialized = TrackChecker is not null && MarkTrackAsOccupied is not null && ClearTrack is not null;
            }
            catch (Exception e)
            {
                Main._modEntry.Logger.LogException("Failed to initialize Signals compatibility when resolving types and methods", e);
                initialized = false;
            }

        }

        public static void OccupyTrack(string carId, RailTrack track)
        {
            if (!initialized) return;
            TokenizedFlagHolder<string, RailTrack> trackTokenHolder = holders.FirstOrDefault(tfh => tfh.HeldObject == track);
            if (trackTokenHolder == null)
            {
                trackTokenHolder = new(track, OccupyInternal, FreeInternal);
                holders.Add(trackTokenHolder);
            }

            //if (!trackTokenHolder.Set(carId)) Main._modEntry.Logger.Error($"{track} couldn't be occupied by {carId}");
            trackTokenHolder.Set(carId);
        }

        public static void FreeTrack(string carId, RailTrack track)
        {
            if (!initialized) return;
            TokenizedFlagHolder<string, RailTrack> trackTokenHolder = holders.FirstOrDefault(tfh => tfh.HeldObject == track);
            if (trackTokenHolder == null) Main._modEntry.Logger.Log($"No occupying flag holder for {track} present, nothing to free");
            else
            {
                if (trackTokenHolder.Unset(carId)) holders.Remove(trackTokenHolder);
                //else Main._modEntry.Logger.Error($"{track} couldn't be freed by {carId}, something´s wrong!");
            }
        }

        private static bool OccupyInternal(RailTrack track, string carId)
        {
            MarkTrackAsOccupied.Invoke(null, [track]);
            Main._modEntry.Logger.Log($"Suspended car {carId} is occupying {track}");
            return true;
        }

        private static bool FreeInternal(RailTrack track, string carId)
        {
            ClearTrack.Invoke(null, [track]);
            Main._modEntry.Logger.Log($"Resumed car {carId} is no longer \"fake-occupying\" {track}");
            return true;
        }
    }
}
