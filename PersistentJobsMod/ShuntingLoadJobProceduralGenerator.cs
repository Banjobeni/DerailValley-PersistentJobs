using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using DV.Logic.Job;
using DV.ThingTypes;
using DV.Utils;
using PersistentJobsMod.CarSpawningJobGenerators;
using PersistentJobsMod.Extensions;
using PersistentJobsMod.Licensing;
using Random = System.Random;

namespace PersistentJobsMod {
    static class ShuntingLoadJobProceduralGenerator {
        private class CargoTypeLiveryCar {
            public CargoType CargoType { get; }
            public TrainCarLivery TrainCarLivery { get; }

            public CargoTypeLiveryCar(CargoType cargoType, TrainCarLivery trainCarLivery) {
                CargoType = cargoType;
                TrainCarLivery = trainCarLivery;
            }
        }

        private class CargoCarGroupForTrack {
            public List<CargoCarGroup> CargoCarGroups { get; }

            public CargoCarGroupForTrack(List<CargoCarGroup> cargoCarGroups) {
                CargoCarGroups = cargoCarGroups;
            }

            public List<CargoTypeLiveryCar> ToCargoTypeLiveryCars() {
                return CargoCarGroups.SelectMany(ccg => ccg.CarLiveries.Select(livery => new CargoTypeLiveryCar(ccg.CargoType, livery))).ToList();
            }
        }

        public static JobChainControllerWithEmptyHaulGeneration GenerateShuntingLoadJobWithCarSpawning(StationController startingStation, bool forceLicenseReqs, System.Random random) {
            Main._modEntry.Logger.Log("load: generating with car spawning");
            var yardTracksOrganizer = YardTracksOrganizer.Instance;

            var possibleCargoGroupsAndTrainCarCountOrNull = CargoGroupsAndCarCountProvider.GetOrNull(startingStation.proceduralJobsRuleset.outputCargoGroups, startingStation.proceduralJobsRuleset, forceLicenseReqs, CargoGroupsAndCarCountProvider.CargoGroupLicenseKind.Cargo, random);

            if (possibleCargoGroupsAndTrainCarCountOrNull == null) {
                return null;
            }

            var (availableCargoGroups, carCount) = possibleCargoGroupsAndTrainCarCountOrNull.Value;

            var chosenCargoGroup = random.GetRandomElement(availableCargoGroups);
            Main._modEntry.Logger.Log($"load: chose cargo group ({string.Join("/", chosenCargoGroup.cargoTypes)}) with {carCount} waggons");

            var cargoCarGroups = CargoCarGroupsRandomizer.GetCargoCarGroups(chosenCargoGroup, carCount, random);

            var cargoCarGroupsForTracks = DistributeCargoCarGroupsToTracks(cargoCarGroups, startingStation.proceduralJobsRuleset.maxShuntingStorageTracks, random);

            // choose starting tracks
            var startingTracksWithCargoLiveryCars = TryFindActualStartingTracksOrNull(startingStation, yardTracksOrganizer, cargoCarGroupsForTracks, random);
            if (startingTracksWithCargoLiveryCars == null) {
                Debug.LogWarning("[PersistentJobs] load: Couldn't find startingTrack with enough free space for train!");
                return null;
            }

            var cargoTypeLiveryCars = startingTracksWithCargoLiveryCars.SelectMany(trackCars => trackCars.CargoLiveryCars).ToList();

            // choose random destination station that has at least 1 available track
            var destinationStation = ChooseDestinationStationHavingFreeTrack(chosenCargoGroup.stations, cargoTypeLiveryCars, yardTracksOrganizer, random);
            if (destinationStation == null) {
                Debug.LogWarning("Couldn't find a station with enough free space for train!");
                return null;
            }

            // spawn trainCars & form carsPerStartingTrack
            Main._modEntry.Logger.Log("load: spawning trainCars");
            var orderedTrainCars = new List<TrainCar>();
            var carsPerStartingTrack = new List<CarsPerTrack>();

            for (var i = 0; i < startingTracksWithCargoLiveryCars.Count; i++) {
                var startingTrack = startingTracksWithCargoLiveryCars[i].Track;
                var trackTrainCarLiveries = startingTracksWithCargoLiveryCars[i].CargoLiveryCars.Select(clc => clc.TrainCarLivery).ToList();

                Main._modEntry.Logger.Log($"load: spawning car group {i + 1}/{startingTracksWithCargoLiveryCars.Count} on track {startingTrack.ID}");

                var railTrack = SingletonBehaviour<LogicController>.Instance.LogicToRailTrack[startingTrack];
                var carOrientations = Enumerable.Range(0, trackTrainCarLiveries.Count).Select(_ => random.Next(2) > 0).ToList();

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
                    Main._modEntry.Logger.Log("load: Failed to spawn some trainCars!");
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
                cargoTypeLiveryCars.Select(clc => clc.CargoType).ToList(),
                random,
                true);

            if (jcc == null) {
                Debug.LogWarning("[PersistentJobs] load: Couldn't generate job chain. Deleting spawned trainCars!");
                SingletonBehaviour<CarSpawner>.Instance.DeleteTrainCars(orderedTrainCars, true);
                return null;
            }

            return jcc;
        }

        private static List<CargoCarGroupForTrack> DistributeCargoCarGroupsToTracks(List<CargoCarGroup> cargoCarGroups, int stationRulesetMaxTrackCount, Random random) {
            var totalCarCount = cargoCarGroups.Select(ccg => ccg.CarLiveries.Count).Sum();
            var desiredTracksCount = Math.Min(stationRulesetMaxTrackCount, GetMaxTracksForCarCount(totalCarCount));

            if (cargoCarGroups.Count < desiredTracksCount) {
                // need to split some cargoCarGroups in order to reach the desired track count

                while (cargoCarGroups.Count < desiredTracksCount) {
                    var largestCargoCargGroup = cargoCarGroups.OrderByDescending(ccg => ccg.CarLiveries.Count).First();
                    if (largestCargoCargGroup.CarLiveries.Count < 4) {
                        // could not find a group that is large enough for splitting
                        break;
                    } else {
                        var newGroup1CarCount = random.Next(0, largestCargoCargGroup.CarLiveries.Count - 1) + 1;
                        var newGroup1 = new CargoCarGroup(largestCargoCargGroup.CargoType, largestCargoCargGroup.CarLiveries.Take(newGroup1CarCount).ToList());
                        var newGroup2 = new CargoCarGroup(largestCargoCargGroup.CargoType, largestCargoCargGroup.CarLiveries.Skip(newGroup1CarCount).ToList());
                        
                        var index = cargoCarGroups.IndexOf(largestCargoCargGroup);
                        cargoCarGroups.RemoveAt(index);
                        cargoCarGroups.Insert(index, newGroup1);
                        cargoCarGroups.Insert(index + 1, newGroup2);
                    }
                }

                return cargoCarGroups.Select(ccg => new CargoCarGroupForTrack(new[] { ccg }.ToList())).ToList();
            } else {
                // there are at least enough cargo car groups for the requested number of tracks
                var result = new List<CargoCarGroupForTrack>();

                foreach (var cargoCarGroup in cargoCarGroups) {
                    if (result.Count < desiredTracksCount) {
                        result.Add(new CargoCarGroupForTrack(new[] { cargoCarGroup }.ToList()));
                    } else {
                        var index = random.Next(desiredTracksCount);
                        result[index].CargoCarGroups.Add(cargoCarGroup);
                    }
                }

                return result;
            }
        }

        private static int GetMaxTracksForCarCount(int carCount) {
            if (carCount <= 3) {
                return 1;
            } else if (carCount <= 4) {
                return 2;
            } else {
                return 3;
            }
        }

        private static StationController ChooseDestinationStationHavingFreeTrack(List<StationController> destinationStations, List<CargoTypeLiveryCar> cargoTypeLiveryCars, YardTracksOrganizer yardTracksOrganizer, Random rng) {
            Main._modEntry.Logger.Log("load: choosing destination");
            
            var trainCarLiveries = cargoTypeLiveryCars.Select(ctlc => ctlc.TrainCarLivery).ToList();
            var approxTrainLength = CarSpawner.Instance.GetTotalCarLiveriesLength(trainCarLiveries, true);

            var availableDestinations = destinationStations.ToList();

            while (availableDestinations.Count > 0) {
                var station = Utilities.GetRandomFromEnumerable(availableDestinations, rng);

                availableDestinations.Remove(station);

                var destinationTrack = Utilities.GetTrackThatHasEnoughFreeSpace(yardTracksOrganizer, yardTracksOrganizer.FilterOutOccupiedTracks(station.logicStation.yard.TransferInTracks), approxTrainLength, rng);

                if (destinationTrack != null) {
                    return station;
                }
            }

            return null;
        }

        private static List<(Track Track, List<CargoTypeLiveryCar> CargoLiveryCars)> TryFindActualStartingTracksOrNull(StationController startingStation, YardTracksOrganizer yardTracksOrganizer, List<CargoCarGroupForTrack> carGroupsOnTracks, Random random) {
            var tracks = startingStation.logicStation.yard.StorageTracks.Select(t => (Track: t, FreeSpace: yardTracksOrganizer.GetFreeSpaceOnTrack(t), JobCount: t.GetJobsOfCarsFullyOnTrack().Count)).ToList();
            foreach (var (track, freeSpace, jobCount) in tracks) {
                Main._modEntry.Logger.Log($"load: Considering track {track.ID} having cars of {jobCount} jobs already and {freeSpace}m of free space");
            }

            var result = new List<(Track Track, List<CargoTypeLiveryCar> CargoLiveryCars)>();
            foreach (var cargoCarGroupForTrack in carGroupsOnTracks) {
                var trackCargoLiveryCars = cargoCarGroupForTrack.ToCargoTypeLiveryCars();
                var requiredTrackLength = CarSpawner.Instance.GetTotalCarLiveriesLength(trackCargoLiveryCars.Select(clc => clc.TrainCarLivery).ToList(), true);

                var alreadyUsedTracks = result.Select(r => r.Track).ToList();

                var availableTracks = startingStation.logicStation.yard.StorageTracks.Except(alreadyUsedTracks).ToList();

                var suitableTracks = new List<Track>();
                foreach (var t in availableTracks) {
                    var freeSpace = yardTracksOrganizer.GetFreeSpaceOnTrack(t);
                    var jobCount = t.GetJobsOfCarsFullyOnTrack().Count;
                    if (jobCount < 3 && freeSpace > requiredTrackLength) {
                        suitableTracks.Add(t);
                    }
                }

                if (suitableTracks.Count == 0) {
                    Main._modEntry.Logger.Log($"load: Could not find any suitable track for track no. {result.Count + 1}");
                    return null;
                }

                var chosenTrack = random.GetRandomElement(suitableTracks);

                Main._modEntry.Logger.Log($"load: For track no. {result.Count + 1}, choosing {chosenTrack.ID}");

                result.Add((chosenTrack, trackCargoLiveryCars));
            }
            return result;
        }

        public static JobChainControllerWithEmptyHaulGeneration GenerateShuntingLoadJobWithExistingCars(
                StationController startingStation,
                List<CarsPerTrack> carsPerStartingTrack,
                StationController destStation,
                List<TrainCar> trainCars,
                List<CargoType> transportedCargoPerCar,
                System.Random rng,
                bool forceCorrectCargoStateOnCars = false) {
            Main._modEntry.Logger.Log("load: generating with pre-spawned cars");
            var yto = YardTracksOrganizer.Instance;
            var approxTrainLength = CarSpawner.Instance.GetTotalTrainCarsLength(trainCars, true);

            // choose warehosue machine
            Main._modEntry.Logger.Log("load: choosing warehouse machine");
            var supportedWMCs = startingStation.warehouseMachineControllers
                .Where(wm => wm.supportedCargoTypes.Intersect(transportedCargoPerCar).Count() > 0)
                .ToList();
            if (supportedWMCs.Count == 0) {
                Debug.LogWarning($"[PersistentJobs] load: Could not create ChainJob[{JobType.ShuntingLoad}]: {startingStation.logicStation.ID} - {destStation.logicStation.ID}. Found no supported WarehouseMachine!");
                return null;
            }
            var loadMachine = Utilities.GetRandomFromEnumerable(supportedWMCs, rng).warehouseMachine;

            // choose destination track
            Main._modEntry.Logger.Log("load: choosing destination track");
            var destinationTrack = Utilities.GetTrackThatHasEnoughFreeSpace(yto, yto.FilterOutOccupiedTracks(startingStation.logicStation.yard.TransferOutTracks), approxTrainLength, new Random());
            if (destinationTrack == null) {
                destinationTrack = Utilities.GetTrackThatHasEnoughFreeSpace(yto, startingStation.logicStation.yard.TransferOutTracks, approxTrainLength, new Random());
            }
            if (destinationTrack == null) {
                Debug.LogWarning($"[PersistentJobs] load: Could not create ChainJob[{JobType.ShuntingLoad}]: {startingStation.logicStation.ID} - {destStation.logicStation.ID}. " + "Found no TransferOutTrack with enough free space!");
                return null;
            }

            Main._modEntry.Logger.Log("load: calculating time/wage/licenses");
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
            Main._modEntry.Logger.Log($"load: attempting to generate ChainJob[{JobType.ShuntingLoad}]: {startingStation.logicStation.ID} - {destStation.logicStation.ID}");
            var gameObject = new GameObject($"ChainJob[{JobType.ShuntingLoad}]: {startingStation.logicStation.ID} - {destStation.logicStation.ID}");
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
                                Debug.LogError("[PersistentJobs] Unexpected trainCar with no overlapping cargoType in cargoGroup!\n" + $"cargo types for train car: [ {String.Join(", ", Utilities.GetCargoTypesForCarType(tc.carType))} ]\n" + $"cargo types for chosen cargo group: [ {String.Join(", ", chosenCargoGroup.cargoTypes)} ]");
                                return CargoType.None;
                            }
                            return Utilities.GetRandomFromEnumerable<CargoType>(intersection, rng);
                        }).ToList();

                    Main._modEntry.Logger.Log($"[PersistentJobs]\ntrain car types: [ {string.Join(", ", trainCarsToLoad.Select(tc => tc.carType))} ]\ncargo types: [ {string.Join(", ", cargoTypes)} ]");

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