using System.Linq;
using CommandTerminal;
using PersistentJobsMod.Persistence;
using UnityEngine;

namespace PersistentJobsMod {
    public static class Console {
        [RegisterCommand("PJ.ClearStationSpawnFlag", Help = "PersistentJobsMod: Clear the flag for a station such that it may spawn cars again", MaxArgCount = 1, MinArgCount = 1)]
        public static void ClearStationSpawnFlag(CommandArg[] args) {
            var stationId = args[0].String;

            if (StationIdCarSpawningPersistence.Instance.GetHasStationSpawnedCarsFlag(stationId)) {
                StationIdCarSpawningPersistence.Instance.SetHasStationSpawnedCarsFlag(stationId, false);
                Debug.Log($"Cleared station spawn flag of {stationId}.");
            } else {
                Debug.Log("Station spawn flag was not set. See PJ.ListStationSpawnFlag for alist of currently set flags.");
            }
        }

        [RegisterCommand("PJ.ListStationSpawnFlag", Help = "PersistentJobsMod: List stations that have already and will not spawn cars again", MaxArgCount = 0, MinArgCount = 0)]
        public static void ListStationSpawnFlag(CommandArg[] args) {

            var stationIds = StationIdCarSpawningPersistence.Instance.GetAllSetStationSpawnedCarFlags();
            if (!stationIds.Any()) {
                Debug.Log("The list of station spawn flags is empty.");
            } else {
                Debug.Log(string.Join(", ", stationIds));
            }
        }
    }
}