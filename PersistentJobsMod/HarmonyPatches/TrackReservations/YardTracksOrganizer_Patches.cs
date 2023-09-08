using DV.Logic.Job;
using HarmonyLib;
using UnityEngine;

namespace PersistentJobsMod.HarmonyPatches.TrackReservations {
    [HarmonyPatch]
    public static class YardTracksOrganizer_Patches {
        [HarmonyPatch(typeof(YardTracksOrganizer), nameof(YardTracksOrganizer.ReserveSpace))]
        [HarmonyPostfix]
        public static void YardTracksOrganizer_ReserveSpace_Postfix(YardTracksOrganizer __instance, Track track, float length, bool ignoreOccupiedTrackLength) {
            Debug.Log($"[PersistentJobsMod] YardTracksOrganizer.ReserveSpace {length:F1}m on track {track.ID.FullDisplayID}. Reserved space is now {__instance.GetReservedSpace(track):F1}m of {track.length:F1}m total");
        }

        [HarmonyPatch(typeof(YardTracksOrganizer), nameof(YardTracksOrganizer.ReleaseReservedSpace))]
        [HarmonyPrefix]
        public static void YardTracksOrganizer_ReleaseReservedSpace_Prefix(float lengthToRelease, out float __state) {
            __state = lengthToRelease;
        }

        [HarmonyPatch(typeof(YardTracksOrganizer), nameof(YardTracksOrganizer.ReleaseReservedSpace))]
        [HarmonyPostfix]
        public static void YardTracksOrganizer_ReleaseReservedSpace_Postfix(YardTracksOrganizer __instance, Track track, float __state) {
            Debug.Log($"[PersistentJobsMod] YardTracksOrganizer.ReleaseReservedSpace {__state:F1}m on track {track.ID.FullDisplayID}. Reserved space is now {__instance.GetReservedSpace(track):F1}m of {track.length:F1}m total");
        }
    }
}