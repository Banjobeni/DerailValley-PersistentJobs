using System;
using DV.Logic.Job;
using DV.ThingTypes;
using DV.Utils;
using PersistentJobsMod.Extensions;
using PersistentJobsMod.Licensing;
using System.Collections.Generic;
using System.Linq;
using PersistentJobsMod.JobGenerators;
using UnityEngine;
using Random = System.Random;

namespace PersistentJobsMod.CarSpawningJobGenerators {
    public static class ShuntingLoadJobWithCarsGenerator {
        private class CargoTypeLiveryCar {
            public CargoType CargoType { get; }
            public TrainCarLivery TrainCarLivery { get; }

            public CargoTypeLiveryCar(CargoType cargoType, TrainCarLivery trainCarLivery) {
                CargoType = cargoType;
                TrainCarLivery = trainCarLivery;
            }
        }

        private class CarSpawnGroupsForTrack {
            public List<CarSpawnGroup> CarSpawnGroups { get; }

            public CarSpawnGroupsForTrack(List<CarSpawnGroup> carSpawnGroups) {
                CarSpawnGroups = carSpawnGroups;
            }

            public List<CargoTypeLiveryCar> ToCargoTypeLiveryCars() {
                return CarSpawnGroups.SelectMany(csg => csg.CargoTypesAndLiveries.Select(tuple => new CargoTypeLiveryCar(tuple.CargoType, tuple.CarLivery))).ToList();
            }
        }

        public static JobChainController TryGenerateJobChainController(StationController startingStation, bool forceLicenseReqs, Random random) {
            Main._modEntry.Logger.Log($"{nameof(ShuntingLoadJobWithCarsGenerator)}: trying to generate job at {startingStation.logicStation.ID}");

            var yardTracksOrganizer = YardTracksOrganizer.Instance;

            var possibleCargoGroupsAndTrainCarCountOrNull = CargoGroupsAndCarCountProvider.GetOrNull(startingStation.proceduralJobsRuleset.outputCargoGroups, startingStation.proceduralJobsRuleset, forceLicenseReqs, CargoGroupsAndCarCountProvider.CargoGroupLicenseKind.Cargo, random);

            if (possibleCargoGroupsAndTrainCarCountOrNull == null || possibleCargoGroupsAndTrainCarCountOrNull.Value.availableCargoGroups == null || !(possibleCargoGroupsAndTrainCarCountOrNull.Value.availableCargoGroups.Any()) || possibleCargoGroupsAndTrainCarCountOrNull.Value.countTrainCars < 1) {
                return null;
            }

            var (availableCargoGroups, carCount) = possibleCargoGroupsAndTrainCarCountOrNull.Value;

            var chosenCargoGroup = random.GetRandomElement(availableCargoGroups);
            Main._modEntry.Logger.Log($"load: chose cargo group ({string.Join("/", chosenCargoGroup.cargoTypes)}) with {carCount} waggons");

            var carSpawnGroups = CarSpawnGroupsRandomizer.GetCarSpawnGroups(chosenCargoGroup.cargoTypes, carCount, random);

            var totalTrainLength = CarSpawner.Instance.GetTotalCarLiveriesLength(carSpawnGroups.SelectMany(csg => csg.CargoTypesAndLiveries.Select(tuple => tuple.CarLivery)).ToList(), true);

            var distinctCargoTypes = carSpawnGroups.SelectMany(csg => csg.CargoTypesAndLiveries.Select(tuple => tuple.CargoType)).Distinct().ToList();

            Main._modEntry.Logger.Log($"load: chosen distinct cargo types: {string.Join(", ", distinctCargoTypes)}");

            var startingStationWarehouseMachines = startingStation.logicStation.yard.GetWarehouseMachinesThatSupportCargoTypes(distinctCargoTypes);
            if (startingStationWarehouseMachines.Count == 0) {
                Debug.LogWarning($"[PersistentJobs] load: Couldn't find a warehouse machine at {startingStation.logicStation.ID} that supports all cargo types!!");
                return null;
            }

            Main._modEntry.Logger.Log($"load: warehouse machines at {startingStation.logicStation.ID} that support those cargo types: {string.Join(", ", startingStationWarehouseMachines.Select(w => w.WarehouseTrack.ID))}");

            var startingStationWarehouseMachine = startingStationWarehouseMachines.FirstOrDefault(wm => wm.WarehouseTrack.GetTotalUsableTrackLength() > totalTrainLength);
            if (startingStationWarehouseMachine == null) {
                Main._modEntry.Logger.Log($"load: Couldn't find a warehouse machine at {startingStation.logicStation.ID} that is long enough for the train!");
                return null;
            }

            Main._modEntry.Logger.Log($"load: chose warehouse machine {startingStationWarehouseMachine.WarehouseTrack.ID}. it declares these loadable cargo types: {string.Join(", ", startingStationWarehouseMachine.SupportedCargoTypes)}");

            var carSpawnGroupsForTracks = DistributeCargoCarGroupsToTracks(carSpawnGroups, startingStation.proceduralJobsRuleset.maxShuntingStorageTracks, startingStation.logicStation.yard.StorageTracks.Count, random);

            // choose starting tracks
            var startingTracksWithCargoLiveryCars = TryFindActualStartingTracksOrNull(startingStation, yardTracksOrganizer, carSpawnGroupsForTracks, random);
            if (startingTracksWithCargoLiveryCars == null) {
                Main._modEntry.Logger.Log("load: Couldn't find starting tracks with enough free space for train!");
                return null;
            }

            var cargoTypeLiveryCars = startingTracksWithCargoLiveryCars.SelectMany(trackCars => trackCars.CargoLiveryCars).ToList();

            // choose random destination station that has at least 1 available track
            var destinationStation = DestinationStationRandomizer.GetRandomStationSupportingCargoTypesAndTrainLengthAndFreeTransferInTrack(chosenCargoGroup.stations, totalTrainLength, distinctCargoTypes, random);
            if (destinationStation == null) {
                Main._modEntry.Logger.Log("load: Couldn't find a compatible station with enough free space for train!");
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

                var railTrack = startingTrack.RailTrack();
                var carOrientations = Enumerable.Range(0, trackTrainCarLiveries.Count).Select(_ => random.Next(2) > 0).ToList();

                var spawnedCars = CarSpawner.Instance.SpawnCarTypesOnTrack(trackTrainCarLiveries, carOrientations, railTrack, true, true);

                if (spawnedCars == null) {
                    Main._modEntry.Logger.Log("load: Failed to spawn some trainCars!");
                    SingletonBehaviour<CarSpawner>.Instance.DeleteTrainCars(orderedTrainCars, true);
                    return null;
                }
                orderedTrainCars.AddRange(spawnedCars);
                carsPerStartingTrack.Add(new CarsPerTrack(startingTrack, (from car in spawnedCars select car.logicCar).ToList()));
            }

            var cargoTypesPerTrainCar = cargoTypeLiveryCars.Select(clc => clc.CargoType).ToList();

            var jobChainController = ShuntingLoadJobGenerator.GenerateJobChainController(
                startingStation,
                carsPerStartingTrack,
                startingStationWarehouseMachine,
                destinationStation,
                orderedTrainCars,
                cargoTypesPerTrainCar,
                true);

            if (jobChainController == null) {
                Debug.LogWarning("[PersistentJobs] load: Couldn't generate job chain. Deleting spawned trainCars!");
                SingletonBehaviour<CarSpawner>.Instance.DeleteTrainCars(orderedTrainCars, true);
                return null;
            }

            return jobChainController;
        }

        private static List<CarSpawnGroupsForTrack> DistributeCargoCarGroupsToTracks(List<CarSpawnGroup> carSpawnGroups, int stationRulesetMaxTrackCount, int numberOfStorageTracks, Random random) {
            var totalCarCount = carSpawnGroups.Select(csg => csg.CargoTypesAndLiveries.Count).Sum();
            var maximumTracksCount = Math.Min(Math.Min(stationRulesetMaxTrackCount, GetMaxTracksForCarCount(totalCarCount)), numberOfStorageTracks);

            var desiredTracksCount = random.Next(1, maximumTracksCount + 1);

            if (carSpawnGroups.Count < desiredTracksCount) {
                // need to split some carSpawnGroups in order to reach the desired track count

                while (carSpawnGroups.Count < desiredTracksCount) {
                    var largestCargoCargGroup = carSpawnGroups.OrderByDescending(csg => csg.CargoTypesAndLiveries.Count).First();
                    if (largestCargoCargGroup.CargoTypesAndLiveries.Count < 4) {
                        // could not find a group that is large enough for splitting
                        break;
                    } else {
                        var newGroup1CarCount = random.Next(0, largestCargoCargGroup.CargoTypesAndLiveries.Count - 1) + 1;
                        var newGroup1 = new CarSpawnGroup(largestCargoCargGroup.TrainCarType, largestCargoCargGroup.CargoTypesAndLiveries.Take(newGroup1CarCount).ToList());
                        var newGroup2 = new CarSpawnGroup(largestCargoCargGroup.TrainCarType, largestCargoCargGroup.CargoTypesAndLiveries.Skip(newGroup1CarCount).ToList());

                        var index = carSpawnGroups.IndexOf(largestCargoCargGroup);
                        carSpawnGroups.RemoveAt(index);
                        carSpawnGroups.Insert(index, newGroup1);
                        carSpawnGroups.Insert(index + 1, newGroup2);
                    }
                }

                return carSpawnGroups.Select(ccg => new CarSpawnGroupsForTrack(new[] { ccg }.ToList())).ToList();
            } else {
                // there are at least enough cargo car groups for the requested number of tracks
                var result = new List<CarSpawnGroupsForTrack>();

                foreach (var cargoCarGroup in carSpawnGroups) {
                    if (result.Count < desiredTracksCount) {
                        result.Add(new CarSpawnGroupsForTrack(new[] { cargoCarGroup }.ToList()));
                    } else {
                        var index = random.Next(desiredTracksCount);
                        result[index].CarSpawnGroups.Add(cargoCarGroup);
                    }
                }

                return result;
            }
        }

        private static int GetMaxTracksForCarCount(int carCount) {
            if (carCount <= 3) {
                return 1;
            } else if (carCount <= 5) {
                return 2;
            } else {
                return 3;
            }
        }

        private static List<(Track Track, List<CargoTypeLiveryCar> CargoLiveryCars)> TryFindActualStartingTracksOrNull(StationController startingStation, YardTracksOrganizer yardTracksOrganizer, List<CarSpawnGroupsForTrack> carGroupsOnTracks, Random random) {
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
    }
}