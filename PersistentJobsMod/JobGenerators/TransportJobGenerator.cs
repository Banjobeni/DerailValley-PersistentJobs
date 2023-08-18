using System.Collections.Generic;
using System.Linq;
using DV.Logic.Job;
using DV.ThingTypes;
using UnityEngine;

namespace PersistentJobsMod.JobGenerators {
    static class TransportJobGenerator {
        public static JobChainController TryGenerateJobChainController(StationController startingStation,
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
            return GenerateTransportChainController(
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

        private static JobChainController GenerateTransportChainController(StationController startingStation,
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
                = new JobChainController(gameObject);
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

                return (JobChainController)TryGenerateJobChainController(
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