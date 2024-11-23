using HarmonyLib;

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
    }
}