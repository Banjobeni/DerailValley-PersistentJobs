using System;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using PersistentJobsMod.Persistence;

namespace PersistentJobsMod.HarmonyPatches {
    [HarmonyPatch(typeof(SaveGameManager), "Save")]
    class SaveGameManager_Save_Patch {
        static void Prefix(SaveGameManager __instance) {
            try {
                var spawnBlockSaveData = new JArray(StationIdCarSpawningPersistence.Instance.GetAllSetStationSpawnedCarFlags().Select(id => new JValue(id)));

                var saveData = new JObject(
                    new JProperty(SaveDataConstants.SAVE_DATA_VERSION_KEY, new JValue(Main._modEntry.Version.ToString())),
                    new JProperty($"{SaveDataConstants.SAVE_DATA_SPAWN_BLOCK_KEY}#{SaveDataConstants.TRACK_HASH_SAVE_KEY}", spawnBlockSaveData));

                __instance.data.SetJObject(SaveDataConstants.SAVE_DATA_PRIMARY_KEY, saveData);
            } catch (Exception e) {
                // TODO: what to do if saving fails?
                Main._modEntry.Logger.Warning($"Saving mod data failed with exception:\n{e}");
            }
        }
    }
}