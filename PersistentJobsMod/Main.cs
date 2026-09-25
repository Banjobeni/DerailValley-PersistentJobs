using DV;
using DV.Common;
using DV.UserManagement;
using DV.UserManagement.Data;
using DV.Utils;
using HarmonyLib;
using PersistentJobsMod.HarmonyPatches.Save;
using PersistentJobsMod.Model;
using PersistentJobsMod.ModInteraction;
using PersistentJobsMod.Utilities;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityModManagerNet;
using static UnityModManagerNet.UnityModManager;

namespace PersistentJobsMod {
    [EnableReloading]
    public static class Main {
        // ReSharper disable InconsistentNaming
        public static UnityModManager.ModEntry _modEntry;
        public static Harmony Harmony;
        public static float _initialDistanceRegular = 0f;
        public static float _initialDistanceAnyJobTaken = 0f;
        public static float _initialGenerateJobsSqrDistance = 0f;
        // ReSharper restore InconsistentNaming

        // ReSharper disable once RedundantDefaultMemberInitializer
        private static bool _isModBroken = false;

        public static bool Stop = false;
        public static bool Pause = false;

        public static float DVJobDestroyDistanceRegular {
            get { return _initialDistanceRegular; }
        }

        public static Settings Settings { get; private set; }

        public static bool yardMasterPresent = false;
        public static bool problematicBetterLoading = false;

        public static UnityModManager.ModEntry PaxJobs { get; set; }
        public static bool paxJobsPresent = false;
        public static bool PaxJobsPresent
        {
            get => paxJobsPresent;
            set 
            {
                //backing field changed by the (un)load method
                if (!paxJobsPresent && value)
                {
                    TryLoadPaxJobsCompat();
                }
                else if (paxJobsPresent && !value)
                {
                    PaxJobsCompat.Unload();
                }
                else
                {
                    paxJobsPresent = value;
                }
            }
        }

        public static void Load(UnityModManager.ModEntry modEntry) {
            Pause = true;
            _modEntry = modEntry;

            Stop = false;
            Harmony ??= new Harmony(modEntry.Info.Id);
            Harmony.PatchAll(Assembly.GetExecutingAssembly());

            Settings = UnityModManager.ModSettings.Load<Settings>(modEntry);

            modEntry.OnToggle = OnToggle;
            modEntry.OnUnload = Unload;
            modEntry.OnGUI = OnGUI;
            modEntry.OnSaveGUI = OnSaveGUI;

            WorldStreamingInit.LoadingFinished += WorldStreamingInitLoadingFinished;
            //when coming from a reload things need to be re-initialized
            if (WorldStreamingInit.IsStreamingDone) SetupOnReload();

            TryLoadPaxJobsCompat();
            YardMasterInit();
            InitializeMPShim(_modEntry);
            _modEntry.OnLateUpdate += InitializeMPShim;
            if (Settings.HideDebugConsole) _modEntry.OnFixedGUI += ((_) => Debug.developerConsoleVisible = false);

            Pause = false;
        }

        public static bool Unload(UnityModManager.ModEntry modEntry)
        {
            try
            {
                Settings.Save(modEntry);
                (SingletonBehaviour<UserManager>.Instance.CurrentUser?.CurrentSession as GameSession)?.Save();
                if (WorldStreamingInit.IsLoaded) SingletonBehaviour<SaveGameManager>.Instance?.Save(SaveType.Auto, null, true);
                PaxJobsCompat.Unload();
                Harmony.UnpatchAll(modEntry.Info.Id);

                WorldStreamingInit.LoadingFinished -= WorldStreamingInitLoadingFinished;
                Stop = true;

                return true;
            }
            catch (Exception e)
            {
                Main.HandleUnhandledException(e, nameof(Main.Unload));
                return false;
            }
        }

        static bool OnToggle(UnityModManager.ModEntry modEntry, bool isTogglingOn) 
        {
            if (_isModBroken) return !isTogglingOn;
            if (isTogglingOn)
            {
                Settings = UnityModManager.ModSettings.Load<Settings>(modEntry);

                if (WorldStreamingInit.IsStreamingDone)
                {
                    SetupOnReload();
                    TryLoadPaxJobsCompat();
                }

                Pause = false;
            }
            else
            {
                Settings.Save(modEntry);
                Pause = true;
                PaxJobsPresent = false;
            }

            return true;
        }

        static void OnGUI(UnityModManager.ModEntry modEntry) {
            Settings.Draw(modEntry);
            Settings.DrawButtons();
        }

        static void OnSaveGUI(UnityModManager.ModEntry modEntry) {
            Settings.Save(modEntry);
        }

        private static void SetupOnReload()
        {
            PersistentJobsMod.Persistence.StationIdCarSpawningPersistence.Instance.ClearStationsSpawnedCarsFlagForAllStations();
            WorldStreamingInitLoadingFinished();
            PersistentJobsMod.HarmonyPatches.Save.CarsSaveManager_Load_Patches.GetModSaveData();
        }

        private static void WorldStreamingInitLoadingFinished() {
            DetailedCargoGroups.Initialize();
            EmptyTrainCarTypeDestinations.Initialize();
            ErrorSoundLogHandler.SoundEnabled = true;
            SignalOccupation.Initialize();
        }

        private static void TryLoadPaxJobsCompat()
        {
            if (_isModBroken) return;

            if (!Settings.PaxJobsCompatibility)
            {
                _modEntry.Logger.Log("PaxJobs compatibility disabled in settings - not loading");
                PaxJobsPresent = false;
                return;
            }

            if (PaxJobsPresent)
            {
                _modEntry.Logger.Error("PaxJobs compatibility already loaded!");
                return;
            }

            PaxJobs = UnityModManager.modEntries.FirstOrDefault(m => m.Info.Id == "PassengerJobs" && m.Enabled && m.Active && !m.ErrorOnLoading /*&& m.Version.ToString() == "5.2"*/);
            if ((PaxJobs != null))
            {
                _modEntry.Logger.Log($"{PaxJobs.Info.DisplayName} version {PaxJobs.Version} is present, enabling mod compatibility");
                if (!PaxJobsCompat.Initialize())
                {
                    PaxJobsPresent = false;
                    _modEntry.Logger.Error("Passenger Jobs compatibility failed to load!");
                    PaxJobsCompat.Unload();
                    HarmonyPatches.Save.WorldStreamingInit_Patch.ShowPopupOnPlayerSpawn($"Passenger Jobs mod v{PaxJobs.Version} is present but the Persistent Jobs compatibility layer is not loaded. \nThis is probably due to a recent update (check mod pages or ask on the Altfuture discord). \nThe game should be in a playable state,\n but new passenger jobs may not be generated and cars will remain jobless.");
                }
                else
                {
                    paxJobsPresent = true;
                }
            }
            else
            {
                _modEntry.Logger.Log($"Targeted version of optional mod Passenger Jobs (5.3) is not present, inactive, or has ran into errors, skipping mod compatibility");
            }
        }

        public static void InitializeMPShim(ModEntry modEntry, float _ = 0)
        {
            modEntry.Logger.Log("Trying to load compatibility with MP mod");
            modEntry.OnLateUpdate -= InitializeMPShim;
            MultiplayerShim.Initialize(_modEntry);
        }

        private static void YardMasterInit()
        {
            yardMasterPresent = (UnityModManager.FindMod("SelfShunt")?.Active == true);
            if (yardMasterPresent) _modEntry.Logger.Log("Yard Master mod is present, most job-related features will be disabled");

            ModEntry betterLoadingME = UnityModManager.FindMod("better_loading");
            problematicBetterLoading = (betterLoadingME?.Active == true) && ((betterLoadingME?.Version ?? new Version(0, 0)) <= new Version(0, 0, 2));
            if (problematicBetterLoading) WorldStreamingInit_Patch.ShowPopupOnPlayerSpawn("You are running a version of the \"Immersive Cargo Loading\" \\ (better_loading) mod that has know internal issues that can cause Persistent Jobs to crash. \nIt is recommended that uninstall the mod (or update to a new version if available) to ensure everything works.");
        }

        public static void HandleUnhandledException(Exception e, string location) {
            _isModBroken = true;
            _modEntry.Active = false;

            _modEntry.Logger.Critical($"Deactivating mod PersistentJobsMod due to critical exception in {location}:\n{e}");

            AddMoreInfoToExceptionHelper.AlertPlayerToExceptionAndCompileDataForBugReport(e, location);
        }
    }
}