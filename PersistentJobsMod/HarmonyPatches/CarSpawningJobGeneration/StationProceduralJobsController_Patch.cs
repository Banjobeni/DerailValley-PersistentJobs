using System;
using HarmonyLib;
using PersistentJobsMod.CarSpawningJobGenerators;
using PersistentJobsMod.Persistence;
using UnityEngine;

namespace PersistentJobsMod.HarmonyPatches.CarSpawningJobGeneration {
    [HarmonyPatch(typeof(StationProceduralJobsController))]
    public static class StationProceduralJobsController_Patch {
        [HarmonyPatch(nameof(StationProceduralJobsController.TryToGenerateJobs))]
        [HarmonyPrefix]
        public static bool TryToGenerateJobs_Prefix(StationProceduralJobsController __instance, StationProceduralJobsRuleset ___generationRuleset, ref Coroutine ___generationCoro) {
            if (!Main._modEntry.Active) {
                return true;
            }

            try {
                if (StationIdCarSpawningPersistence.Instance.GetHasStationSpawnedCarsFlag(__instance.stationController)) {
                    Main._modEntry.Logger.Log($"Station {__instance.stationController.logicStation.ID} has already spawned cars, skipping jobs-with-cars generation");
                } else {
                    StationIdCarSpawningPersistence.Instance.SetHasStationSpawnedCarsFlag(__instance.stationController, true);

                    __instance.StopJobGeneration();
                    ___generationCoro = __instance.StartCoroutine(CarSpawningJobGenerator.GenerateProceduralJobsCoroutine(__instance, ___generationRuleset));
                }
            } catch (Exception e) {
                Main._modEntry.Logger.Error($"Exception thrown during {nameof(StationProceduralJobsController_Patch)} prefix:\n{e}");
                Main.OnCriticalFailure();
            }

            return false;
        }
    }
}