using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using DV;
using DV.Logic.Job;
using DV.ThingTypes;
using DV.Utils;
using HarmonyLib;
using PersistentJobsMod.CarSpawningJobGenerators;
using PersistentJobsMod.Extensions;
using PersistentJobsMod.JobGenerators;
using PersistentJobsMod.Model;
using UnityEngine;
using Random = System.Random;

namespace PersistentJobsMod.HarmonyPatches.JobGeneration {
    /// <summary>tries to generate new jobs for the train cars marked for deletion</summary>
    [HarmonyPatch]
    static class UnusedTrainCarDeleter_Patches {
        private const double TrainCarJobRegenerationSquareDistance = 640000.0;
        private const float COROUTINE_INTERVAL = 60f;

        [HarmonyPatch(typeof(UnusedTrainCarDeleter), "TrainCarsDeleteCheck")]
        [HarmonyPrefix]
        public static bool TrainCarsDeleteCheck_Prefix(
                UnusedTrainCarDeleter __instance,
                ref IEnumerator __result,
                List<TrainCar> ___unusedTrainCarsMarkedForDelete) {
            if (!Main._modEntry.Active) {
                return true;
            } else {
                __result = TrainCarsCreateJobOrDeleteCheck(__instance, COROUTINE_INTERVAL, ___unusedTrainCarsMarkedForDelete);
                return false;
            }
        }

        private static IEnumerator TrainCarsCreateJobOrDeleteCheck(UnusedTrainCarDeleter unusedTrainCarDeleter, float interval, List<TrainCar> ___unusedTrainCarsMarkedForDelete) {
            for (; ; ) {
                yield return WaitFor.SecondsRealtime(interval);

                try {
                    if (PlayerManager.PlayerTransform != null && !FastTravelController.IsFastTravelling) {
                        ReassignRegularTrainCarsAndDeleteNonPlayerSpawnedCars(unusedTrainCarDeleter, ___unusedTrainCarsMarkedForDelete, false);
                    }
                } catch (Exception e) {
                    Main._modEntry.Logger.Error(
                        $"Exception thrown during TrainCarsCreateJobOrDeleteCheck delete candidate collection:\n{e}");
                    Main.OnCriticalFailure();
                }
            }
            // ReSharper disable once IteratorNeverReturns
        }

        [HarmonyPatch(typeof(UnusedTrainCarDeleter), nameof(UnusedTrainCarDeleter.InstantConditionalDeleteOfUnusedCars))]
        [HarmonyPrefix]
        public static bool InstantConditionalDeleteOfUnusedCars_Prefix(UnusedTrainCarDeleter __instance, List<TrainCar> ___unusedTrainCarsMarkedForDelete) {
            if (!Main._modEntry.Active) {
                return true;
            }

            try {
                ReassignRegularTrainCarsAndDeleteNonPlayerSpawnedCars(__instance, ___unusedTrainCarsMarkedForDelete, false);

                return false;
            } catch (Exception e) {
                Main._modEntry.Logger.Error($"Exception thrown during {nameof(UnusedTrainCarDeleter_Patches)}.{nameof(InstantConditionalDeleteOfUnusedCars_Prefix)} patch:" + $"\n{e}");
                Main.OnCriticalFailure();
            }

            return true;
        }

        [HarmonyReversePatch]
        [HarmonyPatch(typeof(UnusedTrainCarDeleter), "AreDeleteConditionsFulfilled")]
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool AreDeleteConditionsFulfilled(UnusedTrainCarDeleter instance, TrainCar trainCar) {
            throw new NotImplementedException("This is a stub");
        }

        public static void ReassignRegularTrainCarsAndDeleteNonPlayerSpawnedCars(UnusedTrainCarDeleter unusedTrainCarDeleter, List<TrainCar> ___unusedTrainCarsMarkedForDelete, bool skipDistanceCheckForRegularTrainCars) {
            if (___unusedTrainCarsMarkedForDelete.Count == 0) {
                return;
            }

            Main._modEntry.Logger.Log("collecting deletion candidates...");
            var toDeleteTrainCars = new List<TrainCar>();

            var regularTrainCars = new List<TrainCar>();

            for (var i = ___unusedTrainCarsMarkedForDelete.Count - 1; i >= 0; i--) {
                var trainCar = ___unusedTrainCarsMarkedForDelete[i];
                if (trainCar == null) {
                    ___unusedTrainCarsMarkedForDelete.RemoveAt(i);
                    continue;
                }

                var isRegularCar = CarTypes.IsRegularCar(trainCar.carLivery);

                if ((isRegularCar && skipDistanceCheckForRegularTrainCars) || AreDeleteConditionsFulfilled(unusedTrainCarDeleter, trainCar)) {
                    if (isRegularCar) {
                        regularTrainCars.Add(trainCar);
                    } else if (!trainCar.playerSpawnedCar) {
                        toDeleteTrainCars.Add(trainCar);
                    }
                }
            }

            Main._modEntry.Logger.Log($"found {regularTrainCars.Count} regular train cars for which to regenerate jobs, and {toDeleteTrainCars.Count} other cars to delete");

            if (toDeleteTrainCars.Count != 0) {
                foreach (var tc in toDeleteTrainCars) {
                    ___unusedTrainCarsMarkedForDelete.Remove(tc);
                }

                SingletonBehaviour<CarSpawner>.Instance.DeleteTrainCars(toDeleteTrainCars.ToList());

                Main._modEntry.Logger.Log($"deleted {toDeleteTrainCars.Count} other cars");
            }

            if (regularTrainCars.Count > 0) {
                // possibly having deleted other cars already, we can safely determine the remaining trainsets now (they may have been split by e.g. deleting a loco out of the middle)
                var candidateTrainSets = regularTrainCars.Select(tc => tc.trainset).Distinct().ToList();
                Main._modEntry.Logger.Log($"found {regularTrainCars.Count} trainsets to check for job regeneration");

                var regenerateJobsTrainsets = candidateTrainSets.Where(ts => skipDistanceCheckForRegularTrainCars || AreAllCarsFarEnoughAwayFromPlayer(ts, TrainCarJobRegenerationSquareDistance)).ToList();
                Main._modEntry.Logger.Log($"found {regenerateJobsTrainsets.Count} trainsets that are far enough away from the player for regeneration");

                var reassignedToJobsTrainCars = ReassignJoblessRegularTrainCarsToJobs(regenerateJobsTrainsets, new Random());

                foreach (var tc in reassignedToJobsTrainCars) {
                    ___unusedTrainCarsMarkedForDelete.Remove(tc);
                }

                Main._modEntry.Logger.Log($"assigned {reassignedToJobsTrainCars.Count} train cars to new jobs");
            }
        }

        private static bool AreAllCarsFarEnoughAwayFromPlayer(Trainset trainset, double distance) {
            foreach (var trainCar in trainset.cars) {
                var squareDistance = (trainCar.transform.position - PlayerManager.PlayerTransform.position).sqrMagnitude;
                if (squareDistance < distance) {
                    return false;
                }
            }
            return true;
        }

        public static IReadOnlyList<TrainCar> ReassignJoblessRegularTrainCarsToJobs(IReadOnlyList<Trainset> trainsets, Random random) {
            var stationsAndTrainsets = trainsets.GroupBy(ts => GetNearestStation(ts.cars.First().gameObject.transform.position)).Select(g => (Station: g.Key, Trainsets: g.ToList())).ToList();

            var jobChainControllers = stationsAndTrainsets.SelectMany(sts => ReassignJoblessRegularTrainCarsToJobsInStationAndCreateJobChainControllers(sts.Station, sts.Trainsets, random)).ToList();

            var trainCars = new List<TrainCar>();
            foreach (var jobChainController in jobChainControllers) {
                var jobChainTrainCars = jobChainController.trainCarsForJobChain;

                EnsureTrainCarsAreConvertedToNonPlayerSpawned(jobChainTrainCars);
                jobChainController.FinalizeSetupAndGenerateFirstJob();

                trainCars.AddRange(jobChainTrainCars);
            }

            return trainCars;
        }

        private static IReadOnlyList<JobChainController> ReassignJoblessRegularTrainCarsToJobsInStationAndCreateJobChainControllers(StationController station, List<Trainset> trainsets, Random random) {
            Main._modEntry.Logger.Log($"Reassigning train cars to jobs in station {station.logicStation.ID}: {trainsets.SelectMany(ts => ts.cars).Count()} cars in {trainsets.Count} trainsets need to be reassigned.");

            var statusTrainCarGroups = trainsets.SelectMany(s => s.cars.GroupConsecutiveBy(GetTrainCarReassignStatus)).ToList();

            var emptyConsecutiveTrainCarGroups = statusTrainCarGroups.Where(s => s.Key == TrainCarReassignStatus.Empty).Select(s => s.Items).ToList();
            var loadedConsecutiveTrainCarGroups = statusTrainCarGroups.Where(s => s.Key == TrainCarReassignStatus.Loaded).Select(s => s.Items).ToList();

            Main._modEntry.Logger.Log($"Found {emptyConsecutiveTrainCarGroups.Count} empty train car groups with a total of {emptyConsecutiveTrainCarGroups.SelectMany(g => g).Count()} cars");
            Main._modEntry.Logger.Log($"Found {loadedConsecutiveTrainCarGroups.Count} loaded train car groups with a total of {loadedConsecutiveTrainCarGroups.SelectMany(g => g).Count()} cars");

            var (loadableConsecuteTrainCarGroups, notLoadableConsecutiveTrainCarGroups) = DivideEmptyConsecutiveTrainCarGroupsIntoLoadableAndNotLoadable(station, emptyConsecutiveTrainCarGroups);
            var (unloadableConsecutiveTrainCarGroups, notUnloadableConsecutiveTrainCarGroups) = DivideLoadedConsecutiveTrainCarGroupsIntoUnloadableAndNotUnloadable(station, loadedConsecutiveTrainCarGroups);

            var emptyHaulJobChainControllers = notLoadableConsecutiveTrainCarGroups
                .SelectMany(carGroup => ChooseTrainCarsRelationAndChopByMaxLength(carGroup, station.proceduralJobsRuleset.maxCarsPerJob, random))
                .Select(t => EmptyHaulJobGenerator.GenerateEmptyHaulJobWithExistingCarsOrNull(station, t.Relation.Station, t.StartingTrack, t.TrainCars, random))
                .WhereNotNull().ToList();

            var transportJobChainControllers = notUnloadableConsecutiveTrainCarGroups
                .SelectMany(carGroup => ChooseTrainCarsRelationAndChopByMaxLength(carGroup, station.proceduralJobsRuleset.maxCarsPerJob, random))
                .Select(t => TransportJobGenerator.TryGenerateJobChainController(station, t.StartingTrack, t.Relation.Station, t.TrainCars, t.TrainCars.Select(tc => tc.LoadedCargo).ToList(), random))
                .WhereNotNull().ToList();

            var shuntingUnloadJobChainControllers = unloadableConsecutiveTrainCarGroups
                .SelectMany(carGroup => ChooseTrainCarsRelationAndChopByMaxLength(carGroup, station.proceduralJobsRuleset.maxCarsPerJob, random))
                .Select(t => ShuntingUnloadJobProceduralGenerator.TryGenerateJobChainController(t.Relation.SourceStation, t.StartingTrack, station, t.TrainCars.ToList(), random))
                .WhereNotNull().ToList();

            var preparedShuntingLoadCarGroups = GroupShuntingLoadIntoMultiplePickups(station.proceduralJobsRuleset, loadableConsecuteTrainCarGroups, random).ToList();

            var shuntingLoadJobChainControllers = preparedShuntingLoadCarGroups.Select(pcg => TryCreateShuntingLoadJobChainController(station, pcg.CargoGroup, pcg.Destination, pcg.SameTrackTrainCarTypeGroups, random)).WhereNotNull().ToList();

            var jobChainControllers = emptyHaulJobChainControllers.Concat(transportJobChainControllers).Concat(shuntingUnloadJobChainControllers).Concat(shuntingLoadJobChainControllers).ToList();

            Main._modEntry.Logger.Log($"Created {jobChainControllers.Count} job chain controllers for a total of {jobChainControllers.SelectMany(c => c.trainCarsForJobChain).Count()} cars");

            return jobChainControllers;
        }

        private static IEnumerable<(OutgoingCargoGroup CargoGroup, StationController Destination, IReadOnlyList<IReadOnlyList<(TrainCarType_v2 TrainCarType, IReadOnlyList<TrainCar> TrainCars)>> SameTrackTrainCarTypeGroups)> GroupShuntingLoadIntoMultiplePickups(StationProceduralJobsRuleset jobsRuleset, IReadOnlyList<IReadOnlyList<(TrainCarType_v2 TrainCarType, IReadOnlyList<TrainCar> TrainCars, IReadOnlyList<OutgoingCargoGroup> CargoGroups)>> consecutiveTrainCarTypeGroupSets, Random random) {
            var randomizedConsecutiveTrainCarTypeGroupSets = random.GetRandomPermutation(consecutiveTrainCarTypeGroupSets);

            var indexedTrainCarTypeGroupSets = randomizedConsecutiveTrainCarTypeGroupSets.SelectMany((a, setIndex) => a.Select((b, groupIndex) => (b.TrainCarType, b.TrainCars, b.CargoGroups, SetIndex: setIndex, GroupIndex: groupIndex)).ToReadOnlyList()).ToReadOnlyList();
            Main._modEntry.Logger.Log($"GroupShuntingLoadIntoMultiplePickups: called with {indexedTrainCarTypeGroupSets.Count} indexed car groups");

            while (indexedTrainCarTypeGroupSets.Any()) {
                var (result, remainingIndexedConsecutiveTrainCarTypeGroupSets) = PickFirstShuntingLoadGroupAndTryToExtend(jobsRuleset, indexedTrainCarTypeGroupSets, random);
                Main._modEntry.Logger.Log($"GroupShuntingLoadIntoMultiplePickups: created result with {result.SameTrackTrainCarTypeGroups.SelectMany(g => g).Count()} car groups on {result.SameTrackTrainCarTypeGroups.Count} tracks, now remaining are {remainingIndexedConsecutiveTrainCarTypeGroupSets.Count} indexed car groups");

                yield return result;
                indexedTrainCarTypeGroupSets = remainingIndexedConsecutiveTrainCarTypeGroupSets;
            }
        }

        private static (
            (OutgoingCargoGroup CargoGroup, StationController Destination, IReadOnlyList<IReadOnlyList<(TrainCarType_v2 TrainCarType, IReadOnlyList<TrainCar> TrainCars)>> SameTrackTrainCarTypeGroups) ResultingShuntingLoad,
            IReadOnlyList<(TrainCarType_v2 TrainCarType, IReadOnlyList<TrainCar> TrainCars, IReadOnlyList<OutgoingCargoGroup> CargoGroups, int SetIndex, int GroupIndex)> remaining
        )
        PickFirstShuntingLoadGroupAndTryToExtend(StationProceduralJobsRuleset jobsRuleset, IReadOnlyList<(TrainCarType_v2 TrainCarType, IReadOnlyList<TrainCar> TrainCars, IReadOnlyList<OutgoingCargoGroup> CargoGroups, int SetIndex, int GroupIndex)> indexedTrainCarTypeGroupSets, Random random) {
            var remainingIndexedTrainCarTypeGroups = new List<(TrainCarType_v2 TrainCarType, IReadOnlyList<TrainCar> TrainCars, IReadOnlyList<OutgoingCargoGroup> CargoGroups, int SetIndex, int GroupIndex)>();

            var (initialTrainCarType, initialTrainCars, initialCargoGroups, currentSetIndex, currentGroupIndex) = indexedTrainCarTypeGroupSets.First();

            var initialTrainLength = CarSpawner.Instance.GetTotalTrainCarsLength(initialTrainCars.ToList(), true);
            var initialLengthCompatibleCargoGroups = initialCargoGroups.Where(cg => cg.Destinations.Any(d => initialTrainLength < d.MaxSourceDestinationTrainLength)).ToList();

            if (initialLengthCompatibleCargoGroups.Any() && initialTrainCars.Count <= jobsRuleset.maxCarsPerJob) {
                var currentTrainCars = initialTrainCars;
                var currentCargoGroups = (IReadOnlyList<OutgoingCargoGroup>)initialLengthCompatibleCargoGroups;
                var currentSameTrackTrainCarTypeGroups = new[] { new[] { (initialTrainCarType, initialTrainCars) }.ToList() }.ToList();

                foreach (var (nextTrainCarType, nextTrainCars, nextCargoGroups, nextSetIndex, nextGroupIndex) in indexedTrainCarTypeGroupSets.Skip(1)) {
                    var trainCarsAndCargoGroupsExtensionResultOrNull = TryGetTrainCarsAndRemainingCargoGroupsForExtendingShuntingLoad(currentTrainCars, currentCargoGroups, nextTrainCars, nextCargoGroups, jobsRuleset.maxCarsPerJob);
                    var isAppendingConsecutiveTrainCarsOnSameTrack = (nextSetIndex == currentSetIndex) && (nextGroupIndex == currentGroupIndex + 1);

                    if (trainCarsAndCargoGroupsExtensionResultOrNull != null && (currentSameTrackTrainCarTypeGroups.Count < jobsRuleset.maxShuntingStorageTracks || isAppendingConsecutiveTrainCarsOnSameTrack)) {
                        // can extend the job with the next train car type group. there are not too many cars in total, there are remaining cargo groups that support all car types and the train length, and there are not too many shunting load pickups
                        (currentTrainCars, currentCargoGroups) = trainCarsAndCargoGroupsExtensionResultOrNull.Value;

                        if (isAppendingConsecutiveTrainCarsOnSameTrack) {
                            currentSameTrackTrainCarTypeGroups.Last().Add((nextTrainCarType, nextTrainCars));
                        } else {
                            currentSameTrackTrainCarTypeGroups.Add(new[] { (nextTrainCarType, nextTrainCars) }.ToList());
                        }

                        (currentSetIndex, currentGroupIndex) = (nextSetIndex, nextGroupIndex);
                    } else {
                        remainingIndexedTrainCarTypeGroups.Add((nextTrainCarType, nextTrainCars, nextCargoGroups, nextSetIndex, nextGroupIndex));
                    }
                }

                // we've gone through all train car type groups. either we used them for extending the current job, or we couldn't take them and they are in remaining
                // we can now decide the cargo group and destination
                var cargoGroup = random.GetRandomElement(currentCargoGroups);
                var cargoGroupDestination = random.GetRandomElement(cargoGroup.Destinations);

                var result = (cargoGroup, cargoGroupDestination.Station, currentSameTrackTrainCarTypeGroups.Select(t => t.ToReadOnlyList()).ToReadOnlyList());

                return (result, remainingIndexedTrainCarTypeGroups);
            } else {
                // initial train cars are too long. pick any cargo group and split the cars to match that cargo group
                var cargoGroup = random.GetRandomElement(initialCargoGroups);
                var cargoGroupDestination = random.GetRandomElement(cargoGroup.Destinations);

                var lengthCompatibleCarCount = ChooseNumberOfCarsNotExceedingLength(initialTrainCars, cargoGroupDestination.MaxSourceDestinationTrainLength, random);
                var carCount = Math.Min(lengthCompatibleCarCount, jobsRuleset.maxCarsPerJob);

                var lengthCompatibleTrainCars = initialTrainCars.Take(carCount).ToReadOnlyList();

                if (carCount < initialTrainCars.Count) { // this should always be the case
                    // put the rest of the train cars back
                    var superfluousTrainCars = initialTrainCars.Skip(carCount).ToList();
                    remainingIndexedTrainCarTypeGroups.Add((initialTrainCarType, superfluousTrainCars, initialCargoGroups, currentSetIndex, currentGroupIndex));
                }

                // the job will consist of a single pickup of just this train car type group
                var trainCarTypeCarGroups = new[] { new[] { (initialTrainCarType, lengthCompatibleTrainCars) }.ToReadOnlyList() }.ToReadOnlyList();

                var result = (cargoGroup, cargoGroupDestination.Station, trainCarTypeCarGroups);

                // all other train car type groups will not be taken for this job
                remainingIndexedTrainCarTypeGroups.AddRange(indexedTrainCarTypeGroupSets.Skip(1));

                return (result, remainingIndexedTrainCarTypeGroups);
            }
        }


        private static JobChainController TryCreateShuntingLoadJobChainController(StationController sourceStation, OutgoingCargoGroup cargoGroup, StationController destinationStation, IReadOnlyList<IReadOnlyList<(TrainCarType_v2 TrainCarType, IReadOnlyList<TrainCar> TrainCars)>> sameTrackTrainCarTypeGroups, Random random) {
            var distinctTrainCarTypes = sameTrackTrainCarTypeGroups.SelectMany(tcot => tcot).Select(tctg => tctg.TrainCarType).Distinct().ToList();

            var trainCarType2CargoTypes = new Dictionary<TrainCarType_v2, IReadOnlyList<CargoType>>();
            if (distinctTrainCarTypes.Count == 1) {
                var trainCarType = distinctTrainCarTypes.Single();
                var possibleCargoTypes = GetLoadableCargoTypesForCargoGroupAndTrainCarType(cargoGroup, trainCarType);
                if (!possibleCargoTypes.Any()) {
                    Debug.LogWarning($"[PersistentJobsMod] Could not find any cargo types that fit train car type {trainCarType.id} for cargo group ({string.Join(", ", cargoGroup.CargoTypes)}) from {sourceStation.logicStation.ID} to {destinationStation.logicStation.ID}");
                    return null;
                }
                trainCarType2CargoTypes.Add(trainCarType, possibleCargoTypes);
            } else {
                foreach (var trainCarType in distinctTrainCarTypes) {
                    var possibleCargoTypes = GetLoadableCargoTypesForCargoGroupAndTrainCarType(cargoGroup, trainCarType);
                    if (!possibleCargoTypes.Any()) {
                        Debug.LogWarning($"[PersistentJobsMod] Could not find any cargo types that fit train car type {trainCarType.id} for cargo group ({string.Join(", ", cargoGroup.CargoTypes)}) from {sourceStation.logicStation.ID} to {destinationStation.logicStation.ID}");
                        return null;
                    }
                    var cargoType = random.GetRandomElement(possibleCargoTypes);
                    trainCarType2CargoTypes.Add(trainCarType, new[] { cargoType });
                }
            }

            var trainCarsWithCargoTypesOnTracks = sameTrackTrainCarTypeGroups.Select(tcot => (TrainCarsWithCargoTypes: tcot.SelectMany(tctg => tctg.TrainCars.Zip(CargoCarGroupsRandomizer.ChooseCargoTypesForNumberOfCars(trainCarType2CargoTypes[tctg.TrainCarType], tctg.TrainCars.Count, random), (tc, ct) => (TrainCar: tc, CargoType: ct))).ToList(), StartingTrack: DetermineStartingTrack(tcot.SelectMany(tcts => tcts.TrainCars).ToList()))).ToList();

            var carsPerStartingTrack = trainCarsWithCargoTypesOnTracks.Select(tcot => new CarsPerTrack(tcot.StartingTrack, tcot.TrainCarsWithCargoTypes.Select(tcwt => tcwt.TrainCar.logicCar).ToList())).ToList();

            var trainCars = trainCarsWithCargoTypesOnTracks.SelectMany(tcot => tcot.TrainCarsWithCargoTypes).Select(tcwct => tcwct.TrainCar).ToList();
            var cargoTypes = trainCarsWithCargoTypesOnTracks.SelectMany(tcot => tcot.TrainCarsWithCargoTypes).Select(tcwct => tcwct.CargoType).ToList();
            var trainLength = CarSpawner.Instance.GetTotalTrainCarsLength(trainCars);

            var warehouseMachines = cargoGroup.SourceWarehouseMachines.Where(w => trainLength < w.WarehouseTrack.GetTotalUsableTrackLength()).ToList();
            if (!warehouseMachines.Any()) {
                Debug.LogWarning($"[PersistentJobsMod] Could not find a warehouse machine at {sourceStation.logicStation.ID} that supports a train length of {trainLength:F1}m");
                return null;
            }

            var warehouseMachine = random.GetRandomElement(warehouseMachines);

            return ShuntingLoadJobGenerator.TryGenerateJobChainController(sourceStation, carsPerStartingTrack, warehouseMachine, destinationStation, trainCars, cargoTypes, random);
        }

        private static (IReadOnlyList<TrainCar> TrainCars, IReadOnlyList<OutgoingCargoGroup> CargoGroups)? TryGetTrainCarsAndRemainingCargoGroupsForExtendingShuntingLoad(IReadOnlyList<TrainCar> currentTrainCars, IReadOnlyList<OutgoingCargoGroup> currentCargoGroups, IReadOnlyList<TrainCar> nextTrainCars, IReadOnlyList<OutgoingCargoGroup> cargoGroups, int maxCarCount) {
            var totalTrainCars = currentTrainCars.Concat(nextTrainCars).ToList();
            if (totalTrainCars.Count > maxCarCount) {
                return null;
            }

            var totalTrainLength = CarSpawner.Instance.GetTotalTrainCarsLength(totalTrainCars, true);
            var intersectionCargoGroups = currentCargoGroups.Intersect(cargoGroups).Where(cg => cg.Destinations.Any(d => totalTrainLength < d.MaxSourceDestinationTrainLength)).ToList();
            if (intersectionCargoGroups.Any()) {
                return (totalTrainCars, intersectionCargoGroups);
            } else {
                return null;
            }
        }

        private static IEnumerable<(IReadOnlyList<TrainCar> TrainCars, TTrainCarRelation Relation, Track StartingTrack)> ChooseTrainCarsRelationAndChopByMaxLength<TTrainCarRelation>(IReadOnlyList<(TrainCar, IReadOnlyList<TTrainCarRelation>)> carGroup, int maxCarCount, Random random) where TTrainCarRelation : IReassignableTrainCarRelationWithMaxTrackLength {
            var remainingCars = carGroup;

            while (remainingCars.Any()) {
                var (trainCars, destinations) = TakeWhileHavingAtLeastOneRelationLeft(remainingCars);

                var destination = random.GetRandomElement(destinations);
                var trainCarCount = Math.Min(ChooseNumberOfCarsNotExceedingLength(trainCars, destination.RelationMaxTrainLength, random), maxCarCount);
                var destinationTrainCars = trainCars.Take(trainCarCount).ToList();
                var startingTrack = DetermineStartingTrack(destinationTrainCars);

                yield return (destinationTrainCars, destination, startingTrack);

                remainingCars = remainingCars.Skip(trainCarCount).ToList();
            }
        }

        private static Track DetermineStartingTrack(IReadOnlyList<TrainCar> trainCars) {
            var tracks = trainCars.Select(tc => tc.logicCar.FrontBogieTrack).Distinct().ToList();

            var yardTracksOrganizerManagedTrack = tracks.FirstOrDefault(YardTracksOrganizer.Instance.IsTrackManagedByOrganizer);
            if (yardTracksOrganizerManagedTrack != null) {
                return yardTracksOrganizerManagedTrack;
            } else {
                Debug.Log($"[PersistentJobsMod] Could not determine a nice-looking starting track for train cars {string.Join(", ", trainCars.Select(tc => tc.ID))}");
            }

            return  tracks.First();
        }

        private static int ChooseNumberOfCarsNotExceedingLength(IReadOnlyList<TrainCar> trainCars, double maxLength, Random random) {
            var liveries = trainCars.Select(tc => tc.carLivery).ToList();

            int currentCount = trainCars.Count;

            while (currentCount > 1) {
                var currentLiveries = liveries.Take(currentCount).ToList();
                if (CarSpawner.Instance.GetTotalCarLiveriesLength(currentLiveries, true) < maxLength) {
                    break;
                }

                currentCount = random.Next(currentCount / 2, currentCount);
            }

            return currentCount;
        }

        private static (IReadOnlyList<TrainCar>, IReadOnlyList<TRelation>) TakeWhileHavingAtLeastOneRelationLeft<TRelation>(IReadOnlyList<(TrainCar TrainCar, IReadOnlyList<TRelation> Destinations)> trainCarDestinations) {
            var currentTrainCars = new List<TrainCar> { trainCarDestinations.First().TrainCar };
            var currentDestinations = trainCarDestinations.First().Destinations;

            foreach (var (trainCar, destinations) in trainCarDestinations.Skip(1)) {
                var remainingDestinations = currentDestinations.Intersect(destinations).ToList();
                if (!remainingDestinations.Any()) {
                    return (currentTrainCars, currentDestinations);
                }

                currentTrainCars.Add(trainCar);
                currentDestinations = remainingDestinations;
            }

            return (currentTrainCars, currentDestinations);
        }

        private static (IReadOnlyList<IReadOnlyList<(TrainCarType_v2 TrainCarType, IReadOnlyList<TrainCar> TrainCars, IReadOnlyList<OutgoingCargoGroup> CargoGroupsWithCargoTypes)>> loadableConsecuteTrainCarGroups, IReadOnlyList<IReadOnlyList<(TrainCar, IReadOnlyList<EmptyTrainCarTypeDestination>)>> notLoadableConsecutiveTrainCarGroups) DivideEmptyConsecutiveTrainCarGroupsIntoLoadableAndNotLoadable(StationController station, IReadOnlyList<IReadOnlyList<TrainCar>> emptyConsecutiveTrainCarGroups) {
            var stationOutgoingCargoGroups = DetailedCargoGroups.GetOutgoingCargoGroups(station);

            var loadableConsecuteTrainCarGroups = new List<IReadOnlyList<(TrainCarType_v2 TrainCarType, IReadOnlyList<TrainCar> TrainCars, IReadOnlyList<OutgoingCargoGroup> CargoGroupsWithCargoTypes)>>();
            var notLoadableConsecutiveTrainCarGroups = new List<IReadOnlyList<(TrainCar, IReadOnlyList<EmptyTrainCarTypeDestination> Destinations)>>();

            foreach (var emptyConsecutiveTrainCars in emptyConsecutiveTrainCarGroups) {
                List<(TrainCarType_v2 TrainCarType, IReadOnlyList<TrainCar> TrainCars, IReadOnlyList<OutgoingCargoGroup>)> currentLoadable = null;
                List<(TrainCar, IReadOnlyList<EmptyTrainCarTypeDestination> Destinations)> currentNotLoadable = null;

                void FlushCurrentState() {
                    if (currentLoadable != null) {
                        loadableConsecuteTrainCarGroups.Add(currentLoadable);
                        currentLoadable = null;
                    }
                    if (currentNotLoadable != null) {
                        notLoadableConsecutiveTrainCarGroups.Add(currentNotLoadable);
                        currentNotLoadable = null;
                    }
                }

                foreach (var (trainCarType, trainCars) in emptyConsecutiveTrainCars.GroupConsecutiveBy(tc => tc.carLivery.parentType)) {
                    var outgoingCargoGroups = stationOutgoingCargoGroups
                        .Where(cg => GetLoadableCargoTypesForCargoGroupAndTrainCarType(cg, trainCarType).Any())
                        .ToList();

                    if (outgoingCargoGroups.Any()) {
                        if (currentLoadable == null) {
                            FlushCurrentState();
                            currentLoadable = new List<(TrainCarType_v2 TrainCarType, IReadOnlyList<TrainCar> TrainCars, IReadOnlyList<OutgoingCargoGroup> CargoGroupsWithCargoTypes)>();
                        }
                        currentLoadable.Add((trainCarType, trainCars, outgoingCargoGroups));
                    } else {
                        var destinations = EmptyTrainCarTypeDestinations.GetStationsThatLoadTrainCarType(trainCarType);
                        if (destinations.Any()) {
                            if (currentNotLoadable == null) {
                                FlushCurrentState();
                                currentNotLoadable = new List<(TrainCar, IReadOnlyList<EmptyTrainCarTypeDestination> Destinations)>();
                            }
                            currentNotLoadable.AddRange(trainCars.Select(tc => (tc, destinations)).ToList());
                        } else {
                            FlushCurrentState();
                            Debug.Log($"[PersistentJobsMod] Could not find any valid destination for consecutive train cars of type {trainCarType.name} starting with {trainCars.First().ID}");
                        }
                    }
                }

                FlushCurrentState();
            }

            return (loadableConsecuteTrainCarGroups, notLoadableConsecutiveTrainCarGroups);
        }

        private static (IReadOnlyList<IReadOnlyList<(TrainCar, IReadOnlyList<IncomingCargoGroup> IncomingCargoGroups)>> unloadableConsecutiveTrainCarGroups, IReadOnlyList<IReadOnlyList<(TrainCar, IReadOnlyList<OutgoingCargoGroupDestination> CargoGroupDestinations)>> notUnloadableConsecutiveTrainCarGroups) DivideLoadedConsecutiveTrainCarGroupsIntoUnloadableAndNotUnloadable(StationController station, IReadOnlyList<IReadOnlyList<TrainCar>> loadedConsecutiveTrainCarGroups) {
            var stationIncomingCargoGroups = DetailedCargoGroups.GetIncomingCargoGroups(station);

            var unloadableConsecutiveTrainCarGroups = new List<IReadOnlyList<(TrainCar, IReadOnlyList<IncomingCargoGroup> IncomingCargoGroups)>>();
            var notUnloadableConsecutiveTrainCarGroups = new List<IReadOnlyList<(TrainCar, IReadOnlyList<OutgoingCargoGroupDestination> CargoGroupDestinations)>>();

            foreach (var loadedConsecutiveTrainCars in loadedConsecutiveTrainCarGroups) {
                List<(TrainCar, IReadOnlyList<IncomingCargoGroup> IncomingCargoGroups)> currentUnloadable = null;
                List<(TrainCar, IReadOnlyList<OutgoingCargoGroupDestination> CargoGroupDestinations)> currentNotUnloadable = null;

                void FlushCurrentState() {
                    if (currentUnloadable != null) {
                        unloadableConsecutiveTrainCarGroups.Add(currentUnloadable);
                        currentUnloadable = null;
                    }
                    if (currentNotUnloadable != null) {
                        notUnloadableConsecutiveTrainCarGroups.Add(currentNotUnloadable);
                        currentNotUnloadable = null;
                    }
                }

                foreach (var trainCar in loadedConsecutiveTrainCars) {
                    var incomingCargoGroups = stationIncomingCargoGroups.Where(icg => icg.CargoTypes.Contains(trainCar.LoadedCargo)).ToList();
                    if (incomingCargoGroups.Any()) {
                        if (currentUnloadable == null) {
                            FlushCurrentState();
                            currentUnloadable = new List<(TrainCar, IReadOnlyList<IncomingCargoGroup> IncomingCargoGroups)>();
                        }
                        currentUnloadable.Add((trainCar, incomingCargoGroups));
                    } else {
                        var cargoDestinations = DetailedCargoGroups.GetCargoTypeDestinations(trainCar.LoadedCargo);
                        if (cargoDestinations.Any()) {
                            if (currentNotUnloadable == null) {
                                FlushCurrentState();
                                currentNotUnloadable = new List<(TrainCar, IReadOnlyList<OutgoingCargoGroupDestination> CargoGroupDestinations)>();
                            }
                            currentNotUnloadable.Add((trainCar, cargoDestinations));
                        } else {
                            FlushCurrentState();
                            Debug.Log($"[PersistentJobsMod] Could not find any valid destination for train car {trainCar.ID} with cargo type {trainCar.LoadedCargo}");
                        }
                    }
                }

                FlushCurrentState();
            }

            return (unloadableConsecutiveTrainCarGroups, notUnloadableConsecutiveTrainCarGroups);
        }

        private static IReadOnlyList<CargoType> GetLoadableCargoTypesForCargoGroupAndTrainCarType(OutgoingCargoGroup cargoGroup, TrainCarType_v2 trainCarType) {
            var trainCarLargoTypes = Globals.G.Types.CarTypeToLoadableCargo[trainCarType].Select(ct2 => ct2.v1);
            return cargoGroup.CargoTypes.Intersect(trainCarLargoTypes).ToList();
        }


        private static StationController GetNearestStation(Vector3 position) {
            return StationController.allStations.OrderBy(sc => (position - sc.gameObject.transform.position).sqrMagnitude).First();
        }

        private enum TrainCarReassignStatus {
            Empty,
            Loaded,
            NonRegularTrainCar,
        }

        private static TrainCarReassignStatus GetTrainCarReassignStatus(TrainCar trainCar) {
            if (CarTypes.IsRegularCar(trainCar.carLivery)) {
                if (trainCar.LoadedCargoAmount < 0.001f) {
                    return TrainCarReassignStatus.Empty;
                } else {
                    return TrainCarReassignStatus.Loaded;
                }
            } else {
                return TrainCarReassignStatus.NonRegularTrainCar;
            }
        }

        private static void EnsureTrainCarsAreConvertedToNonPlayerSpawned(List<TrainCar> trainCars) {
            // force job's train cars to not be treated as player spawned
            // DV will complain if we don't do this
            foreach (var trainCar in trainCars) {
                Utilities.ConvertPlayerSpawnedTrainCar(trainCar);
            }
        }
    }
}