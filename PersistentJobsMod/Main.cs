﻿using System;
using System.IO;
using System.Reflection;
using UnityModManagerNet;
using HarmonyLib;
using PersistentJobsMod.Model;
using UnityEngine;
using MessageBox;

namespace PersistentJobsMod {
    public static class Main {
        // ReSharper disable InconsistentNaming
        public static UnityModManager.ModEntry _modEntry;
        public static float _initialDistanceRegular = 0f;
        public static float _initialDistanceAnyJobTaken = 0f;
        // ReSharper restore InconsistentNaming

        // ReSharper disable once RedundantDefaultMemberInitializer
        private static bool _isModBroken = false;

        public static float DVJobDestroyDistanceRegular {
            get { return _initialDistanceRegular; }
        }

        public static Settings Settings { get; private set; }

        public static void Load(UnityModManager.ModEntry modEntry) {
            _modEntry = modEntry;

            var harmony = new Harmony(modEntry.Info.Id);
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            Settings = UnityModManager.ModSettings.Load<Settings>(modEntry);

            modEntry.OnToggle = OnToggle;
            modEntry.OnGUI = OnGUI;
            modEntry.OnSaveGUI = OnSaveGUI;

            WorldStreamingInit.LoadingFinished += WorldStreamingInitLoadingFinished;
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

            var logMessage = $"Exception thrown at {location}:\n{e}";
            Debug.LogError(logMessage);

            var exceptionLogFilename = $"PersistentJobsMod_Exception_{DateTime.Now.ToString("O").Replace(':', '.')}.log";
            var logExceptionFilepath = Path.Combine(Application.persistentDataPath, exceptionLogFilename);
            File.WriteAllText(logExceptionFilepath, logMessage);

            _modEntry.Logger.Critical($"Deactivating mod PersistentJobsMod due to critical exception in {location}:\n{e}");

            PopupAPI.ShowOk($"Persistent Jobs mod encountered a critical failure. The mod will stay inactive until the game is restarted.\n\nSee {exceptionLogFilename} for details.");
        }
    }
}