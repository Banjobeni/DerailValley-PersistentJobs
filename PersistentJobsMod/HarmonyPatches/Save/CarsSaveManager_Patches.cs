using HarmonyLib;
using Newtonsoft.Json.Linq;
using PersistentJobsMod.Persistence;
using PersistentJobsMod.Utilities;
using System;
using System.Collections;
using System.Diagnostics;
using System.Linq;
using UnityEngine;

namespace PersistentJobsMod.HarmonyPatches.Save
{
    /// <summary>patch CarsSaveManager.Load to ensure CarsSaveManager.TracksHash exists</summary>
    [HarmonyPatch(typeof(CarsSaveManager), "Load")]
    public static class CarsSaveManager_Patches
    {
        public static void Postfix(ref bool __result)
        {
            //if no car data is loaded (eg. game update reset them), expire all jobs and allow new cars to re-spawn 
            if (__result == false)
            {
                Main._modEntry.Logger.Warning($"CarsSaveManager_Patches.Load.Postfix: No savegame data found, possibly due to game update. Resetting all jobs and stations.");
                ResetJobsAndCarsState();
            }
            try
            {
                var saveData = SaveGameManager.Instance.data.GetJObject(SaveDataConstants.SAVE_DATA_PRIMARY_KEY);

                if (saveData == null)
                {
                    Main._modEntry.Logger.Log("Not loading save data: primary object is null.");
                    return;
                }

                var spawnBlockSaveData = (JArray)saveData[$"{SaveDataConstants.SAVE_DATA_SPAWN_BLOCK_KEY}#{SaveDataConstants.TRACK_HASH_SAVE_KEY}"];
                if (spawnBlockSaveData == null)
                {
                    Main._modEntry.Logger.Log("Not loading spawn block list: data is null.");
                }
                else
                {
                    var alreadySpawnedCarsStatioinIds = spawnBlockSaveData.Select(id => (string)id).ToList();
                    StationIdCarSpawningPersistence.Instance.HandleSavegameLoadedSpawnedStationIds(alreadySpawnedCarsStatioinIds);
                    Main._modEntry.Logger.Log($"Loaded station spawn block list: [ {string.Join(", ", alreadySpawnedCarsStatioinIds)} ]");
                }
            }
            catch (Exception e)
            {
                Main._modEntry.Logger.Warning($"Loading mod data failed with exception:\n{e}");
                ResetJobsAndCarsState();
            }
        }

        public static IEnumerator GenerateJobsCurrentStationCoroutine()
        {
            while (PlayerManager.PlayerTransform == null)
            {
                yield return null;
            }
            var station = StationController.allStations.OrderBy(sc => (PlayerManager.PlayerTransform.position - sc.gameObject.transform.position).sqrMagnitude).First();
            station.ProceduralJobsController.TryToGenerateJobs();
            StationIdCarSpawningPersistence.Instance.SetHasStationSpawnedCarsFlag(station, true);
        }

        public static void ResetJobsAndCarsState()
        {
            Main._modEntry.Logger.Warning("Faliure in job loading, reseting all jobs and stations");
            StackTrace trace = new(true);
            Main._modEntry.Logger.Log("Callstack: \n" + trace.ToString());
            PersistentJobsMod.Console.ExpireAvailableJobsInAllStations();
            StationIdCarSpawningPersistence.Instance.ClearStationsSpawnedCarsFlagForAllStations();
            SaveGameManager.Instance.StartCoroutine(GenerateJobsCurrentStationCoroutine());
        }
    }

    [HarmonyPatch(typeof(CarsSaveManager), "DeleteAllExistingCars")]
    public static class CarsSaveManager_DeleteAllExistingCars_Patch
    {
        public static void Postfix()
        {

            if (ReflectionUtilities.IsInCallers(methodName: "LoadingNonBlockingCoro", excludeMethodName: "Manager.Load_Patch", specificFrameNumeric: "", log: false))
            {
                Main._modEntry.Logger.Log($" CarsSaveManager_DeleteAllExistingCars_Patch.Postfix: Savegame data reset, possibly due to mod or game update. Resetting all jobs and stations.");
                CarsSaveManager_Patches.ResetJobsAndCarsState();
            }
        }
    }
}