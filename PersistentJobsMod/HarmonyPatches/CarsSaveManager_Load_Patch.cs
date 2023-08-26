using System;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using PersistentJobsMod.Persistence;

namespace PersistentJobsMod.HarmonyPatches {
    /// <summary>patch CarsSaveManager.Load to ensure CarsSaveManager.TracksHash exists</summary>
    [HarmonyPatch(typeof(CarsSaveManager), "Load")]
    class CarsSaveManager_Load_Patch {
        static void Postfix() {
            try {
                var saveData = SaveGameManager.Instance.data.GetJObject(SaveDataConstants.SAVE_DATA_PRIMARY_KEY);

                if (saveData == null) {
                    Main._modEntry.Logger.Log("Not loading save data: primary object is null.");
                    return;
                }

                var spawnBlockSaveData = (JArray)saveData[$"{SaveDataConstants.SAVE_DATA_SPAWN_BLOCK_KEY}#{SaveDataConstants.TRACK_HASH_SAVE_KEY}"];
                if (spawnBlockSaveData == null) {
                    Main._modEntry.Logger.Log("Not loading spawn block list: data is null.");
                } else {
                    var alreadySpawnedCarsStatioinIds = spawnBlockSaveData.Select(id => (string)id).ToList();
                    StationIdCarSpawningPersistence.Instance.HandleSavegameLoadedSpawnedStationIds(alreadySpawnedCarsStatioinIds);
                    Main._modEntry.Logger.Log($"Loaded station spawn block list: [ {string.Join(", ", alreadySpawnedCarsStatioinIds)} ]");
                }
            } catch (Exception e) {
                Main._modEntry.Logger.Warning($"Loading mod data failed with exception:\n{e}");
            }
        }
    }
}