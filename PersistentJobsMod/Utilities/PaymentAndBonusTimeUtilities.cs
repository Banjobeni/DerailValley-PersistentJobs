using System.Collections.Generic;
using DV.ThingTypes;

namespace PersistentJobsMod.Utilities {
    public static class PaymentAndBonusTimeUtilities {
        /// <summary>based off EmptyHaulJobProceduralGenerator.CalculateBonusTimeLimitAndWage</summary>
        public static void CalculateTransportBonusTimeLimitAndWage(JobType jobType,
            StationController startingStation,
            StationController destStation,
            List<TrainCarLivery> transportedCarLiveries,
            List<CargoType> transportedCargoTypes,
            out float bonusTimeLimit,
            out float initialWage) {
            var distanceBetweenStations
                = JobPaymentCalculator.GetDistanceBetweenStations(startingStation, destStation);
            bonusTimeLimit = JobPaymentCalculator.CalculateHaulBonusTimeLimit(distanceBetweenStations);
            initialWage = JobPaymentCalculator.CalculateJobPayment(
                jobType,
                distanceBetweenStations, ExtractPaymentCalculationData(transportedCarLiveries, transportedCargoTypes)
            );
        }

        public static void CalculateShuntingBonusTimeLimitAndWage(JobType jobType,
            int numberOfTracks,
            List<TrainCarLivery> transportedCarLiveries,
            List<CargoType> transportedCargoTypes,
            out float bonusTimeLimit,
            out float initialWage) {
            // scalar value 500 taken from StationProceduralJobGenerator
            var distance = numberOfTracks * 500f;
            bonusTimeLimit = JobPaymentCalculator.CalculateShuntingBonusTimeLimit(numberOfTracks);
            initialWage = JobPaymentCalculator.CalculateJobPayment(
                jobType,
                distance, ExtractPaymentCalculationData(transportedCarLiveries, transportedCargoTypes)
            );
        }

        /// <summary>based off EmptyHaulJobProceduralGenerator.ExtractEmptyHaulPaymentCalculationData</summary>
        private static PaymentCalculationData ExtractPaymentCalculationData(List<TrainCarLivery> orderedCarLiveries,
            List<CargoType> orderedCargoTypes) {
            if (orderedCarLiveries == null) {
                return null;
            }
            var trainCarTypeToCount = new Dictionary<TrainCarLivery, int>();
            foreach (var trainCarLivery in orderedCarLiveries) {
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
    }
}