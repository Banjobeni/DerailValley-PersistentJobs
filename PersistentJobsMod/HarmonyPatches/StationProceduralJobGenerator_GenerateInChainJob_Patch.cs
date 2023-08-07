using HarmonyLib;

namespace PersistentJobsMod.HarmonyPatches {
    /// <summary>generates shunting unload jobs</summary>
    [HarmonyPatch(typeof(StationProceduralJobGenerator), "GenerateInChainJob")]
    class StationProceduralJobGenerator_GenerateInChainJob_Patch {
        static bool Prefix(ref JobChainController __result) {
            if (Main._modEntry.Active) {
                Main._modEntry.Logger.Log("cancelling inbound job spawning" +
                    " to keep tracks clear for outbound jobs from other stations");
                __result = null;
                return false;
            }
            return true;
        }
    }
}