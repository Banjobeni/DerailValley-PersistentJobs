using HarmonyLib;
using MessageBox;
using PersistentJobsMod.Model;
using PersistentJobsMod.ModInteraction;
using PersistentJobsMod.Utilities;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityModManagerNet;

namespace PersistentJobsMod {
    public static class Main {
        // ReSharper disable InconsistentNaming
        public static UnityModManager.ModEntry _modEntry;
        public static Harmony Harmony;
        public static float _initialDistanceRegular = 0f;
        public static float _initialDistanceAnyJobTaken = 0f;
        // ReSharper restore InconsistentNaming

        // ReSharper disable once RedundantDefaultMemberInitializer
        private static bool _isModBroken = false;

        public static float DVJobDestroyDistanceRegular {
            get { return _initialDistanceRegular; }
        }

        public static Settings Settings { get; private set; }

        public static UnityModManager.ModEntry PaxJobs { get; set; }
        public static bool PaxJobsPresent { get; set; }

        public static void Load(UnityModManager.ModEntry modEntry) {
            _modEntry = modEntry;

            Harmony = new Harmony(modEntry.Info.Id);
            Harmony.PatchAll(Assembly.GetExecutingAssembly());

            Settings = UnityModManager.ModSettings.Load<Settings>(modEntry);

            modEntry.OnToggle = OnToggle;
            modEntry.OnGUI = OnGUI;
            modEntry.OnSaveGUI = OnSaveGUI;

            WorldStreamingInit.LoadingFinished += WorldStreamingInitLoadingFinished;

            PaxJobs = UnityModManager.modEntries.FirstOrDefault(m => m.Info.Id == "PassengerJobs" && m.Enabled && m.Active && !m.ErrorOnLoading /*&& m.Version.ToString() == "5.2"*/);
            PaxJobsPresent = (PaxJobs != null);
            if (PaxJobsPresent)
            {
                _modEntry.Logger.Log($"{PaxJobs.Info.DisplayName} version {PaxJobs.Version} is present, enabling mod compatibility");
                if (!PaxJobsCompat.Initialize())
                {
                    PaxJobsPresent = false;
                    _modEntry.Logger.Error("Passanger Jobs compatibility failed to load!");
                    HarmonyPatches.Save.WorldStreaminInit_Patch.ShowPopupOnPlayerSpawn($"Passenger Jobs mod v{PaxJobs.Version} is present but the Persistent Jobs compatibility layer is not loaded. \nThis is probably due to a recent update (check mod pages or ask on the Altfuture discord). \nThe game should be in a playable state,\n but new passenger jobs may not be generated and cars will remain jobless.");
                }
            }
            else
            {
                _modEntry.Logger.Log($"Targeted version of optional mod Passanger Jobs (5.2) is not present, inactive, or has ran into errors, skipping mod compatibility");
            }
        }

        static bool OnToggle(UnityModManager.ModEntry modEntry, bool isTogglingOn) {
            if (_isModBroken) {
                return !isTogglingOn;
            }

            return true;
        }

        static void OnGUI(UnityModManager.ModEntry modEntry) {
            Settings.Draw(modEntry);
        }

        static void OnSaveGUI(UnityModManager.ModEntry modEntry) {
            Settings.Save(modEntry);
        }

        private static void WorldStreamingInitLoadingFinished() {
            DetailedCargoGroups.Initialize();
            EmptyTrainCarTypeDestinations.Initialize();
        }

        public static void HandleUnhandledException(Exception e, string location) {
            _isModBroken = true;
            _modEntry.Active = false;

            _modEntry.Logger.Critical($"Deactivating mod PersistentJobsMod due to critical exception in {location}:\n{e}");

            AddMoreInfoToExceptionHelper.AlertPlayerToExceptionAndCompileDataForBugReport(e, location);
        }
    }
}