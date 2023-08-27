using System;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace PersistentJobsMod.HarmonyPatches {
    [HarmonyPatch]
    public static class JobChainControllerWithEmptyHaulGeneration_Patch {
        [HarmonyPatch(typeof(JobChainControllerWithEmptyHaulGeneration), "OnLastJobInChainCompleted")]
        [HarmonyPrefix]
        public static bool OnLastJobInChainCompleted_Prefix(JobChainControllerWithEmptyHaulGeneration __instance, DV.Logic.Job.Job lastJobInChain) {
            if (!Main._modEntry.Active) {
                return true;
            }

            // call the base method JobChainController.OnLastJobInChainCompleted (directly, actually, ignoring the harmony prefix of that method).
            // this will register the cars as jobless and they may then be chosen for further jobs.
            JobChainController_OnLastJobInChainCompleted_BaseMethod(__instance, lastJobInChain);
            return false;
        }

        [HarmonyReversePatch]
        [HarmonyPatch(typeof(JobChainController), "OnLastJobInChainCompleted")]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void JobChainController_OnLastJobInChainCompleted_BaseMethod(JobChainController instance, DV.Logic.Job.Job lastJobInChain) {
            throw new NotImplementedException("This is a stub");
        }
    }
}