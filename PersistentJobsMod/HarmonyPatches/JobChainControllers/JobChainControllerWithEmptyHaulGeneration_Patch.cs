using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace PersistentJobsMod.HarmonyPatches.JobChainControllers {
    [HarmonyPatch]
    public static class JobChainControllerWithEmptyHaulGeneration_Patch {
        [HarmonyPatch(typeof(JobChainControllerWithEmptyHaulGeneration), "OnLastJobInChainCompleted")]
        [HarmonyPrefix]
        public static bool OnLastJobInChainCompleted_Prefix(JobChainControllerWithEmptyHaulGeneration __instance, List<StaticJobDefinition> ___jobChain, DV.Logic.Job.Job lastJobInChain) {
            if (!Main._modEntry.Active) {
                return true;
            }

            // we want to skip JobChainControllerWithEmptyHaulGeneration.OnLastJobInChainCompleted, but call JobChainController.OnLastJobInChainCompleted *including the prefix we apply to it*.
            // this does not work, so we need to call the prefix ourselves, then the original base method
            JobChainController_OnLastJobInChainCompleted_Patch.Prefix(__instance, ___jobChain, lastJobInChain);

            // call the base method JobChainController.OnLastJobInChainCompleted (directly, actually, ignoring the harmony prefix of that method).
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