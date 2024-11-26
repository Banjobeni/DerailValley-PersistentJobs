using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using DV;
using HarmonyLib;
using PersistentJobsMod.HarmonyPatches.JobGeneration;
using UnityEngine;

namespace PersistentJobsMod.HarmonyPatches.Sleeping {
    [HarmonyPatch]
    public static class BedSleepingController_Patches {
        [HarmonyPatch(typeof(BedSleepingController), "SleepCoro", MethodType.Enumerator)]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> SleepCoro_Transpiler(IEnumerable<CodeInstruction> instructionsEnumerable, MethodBase originalMethod) {
            var instructions = instructionsEnumerable.ToList();

            var index = instructions.FindIndex(ci => ci.opcode == OpCodes.Call && (ci.operand as MethodInfo) == typeof(TimeAdvance).GetMethod(nameof(TimeAdvance.AdvanceTime)));

            Debug.Log($"index: {index}");

            var newInstructions = new[] {
                new CodeInstruction(OpCodes.Ldarg_0),
                CodeInstruction.LoadField(AccessTools.TypeByName("DV.BedSleepingController+<SleepCoro>d__12"), "amountOfSecondsToSleep"),
                CodeInstruction.Call(typeof(BedSleepingController_Patches), nameof(HandleSleep)),
            };

            instructions.InsertRange(index + 1, newInstructions);

            return instructions;
        }

        public static void HandleSleep(float amountOfSecondsToSleep) {
            if (amountOfSecondsToSleep < 6 * 60 * 60) {
                return;
            }

            try {
                var unusedTrainCarsMarkedForDelete = UnusedTrainCarDeleter.Instance.unusedTrainCarsMarkedForDelete;

                Debug.Log($"unusedTrainCarsMarkedForDelete.Count: {unusedTrainCarsMarkedForDelete.Count}");

                UnusedTrainCarDeleter_Patches.ReassignRegularTrainCarsAndDeleteNonPlayerSpawnedCars(UnusedTrainCarDeleter.Instance, unusedTrainCarsMarkedForDelete, true);
            } catch (Exception e) {
                Main.HandleUnhandledException(e, nameof(BedSleepingController_Patches) + "." + nameof(HandleSleep));
            }
        }
    }
}