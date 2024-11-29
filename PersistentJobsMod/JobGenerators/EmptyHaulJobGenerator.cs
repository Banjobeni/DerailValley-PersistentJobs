using DV.Logic.Job;
using DV.ThingTypes;
using System.Collections.Generic;
using System;
using System.Linq;
using HarmonyLib;
using PersistentJobsMod.Extensions;
using PersistentJobsMod.Licensing;

namespace PersistentJobsMod.JobGenerators {
    [HarmonyPatch]
    public static class EmptyHaulJobGenerator {
        public static JobChainController GenerateEmptyHaulJobWithExistingCarsOrNull(StationController startingStation, StationController destinationStation, Track startingTrack, IReadOnlyList<TrainCar> trainCars, Random random) {
            Main._modEntry.Logger.Log($"empty haul: attempting to generate {JobType.EmptyHaul} job from {startingStation.logicStation.ID} to {destinationStation.logicStation.ID} for {trainCars.Count} cars");

            var trainCarLiveries = trainCars.Select(tc => tc.carLivery).ToList();

            var trainLength = CarSpawner.Instance.GetTotalCarLiveriesLength(trainCarLiveries);
            var targetTrack = GetTargetTrackOrNull(destinationStation, trainLength, random);
            if (targetTrack == null) {
                return null;
            }

            EmptyHaulJobProceduralGenerator.CalculateBonusTimeLimitAndWage(startingStation, destinationStation, trainCarLiveries, out var bonusTimeLimit, out var initialWage);

            var requiredJobLicenses = LicensesUtilities.GetRequiredJobLicenses(JobType.EmptyHaul, trainCarLiveries.Select(l => l.parentType).ToList(), new List<CargoType>(), trainCars.Count);

            var jobChainController = EmptyHaulJobProceduralGenerator.GenerateEmptyHaulChainController(startingStation, destinationStation, startingTrack, trainCars.ToList(), targetTrack, bonusTimeLimit, initialWage, requiredJobLicenses);

            return jobChainController;
        }

        private static Track GetTargetTrackOrNull(StationController destinationStation, float trainLength, Random random) {
            var storageTracks = destinationStation.logicStation.yard.StorageTracks.Where(t => t.GetTotalUsableTrackLength() > trainLength).ToList();
            var freeEnoughStorageTracks = storageTracks.Where(t => YardTracksOrganizer.Instance.GetFreeSpaceOnTrack(t) > trainLength).ToList();
            if (freeEnoughStorageTracks.Any()) {
                return random.GetRandomElement(freeEnoughStorageTracks);
            }
            if (storageTracks.Any()) {
                return random.GetRandomElement(storageTracks);
            }
            return null;
        }
    }
}