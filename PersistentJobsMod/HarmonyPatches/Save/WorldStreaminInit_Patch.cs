using HarmonyLib;
using MessageBox;
using PersistentJobsMod.Persistence;
using System.Collections;
using System.Collections.Generic;

namespace PersistentJobsMod.HarmonyPatches.Save {
    [HarmonyPatch]
    public static class WorldStreaminInit_Patch {
        [HarmonyPatch(typeof(WorldStreamingInit), "LoadingRoutine")]
        [HarmonyPrefix]
        public static void LoadingRoutine_Prefix() {
            Main._modEntry.Logger.Log("WorldStreamingInit.LoadingRoutine prefix: Cleared station spawn flags");
            StationIdCarSpawningPersistence.Instance.ClearStationsSpawnedCarsFlagForAllStations();
        }

        [HarmonyPatch(typeof(WorldStreamingInit), "LoadingRoutine")]
        [HarmonyPostfix]
        public static IEnumerator LoadingRoutine_Postfix(IEnumerator __result)
        {   
            while (__result.MoveNext()) yield return __result.Current;

            foreach (var popup in stringsToShow)
            {
                yield return WaitFor.Seconds(2f);
                PopupAPI.ShowOk(popup);
            }           
        }

        public static void ShowPopupOnPlayerSpawn(string message)
        {
            stringsToShow.Add(message);
            Main._modEntry.Logger.Log($"Message added to queue: \"{message}\" ");
        }

        private static readonly List<string> stringsToShow = new();
    }
}