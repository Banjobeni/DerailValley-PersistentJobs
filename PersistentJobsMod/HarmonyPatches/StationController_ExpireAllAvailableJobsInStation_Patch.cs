using HarmonyLib;

namespace PersistentJobsMod.HarmonyPatches {
    /// <summary>prevents jobs from expiring due to the player's distance from the station</summary>
    [HarmonyPatch(typeof(StationController), "ExpireAllAvailableJobsInStation")]
    class StationController_ExpireAllAvailableJobsInStation_Patch {
        static bool Prefix() {
            // skips the original method entirely when this mod is active
            return !Main.modEntry.Active;
        }
    }
}