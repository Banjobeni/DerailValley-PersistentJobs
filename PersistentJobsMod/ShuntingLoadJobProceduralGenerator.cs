using System;
using System.Collections.Generic;
using System.Linq;
using DV;
using UnityEngine;
using DV.Logic.Job;
using DV.ThingTypes;
using DV.ThingTypes.TransitionHelpers;
using DV.Utils;
using JetBrains.Annotations;
using Random = System.Random;

namespace PersistentJobsMod {
    static class ShuntingLoadJobProceduralGenerator {
        private class CargoLiveryCar {
            public CargoType CargoType { get; }
            public TrainCarLivery TrainCarLivery { get; }

            public CargoLiveryCar(CargoType cargoType, TrainCarLivery trainCarLivery) {
                CargoType = cargoType;
                TrainCarLivery = trainCarLivery;
            }
        }

        public static JobChainControllerWithEmptyHaulGeneration GenerateShuntingLoadJobWithCarSpawning(StationController startingStation, bool forceLicenseReqs, System.Random rng) {
            Debug.Log("[PersistentJobs] load: generating with car spawning");
            var yardTracksOrganizer = YardTracksOrganizer.Instance;

            var possibleCargoGroupsAndTrainCarCountOrNull = TryGetPossibleCargoGroupsAndTrainCarCount(startingStation, forceLicenseReqs, rng);

            if (possibleCargoGroupsAndTrainCarCountOrNull == null) {
                return null;
            }

            var (availableCargoGroups, carCount) = possibleCargoGroupsAndTrainCarCountOrNull.Value;

            if (availableCargoGroups.Count == 0) {
                Debug.LogWarning("[PersistentJobs] load: no available cargo groups");
                return null;
            }

            var chosenCargoGroup = rng.GetRandomElement(availableCargoGroups);

            // choose cargo & trainCar types
            Debug.Log("[PersistentJobs] load: choosing cargo & trainCar types");
            var cargoLiveryCars = new List<CargoLiveryCar>();
            for (var i = 0; i < carCount; i++) {
                var chosenCargoType = rng.GetRandomElement(chosenCargoGroup.cargoTypes);
                var availableTrainCarTypes = Globals.G.Types.CargoToLoadableCarTypes[chosenCargoType.ToV2()];
                var chosenTrainCarType = rng.GetRandomElement(availableTrainCarTypes);
                var chosenTrainCarLivery = rng.GetRandomElement(chosenTrainCarType.liveries);

                cargoLiveryCars.Add(new CargoLiveryCar(chosenCargoType, chosenTrainCarLivery));
            }

            // choose starting tracks
            var maxTracksCount = GetMaxTracksCount(startingStation, carCount, rng);
            Debug.Log($"[PersistentJobs] load: choosing {maxTracksCount} starting tracks at most");

            var startingTracksWithCargoLiveryCars = TryFindStartingTracksOrNull(startingStation, yardTracksOrganizer, cargoLiveryCars, rng, maxTracksCount);
            if (startingTracksWithCargoLiveryCars == null) {
                Debug.LogWarning("[PersistentJobs] load: Couldn't find startingTrack with enough free space for train!");
                return null;
            }

            var cargoDestinationStations = chosenCargoGroup.stations;

            // choose random destination station that has at least 1 available track
            var destinationStation = ChooseDestinationStationHavingFreeTrack(cargoDestinationStations, cargoLiveryCars, yardTracksOrganizer, rng);
            if (destinationStation == null) {
                Debug.LogWarning("Couldn't find a station with enough free space for train!");
                return null;
            }

            // spawn trainCars & form carsPerStartingTrack
            Debug.Log("[PersistentJobs] load: spawning trainCars");
            var orderedTrainCars = new List<TrainCar>();
            var carsPerStartingTrack = new List<CarsPerTrack>();

            for (var i = 0; i < startingTracksWithCargoLiveryCars.Count; i++) {
                var startingTrack = startingTracksWithCargoLiveryCars[i].Track;
                var trackTrainCarLiveries = startingTracksWithCargoLiveryCars[i].CargoLiveryCars.Select(clc => clc.TrainCarLivery).ToList();

                Debug.Log($"[PersistentJobs] load: spawning car group {i + 1}/{startingTracksWithCargoLiveryCars.Count} on track {startingTrack.ID}");

                var railTrack = SingletonBehaviour<LogicController>.Instance.LogicToRailTrack[startingTrack];
                var carOrientations = Enumerable.Range(0, trackTrainCarLiveries.Count).Select(_ => rng.Next(2) > 0).ToList();

                var spawnedCars = CarSpawner.Instance.SpawnCarTypesOnTrack(
                    trackTrainCarLiveries,
                    carOrientations,
                    railTrack,
                    true,
                    true,
                    0.0,
                    false,
                    false);

                if (spawnedCars == null) {
                    Debug.LogWarning("[PersistentJobs] load: Failed to spawn some trainCars!");
                    SingletonBehaviour<CarSpawner>.Instance.DeleteTrainCars(orderedTrainCars, true);
                    return null;
                }
                orderedTrainCars.AddRange(spawnedCars);
                carsPerStartingTrack.Add(
                    new CarsPerTrack(startingTrack, (from car in spawnedCars select car.logicCar).ToList()));
            }

            var jcc = GenerateShuntingLoadJobWithExistingCars(
                startingStation,
                carsPerStartingTrack,
                destinationStation,
                orderedTrainCars,
                cargoLiveryCars.Select(clc => clc.CargoType).ToList(),
                rng,
                true);

            if (jcc == null) {
                Debug.LogWarning("[PersistentJobs] load: Couldn't generate job chain. Deleting spawned trainCars!");
                SingletonBehaviour<CarSpawner>.Instance.DeleteTrainCars(orderedTrainCars, true);
                return null;
            }

            return jcc;
        }

        private static StationController ChooseDestinationStationHavingFreeTrack(List<StationController> destinationStations, List<CargoLiveryCar> carCargoTypes, YardTracksOrganizer yardTracksOrganizer, Random rng) {
            Debug.Log("[PersistentJobs] load: choosing destination");
            var approxTrainLength = CarSpawner.Instance.GetTotalCarLiveriesLength(carCargoTypes.Select(cct => cct.TrainCarLivery).ToList(), true);

            var availableDestinations = destinationStations.ToList();

            while (availableDestinations.Count > 0) {
                var station = Utilities.GetRandomFromEnumerable(availableDestinations, rng);

                availableDestinations.Remove(station);

                var destinationTrack = Utilities.GetTrackThatHasEnoughFreeSpace(yardTracksOrganizer, yardTracksOrganizer.FilterOutOccupiedTracks(station.logicStation.yard.TransferInTracks), approxTrainLength);

                if (destinationTrack != null) {
                    return station;
                }
            }

            return null;
        }

        [CanBeNull]
        private static List<(Track Track, List<CargoLiveryCar> CargoLiveryCars)> TryFindStartingTracksOrNull(StationController startingStation, YardTracksOrganizer yardTracksOrganizer, List<CargoLiveryCar> cargoLiveryCars, Random rng, int maxTracksCount) {
            for (var currentTracksCount = maxTracksCount; currentTracksCount >= 1; currentTracksCount -= 1) {
                Debug.Log($"[PersistentJobs] load: trying to find {currentTracksCount} starting tracks");
                var startingTracks = TryFindXStartingTracks(startingStation, yardTracksOrganizer, cargoLiveryCars, rng, currentTracksCount);
                if (startingTracks != null) {
                    return startingTracks;
                }
            }

            return null;
        }

        [CanBeNull]
        private static List<(Track, List<CargoLiveryCar>)> TryFindXStartingTracks(StationController startingStation, YardTracksOrganizer yardTracksOrganizer, List<CargoLiveryCar> cargoLiveryCars, Random rng, int tracksCount) {
            var trainCarsCount = cargoLiveryCars.Count;
            var countCarsPerTrainset = trainCarsCount / tracksCount;
            var countTrainsetsWithExtraCar = trainCarsCount % tracksCount;

            var result = new List<(Track Track, List<CargoLiveryCar> CargoLiveryCars)>();
            for (var trackIndex = 0; trackIndex < tracksCount; trackIndex++) {
                var rangeStart = trackIndex * countCarsPerTrainset + Math.Min(trackIndex, countTrainsetsWithExtraCar);
                var rangeCount = trackIndex < countTrainsetsWithExtraCar ? countCarsPerTrainset + 1 : countCarsPerTrainset;

                var trackCargoLiveryCars = cargoLiveryCars.GetRange(rangeStart, rangeCount);
                var requiredTrackLength = CarSpawner.Instance.GetTotalCarLiveriesLength(trackCargoLiveryCars.Select(clc => clc.TrainCarLivery).ToList(), true);

                var alreadyUsedTracks = result.Select(r => r.Track).ToList();

                var availableTracks = startingStation.logicStation.yard.StorageTracks.Except(alreadyUsedTracks).ToList();

                var track = Utilities.GetTrackThatHasEnoughFreeSpace(yardTracksOrganizer, availableTracks, requiredTrackLength);
                if (track == null) {
                    Debug.Log($"[PersistentJobs] load: could not find another track having {requiredTrackLength}m of free space");
                    return null;
                }

                Debug.Log($"[PersistentJobs] load: found {trackIndex + 1}/{tracksCount} track {track.ID} having {requiredTrackLength}m of free space");

                result.Add((track, trackCargoLiveryCars));
            }
            return result;
        }

        private static int GetMaxTracksCount(StationController startingStation, int carCount, Random rng) {
            var maxCountTracks = startingStation.proceduralJobsRuleset.maxShuntingStorageTracks;
            var countTracks = rng.Next(1, maxCountTracks + 1);

            // bias toward less than max number of tracks for shorter trains
            if (carCount < 2 * maxCountTracks) {
                countTracks = rng.Next(0, Mathf.FloorToInt(1.5f * maxCountTracks)) % maxCountTracks + 1;
            }
            return countTracks;
        }

        private static (List<CargoGroup> availableCargoGroups, int countTrainCars)? TryGetPossibleCargoGroupsAndTrainCarCount(StationController startingStation, bool forceLicenseReqs, Random rng) {
            var stationOutputCargoGroups = startingStation.proceduralJobsRuleset.outputCargoGroups;
            var trainCarCount = rng.Next(startingStation.proceduralJobsRuleset.minCarsPerJob, startingStation.proceduralJobsRuleset.maxCarsPerJob);

            if (!forceLicenseReqs) {
                return (stationOutputCargoGroups, trainCarCount);
            }

            Debug.Log("[PersistentJobs] load: forcing license requirements");

            if (!LicenseManager.Instance.IsJobLicenseAcquired(JobLicenses.Shunting.ToV2())) {
                Debug.LogError("Trying to generate a ShuntingLoad job with forceLicenseReqs=true should never happen if player doesn't have Shunting license!");
                return null;
            }

            var licensedCargoGroups = stationOutputCargoGroups.Where(cg => LicenseManager.Instance.IsLicensedForJob(JobLicenseType_v2.ToV2List(cg.CargoRequiredLicenses))).ToList();
            var licensedTrainCarCount = Math.Min(trainCarCount, LicenseManager.Instance.GetMaxNumberOfCarsPerJobWithAcquiredJobLicenses());

            return (licensedCargoGroups, licensedTrainCarCount);
        }

        public static JobChainControllerWithEmptyHaulGeneration GenerateShuntingLoadJobWithExistingCars(
                StationController startingStation,
                List<CarsPerTrack> carsPerStartingTrack,
                StationController destStation,
                List<TrainCar> trainCars,
                List<CargoType> transportedCargoPerCar,
                System.Random rng,
                bool forceCorrectCargoStateOnCars = false) {
            Debug.Log("[PersistentJobs] load: generating with pre-spawned cars");
            var yto = YardTracksOrganizer.Instance;
            var approxTrainLength = CarSpawner.Instance.GetTotalTrainCarsLength(trainCars, true);

            // choose warehosue machine
            Debug.Log("[PersistentJobs] load: choosing warehouse machine");
            var supportedWMCs = startingStation.warehouseMachineControllers
                .Where(wm => wm.supportedCargoTypes.Intersect(transportedCargoPerCar).Count() > 0)
                .ToList();
            if (supportedWMCs.Count == 0) {
                Debug.LogWarning(string.Format(
                    "[PersistentJobs] load: Could not create ChainJob[{0}]: {1} - {2}. Found no supported WarehouseMachine!",
                    JobType.ShuntingLoad,
                    startingStation.logicStation.ID,
                    destStation.logicStation.ID
                ));
                return null;
            }
            var loadMachine = Utilities.GetRandomFromEnumerable(supportedWMCs, rng).warehouseMachine;

            // choose destination track
            Debug.Log("[PersistentJobs] load: choosing destination track");
            var destinationTrack = Utilities.GetTrackThatHasEnoughFreeSpace(
                yto,
                yto.FilterOutOccupiedTracks(startingStation.logicStation.yard.TransferOutTracks),
                approxTrainLength
            );
            if (destinationTrack == null) {
                destinationTrack = Utilities.GetTrackThatHasEnoughFreeSpace(
                    yto,
                    startingStation.logicStation.yard.TransferOutTracks,
                    approxTrainLength
                );
            }
            if (destinationTrack == null) {
                Debug.LogWarning(string.Format(
                    "[PersistentJobs] load: Could not create ChainJob[{0}]: {1} - {2}. " +
                    "Found no TransferOutTrack with enough free space!",
                    JobType.ShuntingLoad,
                    startingStation.logicStation.ID,
                    destStation.logicStation.ID
                ));
                return null;
            }

            Debug.Log("[PersistentJobs] load: calculating time/wage/licenses");
            var transportedCarTypes = (from tc in trainCars select tc.carType)
                .ToList<TrainCarType>();
            float bonusTimeLimit;
            float initialWage;
            Utilities.CalculateShuntingBonusTimeLimitAndWage(
                JobType.ShuntingLoad,
                carsPerStartingTrack.Count,
                transportedCarTypes,
                transportedCargoPerCar,
                out bonusTimeLimit,
                out initialWage
            );
            var requiredLicenses = JobLicenseType_v2.ListToFlags(LicenseManager.Instance.GetRequiredLicensesForJobType(JobType.ShuntingLoad))
                | JobLicenseType_v2.ListToFlags(LicenseManager.Instance.GetRequiredLicensesForCargoTypes(transportedCargoPerCar))
                | (LicenseManager.Instance.GetRequiredLicenseForNumberOfTransportedCars(trainCars.Count)?.v1 ?? JobLicenses.Basic);
            return GenerateShuntingLoadChainController(
                startingStation,
                carsPerStartingTrack,
                loadMachine,
                destStation,
                destinationTrack,
                trainCars,
                transportedCargoPerCar,
                Enumerable.Repeat(1.0f, trainCars.Count).ToList(),
                forceCorrectCargoStateOnCars,
                bonusTimeLimit,
                initialWage,
                requiredLicenses
            );
        }

        private static JobChainControllerWithEmptyHaulGeneration GenerateShuntingLoadChainController(StationController startingStation,
            List<CarsPerTrack> carsPerStartingTrack,
            WarehouseMachine loadMachine,
            StationController destStation,
            Track destinationTrack,
            List<TrainCar> orderedTrainCars,
            List<CargoType> orderedCargoTypes,
            List<float> orderedCargoAmounts,
            bool forceCorrectCargoStateOnCars,
            float bonusTimeLimit,
            float initialWage,
            JobLicenses requiredLicenses) {
            Debug.Log(string.Format(
                "[PersistentJobs] load: attempting to generate ChainJob[{0}]: {1} - {2}",
                JobType.ShuntingLoad,
                startingStation.logicStation.ID,
                destStation.logicStation.ID
            ));
            var gameObject = new GameObject(string.Format(
                "ChainJob[{0}]: {1} - {2}",
                JobType.ShuntingLoad,
                startingStation.logicStation.ID,
                destStation.logicStation.ID
            ));
            gameObject.transform.SetParent(startingStation.transform);
            var jobChainController
                = new JobChainControllerWithEmptyHaulGeneration(gameObject);
            var chainData = new StationsChainData(
                startingStation.stationInfo.YardID,
                destStation.stationInfo.YardID
            );
            jobChainController.trainCarsForJobChain = orderedTrainCars;
            var cargoTypeToTrainCarAndAmount
                = new Dictionary<CargoType, List<(TrainCar, float)>>();
            for (var i = 0; i < orderedTrainCars.Count; i++) {
                if (!cargoTypeToTrainCarAndAmount.ContainsKey(orderedCargoTypes[i])) {
                    cargoTypeToTrainCarAndAmount[orderedCargoTypes[i]] = new List<(TrainCar, float)>();
                }
                cargoTypeToTrainCarAndAmount[orderedCargoTypes[i]].Add((orderedTrainCars[i], orderedCargoAmounts[i]));
            }
            var loadData = cargoTypeToTrainCarAndAmount.Select(
                kvPair => new CarsPerCargoType(
                    kvPair.Key,
                    kvPair.Value.Select(t => t.Item1.logicCar).ToList(),
                    kvPair.Value.Aggregate(0.0f, (sum, t) => sum + t.Item2)
                )).ToList();
            var staticShuntingLoadJobDefinition
                = gameObject.AddComponent<StaticShuntingLoadJobDefinition>();
            staticShuntingLoadJobDefinition.PopulateBaseJobDefinition(
                startingStation.logicStation,
                bonusTimeLimit,
                initialWage,
                chainData,
                requiredLicenses
            );
            staticShuntingLoadJobDefinition.carsPerStartingTrack = carsPerStartingTrack;
            staticShuntingLoadJobDefinition.destinationTrack = loadMachine.WarehouseTrack;
            staticShuntingLoadJobDefinition.loadData = loadData;
            staticShuntingLoadJobDefinition.loadMachine = loadMachine;
            staticShuntingLoadJobDefinition.forceCorrectCargoStateOnCars = forceCorrectCargoStateOnCars;
            jobChainController.AddJobDefinitionToChain(staticShuntingLoadJobDefinition);
            return jobChainController;
        }

        public static List<(StationController, List<CarsPerTrack>, StationController, List<TrainCar>, List<CargoType>)>
            ComputeJobInfosFromCargoGroupsPerTrainCarSetPerStation(Dictionary<StationController, List<(List<TrainCar>, List<CargoGroup>)>> cgsPerTcsPerSc,
                System.Random rng) {
            var maxCarsLicensed = LicenseManager.Instance.GetMaxNumberOfCarsPerJobWithAcquiredJobLicenses();
            var jobsToGenerate
                = new List<(StationController, List<CarsPerTrack>, StationController, List<TrainCar>, List<CargoType>)>();

            foreach (var startingStation in cgsPerTcsPerSc.Keys) {
                var hasFulfilledLicenseReqs = false;
                var cgsPerTcs = cgsPerTcsPerSc[startingStation];

                while (cgsPerTcs.Count > 0) {
                    var trainCarsToLoad = new List<TrainCar>();
                    IEnumerable<CargoGroup> cargoGroupsToUse = new HashSet<CargoGroup>();
                    var countTracks = rng.Next(1, startingStation.proceduralJobsRuleset.maxShuntingStorageTracks + 1);
                    var triesLeft = cgsPerTcs.Count;
                    var isFulfillingLicenseReqs = false;

                    for (; countTracks > 0 && triesLeft > 0; triesLeft--) {
                        (var trainCarsToAdd, var cargoGroupsForTrainCars)
                            = cgsPerTcs[cgsPerTcs.Count - 1];

                        var licensedCargoGroups
                            = (from cg in cargoGroupsForTrainCars
                               where LicenseManager.Instance.IsLicensedForJob(JobLicenseType_v2.ToV2List(cg.CargoRequiredLicenses))
                               select cg).ToList();

                        // determine which cargoGroups to choose from
                        if (trainCarsToLoad.Count == 0) {
                            if (!hasFulfilledLicenseReqs &&
                                licensedCargoGroups.Count > 0 &&
                                trainCarsToAdd.Count <= maxCarsLicensed) {
                                isFulfillingLicenseReqs = true;
                            }
                        } else if ((isFulfillingLicenseReqs &&
                                (licensedCargoGroups.Count == 0 ||
                                    (cargoGroupsToUse.Count() > 0 &&
                                        !cargoGroupsToUse.Intersect(licensedCargoGroups).Any()) ||
                                    trainCarsToLoad.Count + trainCarsToAdd.Count <= maxCarsLicensed)) ||
                            (cargoGroupsToUse.Count() > 0 &&
                                !cargoGroupsToUse.Intersect(cargoGroupsForTrainCars).Any())) {
                            // either trying to satisfy licenses, but these trainCars aren't compatible
                            //   or the cargoGroups for these trainCars aren't compatible
                            // shuffle them to the front and try again
                            cgsPerTcs.Insert(0, cgsPerTcs[cgsPerTcs.Count - 1]);
                            cgsPerTcs.RemoveAt(cgsPerTcs.Count - 1);
                            continue;
                        }
                        cargoGroupsForTrainCars
                            = isFulfillingLicenseReqs ? licensedCargoGroups : cargoGroupsForTrainCars;

                        // if we've made it this far, we can add these trainCars to the job
                        cargoGroupsToUse = cargoGroupsToUse.Count() > 0
                            ? cargoGroupsToUse.Intersect(cargoGroupsForTrainCars)
                            : cargoGroupsForTrainCars;
                        trainCarsToLoad.AddRange(trainCarsToAdd);
                        cgsPerTcs.RemoveAt(cgsPerTcs.Count - 1);
                        countTracks--;
                    }

                    if (trainCarsToLoad.Count == 0 || cargoGroupsToUse.Count() == 0) {
                        // no more jobs can be made from the trainCar sets at this station; abandon the rest
                        break;
                    }

                    // if we're fulfilling license requirements this time around,
                    // we won't need to try again for this station
                    hasFulfilledLicenseReqs = isFulfillingLicenseReqs;

                    var chosenCargoGroup
                        = Utilities.GetRandomFromEnumerable<CargoGroup>(cargoGroupsToUse, rng);
                    var destinationStation
                        = Utilities.GetRandomFromEnumerable<StationController>(chosenCargoGroup.stations, rng);
                    var carsPerTrackDict = new Dictionary<Track, List<Car>>();
                    foreach (var trainCar in trainCarsToLoad) {
                        var track = trainCar.logicCar.CurrentTrack;
                        if (!carsPerTrackDict.ContainsKey(track)) {
                            carsPerTrackDict[track] = new List<Car>();
                        }
                        carsPerTrackDict[track].Add(trainCar.logicCar);
                    }

                    var cargoTypes = trainCarsToLoad.Select(
                        tc => {
                            var intersection = chosenCargoGroup.cargoTypes.Intersect(
                                Utilities.GetCargoTypesForCarType(tc.carType)).ToList();
                            if (!intersection.Any()) {
                                Debug.LogError(string.Format(
                                    "[PersistentJobs] Unexpected trainCar with no overlapping cargoType in cargoGroup!\n" +
                                    "cargo types for train car: [ {0} ]\n" +
                                    "cargo types for chosen cargo group: [ {1} ]",
                                    String.Join(", ", Utilities.GetCargoTypesForCarType(tc.carType)),
                                    String.Join(", ", chosenCargoGroup.cargoTypes)));
                                return CargoType.None;
                            }
                            return Utilities.GetRandomFromEnumerable<CargoType>(intersection, rng);
                        }).ToList();

                    Debug.Log(string.Format(
                        "[PersistentJobs]\ntrain car types: [ {0} ]\ncargo types: [ {1} ]",
                        string.Join(", ", trainCarsToLoad.Select(tc => tc.carType)),
                        string.Join(", ", cargoTypes)));

                    // populate all the info; we'll generate the jobs later
                    jobsToGenerate.Add((
                        startingStation,
                        carsPerTrackDict.Select(
                            kvPair => new CarsPerTrack(kvPair.Key, kvPair.Value)).ToList(),
                        destinationStation,
                        trainCarsToLoad,
                        cargoTypes));
                }
            }

            return jobsToGenerate;
        }

        public static IEnumerable<JobChainController> doJobGeneration(List<(StationController, List<CarsPerTrack>, StationController, List<TrainCar>, List<CargoType>)> jobInfos,
            System.Random rng,
            bool forceCorrectCargoStateOnCars = true) {
            return jobInfos.Select((definition) => {
                // I miss having a spread operator :(
                (var ss, var cpst, var ds, _, _) = definition;
                (_, _, _, var tcs, var cts) = definition;

                return (JobChainController)GenerateShuntingLoadJobWithExistingCars(
                    ss,
                    cpst,
                    ds,
                    tcs,
                    cts,
                    rng,
                    forceCorrectCargoStateOnCars);
            });
        }
    }
}