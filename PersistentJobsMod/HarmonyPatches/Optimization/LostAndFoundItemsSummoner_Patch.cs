using DV.Storages;
using HarmonyLib;
using System.Linq;

namespace PersistentJobsMod.HarmonyPatches.Optimization
{
    [HarmonyPatch]
    public static class LostAndFoundItemsSummoner_Patch
    {
        [HarmonyPatch(typeof(LostAndFoundItemsSummoner), "OnSummonPressed")]
        [HarmonyPostfix]
        public static void Postfix()
        {
            if (Main.Settings.SuspendFarAwayCars && WorldStreamingInit.IsLoaded)
            {
                var stations = StationController.allStations?.Where(sc => sc.stationRange.IsPlayerInJobGenerationZone(sc.stationRange.PlayerSqrDistanceFromStationCenter))?.Select(sc => sc.stationInfo.YardID);
                stations ??= [];

                foreach (var stationID in stations) if (PersistentJobsMod.Optimization.FarCarOpt.StationIDtoSuspendedCarGUID.TryGetValue(stationID, out var suspended) && suspended?.Any() is true) PersistentJobsMod.Optimization.FarCarOpt.ResumeCarsInStation(stationID);
            }
        }
    }
}
