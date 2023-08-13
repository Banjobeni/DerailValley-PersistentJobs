using System;
using DV.ThingTypes;
using System.Collections.Generic;
using System.Linq;

namespace PersistentJobsMod.CarSpawningJobGenerators {
    public static class DestinationStationRandomizer {
        public static StationController GetRandomStationSupportingCargoTypesAndTrainLengthAndFreeTransferInTrack(List<StationController> destinationStations, YardTracksOrganizer yardTracksOrganizer, float trainLength, List<CargoType> distinctCargoTypes, Random random) {
            Main._modEntry.Logger.Log("load: choosing destination");

            var randomizedDestinations = random.GetRandomPermutation(destinationStations);

            foreach (var destination in randomizedDestinations) {
                if (DoesStationSupportCargoTypesAndTrainLength(yardTracksOrganizer, trainLength, distinctCargoTypes, destination)) {
                    if (!destination.logicStation.yard.TransferInTracks.Any(t => t.IsFree() && t.length > trainLength)) {
                        UnityEngine.Debug.LogWarning($"[PersistentJobs] load: Couldn't find a free and long enough trackat  destination {destination.logicStation.ID}, skipping destination");
                    } else {
                        return destination;
                    }
                }
            }

            return null;
        }

        private static bool DoesStationSupportCargoTypesAndTrainLength(YardTracksOrganizer yardTracksOrganizer, float trainLength, List<CargoType> distinctCargoTypes, StationController destination) {
            var warehouseMachines = destination.logicStation.yard.GetWarehouseMachinesThatSupportCargoTypes(distinctCargoTypes);
            if (warehouseMachines.Count == 0) {
                UnityEngine.Debug.LogWarning($"[PersistentJobs] load: Couldn't find a warehouse machine at destination {destination.logicStation.ID} that supports all cargo types, skipping destination");
                return false;
            }

            var trainLengthSupportingWarehouseMachine = warehouseMachines.FirstOrDefault(wm => wm.WarehouseTrack.length > trainLength);
            if (trainLengthSupportingWarehouseMachine == null) {
                UnityEngine.Debug.LogWarning($"[PersistentJobs] load: Couldn't find a warehouse machine track at destination {destination.logicStation.ID} that is long enough, skipping destination");
                return false;
            }

            return true;
        }
    }
}