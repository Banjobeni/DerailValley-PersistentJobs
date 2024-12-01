using System;
using System.Collections.Generic;
using System.Linq;
using DV;
using DV.ThingTypes;
using DV.ThingTypes.TransitionHelpers;
using PersistentJobsMod.Extensions;

namespace PersistentJobsMod.CarSpawningJobGenerators {
    public class CarSpawnGroup {
        public CarSpawnGroup(TrainCarType_v2 trainCarType, IReadOnlyList<(CargoType, TrainCarLivery)> cargoTypesAndLiveries) {
            TrainCarType = trainCarType;
            CargoTypesAndLiveries = cargoTypesAndLiveries;
        }

        public TrainCarType_v2 TrainCarType { get; }
        public IReadOnlyList<(CargoType CargoType, TrainCarLivery CarLivery)> CargoTypesAndLiveries { get; }
    }

    public static class CarSpawnGroupsRandomizer {
        public static List<CarSpawnGroup> GetCarSpawnGroups(List<CargoType> cargoTypes, int carCount, Random random) {
            var chosenCargoTypes = ChooseCargoTypes(cargoTypes, random);
            Main._modEntry.Logger.Log($"CargoCarGroupsRandomizer: chose cargo types ({string.Join("/", chosenCargoTypes)})");

            var carSpawnGroups = Enumerable.Range(0, carCount)
                .Select(_ => random.GetRandomElement(chosenCargoTypes))
                .Select(ct => (CargoType: ct, CarLivery: GetRandomLivery(ct, random)))
                .GroupBy(tuple => tuple.CarLivery.parentType)
                .Select(g => new CarSpawnGroup(g.Key, OrderByDistinctKeyOrder(g, tuple => tuple.CargoType).ToList()))
                .ToList();

            Main._modEntry.Logger.Log($"CargoCarGroupsRandomizer: chose car spawn groups ({string.Join(", ", carSpawnGroups.Select(csg => $"{csg.TrainCarType.name}: ({string.Join(", ", csg.CargoTypesAndLiveries.GroupBy(tuple => tuple.CargoType).Select(g => $"{g.Count()} x {g.Key}" )) })"))})");

            return carSpawnGroups;
        }

        private static IOrderedEnumerable<T> OrderByDistinctKeyOrder<T, TKey>(IEnumerable<T> sequence, Func<T, TKey> keySelector) {
            var list = sequence.ToList();
            var keys = list.Select(keySelector).Distinct().ToList();
            return list.OrderBy(item => keys.IndexOf(keySelector(item)));
        }

        private static TrainCarLivery GetRandomLivery(CargoType cargoType, Random random) {
            var availableTrainCarTypes = Globals.G.Types.CargoToLoadableCarTypes[cargoType.ToV2()];
            var chosenTrainCarType = random.GetRandomElement(availableTrainCarTypes);
            var chosenTrainCarLivery = random.GetRandomElement(chosenTrainCarType.liveries);
            return chosenTrainCarLivery;
        }

        public static IReadOnlyList<CargoType> ChooseCargoTypesForNumberOfCars(IReadOnlyList<CargoType> cargoTypes, int carCount, Random random) {
            var chosenCargoTypes = ChooseCargoTypes(cargoTypes, random);
            return Enumerable.Range(0, carCount)
                .Select(_ => random.GetRandomElement(chosenCargoTypes))
                .OrderBy(ct => chosenCargoTypes.IndexOf(ct))
                .ToList();
        }

        private static List<CargoType> ChooseCargoTypes(IReadOnlyList<CargoType> cargoTypes, Random random) {
            if (random.NextDouble() < 0.5 || cargoTypes.Count == 1) {
                // take only one cargo type
                return new[] { random.GetRandomElement(cargoTypes) }.ToList();
            } else {
                // take 2..all cargo types
                var numberOfCargoTypes = random.Next(cargoTypes.Count - 1) + 2;
                return random.GetMultipleRandomsFromList(cargoTypes, numberOfCargoTypes);
            }
        }
    }
}