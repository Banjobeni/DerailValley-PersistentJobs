using System;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;

namespace PersistentJobsMod.HarmonyPatches {
    [HarmonyPatch(typeof(SaveGameManager), "Save")]
    class SaveGameManager_Save_Patch {
        static void Prefix(SaveGameManager __instance) {
            try {
                JArray spawnBlockSaveData = new JArray(from id in Main.StationIdSpawnBlockList select new JValue(id));
                JArray passengerBlockSaveData = new JArray(from id in Main.StationIdPassengerBlockList select new JValue(id));

                JObject saveData = new JObject(
                    new JProperty(SaveDataConstants.SAVE_DATA_VERSION_KEY, new JValue(Main.modEntry.Version.ToString())),
                    new JProperty($"{SaveDataConstants.SAVE_DATA_SPAWN_BLOCK_KEY}#{SaveDataConstants.TRACK_HASH_SAVE_KEY}", spawnBlockSaveData),
                    new JProperty($"{SaveDataConstants.SAVE_DATA_PASSENGER_BLOCK_KEY}#{SaveDataConstants.TRACK_HASH_SAVE_KEY}", passengerBlockSaveData));

                __instance.data.SetJObject(SaveDataConstants.SAVE_DATA_PRIMARY_KEY, saveData);
            } catch (Exception e) {
                // TODO: what to do if saving fails?
                Main.modEntry.Logger.Warning(string.Format("Saving mod data failed with exception:\n{0}", e));
            }
        }
    }
}