using System.Linq;
using DV.Utils;
using PersistentJobsMod.JobGenerators;
using PersistentJobsMod.Licensing;
using UnityEngine;

namespace PersistentJobsMod.CarSpawningJobGenerators {
    public static class TransportJobWithCarsGenerator {
        public static JobChainControllerWithEmptyHaulGeneration TryGenerateJobChainController(StationController startingStation, bool requirePlayerLicensesCompatible, System.Random random) {
            Main._modEntry.Logger.Log("transport: generating with car spawning");
            var yardTracksOrganizer = YardTracksOrganizer.Instance;

            var possibleCargoGroupsAndTrainCarCountOrNull = CargoGroupsAndCarCountProvider.GetOrNull(startingStation.proceduralJobsRuleset.outputCargoGroups, startingStation.proceduralJobsRuleset, requirePlayerLicensesCompatible, CargoGroupsAndCarCountProvider.CargoGroupLicenseKind.Cargo, random);

            if (possibleCargoGroupsAndTrainCarCountOrNull == null) {
                return null;
            }

            var (availableCargoGroups, carCount) = possibleCargoGroupsAndTrainCarCountOrNull.Value;

            var chosenCargoGroup = random.GetRandomElement(availableCargoGroups);
            Main._modEntry.Logger.Log($"transport: chose cargo group ({string.Join("/", chosenCargoGroup.cargoTypes)}) with {carCount} waggons");

            var cargoCarGroups = CargoCarGroupsRandomizer.GetCargoCarGroups(chosenCargoGroup, carCount, random);

            var trainCarLiveries = cargoCarGroups.SelectMany(ccg => ccg.CarLiveries).ToList();

            var requiredTrainLength = CarSpawner.Instance.GetTotalCarLiveriesLength(trainCarLiveries, true);

            var trackCandidates = startingStation.logicStation.yard.TransferOutTracks.Where(t => t.IsFree()).ToList();

            var tracks = YardTracksOrganizer.Instance.FilterOutTracksWithoutRequiredFreeSpace(trackCandidates, requiredTrainLength);

            if (!tracks.Any()) {
                Debug.LogWarning("[PersistentJobs] transport: Couldn't find startingTrack with enough free space for train!");
                return null;
            }

            Main._modEntry.Logger.Log("transport: choosing starting track");
            var startingTrack = random.GetRandomElement(tracks);

            // choose random destination station that has at least 1 available track
            Main._modEntry.Logger.Log("transport: choosing destination");

            var destinationStation = random.GetRandomPermutation(chosenCargoGroup.stations).FirstOrDefault(ds => yardTracksOrganizer.FilterOutTracksWithoutRequiredFreeSpace(ds.logicStation.yard.TransferInTracks, requiredTrainLength).Any(t => t.IsFree()));

            if (destinationStation == null) {
                Debug.LogWarning("[PersistentJobs] transport: Couldn't find a station with enough free space for train!");
                return null;
            }

            // spawn trainCars
            Main._modEntry.Logger.Log("transport: spawning trainCars");
            var railTrack = SingletonBehaviour<LogicController>.Instance.LogicToRailTrack[startingTrack];
            var orderedTrainCars = CarSpawner.Instance.SpawnCarTypesOnTrackRandomOrientation(trainCarLiveries, railTrack, true, true);
            if (orderedTrainCars == null) {
                Debug.LogWarning("[PersistentJobs] transport: Failed to spawn trainCars!");
                return null;
            }

            var cargoTypes = cargoCarGroups.SelectMany(c => Enumerable.Repeat(c.CargoType, c.CarLiveries.Count)).ToList();
            var jcc = TransportJobGenerator.TryGenerateJobChainController(
                startingStation,
                startingTrack,
                destinationStation,
                orderedTrainCars,
                cargoTypes,
                random,
                true);

            if (jcc == null) {
                Debug.LogWarning("[PersistentJobs] transport: Couldn't generate job chain. Deleting spawned trainCars!");
                SingletonBehaviour<CarSpawner>.Instance.DeleteTrainCars(orderedTrainCars, true);
                return null;
            }

            return jcc;
        }
    }
}