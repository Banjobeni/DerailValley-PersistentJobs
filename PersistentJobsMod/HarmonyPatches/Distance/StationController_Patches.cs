using DV.Logic.Job;
using HarmonyLib;
using PersistentJobsMod.Utilities;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace PersistentJobsMod.HarmonyPatches.Distance {
    /// <summary>prevents jobs from expiring due to the player's distance from the station</summary>
    [HarmonyPatch]
    public static class StationController_Patches {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(StationController), nameof(StationController.ExpireAllAvailableJobsInStation))]
        public static bool ExpireAllAvailableJobsInStation_Patch() {
            // we don't want to expire jobs at any time...
            return false;
        }

        // ...unless we call it ourselves explicitly
        [HarmonyReversePatch]
        [HarmonyPatch(typeof(StationController), nameof(StationController.ExpireAllAvailableJobsInStation))]
        public static void ExpireAllAvailableJobsInStation_Original(StationController instance) { }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(StationController), "Start")]
        public static void Start_Postfix(StationController __instance)
        {
            var generateTrackSignsMethod = ReflectionUtilities.CompatAccess.Method(typeof(StationController), "GenerateTrackIdObject", new[] {typeof(List<RailTrack>) });
            var tracksForGen = RailTrackRegistry.Instance.AllTracks.Where(rt => ((rt.LogicTrack().ID.yardId == __instance.stationInfo.YardID) && ((new string[] {"D", "P"}).Contains((string)ReflectionUtilities.CompatAccess.Field(typeof(TrackID), "trackType").GetValue(rt.LogicTrack().ID))))).ToList();
            generateTrackSignsMethod.Invoke(__instance, new object[] {tracksForGen});
        }
    }
}