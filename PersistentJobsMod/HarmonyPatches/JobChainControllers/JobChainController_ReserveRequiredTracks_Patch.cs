using HarmonyLib;

namespace PersistentJobsMod.HarmonyPatches.JobChainControllers {
    [HarmonyPatch(typeof(JobChainController), "ReserveRequiredTracks")]
    public static class JobChainController_ReserveRequiredTracks_Patch {
        public static bool Prefix() {
            if (Main._modEntry.Active && !Main._overrideTrackReservation) {
                Main._modEntry.Logger.Log("skipping track reservation");
                return false;
            }
            return true;
        }
    }
}