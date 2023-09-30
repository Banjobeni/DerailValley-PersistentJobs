using System;
using System.Collections.Generic;
using System.Linq;
using DV;
using DV.ThingTypes;
using DV.ThingTypes.TransitionHelpers;
using PersistentJobsMod.Extensions;

namespace PersistentJobsMod.Model {
    public class EmptyTrainCarTypeDestination : IReassignableTrainCarRelationWithMaxTrackLength {
        public StationController Station { get; }
        public double RelationMaxTrainLength { get; }

        public EmptyTrainCarTypeDestination(StationController station, double maxDestinationTrainLength) {
            Station = station;
            RelationMaxTrainLength = maxDestinationTrainLength;
        }
    }

    public static class EmptyTrainCarTypeDestinations {
        private static Dictionary<TrainCarType_v2, List<EmptyTrainCarTypeDestination>> _trainCarType2Destinations;

        public static void Initialize() {
            var station2DestinationStation = StationController.allStations.ToDictionary(s => s, GetEmptyTrainCarTypeDestination);

            _trainCarType2Destinations = StationController.allStations.SelectMany(
                s => s.proceduralJobsRuleset.outputCargoGroups.SelectMany(cg => cg.cargoTypes).Distinct()
                    .SelectMany(cargoType => Globals.G.Types.CargoToLoadableCarTypes[cargoType.ToV2()]).Distinct()
                    .Select(tct => (TrainCarType: tct, Station: s)))
                .GroupBy(tcts => tcts.TrainCarType, tcts => station2DestinationStation[tcts.Station]).ToDictionary(g => g.Key, g => g.ToList());

            Main._modEntry.Logger.Log($"Initialized TrainCarTypeDestinations with a total of {_trainCarType2Destinations.Values.SelectMany(d => d).Count()} TrainCarType-Destination relations");
        }

        private static EmptyTrainCarTypeDestination GetEmptyTrainCarTypeDestination(StationController destination) {
            var storageTracks = destination.logicStation.yard.StorageTracks;
            if (!storageTracks.Any()) {
                throw new InvalidOperationException($"Could not find any storage track at destination station {destination.logicStation.ID}");
            }

            var maxDestinationTrainLength = storageTracks.Select(t => t.GetTotalUsableTrackLength()).Aggregate(Math.Max);

            return new EmptyTrainCarTypeDestination(destination, maxDestinationTrainLength);
        }

        public static IReadOnlyList<EmptyTrainCarTypeDestination> GetStationsThatLoadTrainCarType(TrainCarType_v2 trainCarType) {
            if (_trainCarType2Destinations == null) {
                throw new InvalidOperationException("Not initialized");
            }

            if (_trainCarType2Destinations.TryGetValue(trainCarType, out var result)) {
                return result;
            }

            return new List<EmptyTrainCarTypeDestination>();
        }
    }
}