using System;
using System.Collections.Generic;
using System.Linq;
using DV;
using UnityEngine;
using DV.Logic.Job;
using DV.ThingTypes;
using DV.ThingTypes.TransitionHelpers;
using DV.Utils;

namespace PersistentJobsMod {
    class TransportJobProceduralGenerator {
        public static JobChainControllerWithEmptyHaulGeneration GenerateTransportJobWithCarSpawning(StationController startingStation,
            bool forceLicenseReqs,
            System.Random rng) {
            Debug.Log("[PersistentJobs] transport: generating with car spawning");
            var yto = YardTracksOrganizer.Instance;
            var availableCargoGroups = startingStation.proceduralJobsRuleset.outputCargoGroups;
            var countTrainCars = rng.Next(
                startingStation.proceduralJobsRuleset.minCarsPerJob,
                startingStation.proceduralJobsRuleset.maxCarsPerJob);

            if (forceLicenseReqs) {
                Debug.Log("[PersistentJobs] transport: forcing license requirements");
                if (!LicenseManager.Instance.IsJobLicenseAcquired(JobLicenses.FreightHaul.ToV2())) {
                    Debug.LogError("[PersistentJobs] transport: Trying to generate a Transport job with " +
                        "forceLicenseReqs=true should never happen if player doesn't have FreightHaul license!");
                    return null;
                }
                availableCargoGroups
                    = (from cg in availableCargoGroups
                        where LicenseManager.Instance.IsLicensedForJob(JobLicenseType_v2.ToV2List(cg.CargoRequiredLicenses))
                        select cg).ToList();
                countTrainCars = Math.Min(countTrainCars, LicenseManager.Instance.GetMaxNumberOfCarsPerJobWithAcquiredJobLicenses());
            }
            if (availableCargoGroups.Count == 0) {
                Debug.LogWarning("[PersistentJobs] transport: no available cargo groups");
                return null;
            }

            var chosenCargoGroup = Utilities.GetRandomFromEnumerable(availableCargoGroups, rng);

            // choose cargo & trainCar types
            Debug.Log("[PersistentJobs] transport: choosing cargo & trainCar types");
            var availableCargoTypes = chosenCargoGroup.cargoTypes;
            var orderedCargoTypes = new List<CargoType>();
            var orderedTrainCarLiveries = new List<TrainCarLivery>();
            for (var i = 0; i < countTrainCars; i++) {
                var chosenCargoType = Utilities.GetRandomFromEnumerable(availableCargoTypes, rng);
                var availableTrainCarTypes = Globals.G.Types.CargoToLoadableCarTypes[chosenCargoType.ToV2()];
                var chosenTrainCarType = Utilities.GetRandomFromEnumerable(availableTrainCarTypes, rng);
                var chosenTrainCarLivery = Utilities.GetRandomFromEnumerable(chosenTrainCarType.liveries, rng);
                //List<CargoContainerType> availableContainers
                //    = CargoTypes.GetCarContainerTypesThatSupportCargoType(chosenCargoType);
                //CargoContainerType chosenContainerType = Utilities.GetRandomFromEnumerable(availableContainers, rng);
                //List<TrainCarType> availableTrainCarTypes
                //    = CargoTypes.GetTrainCarTypesThatAreSpecificContainerType(chosenContainerType);
                //TrainCarType chosenTrainCarType = Utilities.GetRandomFromEnumerable(availableTrainCarTypes, rng);
                orderedCargoTypes.Add(chosenCargoType);
                orderedTrainCarLiveries.Add(chosenTrainCarLivery);
            }
            var approxTrainLength = CarSpawner.Instance.GetTotalCarLiveriesLength(orderedTrainCarLiveries, true);

            // choose starting track
            Debug.Log("[PersistentJobs] transport: choosing starting track");
            var startingTrack
                = Utilities.GetTrackThatHasEnoughFreeSpace(yto, startingStation.logicStation.yard.TransferOutTracks, approxTrainLength, rng);
            if (startingTrack == null) {
                Debug.LogWarning("[PersistentJobs] transport: Couldn't find startingTrack with enough free space for train!");
                return null;
            }

            // choose random destination station that has at least 1 available track
            Debug.Log("[PersistentJobs] transport: choosing destination");
            var availableDestinations = new List<StationController>(chosenCargoGroup.stations);
            StationController destStation = null;
            Track destinationTrack = null;
            while (availableDestinations.Count > 0 && destinationTrack == null) {
                destStation = Utilities.GetRandomFromEnumerable(availableDestinations, rng);
                availableDestinations.Remove(destStation);
                destinationTrack = Utilities.GetTrackThatHasEnoughFreeSpace(yto, yto.FilterOutOccupiedTracks(destStation.logicStation.yard.TransferInTracks), approxTrainLength, rng);
            }
            if (destinationTrack == null) {
                Debug.LogWarning("[PersistentJobs] transport: Couldn't find a station with enough free space for train!");
                return null;
            }

            // spawn trainCars
            Debug.Log("[PersistentJobs] transport: spawning trainCars");
            var railTrack = SingletonBehaviour<LogicController>.Instance.LogicToRailTrack[startingTrack];
            var carOrientations = Enumerable.Range(0, orderedTrainCarLiveries.Count).Select(_ => rng.Next(2) > 0).ToList();
            var orderedTrainCars = CarSpawner.Instance.SpawnCarTypesOnTrack(
                orderedTrainCarLiveries,
                carOrientations,
                railTrack,
                true,
                true,
                0.0,
                false,
                false);
            if (orderedTrainCars == null) {
                Debug.LogWarning("[PersistentJobs] transport: Failed to spawn trainCars!");
                return null;
            }

            var jcc = GenerateTransportJobWithExistingCars(
                startingStation,
                startingTrack,
                destStation,
                orderedTrainCars,
                orderedCargoTypes,
                rng,
                true);

            if (jcc == null) {
                Debug.LogWarning("[PersistentJobs] transport: Couldn't generate job chain. Deleting spawned trainCars!");
                SingletonBehaviour<CarSpawner>.Instance.DeleteTrainCars(orderedTrainCars, true);
                return null;
            }

            return jcc;
        }

        public static JobChainControllerWithEmptyHaulGeneration GenerateTransportJobWithExistingCars(StationController startingStation,
            Track startingTrack,
            StationController destStation,
            List<TrainCar> trainCars,
            List<CargoType> transportedCargoPerCar,
            System.Random rng,
            bool forceCorrectCargoStateOnCars = false) {
            Debug.Log("[PersistentJobs] transport: generating with pre-spawned cars");
            var yto = YardTracksOrganizer.Instance;

            Debug.Log("[PersistentJobs] transport: choosing destination track");
            var approxTrainLength = CarSpawner.Instance.GetTotalTrainCarsLength(trainCars, true);
            var destinationTrack = Utilities.GetTrackThatHasEnoughFreeSpace(yto, yto.FilterOutOccupiedTracks(destStation.logicStation.yard.TransferInTracks), approxTrainLength, rng);
            if (destinationTrack == null) {
                destinationTrack = Utilities.GetTrackThatHasEnoughFreeSpace(yto, destStation.logicStation.yard.TransferInTracks, approxTrainLength, rng);
            }
            if (destinationTrack == null) {
                Debug.LogWarning(string.Format(
                    "[PersistentJobs] transport: Could not create ChainJob[{0}]: {1} - {2}. " +
                    "Found no TransferInTrack with enough free space!",
                    JobType.Transport,
                    startingStation.logicStation.ID,
                    destStation.logicStation.ID
                ));
                return null;
            }
            var transportedCarTypes = (from tc in trainCars select tc.carType)
                .ToList<TrainCarType>();

            Debug.Log("[PersistentJobs] transport: calculating time/wage/licenses");
            float bonusTimeLimit;
            float initialWage;
            Utilities.CalculateTransportBonusTimeLimitAndWage(
                JobType.Transport,
                startingStation,
                destStation,
                transportedCarTypes,
                transportedCargoPerCar,
                out bonusTimeLimit,
                out initialWage
            );
            var requiredLicenses = JobLicenseType_v2.ListToFlags(LicenseManager.Instance.GetRequiredLicensesForJobType(JobType.Transport))
                | JobLicenseType_v2.ListToFlags(LicenseManager.Instance.GetRequiredLicensesForCargoTypes(transportedCargoPerCar))
                | (LicenseManager.Instance.GetRequiredLicenseForNumberOfTransportedCars(trainCars.Count)?.v1 ?? JobLicenses.Basic);
            return TransportJobProceduralGenerator.GenerateTransportChainController(
                startingStation,
                startingTrack,
                destStation,
                destinationTrack,
                trainCars,
                transportedCargoPerCar,
                trainCars.Select(
                    tc => tc.logicCar.CurrentCargoTypeInCar == CargoType.None ? 1.0f : tc.logicCar.LoadedCargoAmount).ToList(),
                forceCorrectCargoStateOnCars,
                bonusTimeLimit,
                initialWage,
                requiredLicenses
            );
        }

        private static JobChainControllerWithEmptyHaulGeneration GenerateTransportChainController(StationController startingStation,
            Track startingTrack,
            StationController destStation,
            Track destTrack,
            List<TrainCar> orderedTrainCars,
            List<CargoType> orderedCargoTypes,
            List<float> orderedCargoAmounts,
            bool forceCorrectCargoStateOnCars,
            float bonusTimeLimit,
            float initialWage,
            JobLicenses requiredLicenses) {
            Debug.Log(string.Format(
                "[PersistentJobs] transport: attempting to generate ChainJob[{0}]: {1} - {2}",
                JobType.ShuntingLoad,
                startingStation.logicStation.ID,
                destStation.logicStation.ID
            ));
            var gameObject = new GameObject(string.Format(
                "ChainJob[{0}]: {1} - {2}",
                JobType.Transport,
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
            var orderedLogicCars = TrainCar.ExtractLogicCars(orderedTrainCars);
            var staticTransportJobDefinition
                = gameObject.AddComponent<StaticTransportJobDefinition>();
            staticTransportJobDefinition.PopulateBaseJobDefinition(
                startingStation.logicStation,
                bonusTimeLimit,
                initialWage,
                chainData,
                requiredLicenses
            );
            staticTransportJobDefinition.startingTrack = startingTrack;
            staticTransportJobDefinition.destinationTrack = destTrack;
            staticTransportJobDefinition.trainCarsToTransport = orderedLogicCars;
            staticTransportJobDefinition.transportedCargoPerCar = orderedCargoTypes;
            staticTransportJobDefinition.cargoAmountPerCar = orderedCargoAmounts;
            staticTransportJobDefinition.forceCorrectCargoStateOnCars = forceCorrectCargoStateOnCars;
            jobChainController.AddJobDefinitionToChain(staticTransportJobDefinition);
            return jobChainController;
        }

        public static List<(StationController, Track, StationController, List<TrainCar>, List<CargoType>)>
            ComputeJobInfosFromCargoGroupsPerTrainCarSetPerStation(Dictionary<StationController, List<(List<TrainCar>, List<CargoGroup>)>> cgsPerTcsPerSc,
                System.Random rng) {
            var jobsToGenerate
                = new List<(StationController, Track, StationController, List<TrainCar>, List<CargoType>)>();

            foreach (var startingStation in cgsPerTcsPerSc.Keys) {
                var cgsPerTcs = cgsPerTcsPerSc[startingStation];

                foreach ((var trainCars, var cargoGroups) in cgsPerTcs) {
                    var chosenCargoGroup = Utilities.GetRandomFromEnumerable(cargoGroups, rng);
                    var destinationStation
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

                return (JobChainController)GenerateTransportJobWithExistingCars(
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