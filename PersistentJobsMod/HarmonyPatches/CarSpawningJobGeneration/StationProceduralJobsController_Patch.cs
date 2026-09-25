using DV.Utils;
using HarmonyLib;
using PersistentJobsMod.CarSpawningJobGenerators;
using PersistentJobsMod.ModInteraction;
using PersistentJobsMod.Optimization;
using PersistentJobsMod.Persistence;
using System;
using UnityEngine;

namespace PersistentJobsMod.HarmonyPatches.CarSpawningJobGeneration {
    [HarmonyPatch(typeof(StationProceduralJobsController))]
    public static class StationProceduralJobsController_Patch
    {
        [HarmonyPatch(nameof(StationProceduralJobsController.TryToGenerateJobs))]
        [HarmonyPrefix]
        public static bool TryToGenerateJobs_Prefix(StationProceduralJobsController __instance, StationProceduralJobsRuleset ___generationRuleset, ref Coroutine ___generationCoro)
        {
            if (!Main._modEntry.Active) return true;
            if (!MultiplayerShim.IsHost) return false;
            try
            {
                if (StationIdCarSpawningPersistence.Instance.GetHasStationSpawnedCarsFlag(__instance.stationController))
                {
                    Main._modEntry.Logger.Log($"Station {__instance.stationController.logicStation.ID} has already spawned cars, skipping jobs-with-cars generation");
                    FarCarOpt.ResumeCarsInStation(__instance.stationController.logicStation.ID);
                }
                else
                {
                    StationIdCarSpawningPersistence.Instance.SetHasStationSpawnedCarsFlag(__instance.stationController, true);
                    __instance.StopJobGeneration();
                    ___generationCoro = __instance.StartCoroutine(CarSpawningJobGenerator.GenerateProceduralJobsCoroutine(__instance, ___generationRuleset));
                }
            }
            catch (Exception e)
            {
                Main.HandleUnhandledException(e, nameof(StationProceduralJobsController_Patch) + "." + nameof(TryToGenerateJobs_Prefix));
            }

            return false;
        }
    }
}