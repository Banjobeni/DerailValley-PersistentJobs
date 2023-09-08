using HarmonyLib;

namespace PersistentJobsMod.HarmonyPatches.JobChainControllers {
    [HarmonyPatch(typeof(JobChainController), "ReserveRequiredTracks")]
    class JobChainController_ReserveRequiredTracks_Patch {
        static bool Prefix() {
            if (Main._modEntry.Active && !Main._overrideTrackReservation) {
                Main._modEntry.Logger.Log("skipping track reservation");
                return false;
            }
            return true;
        }
    }
}