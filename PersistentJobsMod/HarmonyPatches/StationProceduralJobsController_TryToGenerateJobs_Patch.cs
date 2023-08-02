using Harmony12;

namespace PersistentJobsMod.HarmonyPatches {
    [HarmonyPatch(typeof(StationProceduralJobsController), "TryToGenerateJobs")]
    class StationProceduralJobsController_TryToGenerateJobs_Patch {
        static bool Prefix(StationProceduralJobsController __instance) {
            if (Main.modEntry.Active) {
                return !Main.StationIdSpawnBlockList.Contains(__instance.stationController.logicStation.ID);
            }
            return true;
        }

        static void Postfix(StationProceduralJobsController __instance) {
            string stationId = __instance.stationController.logicStation.ID;
            if (!Main.StationIdSpawnBlockList.Contains(stationId)) {
                Main.StationIdSpawnBlockList.Add(stationId);
            }
        }
    }
}