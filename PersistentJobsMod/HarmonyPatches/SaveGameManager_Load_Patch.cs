using System;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;

namespace PersistentJobsMod.HarmonyPatches {
    /// <summary>patch CarsSaveManager.Load to ensure CarsSaveManager.TracksHash exists</summary>
    [HarmonyPatch(typeof(CarsSaveManager), "Load")]
    class SaveGameManager_Load_Patch {
        static void Postfix() {
            try {
                JObject saveData = SaveGameManager.Instance.data.GetJObject(SaveDataConstants.SAVE_DATA_PRIMARY_KEY);

                if (saveData == null) {
                    Main.modEntry.Logger.Log("Not loading save data: primary object is null.");
                    return;
                }

                JArray spawnBlockSaveData = (JArray)saveData[$"{SaveDataConstants.SAVE_DATA_SPAWN_BLOCK_KEY}#{SaveDataConstants.TRACK_HASH_SAVE_KEY}"];
                if (spawnBlockSaveData == null) {
                    Main.modEntry.Logger.Log("Not loading spawn block list: data is null.");
                } else {
                    Main.StationIdSpawnBlockList = spawnBlockSaveData.Select(id => (string)id).ToList();
                    Main.modEntry.Logger.Log($"Loaded station spawn block list: [ {string.Join(", ", Main.StationIdSpawnBlockList)} ]");
                }

                JArray passengerBlockSaveData = (JArray)saveData[$"{SaveDataConstants.SAVE_DATA_PASSENGER_BLOCK_KEY}#{SaveDataConstants.TRACK_HASH_SAVE_KEY}"];
                if (passengerBlockSaveData == null) {
                    Main.modEntry.Logger.Log("Not loading passenger spawn block list: data is null.");
                } else {
                    Main.StationIdPassengerBlockList = passengerBlockSaveData.Select(id => (string)id).ToList();
                    Main.modEntry.Logger.Log($"Loaded passenger spawn block list: [ {string.Join(", ", Main.StationIdPassengerBlockList)} ]");
                }
            } catch (Exception e) {
                // TODO: what to do if loading fails?
                Main.modEntry.Logger.Warning(string.Format("Loading mod data failed with exception:\n{0}", e));
            }
        }
    }
}