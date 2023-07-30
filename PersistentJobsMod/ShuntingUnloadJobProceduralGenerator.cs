using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using DV.Logic.Job;
using DV.ThingTypes;
using DV.Utils;

namespace PersistentJobsMod {
    class ShuntingUnloadJobProceduralGenerator {
        public static JobChainControllerWithEmptyHaulGeneration GenerateShuntingUnloadJobWithCarSpawning(StationController destinationStation,
            bool forceLicenseReqs,
            System.Random rng) {
            Debug.Log("[PersistentJobs] unload: generating with car spawning");
            YardTracksOrganizer yto = YardTracksOrganizer.Instance;
            List<CargoGroup> availableCargoGroups = destinationStation.proceduralJobsRuleset.inputCargoGroups;
            int countTrainCars = rng.Next(
                destinationStation.proceduralJobsRuleset.minCarsPerJob,
                destinationStation.proceduralJobsRuleset.maxCarsPerJob);

            if (forceLicenseReqs) {
                Debug.Log("[PersistentJobs] unload: forcing license requirements");
                if (!LicenseManager.IsJobLicenseAcquired(JobLicenses.Shunting)) {
                    Debug.LogError("[PersistentJobs] unload: Trying to generate a ShuntingUnload job with " +
                        "forceLicenseReqs=true should never happen if player doesn't have Shunting license!");
                    return null;
                }
                availableCargoGroups
                    = (from cg in availableCargoGroups
                        where LicenseManager.IsLicensedForJob(cg.CargoRequiredLicenses)
                        select cg).ToList();
                countTrainCars
                    = Math.Min(countTrainCars, LicenseManager.GetMaxNumberOfCarsPerJobWithAcquiredJobLicenses());
            }
            if (availableCargoGroups.Count == 0) {
                Debug.LogWarning("[PersistentJobs] unload: no available cargo groups");
                return null;
            }

            CargoGroup chosenCargoGroup = Utilities.GetRandomFromEnumerable(availableCargoGroups, rng);

            // choose cargo & trainCar types
            Debug.Log("[PersistentJobs] unload: choosing cargo & trainCar types");
            List<CargoType> availableCargoTypes = chosenCargoGroup.cargoTypes;
            List<CargoType> orderedCargoTypes = new List<CargoType>();
            List<TrainCarType> orderedTrainCarTypes = new List<TrainCarType>();
            for (int i = 0; i < countTrainCars; i++) {
                CargoType chosenCargoType = Utilities.GetRandomFromEnumerable(availableCargoTypes, rng);
                List<CargoContainerType> availableContainers
                    = CargoTypes.GetCarContainerTypesThatSupportCargoType(chosenCargoType);
                CargoContainerType chosenContainerType = Utilities.GetRandomFromEnumerable(availableContainers, rng);
                List<TrainCarType> availableTrainCarTypes
                    = CargoTypes.GetTrainCarTypesThatAreSpecificContainerType(chosenContainerType);
                TrainCarType chosenTrainCarType = Utilities.GetRandomFromEnumerable(availableTrainCarTypes, rng);
                orderedCargoTypes.Add(chosenCargoType);
                orderedTrainCarTypes.Add(chosenTrainCarType);
            }
            float approxTrainLength = yto.GetTotalCarTypesLength(orderedTrainCarTypes)
                + yto.GetSeparationLengthBetweenCars(countTrainCars);

            // choose starting track
            Debug.Log("[PersistentJobs] unload: choosing starting track");
            Track startingTrack
                = Utilities.GetTrackThatHasEnoughFreeSpace(yto, destinationStation.logicStation.yard.TransferInTracks, approxTrainLength);
            if (startingTrack == null) {
                Debug.LogWarning("[PersistentJobs] unload: Couldn't find startingTrack with enough free space for train!");
                return null;
            }

            // choose random starting station
            // no need to ensure it has has free space; this is just a back story
            Debug.Log("[PersistentJobs] unload: choosing origin (inconsequential)");
            List<StationController> availableOrigins = new List<StationController>(chosenCargoGroup.stations);
            StationController startingStation = Utilities.GetRandomFromEnumerable(availableOrigins, rng);

            // spawn trainCars
            Debug.Log("[PersistentJobs] unload: spawning trainCars");
            RailTrack railTrack = SingletonBehaviour<LogicController>.Instance.LogicToRailTrack[startingTrack];
            List<TrainCar> orderedTrainCars = CarSpawner.SpawnCarTypesOnTrack(
                orderedTrainCarTypes,
                railTrack,
                true,
                0.0,
                false,
                true);
            if (orderedTrainCars == null) {
                Debug.LogWarning("[PersistentJobs] unload: Failed to spawn trainCars!");
                return null;
            }

            JobChainControllerWithEmptyHaulGeneration jcc = GenerateShuntingUnloadJobWithExistingCars(
                startingStation,
                startingTrack,
                destinationStation,
                orderedTrainCars,
                orderedCargoTypes,
                rng,
                true);

            if (jcc == null) {
                Debug.LogWarning("[PersistentJobs] unload: Couldn't generate job chain. Deleting spawned trainCars!");
                SingletonBehaviour<CarSpawner>.Instance.DeleteTrainCars(orderedTrainCars, true);
                return null;
            }

            return jcc;
        }

        public static JobChainControllerWithEmptyHaulGeneration GenerateShuntingUnloadJobWithExistingCars(StationController startingStation,
            Track startingTrack,
            StationController destinationStation,
            List<TrainCar> trainCars,
            List<CargoType> transportedCargoPerCar,
            System.Random rng,
            bool forceCorrectCargoStateOnCars = false) {
            Debug.Log("[PersistentJobs] unload: generating with pre-spawned cars");
            YardTracksOrganizer yto = YardTracksOrganizer.Instance;
            HashSet<CargoContainerType> carContainerTypes = new HashSet<CargoContainerType>();
            foreach (TrainCar trainCar in trainCars) {
                carContainerTypes.Add(CargoTypes.CarTypeToContainerType[trainCar.carType]);
            }
            float approxTrainLength = yto.GetTotalTrainCarsLength(trainCars)
                + yto.GetSeparationLengthBetweenCars(trainCars.Count);

            // choose warehouse machine
            Debug.Log("[PersistentJobs] unload: choosing warehouse machine");
            List<WarehouseMachineController> supportedWMCs = destinationStation.warehouseMachineControllers
                .Where(wm => wm.supportedCargoTypes.Intersect(transportedCargoPerCar).Count() > 0)
                .ToList();
            if (supportedWMCs.Count == 0) {
                Debug.LogWarning(string.Format(
                    "[PersistentJobs] unload: Could not create ChainJob[{0}]: {1} - {2}. Found no supported WarehouseMachine!",
                    JobType.ShuntingLoad,
                    startingStation.logicStation.ID,
                    destinationStation.logicStation.ID));
                return null;
            }
            WarehouseMachine loadMachine = Utilities.GetRandomFromEnumerable(supportedWMCs, rng).warehouseMachine;

            // choose destination tracks
            int maxCountTracks = destinationStation.proceduralJobsRuleset.maxShuntingStorageTracks;
            int countTracks = rng.Next(1, maxCountTracks + 1);

            // bias toward less than max number of tracks for shorter trains
            if (trainCars.Count < 2 * maxCountTracks) {
                countTracks = rng.Next(0, Mathf.FloorToInt(1.5f * maxCountTracks)) % maxCountTracks + 1;
            }
            Debug.Log(string.Format("[PersistentJobs] unload: choosing {0} destination tracks", countTracks));
            List<Track> destinationTracks = new List<Track>();
            do {
                destinationTracks.Clear();
                for (int i = 0; i < countTracks; i++) {
                    Track track = Utilities.GetTrackThatHasEnoughFreeSpace(
                        yto,
                        destinationStation.logicStation.yard.StorageTracks.Except(destinationTracks).ToList(),
                        approxTrainLength / (float)countTracks);
                    if (track == null) {
                        break;
                    }
                    destinationTracks.Add(track);
                }
            } while (destinationTracks.Count < countTracks--);
            if (destinationTracks.Count == 0) {
                Debug.LogWarning(string.Format(
                    "[PersistentJobs] unload: Could not create ChainJob[{0}]: {1} - {2}. " +
                    "Found no StorageTrack with enough free space!",
                    JobType.ShuntingUnload,
                    startingStation.logicStation.ID,
                    destinationStation.logicStation.ID));
                return null;
            }

            // divide trainCars between destination tracks
            int countCarsPerTrainset = trainCars.Count / destinationTracks.Count;
            int countTrainsetsWithExtraCar = trainCars.Count % destinationTracks.Count;
            Debug.Log(string.Format(
                "[PersistentJobs] unload: dividing trainCars {0} per track with {1} extra",
                countCarsPerTrainset,
                countTrainsetsWithExtraCar));
            List<TrainCar> orderedTrainCars = new List<TrainCar>();
            List<CarsPerTrack> carsPerDestinationTrack = new List<CarsPerTrack>();
            for (int i = 0; i < destinationTracks.Count; i++) {
                int rangeStart = i * countCarsPerTrainset + Math.Min(i, countTrainsetsWithExtraCar);
                int rangeCount = i < countTrainsetsWithExtraCar ? countCarsPerTrainset + 1 : countCarsPerTrainset;
                Track destinationTrack = destinationTracks[i];
                carsPerDestinationTrack.Add(
                    new CarsPerTrack(
                        destinationTrack,
                        (from car in trainCars.GetRange(rangeStart, rangeCount) select car.logicCar).ToList()));
            }

            Debug.Log("[PersistentJobs] unload: calculating time/wage/licenses");
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
            JobLicenses requiredLicenses = LicenseManager.GetRequiredLicensesForJobType(JobType.ShuntingUnload)
                | LicenseManager.GetRequiredLicensesForCargoTypes(transportedCargoPerCar)
                | LicenseManager.GetRequiredLicenseForNumberOfTransportedCars(trainCars.Count);
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
            Debug.Log(string.Format(
                "[PersistentJobs] unload: attempting to generate ChainJob[{0}]: {1} - {2}",
                JobType.ShuntingLoad,
                startingStation.logicStation.ID,
                destinationStation.logicStation.ID
            ));
            GameObject gameObject = new GameObject(string.Format(
                "ChainJob[{0}]: {1} - {2}",
                JobType.ShuntingUnload,
                startingStation.logicStation.ID,
                destinationStation.logicStation.ID
            ));
            gameObject.transform.SetParent(destinationStation.transform);
            JobChainControllerWithEmptyHaulGeneration jobChainController
                = new JobChainControllerWithEmptyHaulGeneration(gameObject);
            StationsChainData chainData = new StationsChainData(
                startingStation.stationInfo.YardID,
                destinationStation.stationInfo.YardID);
            jobChainController.trainCarsForJobChain = orderedTrainCars;
            Dictionary<CargoType, List<(TrainCar, float)>> cargoTypeToTrainCarAndAmount
                = new Dictionary<CargoType, List<(TrainCar, float)>>();
            for (int i = 0; i < orderedTrainCars.Count; i++) {
                if (!cargoTypeToTrainCarAndAmount.ContainsKey(orderedCargoTypes[i])) {
                    cargoTypeToTrainCarAndAmount[orderedCargoTypes[i]] = new List<(TrainCar, float)>();
                }
                cargoTypeToTrainCarAndAmount[orderedCargoTypes[i]].Add((orderedTrainCars[i], orderedCargoAmounts[i]));
            }
            List<CarsPerCargoType> unloadData = cargoTypeToTrainCarAndAmount.Select(
                kvPair => new CarsPerCargoType(
                    kvPair.Key,
                    kvPair.Value.Select(t => t.Item1.logicCar).ToList(),
                    kvPair.Value.Aggregate(0.0f, (sum, t) => sum + t.Item2))).ToList();
            StaticShuntingUnloadJobDefinition staticShuntingUnloadJobDefinition
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
            List<(StationController, Track, StationController, List<TrainCar>, List<CargoType>)> jobsToGenerate
                = new List<(StationController, Track, StationController, List<TrainCar>, List<CargoType>)>();

            foreach (StationController destinationStation in cgsPerTcsPerSc.Keys) {
                List<(List<TrainCar>, List<CargoGroup>)> cgsPerTcs = cgsPerTcsPerSc[destinationStation];

                foreach ((List<TrainCar> trainCars, List<CargoGroup> cargoGroups) in cgsPerTcs) {
                    CargoGroup chosenCargoGroup = Utilities.GetRandomFromEnumerable(cargoGroups, rng);
                    StationController startingStation
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
                (StationController ss, Track st, StationController ds, _, _) = definition;
                (_, _, _, List<TrainCar> tcs, List<CargoType> cts) = definition;

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