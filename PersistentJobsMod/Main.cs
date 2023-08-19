﻿using System.Collections.Generic;
using System.Reflection;
using UnityModManagerNet;
using HarmonyLib;

namespace PersistentJobsMod {
    static class Main {
        public static UnityModManager.ModEntry _modEntry;
        public static bool _overrideTrackReservation = false;
        public static float _initialDistanceRegular = 0f;
        public static float _initialDistanceAnyJobTaken = 0f;

        public static List<string> StationIdSpawnBlockList = new List<string>();
        public static List<string> StationIdPassengerBlockList = new List<string>();

        private static bool _isModBroken = false;

        public static float DVJobDestroyDistanceRegular {
            get { return _initialDistanceRegular; }
        }

        static void Load(UnityModManager.ModEntry modEntry) {
            Main._modEntry = modEntry;

            var harmony = new Harmony(modEntry.Info.Id);
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            ////paxEntry = UnityModManager.FindMod("PassengerJobs");
            ////if (paxEntry?.Active == true) {
            ////    PatchPassengerJobsMod(paxEntry, harmony);
            ////}

            modEntry.OnToggle = OnToggle;
        }

        static bool OnToggle(UnityModManager.ModEntry modEntry, bool isTogglingOn) {
            if (!isTogglingOn) {
                StationIdSpawnBlockList.Clear();
            }

            if (_isModBroken) {
                return !isTogglingOn;
            }

            return true;
        }

        ////static void PatchPassengerJobsMod(UnityModManager.ModEntry paxMod, HarmonyInstance harmony) {
        ////    Main._modEntry.Logger.Log("Patching PassengerJobsMod...");
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
            _isModBroken = true;
            _modEntry.Active = false;
            _modEntry.Logger.Critical("Deactivating mod PersistentJobs due to critical failure!");
            _modEntry.Logger.Warning("You can reactivate PersistentJobs by restarting the game, but this failure " +
                "type likely indicates an incompatibility between the mod and a recent game update. Please search the " +
                "mod's Github issue tracker for a relevant report. If none is found, please open one. Include the " +
                $"exception message printed above and your game's current build number (likely {UnityModManager.gameVersion}).");
        }
    }
}