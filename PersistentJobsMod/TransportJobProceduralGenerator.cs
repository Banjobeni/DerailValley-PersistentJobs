using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using DV.Logic.Job;
using DV.ThingTypes;
using DV.Utils;
using PersistentJobsMod.CarSpawningJobGenerators;
using PersistentJobsMod.Licensing;

namespace PersistentJobsMod {
    class TransportJobProceduralGenerator {
        public static JobChainControllerWithEmptyHaulGeneration GenerateTransportJobWithCarSpawning(StationController startingStation, bool requirePlayerLicensesCompatible, System.Random random) {
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

            var jcc = GenerateTransportJobWithExistingCars(
                startingStation,
                startingTrack,
                destinationStation,
                orderedTrainCars,
                cargoCarGroups.SelectMany(c => Enumerable.Repeat(c.CargoType, c.CarLiveries.Count)).ToList(),
                random,
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
            Main._modEntry.Logger.Log("transport: generating with pre-spawned cars");
            var yto = YardTracksOrganizer.Instance;

            Main._modEntry.Logger.Log("transport: choosing destination track");
            var approxTrainLength = CarSpawner.Instance.GetTotalTrainCarsLength(trainCars, true);
            var destinationTrack = Utilities.GetTrackThatHasEnoughFreeSpace(yto, yto.FilterOutOccupiedTracks(destStation.logicStation.yard.TransferInTracks), approxTrainLength, rng);
            if (destinationTrack == null) {
                destinationTrack = Utilities.GetTrackThatHasEnoughFreeSpace(yto, destStation.logicStation.yard.TransferInTracks, approxTrainLength, rng);
            }
            if (destinationTrack == null) {
                Debug.LogWarning($"[PersistentJobs] transport: Could not create ChainJob[{JobType.Transport}]: {startingStation.logicStation.ID} - {destStation.logicStation.ID}. " + "Found no TransferInTrack with enough free space!");
                return null;
            }
            var transportedCarTypes = (from tc in trainCars select tc.carType)
                .ToList<TrainCarType>();

            Main._modEntry.Logger.Log("transport: calculating time/wage/licenses");
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
            Main._modEntry.Logger.Log("transport: attempting to generate ChainJob[{JobType.ShuntingLoad}]: {startingStation.logicStation.ID} - {destStation.logicStation.ID}");
            var gameObject = new GameObject($"ChainJob[{JobType.Transport}]: {startingStation.logicStation.ID} - {destStation.logicStation.ID}");
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