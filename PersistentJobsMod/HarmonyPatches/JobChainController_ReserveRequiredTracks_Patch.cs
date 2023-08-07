using HarmonyLib;
using UnityEngine;

namespace PersistentJobsMod.HarmonyPatches {
    [HarmonyPatch(typeof(JobChainController), "ReserveRequiredTracks")]
    class JobChainController_ReserveRequiredTracks_Patch {
        static bool Prefix() {
            if (Main._modEntry.Active && !Main._overrideTrackReservation) {
                Debug.Log("[PersistentJobs] skipping track reservation");
                return false;
            }
            return true;
        }
    }
}