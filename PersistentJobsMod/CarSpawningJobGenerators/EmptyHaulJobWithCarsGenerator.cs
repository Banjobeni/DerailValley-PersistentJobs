using System.Collections.Generic;
using System.Linq;
using DV.Logic.Job;
using DV.ThingTypes;
using PersistentJobsMod.Extensions;
using PersistentJobsMod.Licensing;
using UnityEngine;
using Random = System.Random;

namespace PersistentJobsMod.CarSpawningJobGenerators {
    public static class EmptyHaulJobWithCarsGenerator {
        public static JobChainController TryGenerateJobChain(StationController startingStation, bool requirePlayerLicensesCompatible, Random random) {
            var possibleCargoGroupsAndTrainCarCountOrNull = CargoGroupsAndCarCountProvider.GetOrNull(startingStation.proceduralJobsRuleset.inputCargoGroups, startingStation.proceduralJobsRuleset, requirePlayerLicensesCompatible, CargoGroupsAndCarCountProvider.CargoGroupLicenseKind.Cars, random);

            if (possibleCargoGroupsAndTrainCarCountOrNull == null) {
                return null;
            }

            var (availableCargoGroups, carCount) = possibleCargoGroupsAndTrainCarCountOrNull.Value;

            var chosenCargoGroup = random.GetRandomElement(availableCargoGroups);
            Main._modEntry.Logger.Log($"logistical haul: chose cargo group ({string.Join("/", chosenCargoGroup.cargoTypes)}) with {carCount} waggons");

            var cargoCarGroups = CargoCarGroupsRandomizer.GetCargoCarGroups(chosenCargoGroup, carCount, random);

            var trainCarLiveries = cargoCarGroups.SelectMany(ccg => ccg.CarLiveries).ToList();

            var requiredTrainLength = CarSpawner.Instance.GetTotalCarLiveriesLength(trainCarLiveries, true);

            var trackCandidates = GetTrackCandidates(startingStation.logicStation.yard.TransferOutTracks);

            var tracks = YardTracksOrganizer.Instance.FilterOutTracksWithoutRequiredFreeSpace(trackCandidates, requiredTrainLength);

            if (!tracks.Any()) {
                return null;
            }

            var track = random.GetRandomElement(tracks);

            return EmptyHaulJobProceduralGenerator.GenerateEmptyHaulJobWithCarSpawning(startingStation, track, trainCarLiveries, random);
        }

        private static List<Track> GetTrackCandidates(List<Track> tracks) {
            var tracksWithJobs = tracks.Select(t => (Track: t, Jobs: t.GetJobsOfCarsFullyOnTrack())).ToList();

            var alreadyContainingEmptyHaulTracks = tracksWithJobs.Where(t => t.Jobs.Any() && t.Jobs.All(j => j.jobType == JobType.EmptyHaul)).ToList();

            if (alreadyContainingEmptyHaulTracks.Count < Mathf.FloorToInt(0.5f * tracks.Count)) {
                var noJobsOrAlreadyEmptyHaulTracks = tracksWithJobs.Where(t => t.Jobs.All(j => j.jobType == JobType.EmptyHaul)).Select(t => t.Track).ToList();
                return noJobsOrAlreadyEmptyHaulTracks;
            } else {
                // enough tracks are already occupied with empty hauls. allow to fill up those tracks, but no new ones should be used anymore
                return alreadyContainingEmptyHaulTracks.Select(t => t.Track).ToList();
            }
        }
    }
}