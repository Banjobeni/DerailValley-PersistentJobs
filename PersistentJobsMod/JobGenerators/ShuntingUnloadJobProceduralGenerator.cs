using System;
using System.Collections.Generic;
using System.Linq;
using DV.Logic.Job;
using DV.ThingTypes;
using UnityEngine;

namespace PersistentJobsMod.JobGenerators {
    static class ShuntingUnloadJobProceduralGenerator {
        public static JobChainController TryGenerateJobChainController(
                StationController startingStation,
                Track startingTrack,
                StationController destinationStation,
                List<TrainCar> trainCars,
                System.Random random,
                bool forceCorrectCargoStateOnCars = false) {
            Main._modEntry.Logger.Log("unload: generating with pre-spawned cars");
            var yto = YardTracksOrganizer.Instance;
            var approxTrainLength = CarSpawner.Instance.GetTotalTrainCarsLength(trainCars, true);

            var transportedCargoPerCar = trainCars.Select(tc => tc.logicCar.CurrentCargoTypeInCar).ToList();

            // TODO: fix intersect
            // choose warehouse machine
            Main._modEntry.Logger.Log("unload: choosing warehouse machine");
            var warehouseMachines = destinationStation.warehouseMachineControllers
                .Where(wm => wm.supportedCargoTypes.Intersect(transportedCargoPerCar).Any())
                .ToList();
            if (warehouseMachines.Count == 0) {
                Debug.LogWarning($"[PersistentJobs] unload: Could not create ChainJob[{JobType.ShuntingLoad}]: {startingStation.logicStation.ID} - {destinationStation.logicStation.ID}. Found no supported WarehouseMachine!");
                return null;
            }
            var loadMachine = random.GetRandomElement(warehouseMachines).warehouseMachine;

            // choose destination tracks
            var maxCountTracks = destinationStation.proceduralJobsRuleset.maxShuntingStorageTracks;
            var countTracks = random.Next(1, maxCountTracks + 1);

            // bias toward less than max number of tracks for shorter trains
            if (trainCars.Count < 2 * maxCountTracks) {
                countTracks = random.Next(0, Mathf.FloorToInt(1.5f * maxCountTracks)) % maxCountTracks + 1;
            }

            if (countTracks > trainCars.Count) {
                countTracks = trainCars.Count;
            }

            Main._modEntry.Logger.Log($"unload: choosing {countTracks} destination tracks");
            var destinationTracks = new List<Track>();
            do {
                destinationTracks.Clear();
                for (var i = 0; i < countTracks; i++) {
                    var track = Utilities.GetRandomHavingSpaceOrLongEnoughTrackOrNull(yto, destinationStation.logicStation.yard.StorageTracks.Except(destinationTracks).ToList(), approxTrainLength / countTracks, random);
                    if (track == null) {
                        break;
                    }
                    destinationTracks.Add(track);
                }
            } while (destinationTracks.Count < countTracks--);
            if (destinationTracks.Count == 0) {
                Debug.LogWarning($"[PersistentJobs] unload: Could not create ChainJob[{JobType.ShuntingUnload}]: {startingStation.logicStation.ID} - {destinationStation.logicStation.ID}. Could not find enough StorageTracks in {destinationStation.logicStation.ID} that are long enough!");
                return null;
            }

            // divide trainCars between destination tracks
            var countCarsPerTrainset = trainCars.Count / destinationTracks.Count;
            var countTrainsetsWithExtraCar = trainCars.Count % destinationTracks.Count;
            Main._modEntry.Logger.Log($"unload: dividing trainCars {countCarsPerTrainset} per track with {countTrainsetsWithExtraCar} extra");

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
                trainCars.Select(tc => tc.carLivery).ToList(),
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

        private static JobChainController GenerateShuntingUnloadChainController(StationController startingStation,
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
                = new JobChainController(gameObject);
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
    }
}