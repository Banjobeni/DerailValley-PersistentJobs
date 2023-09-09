using System;
using System.Collections.Generic;
using System.Linq;
using DV;
using DV.Damage;
using UnityEngine;
using DV.Logic.Job;
using DV.ServicePenalty;
using DV.ThingTypes;
using DV.ThingTypes.TransitionHelpers;
using DV.Utils;
using HarmonyLib;
using Random = System.Random;

namespace PersistentJobsMod {
    static class Utilities {
        public static bool IsPassengerCar(TrainCarType carType) {
            switch (carType) {
                case TrainCarType.PassengerBlue:
                case TrainCarType.PassengerGreen:
                case TrainCarType.PassengerRed:
                    return true;
                default:
                    return false;
            }
        }

        public static void ConvertPlayerSpawnedTrainCar(TrainCar trainCar) {
            if (!trainCar.playerSpawnedCar) {
                return;
            }

            trainCar.playerSpawnedCar = false;

            var carStateSave = Traverse.Create(trainCar).Field("carStateSave").GetValue<CarStateSave>();
            if (Traverse.Create(carStateSave).Field("debtTrackerCar").GetValue<DebtTrackerCar>() != null) {
                return;
            }

            var trainPlatesController = Traverse.Create(trainCar).Field("trainPlatesCtrl").GetValue<TrainCarPlatesController>();

            var carDamageModel = GetOrCreateCarDamageModel(trainCar, trainPlatesController);

            var cargoDamageModelOrNull = GetOrCreateCargoDamageModelOrNull(trainCar, trainPlatesController);

            var carDebtController = Traverse.Create(trainCar).Field("carDebtController").GetValue<CarDebtController>();
            carDebtController.SetDebtTracker(carDamageModel, cargoDamageModelOrNull);

            carStateSave.Initialize(carDamageModel, cargoDamageModelOrNull);
            carStateSave.SetDebtTrackerCar(carDebtController.CarDebtTracker);

            Main._modEntry.Logger.Log($"Converted player spawned TrainCar {trainCar.logicCar.ID}");
        }

        private static CarDamageModel GetOrCreateCarDamageModel(TrainCar trainCar, TrainCarPlatesController trainPlatesController) {
            if (trainCar.CarDamage != null) {
                return trainCar.CarDamage;
            }

            Main._modEntry.Logger.Log($"Creating CarDamageModel for TrainCar[{trainCar.logicCar.ID}]...");

            var carDamageModel = trainCar.gameObject.AddComponent<CarDamageModel>();

            Traverse.Create(trainCar).Field("carDmg").SetValue(carDamageModel);
            carDamageModel.OnCreated(trainCar);

            var updateCarHealthDataMethodTraverse = Traverse.Create(trainPlatesController).Method("UpdateCarHealthData", new Type[] { typeof(float) });
            updateCarHealthDataMethodTraverse.GetValue(carDamageModel.EffectiveHealthPercentage100Notation);
            carDamageModel.CarEffectiveHealthStateUpdate += carHealthPercentage => updateCarHealthDataMethodTraverse.GetValue(carHealthPercentage);

            return carDamageModel;
        }

        private static CargoDamageModel GetOrCreateCargoDamageModelOrNull(TrainCar trainCar, TrainCarPlatesController trainPlatesCtrl) {
            if (trainCar.CargoDamage != null || trainCar.IsLoco) {
                return trainCar.CargoDamage;
            }

            Main._modEntry.Logger.Log($"Creating CargoDamageModel for TrainCar[{trainCar.logicCar.ID}]...");

            var cargoDamageModel = trainCar.gameObject.AddComponent<CargoDamageModel>();

            Traverse.Create(trainCar).Property("cargoDamage").SetValue(cargoDamageModel);
            cargoDamageModel.OnCreated(trainCar);

            var updateCargoHealthDataMethodTraverse = Traverse.Create(trainPlatesCtrl).Method("UpdateCargoHealthData", new Type[] { typeof(float) });
            updateCargoHealthDataMethodTraverse.GetValue(cargoDamageModel.EffectiveHealthPercentage100Notation);
            cargoDamageModel.CargoEffectiveHealthStateUpdate += cargoHealthPercentage => updateCargoHealthDataMethodTraverse.GetValue(cargoHealthPercentage);

            return cargoDamageModel;
        }

        // taken from JobChainControllerWithEmptyHaulGeneration.ExtractCorrespondingTrainCars
        public static List<TrainCar> ExtractCorrespondingTrainCars(JobChainController context, List<Car> logicCars) {
            if (logicCars == null || logicCars.Count == 0) {
                return null;
            }
            var list = new List<TrainCar>();
            for (var i = 0; i < logicCars.Count; i++) {
                for (var j = 0; j < context.trainCarsForJobChain.Count; j++) {
                    if (context.trainCarsForJobChain[j].logicCar == logicCars[i]) {
                        list.Add(context.trainCarsForJobChain[j]);
                        break;
                    }
                }
            }
            if (list.Count != logicCars.Count) {
                return null;
            }
            return list;
        }

        // based off EmptyHaulJobProceduralGenerator.CalculateBonusTimeLimitAndWage
        public static void CalculateTransportBonusTimeLimitAndWage(JobType jobType,
            StationController startingStation,
            StationController destStation,
            List<TrainCarType> transportedCarTypes,
            List<CargoType> transportedCargoTypes,
            out float bonusTimeLimit,
            out float initialWage) {
            var distanceBetweenStations
                = JobPaymentCalculator.GetDistanceBetweenStations(startingStation, destStation);
            bonusTimeLimit = JobPaymentCalculator.CalculateHaulBonusTimeLimit(distanceBetweenStations, false);
            initialWage = JobPaymentCalculator.CalculateJobPayment(
                jobType,
                distanceBetweenStations,
                ExtractPaymentCalculationData(transportedCarTypes, transportedCargoTypes)
            );
        }

        public static void CalculateShuntingBonusTimeLimitAndWage(JobType jobType,
            int numberOfTracks,
            List<TrainCarType> transportedCarTypes,
            List<CargoType> transportedCargoTypes,
            out float bonusTimeLimit,
            out float initialWage) {
            // scalar value 500 taken from StationProceduralJobGenerator
            var distance = (float)numberOfTracks * 500f;
            bonusTimeLimit = JobPaymentCalculator.CalculateShuntingBonusTimeLimit(numberOfTracks);
            initialWage = JobPaymentCalculator.CalculateJobPayment(
                jobType,
                distance,
                ExtractPaymentCalculationData(transportedCarTypes, transportedCargoTypes)
            );
        }

        // based off EmptyHaulJobProceduralGenerator.ExtractEmptyHaulPaymentCalculationData
        private static PaymentCalculationData ExtractPaymentCalculationData(List<TrainCarType> orderedCarTypes,
            List<CargoType> orderedCargoTypes) {
            if (orderedCarTypes == null) {
                return null;
            }
            var trainCarTypeToCount = new Dictionary<TrainCarLivery, int>();
            foreach (var trainCarType in orderedCarTypes) {
                var trainCarLivery = trainCarType.ToV2();
                if (!trainCarTypeToCount.ContainsKey(trainCarLivery)) {
                    trainCarTypeToCount[trainCarLivery] = 0;
                }
                trainCarTypeToCount[trainCarLivery] += 1;
            }
            var cargoTypeToCount = new Dictionary<CargoType, int>();
            foreach (var cargoType in orderedCargoTypes) {
                if (!cargoTypeToCount.ContainsKey(cargoType)) {
                    cargoTypeToCount[cargoType] = 0;
                }
                cargoTypeToCount[cargoType] += 1;
            }
            return new PaymentCalculationData(trainCarTypeToCount, cargoTypeToCount);
        }

        public static List<CargoType> GetCargoTypesForCarType(TrainCarType trainCarType) {
            var trainCarTypeV2 = trainCarType.ToV2().parentType;
            var cargoTypeV2s = Globals.G.Types.CarTypeToLoadableCargo[trainCarTypeV2];
            return cargoTypeV2s.Select(ct => ct.v1).ToList();
        }

        public static void TaskDoDFS(Task task, Action<Task> action) {
            if (task is ParallelTasks || task is SequentialTasks) {
                Traverse.Create(task)
                    .Field("tasks")
                    .GetValue<IEnumerable<Task>>()
                    .Do(t => TaskDoDFS(t, action));
            }
            action(task);
        }

        public static bool TaskAnyDFS(Task task, Func<Task, bool> predicate) {
            if (task is ParallelTasks || task is SequentialTasks) {
                return Traverse.Create(task)
                    .Field("tasks")
                    .GetValue<IEnumerable<Task>>()
                    .Any(t => TaskAnyDFS(t, predicate));
            }
            return predicate(task);
        }

        public static Task TaskFindDFS(Task task, Func<Task, bool> predicate) {
            if (task is ParallelTasks || task is SequentialTasks) {
                return Traverse.Create(task)
                    .Field("tasks")
                    .GetValue<IEnumerable<Task>>()
                    .Aggregate(null as Task, (found, t) => found == null ? TaskFindDFS(t, predicate) : found);
            }
            return predicate(task) ? task : null;
        }

        // taken from StationProcedurationJobGenerator.GetRandomFromList
        public static T GetRandomFromEnumerable<T>(IEnumerable<T> list, Random rng) {
            return list.ElementAt(rng.Next(0, list.Count()));
        }

        public static T GetRandomElement<T>(this Random rng, IReadOnlyList<T> list) {
            var index = rng.Next(0, list.Count);
            return list[index];
        }

        // taken from StationProcedurationJobGenerator.GetMultipleRandomsFromList
        public static List<T> GetMultipleRandomsFromList<T>(this Random rng, List<T> list, int countToGet) {
            var list2 = new List<T>(list);
            if (countToGet > list2.Count) {
                Debug.LogError("Trying to get more random items from list than it contains. Returning all items from list.");
                return list2;
            }
            var list3 = new List<T>();
            for (var i = 0; i < countToGet; i++) {
                var index = rng.Next(0, list2.Count);
                list3.Add(list2[index]);
                list2.RemoveAt(index);
            }
            return list3;
        }

        public static List<T> GetRandomPermutation<T>(this Random rng, List<T> list) {
            return GetMultipleRandomsFromList(rng, list, list.Count);
        }

        public static Track GetTrackThatHasEnoughFreeSpace(YardTracksOrganizer yto, List<Track> tracks, float requiredLength, Random rng) {
            Main._modEntry.Logger.Log("getting random track with free space");
            var tracksWithFreeSpace = yto.FilterOutTracksWithoutRequiredFreeSpace(tracks, requiredLength);
            Main._modEntry.Logger.Log($"{tracksWithFreeSpace.Count}/{tracks.Count} tracks have at least {requiredLength}m available");
            if (tracksWithFreeSpace.Count > 0) {
                return rng.GetRandomElement(tracksWithFreeSpace);
            }
            return null;
        }

        public static List<TrainCar> FilterOutTrainCarsWhereOnlyPartOfConsistIsToBeDeleted(List<TrainCar> registeredToDeleteTrainCars) {
            var allowedToDeleteTrainCars = new List<TrainCar>();

            foreach (var trainSetGroup in registeredToDeleteTrainCars.GroupBy(tc => tc.trainset)) {
                var trainSet = trainSetGroup.Key;
                var deletableTrainCars = trainSetGroup.ToHashSet();

                var joblessTrainCarsOfTrainSet = trainSet.cars.Where(tc => CarTypes.IsRegularCar(tc.carLivery) && SingletonBehaviour<JobsManager>.Instance.GetJobOfCar(tc) == null).ToHashSet();

                var joblessButNotRegisteredForDeleteCars = joblessTrainCarsOfTrainSet.Except(deletableTrainCars).ToList();

                if (joblessButNotRegisteredForDeleteCars.Any()) {
                    Main._modEntry.Logger.Log($"Prevented reassigning the train cars {string.Join(", ", deletableTrainCars.Select(tc => tc.ID))} to new jobs because the other jobless train cars in the same consist {string.Join(", ", joblessButNotRegisteredForDeleteCars.Select(tc => tc.ID))} are not registered for deletion yet");
                } else {
                    allowedToDeleteTrainCars.AddRange(deletableTrainCars);
                }
            }

            return allowedToDeleteTrainCars;
        }
    }
}