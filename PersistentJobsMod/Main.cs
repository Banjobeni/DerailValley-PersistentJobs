using System;
using System.IO;
using System.Reflection;
using UnityModManagerNet;
using HarmonyLib;
using PersistentJobsMod.Model;
using UnityEngine;

namespace PersistentJobsMod {
    public static class Main {
        public static UnityModManager.ModEntry _modEntry;
        public static float _initialDistanceRegular = 0f;
        public static float _initialDistanceAnyJobTaken = 0f;

        private static bool _isModBroken = false;

        public static float DVJobDestroyDistanceRegular {
            get { return _initialDistanceRegular; }
        }

        public static void Load(UnityModManager.ModEntry modEntry) {
            _modEntry = modEntry;

            var harmony = new Harmony(modEntry.Info.Id);
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            modEntry.OnToggle = OnToggle;

            WorldStreamingInit.LoadingFinished += WorldStreamingInitLoadingFinished;
        }

        static bool OnToggle(UnityModManager.ModEntry modEntry, bool isTogglingOn) {
            if (_isModBroken) {
                return !isTogglingOn;
            }

            return true;
        }

        private static void WorldStreamingInitLoadingFinished() {
            DetailedCargoGroups.Initialize();
            EmptyTrainCarTypeDestinations.Initialize();
        }

        public static void HandleUnhandledException(Exception e, string location) {
            _isModBroken = true;
            _modEntry.Active = false;

            var logMessage = $"Exception thrown at {location}:\n{e}";
            Debug.LogError(logMessage);

            _modEntry.Logger.Critical($"Deactivating mod PersistentJobsMod due to critical exception in {location}:\n{e}");

            UnityEngine.Debug.LogError("[PersistentJobsMod] Deactivating mod due to critical failure. See UnityModManager console or Player.log for details. The mod will stay inactive until the game is restarted.");

            var logExceptionFilepath = Path.Combine(Application.persistentDataPath, $"PersistentJobsMod_Exception_{DateTime.Now.ToString("O").Replace(':', '.')}.log");
            File.WriteAllText(logExceptionFilepath, logMessage);
        }
    }
}