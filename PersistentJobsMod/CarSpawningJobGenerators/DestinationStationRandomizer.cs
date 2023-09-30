using System;
using DV.ThingTypes;
using System.Collections.Generic;
using System.Linq;
using PersistentJobsMod.Extensions;

namespace PersistentJobsMod.CarSpawningJobGenerators {
    public static class DestinationStationRandomizer {
        public static StationController GetRandomStationSupportingCargoTypesAndTrainLengthAndFreeTransferInTrack(List<StationController> destinationStations, float trainLength, List<CargoType> distinctCargoTypes, Random random) {
            Main._modEntry.Logger.Log("choosing destination");

            var randomizedDestinations = random.GetRandomPermutation(destinationStations);

            foreach (var destination in randomizedDestinations) {
                if (DoesStationSupportCargoTypesAndTrainLength(trainLength, distinctCargoTypes, destination)) {
                    if (!destination.logicStation.yard.TransferInTracks.Any(t => t.IsFree() && t.GetTotalUsableTrackLength() > trainLength)) {
                        Main._modEntry.Logger.Log($"Couldn't find a free and long enough track at destination {destination.logicStation.ID}, skipping destination");
                    } else {
                        Main._modEntry.Logger.Log($"Chose {destination.logicStation.ID}");
                        return destination;
                    }
                }
            }

            return null;
        }

        private static bool DoesStationSupportCargoTypesAndTrainLength(float trainLength, List<CargoType> distinctCargoTypes, StationController destination) {
            var warehouseMachines = destination.logicStation.yard.GetWarehouseMachinesThatSupportCargoTypes(distinctCargoTypes);
            if (warehouseMachines.Count == 0) {
                UnityEngine.Debug.LogWarning($"[PersistentJobs] Couldn't find a warehouse machine at destination {destination.logicStation.ID} that supports all cargo types, skipping destination");
                return false;
            }

            var trainLengthSupportingWarehouseMachine = warehouseMachines.FirstOrDefault(wm => wm.WarehouseTrack.GetTotalUsableTrackLength() > trainLength);
            if (trainLengthSupportingWarehouseMachine == null) {
                Main._modEntry.Logger.Log($"Couldn't find a warehouse machine track at destination {destination.logicStation.ID} that is long enough, skipping destination");
                return false;
            }

            return true;
        }
    }
}