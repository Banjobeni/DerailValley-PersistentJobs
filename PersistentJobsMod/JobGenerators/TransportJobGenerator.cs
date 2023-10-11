using System.Collections.Generic;
using System.Linq;
using DV.Logic.Job;
using DV.ThingTypes;
using PersistentJobsMod.Utilities;
using UnityEngine;

namespace PersistentJobsMod.JobGenerators {
    static class TransportJobGenerator {
        public static JobChainController TryGenerateJobChainController(
                StationController startingStation,
                Track startingTrack,
                StationController destStation,
                IReadOnlyList<TrainCar> trainCars,
                List<CargoType> transportedCargoPerCar,
                System.Random random,
                bool forceCorrectCargoStateOnCars = false) {
            Main._modEntry.Logger.Log("transport: generating with pre-spawned cars");
            var yto = YardTracksOrganizer.Instance;

            Main._modEntry.Logger.Log("transport: choosing destination track");
            var approxTrainLength = CarSpawner.Instance.GetTotalTrainCarsLength(trainCars.ToList(), true);
            var destinationTrack = TrackUtilities.GetRandomHavingSpaceOrLongEnoughTrackOrNull(yto, destStation.logicStation.yard.TransferInTracks, approxTrainLength, random);

            if (destinationTrack == null) {
                Debug.LogWarning($"[PersistentJobs] transport: Could not create ChainJob[{JobType.Transport}]: {startingStation.logicStation.ID} - {destStation.logicStation.ID}. Could not find any TransferInTrack in {destStation.logicStation.ID} that is long enough!");
                return null;
            }

            var transportedCarLiveries = trainCars.Select(tc => tc.carLivery).ToList();

            Main._modEntry.Logger.Log("transport: calculating time/wage/licenses");
            float bonusTimeLimit;
            float initialWage;
            PaymentAndBonusTimeUtilities.CalculateTransportBonusTimeLimitAndWage(
                JobType.Transport,
                startingStation,
                destStation,
                transportedCarLiveries,
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
                trainCars.ToList(),
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
            Main._modEntry.Logger.Log($"transport: attempting to generate ChainJob[{JobType.ShuntingLoad}]: {startingStation.logicStation.ID} - {destStation.logicStation.ID}");
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
    }
}