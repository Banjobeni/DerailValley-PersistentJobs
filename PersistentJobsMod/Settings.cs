using System.Linq;
using UnityEngine;
using UnityModManagerNet;

namespace PersistentJobsMod
{
    public sealed class Settings : UnityModManager.ModSettings, IDrawable
    {
        [Draw("Prevent accepting shunting (un)load jobs if cars are already on a loading track (L)")]
        public bool PreventStartingShuntingJobForCarsOnWarehouseTrack = true;

        public bool PreventStartingShuntingJobForCarsOnWarehouseTrackMessageWasShown = false;

        [Draw("Make shunting unload jobs end on loading tracks (L) (not recommended)")]
        public bool ShuntingUnloadEndsOnLTracks = false;

        [Draw("Replace destination tracks if there is no space")]
        public bool DestinationTrackChange = true;

        [Draw("Show track signs for all named tracks")]
        public bool GenerateTrackSigns = false;

        [Draw("Intercompatibility with Passenger Jobs mod (toggle mod off/on for setting change to take effect)")]
        public bool PaxJobsCompatibility = true;

        [Draw("Suspend cars in far away stations in order to improve performance")]
        public bool SuspendFarAwayCars = true;

        [Draw("\"Occupy\" track where cars were suspended by a dummy bogie - for use with signals mods (experimental!)", VisibleOn = "SuspendFarAwayCars|true")]
        public bool DummyBogiesForTracksOfSuspendedCars = false;
#if DEBUG
        [Draw($"Hides the debug console in \"OnFixedGUI\"")]
#endif
        public bool HideDebugConsole = false;

        [Draw("Block cars of suspended cars with dummy colliders", VisibleOn = "SuspendFarAwayCars|true")]
        public bool TempCarColliders = false;

        public void DrawButtons()
        {
            if (SuspendFarAwayCars && WorldStreamingInit.IsLoaded)
            {
                var stations = StationController.allStations?.Where(sc => sc.stationRange.IsPlayerInJobGenerationZone(sc.stationRange.PlayerSqrDistanceFromStationCenter))?.Select(sc => sc.stationInfo.YardID);
                stations ??= [];

                GUILayout.BeginVertical();
                GUILayout.Space(20);
                if (GUILayout.Button("Resume all cars")) PersistentJobsMod.Optimization.FarCarOpt.ResumeCarsInStation("ALL");

                foreach (var stationID in stations)
                {
                    if (PersistentJobsMod.Optimization.FarCarOpt.StationIDtoSuspendedCarGUID.TryGetValue(stationID, out var suspended) && suspended?.Any() is true)
                    {
                        GUILayout.Space(5);
                        if (GUILayout.Button($"Resume cars in {stationID}")) PersistentJobsMod.Optimization.FarCarOpt.ResumeCarsInStation(stationID);
                    }
                }

                GUILayout.EndVertical();
            }
        }

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Save(this, modEntry);
        }

        void IDrawable.OnChange() { }
    }
}