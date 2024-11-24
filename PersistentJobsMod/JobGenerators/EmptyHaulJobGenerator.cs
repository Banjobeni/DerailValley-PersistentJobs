﻿using DV.Logic.Job;
using DV.ThingTypes;
using System.Collections.Generic;
using System;
using System.Linq;
using HarmonyLib;
using PersistentJobsMod.Extensions;
using System.Runtime.CompilerServices;
using PersistentJobsMod.Licensing;

namespace PersistentJobsMod.JobGenerators {
    [HarmonyPatch]
    public static class EmptyHaulJobGenerator {
        public static JobChainController GenerateEmptyHaulJobWithExistingCarsOrNull(StationController startingStation, StationController destinationStation, Track startingTrack, IReadOnlyList<TrainCar> trainCars, Random random) {
            var trainCarLiveries = trainCars.Select(tc => tc.carLivery).ToList();

            var trainLength = CarSpawner.Instance.GetTotalCarLiveriesLength(trainCarLiveries);
            var targetTrack = GetTargetTrackOrNull(destinationStation, trainLength, random);
            if (targetTrack == null) {
                return null;
            }

            CalculateBonusTimeLimitAndWage(startingStation, destinationStation, trainCarLiveries, out var bonusTimeLimit, out var initialWage);

            var requiredJobLicenses = LicensesUtilities.GetRequiredJobLicenses(JobType.EmptyHaul, trainCarLiveries.Select(l => l.parentType).ToList(), new List<CargoType>(), trainCars.Count);

            return GenerateEmptyHaulChainController(startingStation, destinationStation, startingTrack, trainCars.ToList(), targetTrack, bonusTimeLimit, initialWage, requiredJobLicenses);
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

        [HarmonyReversePatch]
        [HarmonyPatch(typeof(EmptyHaulJobProceduralGenerator), "CalculateBonusTimeLimitAndWage")]
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void CalculateBonusTimeLimitAndWage(
                StationController startingStation,
                StationController destStation,
                List<TrainCarLivery> transportedCarLiveries,
                out float bonusTimeLimit,
                out float initialWage) {
            throw new NotImplementedException("It's a reverse patch");
        }

        [HarmonyReversePatch]
        [HarmonyPatch(typeof(EmptyHaulJobProceduralGenerator), "GenerateEmptyHaulChainController")]
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static JobChainController GenerateEmptyHaulChainController(
                StationController startingStation,
                StationController destStation,
                Track startingTrack,
                List<TrainCar> trainCars,
                Track destStorageTrack,
                float bonusTimeLimit,
                float initialWage,
                JobLicenses requiredLicenses) {
            throw new NotImplementedException("It's a reverse patch");
        }
    }
}