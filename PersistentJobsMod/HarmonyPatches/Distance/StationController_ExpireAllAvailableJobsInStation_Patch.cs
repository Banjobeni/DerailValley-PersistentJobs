using HarmonyLib;

namespace PersistentJobsMod.HarmonyPatches.Distance {
    /// <summary>prevents jobs from expiring due to the player's distance from the station</summary>
    [HarmonyPatch(typeof(StationController), "ExpireAllAvailableJobsInStation")]
    public static class StationController_ExpireAllAvailableJobsInStation_Patch {
        public static bool Prefix() {
            // skips the original method entirely when this mod is active
            return !Main._modEntry.Active;
        }
    }
}