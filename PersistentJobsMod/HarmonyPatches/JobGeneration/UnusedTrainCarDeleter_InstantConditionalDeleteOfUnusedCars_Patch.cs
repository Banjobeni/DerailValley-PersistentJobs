﻿using System;
using System.Collections.Generic;
using System.Linq;
using DV.ThingTypes;
using DV.Utils;
using HarmonyLib;
using PersistentJobsMod.JobGenerators;

namespace PersistentJobsMod.HarmonyPatches.JobGeneration {
    /// <summary>tries to generate new jobs for the train cars marked for deletion</summary>
    [HarmonyPatch(typeof(UnusedTrainCarDeleter), nameof(UnusedTrainCarDeleter.InstantConditionalDeleteOfUnusedCars))]
    class UnusedTrainCarDeleter_InstantConditionalDeleteOfUnusedCars_Patch {
        static bool Prefix(UnusedTrainCarDeleter __instance,
            List<TrainCar> ___unusedTrainCarsMarkedForDelete) {
            if (Main._modEntry.Active) {
                try {
                    if (___unusedTrainCarsMarkedForDelete.Count == 0) {
                        return false;
                    }

                    Main._modEntry.Logger.Log("collecting deletion candidates...");
                    var unusedAndDeleteConditionsFulfillingTrainCars = new List<TrainCar>();
                    for (var i = ___unusedTrainCarsMarkedForDelete.Count - 1; i >= 0; i--) {
                        var trainCar = ___unusedTrainCarsMarkedForDelete[i];
                        if (trainCar == null) {
                            ___unusedTrainCarsMarkedForDelete.RemoveAt(i);
                            continue;
                        }
                        var areDeleteConditionsFulfilled = Traverse.Create(__instance)
                            .Method("AreDeleteConditionsFulfilled", new Type[] { typeof(TrainCar) })
                            .GetValue<bool>(trainCar);
                        if (areDeleteConditionsFulfilled) {
                            unusedAndDeleteConditionsFulfillingTrainCars.Add(trainCar);
                        }
                    }

                    var toReassignToNewJobTrainCars = Utilities.FilterOutTrainCarsWhereOnlyPartOfConsistIsToBeDeleted(unusedAndDeleteConditionsFulfillingTrainCars);

                    Main._modEntry.Logger.Log($"[PersistentJobs] found {toReassignToNewJobTrainCars.Count} cars marked for deletion");
                    if (toReassignToNewJobTrainCars.Count == 0) {
                        return false;
                    }

                    var reassignedToJobsTrainCars = TryToReassignJobsToTrainCarsAndReturnAssignedTrainCars(toReassignToNewJobTrainCars);

                    foreach (var reassignedTrainCar in reassignedToJobsTrainCars) {
                        ___unusedTrainCarsMarkedForDelete.Remove(reassignedTrainCar);
                    }

                    var remainingJoblessTrainCars = toReassignToNewJobTrainCars.Except(reassignedToJobsTrainCars).ToList();

                    // delete non-player-spawned locos
                    var trainCarsToDelete = remainingJoblessTrainCars.Where(tc => CarTypes.IsAnyLocomotiveOrTender(tc.carLivery) && !tc.playerSpawnedCar).ToList();
                    Main._modEntry.Logger.Log($"preserved {unusedAndDeleteConditionsFulfillingTrainCars.Except(trainCarsToDelete).Count()} cars");

                    Main._modEntry.Logger.Log("deleting cars...");
                    foreach (var tc in trainCarsToDelete) {
                        ___unusedTrainCarsMarkedForDelete.Remove(tc);
                    }

                    var locosToDelete = trainCarsToDelete.Where(tc => CarTypes.IsAnyLocomotiveOrTender(tc.carLivery)).ToList();
                    var carsToDelete = trainCarsToDelete.Where(tc => !CarTypes.IsAnyLocomotiveOrTender(tc.carLivery)).ToList();

                    if (trainCarsToDelete.Count != 0) {
                        SingletonBehaviour<CarSpawner>.Instance.DeleteTrainCars(new List<TrainCar>(trainCarsToDelete), false);
                    }

                    Main._modEntry.Logger.Log($"deleted {locosToDelete.Count} locomotives or tenders");
                    Main._modEntry.Logger.Log($"deleted {carsToDelete.Count} cars");
                    
                    return false;
                } catch (Exception e) {
                    Main._modEntry.Logger.Error(
                        $"Exception thrown during {"UnusedTrainCarDeleter"}.{"InstantConditionalDeleteOfUnusedCars"} {"prefix"} patch:" +
                        $"\n{e.ToString()}");
                    Main.OnCriticalFailure();
                }
            }
            return true;
        }

        public static List<TrainCar> TryToReassignJobsToTrainCarsAndReturnAssignedTrainCars(IReadOnlyList<TrainCar> trainCarsToDelete) {
            // group trainCars by trainset
            Main._modEntry.Logger.Log("grouping trainCars by trainSet...");
            var nonLocoOrPaxTrainCars = trainCarsToDelete
                .Where(tc => !CarTypes.IsAnyLocomotiveOrTender(tc.carLivery) && !Utilities.IsPassengerCar(tc.carType))
                .ToList();
            var emptyFreightCars = nonLocoOrPaxTrainCars
                .Where(
                    tc => tc.logicCar.CurrentCargoTypeInCar == CargoType.None
                        || tc.logicCar.LoadedCargoAmount < 0.001f)
                .ToList();
            var loadedFreightTrainCars = nonLocoOrPaxTrainCars
                .Where(
                    tc => tc.logicCar.CurrentCargoTypeInCar != CargoType.None
                        && tc.logicCar.LoadedCargoAmount >= 0.001f)
                .ToList();
            var emptyTrainCarsPerTrainSet = JobProceduralGenerationUtilities.GroupTrainCarsByTrainset(emptyFreightCars);
            var loadedTrainCarsPerTrainSet = JobProceduralGenerationUtilities.GroupTrainCarsByTrainset(loadedFreightTrainCars);
            Main._modEntry.Logger.Log(
                $"[PersistentJobs]\n" +
                $"    found {emptyTrainCarsPerTrainSet.Count} empty trainSets,\n" +
                $"    and {loadedTrainCarsPerTrainSet.Count} loaded trainSets");

            // group trainCars sets by nearest stationController
            Main._modEntry.Logger.Log("grouping trainSets by nearest station...");
            var emptyCgsPerTcsPerSc
                = JobProceduralGenerationUtilities.GroupTrainCarSetsByNearestStation(emptyTrainCarsPerTrainSet);
            var loadedCgsPerTcsPerSc
                = JobProceduralGenerationUtilities.GroupTrainCarSetsByNearestStation(loadedTrainCarsPerTrainSet);
            Main._modEntry.Logger.Log(
                $"[PersistentJobs]\n" +
                $"    found {emptyCgsPerTcsPerSc.Count} stations for empty trainSets,\n" +
                $"    and {loadedCgsPerTcsPerSc.Count} stations for loaded trainSets");

            // populate possible cargoGroups per group of trainCars
            Main._modEntry.Logger.Log("populating cargoGroups...");
            JobProceduralGenerationUtilities.PopulateCargoGroupsPerTrainCarSet(emptyCgsPerTcsPerSc);
            JobProceduralGenerationUtilities.PopulateCargoGroupsPerLoadedTrainCarSet(loadedCgsPerTcsPerSc);
            var emptyTcsPerSc
                = JobProceduralGenerationUtilities.ExtractEmptyHaulTrainSets(emptyCgsPerTcsPerSc);

            // pick new jobs for the trainCars at each station
            Main._modEntry.Logger.Log("picking jobs...");
            var rng = new System.Random(Environment.TickCount);
            var
                shuntingLoadJobInfos = ShuntingLoadJobGenerator
                    .ComputeJobInfosFromCargoGroupsPerTrainCarSetPerStation(emptyCgsPerTcsPerSc, rng);
            var
                transportJobInfos = TransportJobGenerator
                    .ComputeJobInfosFromCargoGroupsPerTrainCarSetPerStation(
                        loadedCgsPerTcsPerSc.Select(
                                kv => (
                                    kv.Key,
                                    kv.Value.Where(
                                        tpl => {
                                            var cg0 = tpl.Item2.FirstOrDefault();
                                            return cg0 != null && kv.Key.proceduralJobsRuleset.outputCargoGroups.Contains(cg0);
                                        }).ToList()))
                            .Where(tpl => tpl.Item2.Count > 0)
                            .ToDictionary(tpl => tpl.Item1, tpl => tpl.Item2),
                        rng);
            var
                shuntingUnloadJobInfos = ShuntingUnloadJobProceduralGenerator
                    .ComputeJobInfosFromCargoGroupsPerTrainCarSetPerStation(
                        loadedCgsPerTcsPerSc.Select(
                                kv => (
                                    kv.Key,
                                    kv.Value.Where(
                                        tpl => {
                                            var cg0 = tpl.Item2.FirstOrDefault();
                                            return cg0 != null && kv.Key.proceduralJobsRuleset.inputCargoGroups.Contains(cg0);
                                        }).ToList()))
                            .Where(tpl => tpl.Item2.Count > 0)
                            .ToDictionary(tpl => tpl.Item1, tpl => tpl.Item2),
                        rng);
            Main._modEntry.Logger.Log(
                $"[PersistentJobs]\n" +
                $"    chose {shuntingLoadJobInfos.Count} shunting load jobs,\n" +
                $"    {transportJobInfos.Count} transport jobs,\n" +
                $"    {shuntingUnloadJobInfos.Count} shunting unload jobs,\n" +
                $"    and {emptyTcsPerSc.Aggregate(0, (acc, kv) => acc + kv.Value.Count)} empty haul jobs");

            // try to generate jobs
            Main._modEntry.Logger.Log("generating jobs...");
            var shuntingLoadJobChainControllers
                = ShuntingLoadJobGenerator.doJobGeneration(shuntingLoadJobInfos, rng);
            var transportJobChainControllers
                = TransportJobGenerator.doJobGeneration(transportJobInfos, rng);
            var shuntingUnloadJobChainControllers
                = ShuntingUnloadJobProceduralGenerator.doJobGeneration(shuntingUnloadJobInfos, rng);
            IEnumerable<JobChainController> emptyHaulJobChainControllers = emptyTcsPerSc.Aggregate(
                new List<JobChainController>(),
                (list, kv) => {
                    list.AddRange(
                        kv.Value.Select(
                            tcs => EmptyHaulJobProceduralGenerator
                                .GenerateEmptyHaulJobWithExistingCars(kv.Key, tcs[0].logicCar.FrontBogieTrack, tcs, rng)));
                    return list;
                });
            Main._modEntry.Logger.Log(
                $"[PersistentJobs]\n" +
                $"    generated {shuntingLoadJobChainControllers.Where(jcc => jcc != null).Count()} shunting load jobs,\n" +
                $"    {transportJobChainControllers.Where(jcc => jcc != null).Count()} transport jobs,\n" +
                $"    {shuntingUnloadJobChainControllers.Where(jcc => jcc != null).Count()} shunting unload jobs,\n" +
                $"    and {emptyHaulJobChainControllers.Where(jcc => jcc != null).Count()} empty haul jobs");

            // finalize jobs & preserve job train cars
            Main._modEntry.Logger.Log("finalizing jobs...");

            var preservedTrainCars = new List<TrainCar>();

            foreach (var jcc in shuntingLoadJobChainControllers) {
                if (jcc != null) {
                    EnsureTrainCarsAreConvertedToNonPlayerSpawned(jcc.trainCarsForJobChain);
                    preservedTrainCars.AddRange(jcc.trainCarsForJobChain);
                    jcc.FinalizeSetupAndGenerateFirstJob();
                }
            }
            foreach (var jcc in transportJobChainControllers) {
                if (jcc != null) {
                    EnsureTrainCarsAreConvertedToNonPlayerSpawned(jcc.trainCarsForJobChain);
                    preservedTrainCars.AddRange(jcc.trainCarsForJobChain);
                    jcc.FinalizeSetupAndGenerateFirstJob();
                }
            }
            foreach (var jcc in shuntingUnloadJobChainControllers) {
                if (jcc != null) {
                    EnsureTrainCarsAreConvertedToNonPlayerSpawned(jcc.trainCarsForJobChain);
                    preservedTrainCars.AddRange(jcc.trainCarsForJobChain);
                    jcc.FinalizeSetupAndGenerateFirstJob();
                }
            }
            foreach (var jcc in emptyHaulJobChainControllers) {
                if (jcc != null) {
                    EnsureTrainCarsAreConvertedToNonPlayerSpawned(jcc.trainCarsForJobChain);
                    preservedTrainCars.AddRange(jcc.trainCarsForJobChain);
                    jcc.FinalizeSetupAndGenerateFirstJob();
                }
            }

            return preservedTrainCars;
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