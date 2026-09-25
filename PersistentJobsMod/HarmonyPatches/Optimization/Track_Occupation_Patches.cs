using DV.Logic.Job;
using HarmonyLib;
using PersistentJobsMod.Optimization;

namespace PersistentJobsMod.HarmonyPatches.Optimization
{
    [HarmonyPatch]
    public static class Track_Occupation_Patches
    {
        [HarmonyPatch(typeof(Track), "get_OccupiedLength")]
        [HarmonyPostfix]
        public static void Track_get_OccupiedLength_Postfix(Track __instance, ref float __result)
        {
            if (Main.Settings.SuspendFarAwayCars && FarCarOpt.TracksToSpaceOccupiedBySuspendedCars.TryGetValue(__instance, out var len)) __result += len;
        }

        [HarmonyPatch(typeof(Track), "IsFree")]
        [HarmonyPostfix]
        public static void Track_IsFree_Postfix(Track __instance, ref bool __result)
        {
            if (Main.Settings.SuspendFarAwayCars && FarCarOpt.TracksToSpaceOccupiedBySuspendedCars.TryGetValue(__instance, out var len) && len > 0.1f) __result = false;
        }
    }

}
