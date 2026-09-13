using HarmonyLib;
using MessageBox;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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

        public static bool IsInCallers(string methodName, Exception fromEx = null, StackTrace trace = null, string excludeMethodName = "", string specificFrameNumeric = "", int framesToSkip = 0, bool log = false)
        {
            bool specific = int.TryParse(specificFrameNumeric, out int intSpecificFrame);
            if (fromEx != null) trace = new StackTrace(fromEx, framesToSkip, true);
            trace ??= new(framesToSkip, log);
            StringBuilder callerNames = new();
            if (specific)
            {
                var method = trace.GetFrame(intSpecificFrame)?.GetMethod();
                callerNames.Append($"{method?.DeclaringType?.Namespace}.{method?.DeclaringType?.Name}.{method?.Name}");
                if (log) Main._modEntry.Logger.Log($"frame {intSpecificFrame} is {method?.Name}");
            }
            else
            {
                if (log) Main._modEntry.Logger.Log("getting all frames");
                foreach (StackFrame frame in trace.GetFrames())
                {
                    var method = frame?.GetMethod();
                    callerNames.Append($"{method?.DeclaringType?.Namespace}.{method?.DeclaringType?.Name}.{method?.Name} \n");
                }
            }
            if (log) Main._modEntry.Logger.Log(callerNames.ToString());
            if (excludeMethodName.Length > 1 && ((callerNames.ToString()).Contains(excludeMethodName))) return false;
            if ((callerNames.ToString()).Contains(methodName)) return true;
            return false;
        }

        public static List<(MethodInfo target, Type patchContainer, string patchMethodName)> PatchRecord = [];

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

            if (PatchRecord.Contains((target, patchContainer, patchMethodName)))
            {
                Main._modEntry.Logger.Error($"Patch {patchMethodName} on {target.Name} already applied, aborting!");
                throw new ArgumentException();
            }

            applyPatch(Main.Harmony, target, new HarmonyMethod(patchMethod));

            PatchRecord.Add((target, patchContainer, patchMethodName));

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

            if (PatchRecord.Contains((target, patchContainer, patchMethodName)))
            {
                Main._modEntry.Logger.Error($"Patch {patchMethodName} on {target.Name} already applied, aborting!");
                throw new ArgumentException();
            }

            Main.Harmony.CreateReversePatcher(target, new HarmonyMethod(patchMethod)).Patch();

            PatchRecord.Add((target, patchContainer, patchMethodName));

            Main._modEntry.Logger.Log($"Successfully reverse patched {logName}");
        }

        public static void UnpatchAllIn(Type patchContainerType = null)
        {
            StringBuilder s = new();
            if (patchContainerType is null)
            {
                UnpatchMany(PatchRecord, ref s);
            }
            else
            {
                UnpatchMany(PatchRecord.Where(p => p.patchContainer == patchContainerType).ToList(), ref s);
            }
            if (s.Length > 0)
            {
                if (!WorldStreamingInit.IsLoaded)
                {
                    HarmonyPatches.Save.WorldStreamingInit_Patch.ShowPopupOnPlayerSpawn("State is not clean, there might be problems. \n" + s);
                }
                else
                {
                    PopupAPI.ShowOk("State is not clean, there might be problems. \n" + s);
                }
            }
        }

        private static void UnpatchMany(List<(MethodInfo target, Type patchContainer, string patchMethodName)> patchRecord, ref StringBuilder s)
        {
            foreach (var (target, patchContainer, patchMethodName) in patchRecord)
            {
                try
                {
                    Main.Harmony.Unpatch(target, HarmonyPatchType.All, Main.Harmony.Id);
                    Main._modEntry.Logger.Log($"Unpatched {target.Name}");
                }
                catch (Exception ex)
                {
                    Main._modEntry.Logger.LogException($"Error when unpatching {target.Name}!", ex);
                    s.AppendLine("Error when unpatching " + target.Name + ": " + Environment.NewLine + ex.Message);
                }
            }
            patchRecord.Clear();
            PatchRecord.RemoveAll(r => patchRecord.Contains(r));
        }
    }
}