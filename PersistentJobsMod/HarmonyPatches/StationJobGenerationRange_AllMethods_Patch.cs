using System;
using System.Reflection;
using HarmonyLib;

namespace PersistentJobsMod.HarmonyPatches {
    /// <summary>expands the distance at which the job generation trigger is rearmed</summary>
    [HarmonyPatch(typeof(StationJobGenerationRange))]
    [HarmonyPatchAll]
    class StationJobGenerationRange_AllMethods_Patch {
        static void Prefix(StationJobGenerationRange __instance, MethodBase __originalMethod) {
            try {
                // backup existing values before overwriting
                if (Main.initialDistanceRegular < 1f) {
                    Main.initialDistanceRegular = __instance.destroyGeneratedJobsSqrDistanceRegular;
                }
                if (Main.initialDistanceAnyJobTaken < 1f) {
                    Main.initialDistanceAnyJobTaken = __instance.destroyGeneratedJobsSqrDistanceAnyJobTaken;
                }

                if (Main.modEntry.Active) {
                    if (__instance.destroyGeneratedJobsSqrDistanceAnyJobTaken < 4000000f) {
                        __instance.destroyGeneratedJobsSqrDistanceAnyJobTaken = 4000000f;
                    }
                    __instance.destroyGeneratedJobsSqrDistanceRegular =
                        __instance.destroyGeneratedJobsSqrDistanceAnyJobTaken;
                } else {
                    __instance.destroyGeneratedJobsSqrDistanceRegular = Main.initialDistanceRegular;
                    __instance.destroyGeneratedJobsSqrDistanceAnyJobTaken = Main.initialDistanceAnyJobTaken;
                }
            } catch (Exception e) {
                Main.modEntry.Logger.Error(string.Format(
                    "Exception thrown during StationJobGenerationRange.{0} prefix patch:\n{1}",
                    __originalMethod.Name,
                    e.ToString()
                ));
                Main.OnCriticalFailure();
            }
        }
    }
}