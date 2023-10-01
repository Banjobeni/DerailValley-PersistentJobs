using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace PersistentJobsMod {
    public static class TranspilingUtilities {
        public static List<CodeInstruction> RemoveMethodCallAssumingArgumentsAreLoadedUsingLdarg(IEnumerable<CodeInstruction> instructionsEnumerable, MethodInfo toRemoveCallToMethod, MethodBase originalMethod) {
            var instructions = instructionsEnumerable.ToList();

            var indices = new List<int>();

            for (var index = 0; index < instructions.Count; index += 1) {
                var instruction = instructions[index];
                if (instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) {
                    var methodBase = instruction.operand as MethodInfo;
                    if (methodBase != null) {
                        if (ReferenceEquals(instruction.operand, toRemoveCallToMethod)) {
                            indices.Add(index);
                        }
                    }
                }
            }

            var numberOfArguments = (toRemoveCallToMethod.IsStatic ? 0 : 1) + toRemoveCallToMethod.GetParameters().Length;

            foreach (var index in indices) {
                if (index < numberOfArguments) {
                    Debug.LogWarning($"[PersistentJobsMod] Transpiling {originalMethod.DeclaringType.FullName}:{originalMethod.Name} at index {index}: Could not remove method call to {toRemoveCallToMethod.Name} because it requires {numberOfArguments} arguments but has only {index} code instructions before it.");
                } else {
                    if (instructions.Skip(index - numberOfArguments).Take(numberOfArguments).All(i => i.opcode == OpCodes.Ldarg_0 || i.opcode == OpCodes.Ldarg_1 || i.opcode == OpCodes.Ldarg_2 || i.opcode == OpCodes.Ldarg_3)) {
                        for (var patchIndex = index - numberOfArguments; patchIndex < index + 1; patchIndex += 1) {
                            instructions[patchIndex] = new CodeInstruction(OpCodes.Nop);
                        }

                        Debug.Log($"[PersistentJobsMod] Transpiling {originalMethod.DeclaringType.FullName}:{originalMethod.Name} at index {index}: Removed method call to {toRemoveCallToMethod.Name}.");
                    } else {
                        Debug.LogWarning($"[PersistentJobsMod] Transpiling {originalMethod.DeclaringType.FullName}:{originalMethod.Name} at index {index}: Could not remove method call to {toRemoveCallToMethod.Name} because it requires {numberOfArguments} arguments but did not have as many Ldarg code instructions before it.");
                    }
                }
            }

            return instructions;
        }
    }
}