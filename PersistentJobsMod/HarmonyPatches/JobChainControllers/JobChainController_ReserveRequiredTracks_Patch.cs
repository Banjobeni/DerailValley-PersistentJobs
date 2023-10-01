using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace PersistentJobsMod.HarmonyPatches.JobChainControllers {
    [HarmonyPatch]
    public static class JobChainController_Patches {
        [HarmonyPatch(typeof(JobChainController), nameof(JobChainController.FinalizeSetupAndGenerateFirstJob))]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> FinalizeSetupAndGenerateFirstJob_Transpiler(IEnumerable<CodeInstruction> instructionsEnumerable, MethodBase originalMethod) {
            var targetMethod = typeof(JobChainController).GetMethod("ReserveRequiredTracks", BindingFlags.NonPublic | BindingFlags.Instance);
            if (targetMethod == null) {
                throw new InvalidOperationException("could not find target method");
            }

            var instructions = TranspilingUtilities.RemoveMethodCallAssumingArgumentsAreLoadedUsingLdarg(instructionsEnumerable, targetMethod, originalMethod);

            return instructions;
        }
    }
}