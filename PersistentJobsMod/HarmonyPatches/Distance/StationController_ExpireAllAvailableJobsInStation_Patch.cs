using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace PersistentJobsMod.HarmonyPatches.Distance {
    /// <summary>prevents jobs from expiring due to the player's distance from the station</summary>
    [HarmonyPatch]
    public static class StationController_Patches {
        [HarmonyPatch(typeof(StationController), "Update")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> ExpireAllAvailableJobsInStation_Transpiler(IEnumerable<CodeInstruction> instructionsEnumerable) {
            var targetMethod = typeof(StationController).GetMethod(nameof(StationController.ExpireAllAvailableJobsInStation));
            if (targetMethod == null) {
                throw new InvalidOperationException("could not find target method");
            }

            var instructions = instructionsEnumerable.ToList();

            var indices = new List<int>();

            for (var index = 0; index < instructions.Count; index += 1) {
                var instruction = instructions[index];
                if (instruction.opcode == OpCodes.Call) {
                    System.Console.WriteLine($"Transpiler: Index {index} is a method call");

                    var methodBase = instruction.operand as MethodInfo;
                    if (methodBase == null) {
                        System.Console.WriteLine($"Transpiler: Index {index} is a method call but does not habe a MethodBase as operand");
                    } else {
                        System.Console.WriteLine($"Transpiler: Index {index} is a method call to the method {methodBase.Name}");
                        if (ReferenceEquals(instruction.operand, targetMethod)) {
                            System.Console.WriteLine($"Transpiler: Index {index} recorded");
                            indices.Add(index);
                        }
                    }
                }
            }

            System.Console.WriteLine($"Transpiler: Found {indices.Count} indices to patch");

            foreach (var index in indices) {
                if (index > 0 && instructions[index - 1].opcode == OpCodes.Ldarg_0) {
                    instructions[index - 1] = new CodeInstruction(OpCodes.Nop);
                    instructions[index] = new CodeInstruction(OpCodes.Nop);

                    System.Console.WriteLine($"Transpiler: Index {index} patched with no-ops");
                }
            }

            return instructions;
        }
    }
}