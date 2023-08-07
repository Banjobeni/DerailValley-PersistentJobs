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
            if (!trainCar.playerSpawnedCar) return;

            trainCar.playerSpawnedCar = false;

            var carStateSave = Traverse.Create(trainCar).Field("carStateSave").GetValue<CarStateSave>();
            if (Traverse.Create(carStateSave).Field("debtTrackerCar").GetValue<DebtTrackerCar>() != null) return;

            var trainPlatesCtrl
                = Traverse.Create(trainCar).Field("trainPlatesCtrl").GetValue<TrainCarPlatesController>();

            var carDamage = Traverse.Create(trainCar).Field("carDmg").GetValue<CarDamageModel>();
            if (carDamage == null) {
                Main._modEntry.Logger.Log($"Creating CarDamageModel for TrainCar[{trainCar.logicCar.ID}]...");
                carDamage = trainCar.gameObject.AddComponent<CarDamageModel>();
                Traverse.Create(trainCar).Field("carDmg").SetValue(carDamage);
                carDamage.OnCreated(trainCar);
                Traverse.Create(trainPlatesCtrl)
                    .Method("UpdateCarHealth", new Type[] { typeof(float) })
                    .GetValue(carDamage.EffectiveHealthPercentage100Notation);
                carDamage.CarEffectiveHealthStateUpdate += carHealthPercentage => Traverse.Create(trainPlatesCtrl)
                    .Method("UpdateCarHealth", new Type[] { typeof(float) })
                    .GetValue(carHealthPercentage);
            }

            var cargoDamage = trainCar.CargoDamage;
            if (cargoDamage == null && !trainCar.IsLoco) {
                Main._modEntry.Logger.Log($"Creating CargoDamageModel for TrainCar[{trainCar.logicCar.ID}]...");
                cargoDamage = trainCar.gameObject.AddComponent<CargoDamageModel>();
                Traverse.Create(trainCar).Property("cargoDamage").SetValue(cargoDamage);
                cargoDamage.OnCreated(trainCar);
                Traverse.Create(trainPlatesCtrl)
                    .Method("UpdateCargoHealth", new Type[] { typeof(float) })
                    .GetValue(cargoDamage.EffectiveHealthPercentage100Notation);
                cargoDamage.CargoEffectiveHealthStateUpdate += cargoHealthPercentage => Traverse.Create(trainPlatesCtrl)
                    .Method("UpdateCargoHealth", new Type[] { typeof(float) })
                    .GetValue(cargoHealthPercentage);
            }

            var carDebtController
                = Traverse.Create(trainCar).Field("carDebtController").GetValue<CarDebtController>();
            carDebtController.SetDebtTracker(carDamage, cargoDamage);

            carStateSave.Initialize(carDamage, cargoDamage);
            carStateSave.SetDebtTrackerCar(carDebtController.CarDebtTracker);

            Main._modEntry.Logger.Log($"Converted player spawned TrainCar {trainCar.logicCar.ID}");
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
            return cargoTypeV2s.Select(ct => ct.v1).ToList();;
        }

        ////// based on StationProceduralJobGenerator.GenerateBaseCargoTrainData
        ////public static (
        ////    List<CarTypesPerCargoType>,
        ////    List<TrainCarType>,
        ////    CargoGroup
        ////    ) GenerateBaseCargoTrainData(int minCountCars,
        ////        int maxCountCars,
        ////        List<CargoGroup> availableCargoGroups,
        ////        System.Random rng) {
        ////    List<CarTypesPerCargoType> carTypesPerCargoTypes = new List<CarTypesPerCargoType>();
        ////    List<TrainCarType> allCarTypes = new List<TrainCarType>();
        ////    int countCarsInTrain = rng.Next(minCountCars, maxCountCars + 1);
        ////    CargoGroup pickedCargoGroup = GetRandomFromEnumerable<CargoGroup>(availableCargoGroups, rng);
        ////    List<CargoType> pickedCargoTypes = pickedCargoGroup.cargoTypes;
        ////    pickedCargoTypes = GetMultipleRandomsFromList<CargoType>(
        ////        pickedCargoTypes,
        ////        Math.Min(countCarsInTrain, rng.Next(1, pickedCargoTypes.Count + 1)),
        ////        rng
        ////    );
        ////    int countCargoTypes = pickedCargoTypes.Count;
        ////    int countCarsPerCargoType = countCarsInTrain / countCargoTypes;
        ////    int countCargoTypesWithExtraCar = countCarsInTrain % countCargoTypes;
        ////    for (int i = 0; i < countCargoTypes; i++) {
        ////        int countCars = i < countCargoTypesWithExtraCar ? countCarsPerCargoType + 1 : countCarsPerCargoType;
        ////        List<CargoContainerType> cargoContainerTypesThatSupportCargoType
        ////            = CargoTypes.GetCarContainerTypesThatSupportCargoType(pickedCargoTypes[i]);
        ////        List<TrainCarType> trainCarTypesThatAreSpecificContainerType
        ////            = CargoTypes.GetTrainCarTypesThatAreSpecificContainerType(
        ////                GetRandomFromEnumerable<CargoContainerType>(cargoContainerTypesThatSupportCargoType, rng)
        ////            );
        ////        List<TrainCarType> trainCarTypes = new List<TrainCarType>();
        ////        for (int j = 0; j < countCars; j++) {
        ////            trainCarTypes.Add(
        ////                GetRandomFromEnumerable<TrainCarType>(trainCarTypesThatAreSpecificContainerType, rng));
        ////        }
        ////        carTypesPerCargoTypes
        ////            .Add(new CarTypesPerCargoType(trainCarTypes, pickedCargoTypes[i], (float)trainCarTypes.Count));
        ////        allCarTypes.AddRange(trainCarTypes);
        ////    }
        ////    return (carTypesPerCargoTypes, allCarTypes, pickedCargoGroup);
        ////}

        ////public static (
        ////    List<CargoType>,
        ////    CargoGroup
        ////    ) GenerateCargoTypesForExistingCars(List<TrainCar> orderedTrainCars,
        ////        List<CargoGroup> availableCargoGroups,
        ////        System.Random rng) {
        ////    List<CarsPerCargoType> carsPerCargoTypes = new List<CarsPerCargoType>();
        ////    List<List<CargoType>> orderedCargoTypesPerTrainCar
        ////        = (from tc in orderedTrainCars select GetCargoTypesForCarType(tc.carType)).ToList();

        ////    // find cargo groups that satisfy at least one cargo type for every train car
        ////    List<CargoGroup> filteredCargoGroups = availableCargoGroups
        ////        .Where(cg => orderedCargoTypesPerTrainCar.All(cts => cts.Intersect(cg.cargoTypes).Count() > 0))
        ////        .ToList();
        ////    if (filteredCargoGroups.Count == 0) {
        ////        return (null, null);
        ////    }
        ////    CargoGroup pickedCargoGroup = GetRandomFromEnumerable<CargoGroup>(filteredCargoGroups, rng);
        ////    List<CargoType> pickedCargoTypes = pickedCargoGroup.cargoTypes;
        ////    List<CargoType> orderedCargoTypes = orderedCargoTypesPerTrainCar.Select(
        ////        cts => GetRandomFromEnumerable<CargoType>(cts.Intersect(pickedCargoTypes).ToList(), rng)
        ////    ).ToList();
        ////    return (orderedCargoTypes, pickedCargoGroup);
        ////}

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
        public static T GetRandomFromEnumerable<T>(IEnumerable<T> list, System.Random rng) {
            return list.ElementAt(rng.Next(0, list.Count()));
        }

        public static T GetRandomElement<T>(this System.Random rng, IReadOnlyList<T> list) {
            var index = rng.Next(0, list.Count);
            return list[index];
        }

        // taken from StationProcedurationJobGenerator.GetMultipleRandomsFromList
        public static List<T> GetMultipleRandomsFromList<T>(List<T> list, int countToGet, System.Random rng) {
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

        public static Track GetTrackThatHasEnoughFreeSpace(YardTracksOrganizer yto, List<Track> tracks, float requiredLength, Random rng) {
            Main._modEntry.Logger.Log("getting random track with free space");
            var tracksWithFreeSpace = yto.FilterOutTracksWithoutRequiredFreeSpace(tracks, requiredLength);
            Main._modEntry.Logger.Log($"{tracksWithFreeSpace.Count}/{tracks.Count} tracks have at least {requiredLength}m available");
            if (tracksWithFreeSpace.Count > 0) {
                return rng.GetRandomElement(tracksWithFreeSpace);
            }
            return null;
        }
    }
}