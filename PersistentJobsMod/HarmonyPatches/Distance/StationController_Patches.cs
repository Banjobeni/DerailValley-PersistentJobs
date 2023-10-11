using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using PersistentJobsMod.Utilities;

namespace PersistentJobsMod.HarmonyPatches.Distance {
    /// <summary>prevents jobs from expiring due to the player's distance from the station</summary>
    [HarmonyPatch]
    public static class StationController_Patches {
        [HarmonyPatch(typeof(StationController), "Update")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> ExpireAllAvailableJobsInStation_Transpiler(IEnumerable<CodeInstruction> instructionsEnumerable, MethodBase originalMethod) {
            var targetMethod = typeof(StationController).GetMethod(nameof(StationController.ExpireAllAvailableJobsInStation));
            if (targetMethod == null) {
                throw new InvalidOperationException("could not find target method");
            }

            var instructions = TranspilingUtilities.RemoveMethodCallAssumingArgumentsAreLoadedUsingLdarg(instructionsEnumerable, targetMethod, originalMethod);

            return instructions;
        }
    }
}