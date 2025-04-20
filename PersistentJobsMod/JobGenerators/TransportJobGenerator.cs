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
                StationController destinationStation,
                IReadOnlyList<TrainCar> trainCars,
                List<CargoType> transportedCargoPerCar,
                System.Random random,
                bool forceCorrectCargoStateOnCars = false) {
            Main._modEntry.Logger.Log($"transport: attempting to generate {JobType.Transport} job from {startingStation.logicStation.ID} to {destinationStation.logicStation.ID} for {trainCars.Count} cars");
            var yto = YardTracksOrganizer.Instance;
            List<Car> logicCars = TrainCar.ExtractLogicCars((List<TrainCar>)trainCars);
            var approxTrainLength = CarSpawner.Instance.GetTotalTrainCarsLength(logicCars.ToList(), true);
            var destinationTrack = TrackUtilities.GetRandomHavingSpaceOrLongEnoughTrackOrNull(yto, destinationStation.logicStation.yard.TransferInTracks, approxTrainLength, random);

            if (destinationTrack == null) {
                Debug.LogWarning($"[PersistentJobs] transport: Could not create ChainJob[{JobType.Transport}]: {startingStation.logicStation.ID} - {destinationStation.logicStation.ID}. Could not find any TransferInTrack in {destinationStation.logicStation.ID} that is long enough!");
                return null;
            }

            var transportedCarLiveries = trainCars.Select(tc => tc.carLivery).ToList();

            float bonusTimeLimit;
            float initialWage;
            PaymentAndBonusTimeUtilities.CalculateTransportBonusTimeLimitAndWage(
                JobType.Transport,
                startingStation,
                destinationStation,
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
                destinationStation,
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
            StationController destinationStation,
            Track destTrack,
            List<TrainCar> orderedTrainCars,
            List<CargoType> orderedCargoTypes,
            List<float> orderedCargoAmounts,
            bool forceCorrectCargoStateOnCars,
            float bonusTimeLimit,
            float initialWage,
            JobLicenses requiredLicenses) {

            var gameObject = new GameObject($"ChainJob[{JobType.Transport}]: {startingStation.logicStation.ID} - {destinationStation.logicStation.ID}");
            gameObject.transform.SetParent(startingStation.transform);
            var jobChainController
                = new JobChainController(gameObject);
            var chainData = new StationsChainData(
                startingStation.stationInfo.YardID,
                destinationStation.stationInfo.YardID
            );
            var orderedLogicCars = jobChainController.carsForJobChain;
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
            staticTransportJobDefinition.carsToTransport = orderedLogicCars;
            staticTransportJobDefinition.transportedCargoPerCar = orderedCargoTypes;
            staticTransportJobDefinition.cargoAmountPerCar = orderedCargoAmounts;
            staticTransportJobDefinition.forceCorrectCargoStateOnCars = forceCorrectCargoStateOnCars;
            jobChainController.AddJobDefinitionToChain(staticTransportJobDefinition);

            return jobChainController;
        }
    }
}