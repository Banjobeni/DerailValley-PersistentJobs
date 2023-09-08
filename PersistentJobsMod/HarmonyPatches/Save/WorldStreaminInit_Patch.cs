using HarmonyLib;
using PersistentJobsMod.Persistence;

namespace PersistentJobsMod.HarmonyPatches.Save {
    [HarmonyPatch]
    public static class WorldStreaminInit_Patch {
        [HarmonyPatch(typeof(WorldStreamingInit), "LoadingRoutine")]
        [HarmonyPrefix]
        public static void LoadingRoutine_Prefix() {
            Main._modEntry.Logger.Log("WorldStreamingInit.LoadingRoutine prefix: Cleared station spawn flags");
            StationIdCarSpawningPersistence.Instance.ClearStationsSpawnedCarsFlagForAllStations();
        }
    }
}