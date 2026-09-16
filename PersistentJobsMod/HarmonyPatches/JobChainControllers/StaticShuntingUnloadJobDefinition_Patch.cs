using HarmonyLib;
using System.Collections.Generic;

namespace PersistentJobsMod.HarmonyPatches.JobChainControllers
{

    [HarmonyPatch(typeof(StaticShuntingUnloadJobDefinition), nameof(StaticShuntingUnloadJobDefinition.GetRequiredTrackReservations))]
    public static class StaticShuntingUnloadJobDefinition_Patch
    {
        public static void Postfix(ref List<TrackReservation> __result)
        {
            if (!Main._modEntry.Active) return;
            __result?.RemoveAll(tr => tr.track.ID.trackType == "L");
        }
    }
}
