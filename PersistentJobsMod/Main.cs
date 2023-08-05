using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityModManagerNet;
using DV.Utils;
using HarmonyLib;

namespace PersistentJobsMod {
    static class Main {
        public static UnityModManager.ModEntry modEntry;
        public static UnityModManager.ModEntry paxEntry;
        public static bool overrideTrackReservation = false;
        public static float initialDistanceRegular = 0f;
        public static float initialDistanceAnyJobTaken = 0f;

        public static List<string> StationIdSpawnBlockList = new List<string>();
        public static List<string> StationIdPassengerBlockList = new List<string>();

        private static bool isModBroken = false;

#if DEBUG
        private const float PERIOD = 60f;
#else
		private const float PERIOD = 5f * 60f;
#endif
        public static float DVJobDestroyDistanceRegular {
            get { return initialDistanceRegular; }
        }

        static void Load(UnityModManager.ModEntry modEntry) {
            Main.modEntry = modEntry;

            var harmony = new Harmony(modEntry.Info.Id);
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            ////paxEntry = UnityModManager.FindMod("PassengerJobs");
            ////if (paxEntry?.Active == true) {
            ////    PatchPassengerJobsMod(paxEntry, harmony);
            ////}

            modEntry.OnToggle = OnToggle;
        }

        static bool OnToggle(UnityModManager.ModEntry modEntry, bool isTogglingOn) {
            var isTogglingOff = !isTogglingOn;

            if (SingletonBehaviour<UnusedTrainCarDeleter>.Instance == null) {
                // delay initialization
                modEntry.OnUpdate = (entry, delta) => {
                    if (SingletonBehaviour<UnusedTrainCarDeleter>.Instance != null) {
                        modEntry.OnUpdate = null;
                        ReplaceCoroutine(isTogglingOn);
                    }
                };
                return true;
            } else {
                ReplaceCoroutine(isTogglingOn);
            }

            if (isTogglingOff) {
                StationIdSpawnBlockList.Clear();
            }

            if (isModBroken) {
                return !isTogglingOn;
            }

            return true;
        }

        static void ReplaceCoroutine(bool isTogglingOn) {
            float? carsCheckPeriod = Traverse.Create(SingletonBehaviour<UnusedTrainCarDeleter>.Instance)
                .Field("DELETE_CARS_CHECK_PERIOD")
                .GetValue<float>();
            if (carsCheckPeriod == null) {
                carsCheckPeriod = 0.5f;
            }
            SingletonBehaviour<UnusedTrainCarDeleter>.Instance.StopAllCoroutines();
            if (isTogglingOn && !isModBroken) {
                modEntry.Logger.Log("Injected mod coroutine.");
                SingletonBehaviour<UnusedTrainCarDeleter>.Instance
                    .StartCoroutine(UnusedTrainCarDeleterPatch.TrainCarsCreateJobOrDeleteCheck(PERIOD, Mathf.Max(carsCheckPeriod.Value, 1.0f)));
            } else {
                modEntry.Logger.Log("Restored game coroutine.");
                SingletonBehaviour<UnusedTrainCarDeleter>.Instance.StartCoroutine(
                    SingletonBehaviour<UnusedTrainCarDeleter>.Instance.TrainCarsDeleteCheck(carsCheckPeriod.Value)
                );
            }
        }

        ////static void PatchPassengerJobsMod(UnityModManager.ModEntry paxMod, HarmonyInstance harmony) {
        ////    Debug.Log("Patching PassengerJobsMod...");
        ////    try {
        ////        // spawn block list handling for passenger jobs
        ////        Type paxJobGen = paxMod.Assembly.GetType("PassengerJobsMod.PassengerJobGenerator", true);
        ////        var StartGenAsync = AccessTools.Method(paxJobGen, "StartGenerationAsync");
        ////        var StartGenAsyncPrefix = AccessTools.Method(typeof(PassengerJobGenerator_StartGenerationAsync_Patch), "Prefix");
        ////        var StartGenAsyncPostfix = AccessTools.Method(typeof(PassengerJobGenerator_StartGenerationAsync_Patch), "Postfix");
        ////        harmony.Patch(StartGenAsync, prefix: new HarmonyMethod(StartGenAsyncPrefix), postfix: new HarmonyMethod(StartGenAsyncPostfix));
        ////        Type paxJobSettings = paxMod.Assembly.GetType("PassengerJobsMod.PJModSettings", true);
        ////        var PurgeData = AccessTools.Method(paxJobSettings, "PurgeData");
        ////        var PurgeDataPostfix = AccessTools.Method(typeof(PJModSettings_PurgeData_Patch), "Postfix");
        ////        harmony.Patch(PurgeData, postfix: new HarmonyMethod(PurgeDataPostfix));

        ////        // train car preservation
        ////        Type paxCommuterCtrl = paxMod.Assembly.GetType("PassengerJobsMod.CommuterChainController", true);
        ////        var LastJobInChainComplete_Commuter = AccessTools.Method(paxCommuterCtrl, "OnLastJobInChainCompleted");
        ////        var ReplaceDeleteTrainCars = AccessTools.Method(typeof(CarSpawner_DeleteTrainCars_Replacer), "Transpiler");
        ////        harmony.Patch(LastJobInChainComplete_Commuter, transpiler: new HarmonyMethod(ReplaceDeleteTrainCars));
        ////        Type paxExpressCtrl = paxMod.Assembly.GetType("PassengerJobsMod.PassengerTransportChainController", true);
        ////        var LastJobInChainComplete_Express = AccessTools.Method(paxExpressCtrl, "OnLastJobInChainCompleted");
        ////        harmony.Patch(LastJobInChainComplete_Express, transpiler: new HarmonyMethod(ReplaceDeleteTrainCars));
        ////        Type paxAbandonPatch = paxMod.Assembly.GetType("PassengerJobsMod.JCC_OnJobAbandoned_Patch");
        ////        var OnAnyJobFromChainAbandonedPrefix = AccessTools.Method(paxAbandonPatch, "Prefix");
        ////        harmony.Patch(OnAnyJobFromChainAbandonedPrefix, transpiler: new HarmonyMethod(ReplaceDeleteTrainCars));
        ////    } catch (Exception e) {
        ////        Debug.LogError($"Failed to patch PassengerJobsMod!\n{e}");
        ////    }
        ////}

        public static void OnCriticalFailure() {
            isModBroken = true;
            modEntry.Active = false;
            modEntry.Logger.Critical("Deactivating mod PersistentJobs due to critical failure!");
            modEntry.Logger.Warning("You can reactivate PersistentJobs by restarting the game, but this failure " +
                "type likely indicates an incompatibility between the mod and a recent game update. Please search the " +
                "mod's Github issue tracker for a relevant report. If none is found, please open one. Include the " +
                $"exception message printed above and your game's current build number (likely {UnityModManager.gameVersion}).");
        }
    }
}