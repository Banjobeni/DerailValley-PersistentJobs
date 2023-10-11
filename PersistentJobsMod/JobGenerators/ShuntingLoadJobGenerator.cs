using System.Collections.Generic;
using System.Linq;
using DV.Logic.Job;
using DV.ThingTypes;
using PersistentJobsMod.Utilities;
using UnityEngine;

namespace PersistentJobsMod.JobGenerators {
    static class ShuntingLoadJobGenerator {
        public static JobChainController TryGenerateJobChainController(
                StationController startingStation,
                List<CarsPerTrack> carsPerStartingTrack,
                WarehouseMachine sourceWarehouseMachine,
                StationController destStation,
                List<TrainCar> trainCars,
                List<CargoType> transportedCargoPerCar,
                System.Random random,
                bool forceCorrectCargoStateOnCars = false) {
            Main._modEntry.Logger.Log("load: generating with pre-spawned cars");

            Main._modEntry.Logger.Log("load: calculating time/wage/licenses");

            float bonusTimeLimit;
            float initialWage;
            PaymentAndBonusTimeUtilities.CalculateShuntingBonusTimeLimitAndWage(
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
                sourceWarehouseMachine,
                destStation,
                sourceWarehouseMachine.WarehouseTrack,
                trainCars,
                transportedCargoPerCar,
                Enumerable.Repeat(1.0f, trainCars.Count).ToList(),
                forceCorrectCargoStateOnCars,
                bonusTimeLimit,
                initialWage,
                requiredLicenses
            );
        }

        private static JobChainController GenerateShuntingLoadChainController(
                StationController startingStation,
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
    }
}