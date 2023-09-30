using System;
using System.Collections.Generic;
using System.Linq;
using DV.Logic.Job;
using DV.ThingTypes;
using UnityEngine;

namespace PersistentJobsMod.JobGenerators {
    static class ShuntingLoadJobGenerator {
        public static JobChainController TryGenerateJobChainController(
                StationController startingStation,
                List<CarsPerTrack> carsPerStartingTrack,
                StationController destStation,
                List<TrainCar> trainCars,
                List<CargoType> transportedCargoPerCar,
                System.Random random,
                bool forceCorrectCargoStateOnCars = false) {
            Main._modEntry.Logger.Log("load: generating with pre-spawned cars");
            var yto = YardTracksOrganizer.Instance;
            var approxTrainLength = CarSpawner.Instance.GetTotalTrainCarsLength(trainCars, true);

            // TODO fix intersect. a warehouse machine should be able to handle *all* cargo types, no?
            // choose warehouse machine
            Main._modEntry.Logger.Log("load: choosing warehouse machine");
            var supportedWMCs = startingStation.warehouseMachineControllers
                .Where(wm => wm.supportedCargoTypes.Intersect(transportedCargoPerCar).Count() > 0)
                .ToList();
            if (supportedWMCs.Count == 0) {
                Debug.LogWarning($"[PersistentJobs] load: Could not create ChainJob[{JobType.ShuntingLoad}]: {startingStation.logicStation.ID} - {destStation.logicStation.ID}. Found no supported WarehouseMachine!");
                return null;
            }
            var warehouseMachine = Utilities.GetRandomFromEnumerable(supportedWMCs, random).warehouseMachine;

            Main._modEntry.Logger.Log("load: calculating time/wage/licenses");

            float bonusTimeLimit;
            float initialWage;
            Utilities.CalculateShuntingBonusTimeLimitAndWage(
                JobType.ShuntingLoad,
                carsPerStartingTrack.Count,
                trainCars.Select(tc => tc.carLivery).ToList(),
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
                warehouseMachine,
                destStation,
                warehouseMachine.WarehouseTrack,
                trainCars,
                transportedCargoPerCar,
                Enumerable.Repeat(1.0f, trainCars.Count).ToList(),
                forceCorrectCargoStateOnCars,
                bonusTimeLimit,
                initialWage,
                requiredLicenses
            );
        }

        private static JobChainController GenerateShuntingLoadChainController(StationController startingStation,
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
                = new JobChainController(gameObject);
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
                        var track = trainCar.logicCar.FrontBogieTrack;
                        if (!carsPerTrackDict.ContainsKey(track)) {
                            carsPerTrackDict[track] = new List<Car>();
                        }
                        carsPerTrackDict[track].Add(trainCar.logicCar);
                    }

                    var cargoTypes = trainCarsToLoad.Select(
                        tc => {
                            var intersection = chosenCargoGroup.cargoTypes.Intersect(
                                Utilities.GetCargoTypesForCarType(tc.carLivery.parentType)).ToList();
                            if (!intersection.Any()) {
                                Debug.LogError("[PersistentJobs] Unexpected trainCar with no overlapping cargoType in cargoGroup!\n" + $"cargo types for train car: [ {String.Join(", ", Utilities.GetCargoTypesForCarType(tc.carLivery.parentType))} ]\n" + $"cargo types for chosen cargo group: [ {String.Join(", ", chosenCargoGroup.cargoTypes)} ]");
                                return CargoType.None;
                            }
                            return Utilities.GetRandomFromEnumerable<CargoType>(intersection, rng);
                        }).ToList();


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

                return (JobChainController)TryGenerateJobChainController(
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