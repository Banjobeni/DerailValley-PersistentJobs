using DV.Utils;
using HarmonyLib;
using MessageBox;
using PersistentJobsMod.Persistence;
using System.Collections;

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
            if (Main.PaxJobsPresent)
            {
                while (__result.MoveNext()) yield return __result.Current;

                yield return WaitFor.Seconds(2f);

                if ((Main.PaxJobs != null) && !Main.PaxJobsPresent)
                {
                    Main._modEntry.Logger.Log("PaxJobsCompat not active, showing message to player");
                    PopupAPI.ShowOk($"Passenger Jobs mod v{Main.PaxJobs.Version} is present but the Persistent Jobs compatibility layer is not loaded. \nThis is probably due to a recent update (check mod pages or ask on the Altfuture discord). \nThe game should be in a playable state, but new passenger jobs may not be generated \nand cars will remain jobless.");
                }
            }
        }
    }
}