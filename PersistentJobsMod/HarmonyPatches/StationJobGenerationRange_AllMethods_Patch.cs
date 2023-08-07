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
                if (Main._initialDistanceRegular < 1f) {
                    Main._initialDistanceRegular = __instance.destroyGeneratedJobsSqrDistanceRegular;
                }
                if (Main._initialDistanceAnyJobTaken < 1f) {
                    Main._initialDistanceAnyJobTaken = __instance.destroyGeneratedJobsSqrDistanceAnyJobTaken;
                }

                if (Main._modEntry.Active) {
                    if (__instance.destroyGeneratedJobsSqrDistanceAnyJobTaken < 4000000f) {
                        __instance.destroyGeneratedJobsSqrDistanceAnyJobTaken = 4000000f;
                    }
                    __instance.destroyGeneratedJobsSqrDistanceRegular =
                        __instance.destroyGeneratedJobsSqrDistanceAnyJobTaken;
                } else {
                    __instance.destroyGeneratedJobsSqrDistanceRegular = Main._initialDistanceRegular;
                    __instance.destroyGeneratedJobsSqrDistanceAnyJobTaken = Main._initialDistanceAnyJobTaken;
                }
            } catch (Exception e) {
                Main._modEntry.Logger.Error($"Exception thrown during StationJobGenerationRange.{__originalMethod.Name} prefix patch:\n{e.ToString()}");
                Main.OnCriticalFailure();
            }
        }
    }
}