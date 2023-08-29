using System;
using DV.Logic.Job;
using DV.ThingTypes;
using DV.Utils;
using PersistentJobsMod.Extensions;
using PersistentJobsMod.Licensing;
using System.Collections.Generic;
using System.Linq;
using PersistentJobsMod.JobGenerators;

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

        private class CargoCarGroupForTrack {
            public List<CargoCarGroup> CargoCarGroups { get; }

            public CargoCarGroupForTrack(List<CargoCarGroup> cargoCarGroups) {
                CargoCarGroups = cargoCarGroups;
            }

            public List<CargoTypeLiveryCar> ToCargoTypeLiveryCars() {
                return CargoCarGroups.SelectMany(ccg => ccg.CarLiveries.Select(livery => new CargoTypeLiveryCar(ccg.CargoType, livery))).ToList();
            }
        }

        public static JobChainController TryGenerateJobChainController(StationController startingStation, bool forceLicenseReqs, System.Random random) {
            Main._modEntry.Logger.Log($"{nameof(ShuntingLoadJobWithCarsGenerator)}: trying to generate job at {startingStation.logicStation.ID}");

            var yardTracksOrganizer = YardTracksOrganizer.Instance;

            var possibleCargoGroupsAndTrainCarCountOrNull = CargoGroupsAndCarCountProvider.GetOrNull(startingStation.proceduralJobsRuleset.outputCargoGroups, startingStation.proceduralJobsRuleset, forceLicenseReqs, CargoGroupsAndCarCountProvider.CargoGroupLicenseKind.Cargo, random);

            if (possibleCargoGroupsAndTrainCarCountOrNull == null) {
                return null;
            }

            var (availableCargoGroups, carCount) = possibleCargoGroupsAndTrainCarCountOrNull.Value;

            var chosenCargoGroup = random.GetRandomElement(availableCargoGroups);
            Main._modEntry.Logger.Log($"load: chose cargo group ({string.Join("/", chosenCargoGroup.cargoTypes)}) with {carCount} waggons");

            var cargoCarGroups = CargoCarGroupsRandomizer.GetCargoCarGroups(chosenCargoGroup, carCount, random);

            var totalTrainLength = CarSpawner.Instance.GetTotalCarLiveriesLength(cargoCarGroups.SelectMany(ccg => ccg.CarLiveries).ToList(), true);

            var distinctCargoTypes = cargoCarGroups.Select(cg => cg.CargoType).Distinct().ToList();

            var startingStationWarehouseMachines = startingStation.logicStation.yard.GetWarehouseMachinesThatSupportCargoTypes(distinctCargoTypes);
            if (startingStationWarehouseMachines.Count == 0) {
                UnityEngine.Debug.LogWarning($"[PersistentJobs] load: Couldn't find a warehouse machine at {startingStation.logicStation.ID} that supports all cargo types!!");
                return null;
            }

            var startingStationWarehouseMachine = startingStationWarehouseMachines.FirstOrDefault(wm => wm.WarehouseTrack.GetTotalUsableTrackLength() > totalTrainLength);
            if (startingStationWarehouseMachine == null) {
                Main._modEntry.Logger.Log($"load: Couldn't find a warehouse machine at {startingStation.logicStation.ID} that is long enough for the train!");
                return null;
            }

            var cargoCarGroupsForTracks = DistributeCargoCarGroupsToTracks(cargoCarGroups, startingStation.proceduralJobsRuleset.maxShuntingStorageTracks, startingStation.logicStation.yard.StorageTracks.Count, random);

            // choose starting tracks
            var startingTracksWithCargoLiveryCars = TryFindActualStartingTracksOrNull(startingStation, yardTracksOrganizer, cargoCarGroupsForTracks, random);
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

                var railTrack = SingletonBehaviour<LogicController>.Instance.LogicToRailTrack[startingTrack];
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

            var jcc = ShuntingLoadJobGenerator.TryGenerateJobChainController(
                startingStation,
                carsPerStartingTrack,
                destinationStation,
                orderedTrainCars,
                cargoTypeLiveryCars.Select(clc => clc.CargoType).ToList(),
                random,
                true);

            if (jcc == null) {
                UnityEngine.Debug.LogWarning("[PersistentJobs] load: Couldn't generate job chain. Deleting spawned trainCars!");
                SingletonBehaviour<CarSpawner>.Instance.DeleteTrainCars(orderedTrainCars, true);
                return null;
            }

            return jcc;
        }

        private static List<CargoCarGroupForTrack> DistributeCargoCarGroupsToTracks(List<CargoCarGroup> cargoCarGroups, int stationRulesetMaxTrackCount, int numberOfStorageTracks, Random random) {
            var totalCarCount = cargoCarGroups.Select(ccg => ccg.CarLiveries.Count).Sum();
            var maximumTracksCount = Math.Min(Math.Min(stationRulesetMaxTrackCount, GetMaxTracksForCarCount(totalCarCount)), numberOfStorageTracks);

            var desiredTracksCount = random.Next(1, maximumTracksCount + 1);

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
            } else if (carCount <= 5) {
                return 2;
            } else {
                return 3;
            }
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
    }
}