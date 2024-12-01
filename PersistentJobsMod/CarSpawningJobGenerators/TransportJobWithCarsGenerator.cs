using System.Linq;
using DV.Utils;
using PersistentJobsMod.Extensions;
using PersistentJobsMod.JobGenerators;
using PersistentJobsMod.Licensing;
using UnityEngine;

namespace PersistentJobsMod.CarSpawningJobGenerators {
    public static class TransportJobWithCarsGenerator {
        public static JobChainController TryGenerateJobChainController(StationController startingStation, bool requirePlayerLicensesCompatible, System.Random random) {
            Main._modEntry.Logger.Log("transport: generating with car spawning");

            var possibleCargoGroupsAndTrainCarCountOrNull = CargoGroupsAndCarCountProvider.GetOrNull(startingStation.proceduralJobsRuleset.outputCargoGroups, startingStation.proceduralJobsRuleset, requirePlayerLicensesCompatible, CargoGroupsAndCarCountProvider.CargoGroupLicenseKind.Cargo, random);

            if (possibleCargoGroupsAndTrainCarCountOrNull == null) {
                return null;
            }

            var (availableCargoGroups, carCount) = possibleCargoGroupsAndTrainCarCountOrNull.Value;

            var chosenCargoGroup = random.GetRandomElement(availableCargoGroups);
            Main._modEntry.Logger.Log($"transport: chose cargo group ({string.Join("/", chosenCargoGroup.cargoTypes)}) with {carCount} waggons");

            var carSpawnGroups = CarSpawnGroupsRandomizer.GetCarSpawnGroups(chosenCargoGroup.cargoTypes, carCount, random);

            var trainCarLiveries = carSpawnGroups.SelectMany(csg => csg.CargoTypesAndLiveries.Select(tuple => tuple.CarLivery)).ToList();

            var totalTrainLength = CarSpawner.Instance.GetTotalCarLiveriesLength(trainCarLiveries, true);

            var trackCandidates = startingStation.logicStation.yard.TransferOutTracks.Where(t => t.IsFree()).ToList();

            var tracks = YardTracksOrganizer.Instance.FilterOutTracksWithoutRequiredFreeSpace(trackCandidates, totalTrainLength);

            if (!tracks.Any()) {
                Main._modEntry.Logger.Log("transport: Couldn't find startingTrack with enough free space for train!");
                return null;
            }

            Main._modEntry.Logger.Log("transport: choosing starting track");
            var startingTrack = random.GetRandomElement(tracks);

            // choose random destination station that has at least 1 available track
            Main._modEntry.Logger.Log("transport: choosing destination");

            var cargoTypesSequence = carSpawnGroups.SelectMany(csg => csg.CargoTypesAndLiveries.Select(tuple => tuple.CargoType)).ToList();
            var destinationStation = DestinationStationRandomizer.GetRandomStationSupportingCargoTypesAndTrainLengthAndFreeTransferInTrack(chosenCargoGroup.stations, totalTrainLength, cargoTypesSequence.Distinct().ToList(), random);

            if (destinationStation == null) {
                Main._modEntry.Logger.Log("transport: Couldn't find a station with enough free space for train!");
                return null;
            }

            // spawn trainCars
            Main._modEntry.Logger.Log("transport: spawning trainCars");
            var railTrack = SingletonBehaviour<LogicController>.Instance.LogicToRailTrack[startingTrack];
            var orderedTrainCars = CarSpawner.Instance.SpawnCarTypesOnTrackRandomOrientation(trainCarLiveries, railTrack, true, applyHandbrakeOnLastCars: true);
            if (orderedTrainCars == null) {
                Main._modEntry.Logger.Log("transport: Failed to spawn trainCars!");
                return null;
            }

            var jobChainController = TransportJobGenerator.TryGenerateJobChainController(
                startingStation,
                startingTrack,
                destinationStation,
                orderedTrainCars,
                cargoTypesSequence,
                random,
                true);

            if (jobChainController == null) {
                Debug.LogWarning("[PersistentJobs] transport: Couldn't generate job chain. Deleting spawned trainCars!");
                SingletonBehaviour<CarSpawner>.Instance.DeleteTrainCars(orderedTrainCars, true);
                return null;
            }

            return jobChainController;
        }
    }
}