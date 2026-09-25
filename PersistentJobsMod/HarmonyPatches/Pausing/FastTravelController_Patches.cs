using DV.Teleporters;
using HarmonyLib;
using PersistentJobsMod.Optimization;
using System.Collections;

namespace PersistentJobsMod.HarmonyPatches.Pausing
{
    [HarmonyPatch(typeof(FastTravelController), "FastTravel")]
    public static class FastTravelController_Patches
    {
        public static void Prefix(FastTravelDestination marker)
        {
            Main.Pause = true;
        }

        public static IEnumerator Postfix(IEnumerator __result)
        {
            while (__result.MoveNext()) yield return __result.Current;

            Main.Pause = false;
            yield break;
        }
    }
}
