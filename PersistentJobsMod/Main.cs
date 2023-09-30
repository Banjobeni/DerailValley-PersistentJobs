using System.Reflection;
using UnityModManagerNet;
using HarmonyLib;
using PersistentJobsMod.Model;

namespace PersistentJobsMod {
    public static class Main {
        public static UnityModManager.ModEntry _modEntry;
        public static bool _overrideTrackReservation = false;
        public static float _initialDistanceRegular = 0f;
        public static float _initialDistanceAnyJobTaken = 0f;

        private static bool _isModBroken = false;

        public static float DVJobDestroyDistanceRegular {
            get { return _initialDistanceRegular; }
        }

        public static void Load(UnityModManager.ModEntry modEntry) {
            Main._modEntry = modEntry;

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

        public static void OnCriticalFailure() {
            _isModBroken = true;
            _modEntry.Active = false;
            _modEntry.Logger.Critical("Deactivating mod PersistentJobs due to critical failure!");
            _modEntry.Logger.Warning("You can reactivate PersistentJobs by restarting the game, but this failure " +
                "type likely indicates an incompatibility between the mod and a recent game update. Please search the " +
                "mod's Github issue tracker for a relevant report. If none is found, please open one. Include the " +
                $"exception message printed above and your game's current build number (likely {UnityModManager.gameVersion}).");
            UnityEngine.Debug.LogError("[PersistentJobsMod] Deactivating mod due to critical failure. See UnityModManager console or Player.log for details. The mod will stay inactive until the game is restarted.");
        }
    }
}