using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using DV.Logic.Job;
using DV.ThingTypes;
using Random = System.Random;

namespace PersistentJobsMod {
    static class ShuntingUnloadJobProceduralGenerator {
        public static JobChainControllerWithEmptyHaulGeneration GenerateShuntingUnloadJobWithExistingCars(StationController startingStation,
            Track startingTrack,
            StationController destinationStation,
            List<TrainCar> trainCars,
            List<CargoType> transportedCargoPerCar,
            System.Random rng,
            bool forceCorrectCargoStateOnCars = false) {
            Main._modEntry.Logger.Log("unload: generating with pre-spawned cars");
            var yto = YardTracksOrganizer.Instance;
            var approxTrainLength = CarSpawner.Instance.GetTotalTrainCarsLength(trainCars, true);

            // choose warehouse machine
            Main._modEntry.Logger.Log("unload: choosing warehouse machine");
            var supportedWMCs = destinationStation.warehouseMachineControllers
                .Where(wm => wm.supportedCargoTypes.Intersect(transportedCargoPerCar).Count() > 0)
                .ToList();
            if (supportedWMCs.Count == 0) {
                Debug.LogWarning($"[PersistentJobs] unload: Could not create ChainJob[{JobType.ShuntingLoad}]: {startingStation.logicStation.ID} - {destinationStation.logicStation.ID}. Found no supported WarehouseMachine!");
                return null;
            }
            var loadMachine = Utilities.GetRandomFromEnumerable(supportedWMCs, rng).warehouseMachine;

            // choose destination tracks
            var maxCountTracks = destinationStation.proceduralJobsRuleset.maxShuntingStorageTracks;
            var countTracks = rng.Next(1, maxCountTracks + 1);

            // bias toward less than max number of tracks for shorter trains
            if (trainCars.Count < 2 * maxCountTracks) {
                countTracks = rng.Next(0, Mathf.FloorToInt(1.5f * maxCountTracks)) % maxCountTracks + 1;
            }
            Main._modEntry.Logger.Log($"unload: choosing {countTracks} destination tracks");
            var destinationTracks = new List<Track>();
            do {
                destinationTracks.Clear();
                for (var i = 0; i < countTracks; i++) {
                    var track = Utilities.GetTrackThatHasEnoughFreeSpace(yto, destinationStation.logicStation.yard.StorageTracks.Except(destinationTracks).ToList(), approxTrainLength / (float)countTracks, new Random());
                    if (track == null) {
                        break;
                    }
                    destinationTracks.Add(track);
                }
            } while (destinationTracks.Count < countTracks--);
            if (destinationTracks.Count == 0) {
                Debug.LogWarning($"[PersistentJobs] unload: Could not create ChainJob[{JobType.ShuntingUnload}]: {startingStation.logicStation.ID} - {destinationStation.logicStation.ID}. " + "Found no StorageTrack with enough free space!");
                return null;
            }

            // divide trainCars between destination tracks
            var countCarsPerTrainset = trainCars.Count / destinationTracks.Count;
            var countTrainsetsWithExtraCar = trainCars.Count % destinationTracks.Count;
            Main._modEntry.Logger.Log($"unload: dividing trainCars {countCarsPerTrainset} per track with {countTrainsetsWithExtraCar} extra");
            var orderedTrainCars = new List<TrainCar>();
            var carsPerDestinationTrack = new List<CarsPerTrack>();
            for (var i = 0; i < destinationTracks.Count; i++) {
                var rangeStart = i * countCarsPerTrainset + Math.Min(i, countTrainsetsWithExtraCar);
                var rangeCount = i < countTrainsetsWithExtraCar ? countCarsPerTrainset + 1 : countCarsPerTrainset;
                var destinationTrack = destinationTracks[i];
                carsPerDestinationTrack.Add(
                    new CarsPerTrack(
                        destinationTrack,
                        (from car in trainCars.GetRange(rangeStart, rangeCount) select car.logicCar).ToList()));
            }

            Main._modEntry.Logger.Log("unload: calculating time/wage/licenses");
            float bonusTimeLimit;
            float initialWage;
            Utilities.CalculateShuntingBonusTimeLimitAndWage(
                JobType.ShuntingLoad,
                destinationTracks.Count,
                (from tc in trainCars select tc.carType).ToList<TrainCarType>(),
                transportedCargoPerCar,
                out bonusTimeLimit,
                out initialWage
            );
            var requiredLicenses = JobLicenseType_v2.ListToFlags(LicenseManager.Instance.GetRequiredLicensesForJobType(JobType.ShuntingUnload))
                | JobLicenseType_v2.ListToFlags(LicenseManager.Instance.GetRequiredLicensesForCargoTypes(transportedCargoPerCar))
                | (LicenseManager.Instance.GetRequiredLicenseForNumberOfTransportedCars(trainCars.Count)?.v1 ?? JobLicenses.Basic);
            return GenerateShuntingUnloadChainController(
                startingStation,
                startingTrack,
                loadMachine,
                destinationStation,
                carsPerDestinationTrack,
                trainCars,
                transportedCargoPerCar,
                trainCars.Select(
                    tc => tc.logicCar.CurrentCargoTypeInCar == CargoType.None ? 1.0f : tc.logicCar.LoadedCargoAmount).ToList(),
                forceCorrectCargoStateOnCars,
                bonusTimeLimit,
                initialWage,
                requiredLicenses);
        }

        private static JobChainControllerWithEmptyHaulGeneration GenerateShuntingUnloadChainController(StationController startingStation,
            Track startingTrack,
            WarehouseMachine unloadMachine,
            StationController destinationStation,
            List<CarsPerTrack> carsPerDestinationTrack,
            List<TrainCar> orderedTrainCars,
            List<CargoType> orderedCargoTypes,
            List<float> orderedCargoAmounts,
            bool forceCorrectCargoStateOnCars,
            float bonusTimeLimit,
            float initialWage,
            JobLicenses requiredLicenses) {
            Main._modEntry.Logger.Log($"unload: attempting to generate ChainJob[{JobType.ShuntingLoad}]: {startingStation.logicStation.ID} - {destinationStation.logicStation.ID}");
            var gameObject = new GameObject($"ChainJob[{JobType.ShuntingUnload}]: {startingStation.logicStation.ID} - {destinationStation.logicStation.ID}");
            gameObject.transform.SetParent(destinationStation.transform);
            var jobChainController
                = new JobChainControllerWithEmptyHaulGeneration(gameObject);
            var chainData = new StationsChainData(
                startingStation.stationInfo.YardID,
                destinationStation.stationInfo.YardID);
            jobChainController.trainCarsForJobChain = orderedTrainCars;
            var cargoTypeToTrainCarAndAmount
                = new Dictionary<CargoType, List<(TrainCar, float)>>();
            for (var i = 0; i < orderedTrainCars.Count; i++) {
                if (!cargoTypeToTrainCarAndAmount.ContainsKey(orderedCargoTypes[i])) {
                    cargoTypeToTrainCarAndAmount[orderedCargoTypes[i]] = new List<(TrainCar, float)>();
                }
                cargoTypeToTrainCarAndAmount[orderedCargoTypes[i]].Add((orderedTrainCars[i], orderedCargoAmounts[i]));
            }
            var unloadData = cargoTypeToTrainCarAndAmount.Select(
                kvPair => new CarsPerCargoType(
                    kvPair.Key,
                    kvPair.Value.Select(t => t.Item1.logicCar).ToList(),
                    kvPair.Value.Aggregate(0.0f, (sum, t) => sum + t.Item2))).ToList();
            var staticShuntingUnloadJobDefinition
                = gameObject.AddComponent<StaticShuntingUnloadJobDefinition>();
            staticShuntingUnloadJobDefinition.PopulateBaseJobDefinition(
                destinationStation.logicStation,
                bonusTimeLimit,
                initialWage,
                chainData,
                requiredLicenses);
            staticShuntingUnloadJobDefinition.startingTrack = startingTrack;
            staticShuntingUnloadJobDefinition.carsPerDestinationTrack = carsPerDestinationTrack;
            staticShuntingUnloadJobDefinition.unloadData = unloadData;
            staticShuntingUnloadJobDefinition.unloadMachine = unloadMachine;
            staticShuntingUnloadJobDefinition.forceCorrectCargoStateOnCars = forceCorrectCargoStateOnCars;
            jobChainController.AddJobDefinitionToChain(staticShuntingUnloadJobDefinition);
            return jobChainController;
        }

        public static List<(StationController, Track, StationController, List<TrainCar>, List<CargoType>)>
            ComputeJobInfosFromCargoGroupsPerTrainCarSetPerStation(Dictionary<StationController, List<(List<TrainCar>, List<CargoGroup>)>> cgsPerTcsPerSc,
                System.Random rng) {
            var jobsToGenerate
                = new List<(StationController, Track, StationController, List<TrainCar>, List<CargoType>)>();

            foreach (var destinationStation in cgsPerTcsPerSc.Keys) {
                var cgsPerTcs = cgsPerTcsPerSc[destinationStation];

                foreach ((var trainCars, var cargoGroups) in cgsPerTcs) {
                    var chosenCargoGroup = Utilities.GetRandomFromEnumerable(cargoGroups, rng);
                    var startingStation
                        = Utilities.GetRandomFromEnumerable(chosenCargoGroup.stations, rng);

                    // populate all the info; we'll generate the jobs later
                    jobsToGenerate.Add((
                        startingStation,
                        trainCars[0].logicCar.CurrentTrack,
                        destinationStation,
                        trainCars,
                        trainCars.Select(tc => tc.logicCar.CurrentCargoTypeInCar).ToList()));
                }
            }

            return jobsToGenerate;
        }

        public static IEnumerable<JobChainController> doJobGeneration(List<(StationController, Track, StationController, List<TrainCar>, List<CargoType>)> jobInfos,
            System.Random rng,
            bool forceCorrectCargoStateOnCars = true) {
            return jobInfos.Select((definition) => {
                // I miss having a spread operator :(
                (var ss, var st, var ds, _, _) = definition;
                (_, _, _, var tcs, var cts) = definition;

                return (JobChainController)GenerateShuntingUnloadJobWithExistingCars(
                    ss,
                    st,
                    ds,
                    tcs,
                    cts,
                    rng,
                    forceCorrectCargoStateOnCars);
            });
        }
    }
}