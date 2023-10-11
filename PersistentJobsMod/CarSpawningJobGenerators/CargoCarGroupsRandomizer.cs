using System;
using System.Collections.Generic;
using System.Linq;
using DV;
using DV.ThingTypes;
using DV.ThingTypes.TransitionHelpers;
using PersistentJobsMod.Extensions;

namespace PersistentJobsMod.CarSpawningJobGenerators {
    public static class CargoCarGroupsRandomizer {
        public static List<CargoCarGroup> GetCargoCarGroups(List<CargoType> cargoTypes, int carCount, Random random) {
            var chosenCargoTypes = ChooseCargoTypes(cargoTypes, random);
            Main._modEntry.Logger.Log($"CargoCarGroupsRandomizer: chose cargo types ({string.Join("/", chosenCargoTypes)})");

            var cargoCarGroups = ChooseCargoCarGroups(chosenCargoTypes, carCount, random);
            Main._modEntry.Logger.Log($"CargoCarGroupsRandomizer: chose cargo car groups ({string.Join(", ", cargoCarGroups.Select(g => $"{g.CarLiveries.Count} x {g.CargoType}"))})");

            return cargoCarGroups;
        }

        public static IReadOnlyList<CargoType> ChooseCargoTypesForNumberOfCars(IReadOnlyList<CargoType> cargoTypes, int carCount, Random random) {
            var chosenCargoTypes = ChooseCargoTypes(cargoTypes, random);
            return Enumerable.Range(0, carCount)
                .Select(_ => random.GetRandomElement(chosenCargoTypes))
                .OrderBy(ct => chosenCargoTypes.IndexOf(ct))
                .ToList();
        }

        private static List<CargoCarGroup> ChooseCargoCarGroups(List<CargoType> cargoTypes, int carCount, Random random) {
            return Enumerable.Range(0, carCount)
                .Select(_ => random.GetRandomElement(cargoTypes))
                .GroupBy(ct => ct, ct => GetRandomLivery(ct, random), (type, liveries) => new CargoCarGroup(type, liveries.ToList()))
                .OrderBy(ccg => cargoTypes.IndexOf(ccg.CargoType))
                .ToList();
        }

        private static TrainCarLivery GetRandomLivery(CargoType cargoType, Random random) {
            var availableTrainCarTypes = Globals.G.Types.CargoToLoadableCarTypes[cargoType.ToV2()];
            var chosenTrainCarType = random.GetRandomElement(availableTrainCarTypes);
            var chosenTrainCarLivery = random.GetRandomElement(chosenTrainCarType.liveries);
            return chosenTrainCarLivery;
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