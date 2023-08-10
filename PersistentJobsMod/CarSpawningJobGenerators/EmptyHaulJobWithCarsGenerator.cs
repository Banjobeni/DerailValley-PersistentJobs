using System;
using System.Linq;
using DV.ThingTypes;
using PersistentJobsMod.Extensions;
using PersistentJobsMod.Licensing;

namespace PersistentJobsMod.CarSpawningJobGenerators {
    public static class EmptyHaulJobWithCarsGenerator {
        public static JobChainController TryGenerateJobChain(StationController startingStation, bool requirePlayerLicensesCompatible, Random random) {
            var yardTracksOrganizer = YardTracksOrganizer.Instance;

            var possibleCargoGroupsAndTrainCarCountOrNull = CargoGroupsAndCarCountProvider.GetOrNull(startingStation.proceduralJobsRuleset.outputCargoGroups, startingStation.proceduralJobsRuleset, requirePlayerLicensesCompatible, CargoGroupsAndCarCountProvider.CargoGroupLicenseKind.Cars, random);

            if (possibleCargoGroupsAndTrainCarCountOrNull == null) {
                return null;
            }

            var (availableCargoGroups, carCount) = possibleCargoGroupsAndTrainCarCountOrNull.Value;

            var chosenCargoGroup = random.GetRandomElement(availableCargoGroups);
            Main._modEntry.Logger.Log($"logistical haul: chose cargo group ({string.Join("/", chosenCargoGroup.cargoTypes)}) with {carCount} waggons");

            var cargoCarGroups = CargoCarGroupsRandomizer.GetCargoCarGroups(chosenCargoGroup, carCount, random);

            var trainCarLiveries = cargoCarGroups.SelectMany(ccg => ccg.CarLiveries).ToList();

            var requiredTrainLengt = CarSpawner.Instance.GetTotalCarLiveriesLength(trainCarLiveries, true);

            var emptyOrAlreadyEmptyHaulTracks = startingStation.logicStation.yard.TransferOutTracks.Where(t => t.GetJobsOfCarsFullyOnTrack().All(j => j.jobType == JobType.EmptyHaul)).ToList();

            var tracks = yardTracksOrganizer.FilterOutTracksWithoutRequiredFreeSpace(emptyOrAlreadyEmptyHaulTracks, requiredTrainLengt);

            if (!tracks.Any()) {
                return null;
            }

            var track = random.GetRandomElement(tracks);

            return EmptyHaulJobProceduralGenerator.GenerateEmptyHaulJobWithCarSpawning(startingStation, track, trainCarLiveries, random);
        }
    }
}