using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace PersistentJobsMod.Utilities
{
    public static class ReflectionUtilities
    {
        public class CompatAccess
        {
            public static Type Type(string fullName) => AccessTools.TypeByName(fullName) ?? throw new TypeLoadException($"Type not found: {fullName}");
            public static MethodInfo Method(Type type, string name, Type[] args = null) => (args == null ? AccessTools.Method(type, name) : AccessTools.Method(type, name, args)) ?? throw new MissingMethodException(type.FullName, name);
            public static ConstructorInfo Ctor(Type type, Type[] args) => AccessTools.Constructor(type, args) ?? throw new MissingMethodException(type.FullName, ".ctor");
            public static PropertyInfo Property(Type type, string name) => AccessTools.Property(type, name) ?? throw new MissingMemberException(type.FullName, name);
            public static FieldInfo Field(Type type, string name) => AccessTools.Field(type, name) ?? throw new MissingFieldException(type.FullName, name);
            public static Type IEnumerableOf(Type elementType) => typeof(IEnumerable<>).MakeGenericType(elementType);
        }

        public readonly struct Foreign<TTag>
        {
            public readonly object Value;
            public Foreign(object value) => Value = value /*?? throw new ArgumentNullException(nameof(value))*/;
            public override string ToString() => $"{typeof(TTag).Name}";
        }

        public static bool IsInCallers(string methodName, string excludeMethodName = "", string specificFrameNumeric = "", int framesToSkip = 0, bool log = false)
        {
            bool specific = int.TryParse(specificFrameNumeric, out int intSpecificFrame);
            StackTrace trace = new(framesToSkip, log);
            StringBuilder callerNames = new();
            if (specific)
            {
                var method = trace.GetFrame(intSpecificFrame).GetMethod();
                callerNames.Append($"{method.DeclaringType?.Namespace}.{method.DeclaringType?.Name}.{method.Name}");
                if (log) Main._modEntry.Logger.Log($"frame {intSpecificFrame} is {methodName}");
            }
            else
            {
                if (log) Main._modEntry.Logger.Log("getting all frames");
                foreach (StackFrame frame in trace.GetFrames())
                {
                    var method = frame.GetMethod();
                    callerNames.Append($"{method.DeclaringType?.Namespace}.{method.DeclaringType?.Name}.{method.Name} \n");
                }
            }
            if (log) Main._modEntry.Logger.Log(callerNames.ToString());
            if (excludeMethodName.Length > 1 && ((callerNames.ToString()).Contains(excludeMethodName))) return false;
            if ((callerNames.ToString()).Contains(methodName)) return true;
            return false;
        }

        public static List<(MethodInfo target, Type patchContainer, string patchMethodName)> patchRecord = new();

        public static void PatchPrefix(MethodInfo target, Type patchContainer, string patchMethodName) => PatchMethod(target, patchContainer, patchMethodName, (harmony, t, hm) => harmony.Patch(t, prefix: hm), (target.DeclaringType.Namespace + "." + target.DeclaringType.Name + "." + target.Name));

        public static void PatchPostfix(MethodInfo target, Type patchContainer, string patchMethodName) => PatchMethod(target, patchContainer, patchMethodName, (harmony, t, hm) => harmony.Patch(t, postfix: hm), (target.DeclaringType.Namespace + "." + target.DeclaringType.Name + "." + target.Name));

        public static void PatchReverse(MethodInfo target, Type patchContainer, string patchMethodName) => PatchReverseMethod(target, patchContainer, patchMethodName, target.DeclaringType.Namespace + "." + target.DeclaringType.Name + "." + target.Name);

        private static void PatchMethod(MethodInfo target, Type patchContainer, string patchMethodName, Action<Harmony, MethodInfo, HarmonyMethod> applyPatch, string logName)
        {
            if (target == null)
            {
                Main._modEntry.Logger.Error($"Target method not found for {logName}");
                throw new MethodAccessException();
            }

            var patchMethod = patchContainer.GetMethod(patchMethodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            if (patchMethod == null)
            {
                Main._modEntry.Logger.Error($"Patch method '{patchMethodName}' not found for {logName}");
                throw new MethodAccessException();
            }

            applyPatch(Main.Harmony, target, new HarmonyMethod(patchMethod));

            patchRecord.Add((target, patchContainer, patchMethodName));

            Main._modEntry.Logger.Log($"Successfully patched {logName}");
        }

        private static void PatchReverseMethod(MethodInfo target, Type patchContainer, string patchMethodName, string logName)
        {
            if (target == null)
            {
                Main._modEntry.Logger.Error($"Target method not found for {logName}");
                throw new MethodAccessException();
            }

            var patchMethod = patchContainer.GetMethod(patchMethodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            if (patchMethod == null)
            {
                Main._modEntry.Logger.Error($"Reverse patch method '{patchMethodName}' not found for {logName}");
                throw new MethodAccessException();
            }

            Main.Harmony.CreateReversePatcher(target, new HarmonyMethod(patchMethod)).Patch();

            patchRecord.Add((target, patchContainer, patchMethodName));

            Main._modEntry.Logger.Log($"Successfully reverse patched {logName}");
        }

        public static void UnpatchAll()
        {
            StringBuilder s = new();
            foreach (var (target, patchContainer, patchMethodName) in ReflectionUtilities.patchRecord)
            {
                try
                {
                    Main.Harmony.Unpatch(target, HarmonyPatchType.All, Main.Harmony.Id);
                }
                catch (Exception ex)
                {
                    Main._modEntry.Logger.LogException($"Error when unpatching {target.Name}!", ex);
                    s.AppendLine(target.Name + ": " + ex.Message);
                }
            }
            if (s.Length > 0) HarmonyPatches.Save.WorldStreaminInit_Patch.ShowPopupOnPlayerSpawn("State is not clean, there might be problems. \n" + s);
        }
    }
}