using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;

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

        public static void PatchPrefix(MethodInfo target, Type patchContainer, string patchMethodName) => PatchMethod(target, patchContainer, patchMethodName, (harmony, t, hm) => harmony.Patch(t, prefix: hm), (target.DeclaringType.Name + "." + target.Name));

        public static void PatchPostfix(MethodInfo target, Type patchContainer, string patchMethodName) => PatchMethod(target, patchContainer, patchMethodName, (harmony, t, hm) => harmony.Patch(t, postfix: hm), (target.DeclaringType.Name + "." + target.Name));

        private static void PatchMethod(MethodInfo target, Type patchContainer, string patchMethodName, Action<Harmony, MethodInfo, HarmonyMethod> applyPatch, string logName)
        {
            if (target == null)
            {
                Main._modEntry.Logger.Error($"Target method not found for {logName}");
                throw new MethodAccessException();
            }

            var patchMethod = patchContainer.GetMethod(patchMethodName, BindingFlags.Static | BindingFlags.NonPublic);

            if (patchMethod == null)
            {
                Main._modEntry.Logger.Error($"Patch method '{patchMethodName}' not found for {logName}");
                throw new MethodAccessException();
            }

            applyPatch(Main.Harmony, target, new HarmonyMethod(patchMethod));

            Main._modEntry.Logger.Log($"Successfully patched {logName}");
        }
    }
}