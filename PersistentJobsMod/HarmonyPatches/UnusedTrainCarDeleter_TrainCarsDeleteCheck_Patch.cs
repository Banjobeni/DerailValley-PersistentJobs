using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using DV.Logic.Job;
using DV.ThingTypes;
using DV.Utils;
using HarmonyLib;
using PersistentJobsMod.JobGenerators;
using UnityEngine;

namespace PersistentJobsMod.HarmonyPatches {
    // override/replacement for UnusedTrainCarDeleter.TrainCarsDeleteCheck coroutine
    // tries to generate new jobs for the train cars marked for deletion
    [HarmonyPatch]
    public static class UnusedTrainCarDeleter_TrainCarsDeleteCheck_Patch {
#if DEBUG
        private const float COROUTINE_INTERVAL = 60f;
#else
        private const float COROUTINE_INTERVAL = 5f * 60f;
#endif
        [HarmonyPatch(typeof(UnusedTrainCarDeleter), "TrainCarsDeleteCheck")]
        [HarmonyPrefix]
        public static bool TrainCarsDeleteCheck_Prefix(
                UnusedTrainCarDeleter __instance,
                float period,
                ref IEnumerator __result,
                List<TrainCar> ___unusedTrainCarsMarkedForDelete) {
            if (!Main._modEntry.Active) {
                return true;
            } else {
                Main._modEntry.Logger.Log("UnusedTrainCarDeleter_TrainCarsDeleteCheck_Patch taking Prefix");

                __result = TrainCarsCreateJobOrDeleteCheck(__instance, COROUTINE_INTERVAL, period, ___unusedTrainCarsMarkedForDelete);
                return false;
            }
        }

        [HarmonyReversePatch]
        [HarmonyPatch(typeof(UnusedTrainCarDeleter), "AreDeleteConditionsFulfilled")]
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool AreDeleteConditionsFulfilled(UnusedTrainCarDeleter instance, TrainCar trainCar) {
            throw new NotImplementedException("This is a stub");
        }

        private static IEnumerator TrainCarsCreateJobOrDeleteCheck(UnusedTrainCarDeleter unusedTrainCarDeleter, float interval, float stagesInteropPeriod, List<TrainCar> unusedTrainCarsMarkedForDelete) {
            for (; ; ) {
                yield return WaitFor.SecondsRealtime(interval);

                try {
                    if (PlayerManager.PlayerTransform == null || FastTravelController.IsFastTravelling) {
                        continue;
                    }

                    if (unusedTrainCarsMarkedForDelete.Count == 0) {
                        continue;
                    }
                } catch (Exception e) {
                    Main._modEntry.Logger.Error(
                        $"Exception thrown during TrainCarsCreateJobOrDeleteCheck skip checks:\n{e.ToString()}");
                    Main.OnCriticalFailure();
                }

                Main._modEntry.Logger.Log("collecting deletion candidates... (coroutine)");

                var trainCarCandidatesForDelete = new List<TrainCar>();
                try {
                    for (var i = unusedTrainCarsMarkedForDelete.Count - 1; i >= 0; i--) {
                        var trainCar = unusedTrainCarsMarkedForDelete[i];
                        if (trainCar == null) {
                            unusedTrainCarsMarkedForDelete.RemoveAt(i);
                        } else if (AreDeleteConditionsFulfilled(unusedTrainCarDeleter, trainCar)) {
                            unusedTrainCarsMarkedForDelete.RemoveAt(i);
                            trainCarCandidatesForDelete.Add(trainCar);
                        }
                    }
                    Main._modEntry.Logger.Log(
                        $"[PersistentJobs] found {trainCarCandidatesForDelete.Count} cars marked for deletion (coroutine)");
                    if (trainCarCandidatesForDelete.Count == 0) {
                        continue;
                    }
                } catch (Exception e) {
                    Main._modEntry.Logger.Error(
                        $"Exception thrown during TrainCarsCreateJobOrDeleteCheck delete candidate collection:\n{e.ToString()}");
                    Main.OnCriticalFailure();
                }

                yield return WaitFor.SecondsRealtime(stagesInteropPeriod);

                // ------ BEGIN JOB GENERATION ------
                // group trainCars by trainset
                Main._modEntry.Logger.Log("grouping trainCars by trainSet... (coroutine)");
                Dictionary<Trainset, List<TrainCar>> paxTrainCarsPerTrainSet = null;
                Dictionary<Trainset, List<TrainCar>> emptyTrainCarsPerTrainSet = null;
                Dictionary<Trainset, List<TrainCar>> loadedTrainCarsPerTrainSet = null;
                try {
                    var paxTrainCars = trainCarCandidatesForDelete
                        .Where(tc => Utilities.IsPassengerCar(tc.carType))
                        .ToList();
                    var nonLocoOrPaxTrainCars = trainCarCandidatesForDelete
                        .Where(tc => !CarTypes.IsAnyLocomotiveOrTender(tc.carLivery) && !Utilities.IsPassengerCar(tc.carType))
                        .ToList();
                    var emptyFreightCars = nonLocoOrPaxTrainCars
                        .Where(tc => tc.logicCar.CurrentCargoTypeInCar == CargoType.None
                            || tc.logicCar.LoadedCargoAmount < 0.001f)
                        .ToList();
                    var loadedFreightCars = nonLocoOrPaxTrainCars
                        .Where(tc => tc.logicCar.CurrentCargoTypeInCar != CargoType.None
                            && tc.logicCar.LoadedCargoAmount >= 0.001f)
                        .ToList();

                    paxTrainCarsPerTrainSet = JobProceduralGenerationUtilities
                        .GroupTrainCarsByTrainset(paxTrainCars);
                    emptyTrainCarsPerTrainSet = JobProceduralGenerationUtilities
                        .GroupTrainCarsByTrainset(emptyFreightCars);
                    loadedTrainCarsPerTrainSet = JobProceduralGenerationUtilities
                        .GroupTrainCarsByTrainset(loadedFreightCars);
                } catch (Exception e) {
                    Main._modEntry.Logger.Error(
                        $"Exception thrown during TrainCarsCreateJobOrDeleteCheck trainset grouping:\n{e.ToString()}");
                    Main.OnCriticalFailure();
                }
                Main._modEntry.Logger.Log(
                    $"[PersistentJobs]\n" +
                    $"    found {paxTrainCarsPerTrainSet.Count} passenger trainSets,\n" +
                    $"    {emptyTrainCarsPerTrainSet.Count} empty trainSets,\n" +
                    $"    and {loadedTrainCarsPerTrainSet.Count} loaded trainSets (coroutine)");

                yield return WaitFor.SecondsRealtime(stagesInteropPeriod);

                // group trainCars sets by nearest stationController
                Main._modEntry.Logger.Log("grouping trainSets by nearest station... (coroutine)");
                Dictionary<StationController, List<(List<TrainCar>, List<CargoGroup>)>> paxTcsPerSc = null;
                Dictionary<StationController, List<(List<TrainCar>, List<CargoGroup>)>> emptyCgsPerTcsPerSc = null;
                Dictionary<StationController, List<(List<TrainCar>, List<CargoGroup>)>> loadedCgsPerTcsPerSc = null;
                try {
                    paxTcsPerSc = JobProceduralGenerationUtilities
                        .GroupTrainCarSetsByNearestStation(paxTrainCarsPerTrainSet);
                    emptyCgsPerTcsPerSc = JobProceduralGenerationUtilities
                        .GroupTrainCarSetsByNearestStation(emptyTrainCarsPerTrainSet);
                    loadedCgsPerTcsPerSc = JobProceduralGenerationUtilities
                        .GroupTrainCarSetsByNearestStation(loadedTrainCarsPerTrainSet);
                } catch (Exception e) {
                    Main._modEntry.Logger.Error(
                        $"Exception thrown during TrainCarsCreateJobOrDeleteCheck station grouping:\n{e.ToString()}");
                    Main.OnCriticalFailure();
                }
                Main._modEntry.Logger.Log(
                    $"[PersistentJobs]\n" +
                    $"    found {paxTcsPerSc.Count} stations for passenger trainSets\n," +
                    $"    {emptyCgsPerTcsPerSc.Count} stations for empty trainSets\n," +
                    $"    and {loadedCgsPerTcsPerSc.Count} stations for loaded trainSets (coroutine)");

                yield return WaitFor.SecondsRealtime(stagesInteropPeriod);

                // populate possible cargoGroups per group of trainCars
                Dictionary<StationController, List<List<TrainCar>>> emptyTcsPerSc = null;
                Main._modEntry.Logger.Log("populating cargoGroups... (coroutine)");
                try {
                    JobProceduralGenerationUtilities.PopulateCargoGroupsPerTrainCarSet(emptyCgsPerTcsPerSc);
                    JobProceduralGenerationUtilities.PopulateCargoGroupsPerLoadedTrainCarSet(loadedCgsPerTcsPerSc);
                    emptyTcsPerSc = JobProceduralGenerationUtilities.ExtractEmptyHaulTrainSets(emptyCgsPerTcsPerSc);
                } catch (Exception e) {
                    Main._modEntry.Logger.Error(
                        $"Exception thrown during TrainCarsCreateJobOrDeleteCheck cargoGroup population:\n{e.ToString()}");
                    Main.OnCriticalFailure();
                }

                yield return WaitFor.SecondsRealtime(stagesInteropPeriod);

                // pick new jobs for the trainCars at each station
                Main._modEntry.Logger.Log("picking jobs... (coroutine)");
                var rng = new System.Random(Environment.TickCount);
                List<(StationController, List<CarsPerTrack>, StationController, List<TrainCar>, List<CargoType>)>
                    shuntingLoadJobInfos = null;
                List<(StationController, Track, StationController, List<TrainCar>, List<CargoType>)>
                    transportJobInfos = null;
                List<(StationController, Track, StationController, List<TrainCar>, List<CargoType>)>
                    shuntingUnloadJobInfos = null;
                try {
                    shuntingLoadJobInfos = ShuntingLoadJobGenerator
                        .ComputeJobInfosFromCargoGroupsPerTrainCarSetPerStation(emptyCgsPerTcsPerSc, rng);

                    transportJobInfos = TransportJobGenerator
                        .ComputeJobInfosFromCargoGroupsPerTrainCarSetPerStation(
                            loadedCgsPerTcsPerSc.Select(kv => (
                                    kv.Key,
                                    kv.Value.Where(tpl => {
                                        var cg0 = tpl.Item2.FirstOrDefault();
                                        return cg0 != null && kv.Key.proceduralJobsRuleset.outputCargoGroups.Contains(cg0);
                                    }).ToList()))
                                .Where(tpl => tpl.Item2.Count > 0)
                                .ToDictionary(tpl => tpl.Item1, tpl => tpl.Item2),
                            rng);

                    shuntingUnloadJobInfos = ShuntingUnloadJobProceduralGenerator
                        .ComputeJobInfosFromCargoGroupsPerTrainCarSetPerStation(
                            loadedCgsPerTcsPerSc.Select(kv => (
                                    kv.Key,
                                    kv.Value.Where(tpl => {
                                        var cg0 = tpl.Item2.FirstOrDefault();
                                        return cg0 != null && kv.Key.proceduralJobsRuleset.inputCargoGroups.Contains(cg0);
                                    }).ToList()))
                                .Where(tpl => tpl.Item2.Count > 0)
                                .ToDictionary(tpl => tpl.Item1, tpl => tpl.Item2),
                            rng);
                } catch (Exception e) {
                    Main._modEntry.Logger.Error(
                        $"Exception thrown during TrainCarsCreateJobOrDeleteCheck job info selection:\n{e.ToString()}");
                    Main.OnCriticalFailure();
                }
                Main._modEntry.Logger.Log(
                    $"[PersistentJobs]\n" +
                    $"    chose {shuntingLoadJobInfos.Count} shunting load jobs,\n" +
                    $"    {transportJobInfos.Count} transport jobs,\n" +
                    $"    {shuntingUnloadJobInfos.Count} shunting unload jobs,\n" +
                    $"    and {emptyTcsPerSc.Aggregate(0, (acc, kv) => acc + kv.Value.Count)} empty haul jobs (coroutine)");

                yield return WaitFor.SecondsRealtime(stagesInteropPeriod);

                // try to generate jobs
                Main._modEntry.Logger.Log("generating jobs... (coroutine)");
                IEnumerable<JobChainController> shuntingLoadJobChainControllers = null;
                IEnumerable<JobChainController> transportJobChainControllers = null;
                IEnumerable<JobChainController> shuntingUnloadJobChainControllers = null;
                IEnumerable<JobChainController> emptyHaulJobChainControllers = null;
                try {
                    shuntingLoadJobChainControllers
                        = ShuntingLoadJobGenerator.doJobGeneration(shuntingLoadJobInfos, rng);
                    transportJobChainControllers
                        = TransportJobGenerator.doJobGeneration(transportJobInfos, rng);
                    shuntingUnloadJobChainControllers
                        = ShuntingUnloadJobProceduralGenerator.doJobGeneration(shuntingUnloadJobInfos, rng);
                    emptyHaulJobChainControllers = emptyTcsPerSc.Aggregate(
                        new List<JobChainController>(),
                        (list, kv) => {
                            list.AddRange(
                                kv.Value.Select(tcs => EmptyHaulJobProceduralGenerator
                                    .GenerateEmptyHaulJobWithExistingCars(kv.Key, tcs[0].logicCar.FrontBogieTrack, tcs, rng)));
                            return list;
                        });
                } catch (Exception e) {
                    Main._modEntry.Logger.Error(
                        $"Exception thrown during TrainCarsCreateJobOrDeleteCheck job generation:\n{e.ToString()}");
                    Main.OnCriticalFailure();
                }
                Main._modEntry.Logger.Log(
                    $"[PersistentJobs]\n" +
                    $"    generated {shuntingLoadJobChainControllers.Where(jcc => jcc != null).Count()} shunting load jobs,\n" +
                    $"    {transportJobChainControllers.Where(jcc => jcc != null).Count()} transport jobs,\n" +
                    $"    {shuntingUnloadJobChainControllers.Where(jcc => jcc != null).Count()} shunting unload jobs,\n" +
                    $"    and {emptyHaulJobChainControllers.Where(jcc => jcc != null).Count()} empty haul jobs (coroutine)");

                yield return WaitFor.SecondsRealtime(stagesInteropPeriod);

                // finalize jobs & preserve job train cars
                Main._modEntry.Logger.Log("finalizing jobs... (coroutine)");
                var totalCarsPreserved = 0;
                try {
                    foreach (var jcc in shuntingLoadJobChainControllers) {
                        if (jcc != null) {
                            jcc.trainCarsForJobChain.ForEach(tc => {
                                // force job's train cars to not be treated as player spawned
                                // DV will complain if we don't do this
                                Utilities.ConvertPlayerSpawnedTrainCar(tc);
                                trainCarCandidatesForDelete.Remove(tc);
                            });
                            totalCarsPreserved += jcc.trainCarsForJobChain.Count;
                            jcc.FinalizeSetupAndGenerateFirstJob();
                        }
                    }

                    foreach (var jcc in transportJobChainControllers) {
                        if (jcc != null) {
                            jcc.trainCarsForJobChain.ForEach(tc => {
                                // force job's train cars to not be treated as player spawned
                                // DV will complain if we don't do this
                                Utilities.ConvertPlayerSpawnedTrainCar(tc);
                                trainCarCandidatesForDelete.Remove(tc);
                            });
                            totalCarsPreserved += jcc.trainCarsForJobChain.Count;
                            jcc.FinalizeSetupAndGenerateFirstJob();
                        }
                    }

                    foreach (var jcc in shuntingUnloadJobChainControllers) {
                        if (jcc != null) {
                            jcc.trainCarsForJobChain.ForEach(tc => {
                                // force job's train cars to not be treated as player spawned
                                // DV will complain if we don't do this
                                Utilities.ConvertPlayerSpawnedTrainCar(tc);
                                trainCarCandidatesForDelete.Remove(tc);
                            });
                            totalCarsPreserved += jcc.trainCarsForJobChain.Count;
                            jcc.FinalizeSetupAndGenerateFirstJob();
                        }
                    }

                    foreach (var jcc in emptyHaulJobChainControllers) {
                        if (jcc != null) {
                            jcc.trainCarsForJobChain.ForEach(tc => {
                                // force job's train cars to not be treated as player spawned
                                // DV will complain if we don't do this
                                Utilities.ConvertPlayerSpawnedTrainCar(tc);
                                trainCarCandidatesForDelete.Remove(tc);
                            });
                            totalCarsPreserved += jcc.trainCarsForJobChain.Count;
                            jcc.FinalizeSetupAndGenerateFirstJob();
                        }
                    }
                } catch (Exception e) {
                    Main._modEntry.Logger.Error(
                        $"Exception thrown during TrainCarsCreateJobOrDeleteCheck trainCar preservation:\n{e.ToString()}");
                    Main.OnCriticalFailure();
                }

                yield return WaitFor.SecondsRealtime(stagesInteropPeriod);

                // preserve all trainCars that are not locomotives
                Main._modEntry.Logger.Log("preserving cars... (coroutine)");
                try {
                    foreach (var tc in new List<TrainCar>(trainCarCandidatesForDelete)) {
                        if (tc.playerSpawnedCar || !CarTypes.IsAnyLocomotiveOrTender(tc.carLivery)) {
                            trainCarCandidatesForDelete.Remove(tc);
                            unusedTrainCarsMarkedForDelete.Add(tc);
                            totalCarsPreserved += 1;
                        }
                    }
                    Main._modEntry.Logger.Log($"preserved {totalCarsPreserved} cars (coroutine)");
                } catch (Exception e) {
                    Main._modEntry.Logger.Error(
                        $"Exception thrown during TrainCarsCreateJobOrDeleteCheck trainCar preservation:\n{e.ToString()}");
                    Main.OnCriticalFailure();
                }

                // ------ END JOB GENERATION ------

                yield return WaitFor.SecondsRealtime(stagesInteropPeriod);

                Main._modEntry.Logger.Log("deleting cars... (coroutine)");
                
                var trainCarsToDelete = new List<TrainCar>();
                try {
                    for (var j = trainCarCandidatesForDelete.Count - 1; j >= 0; j--) {
                        var trainCar2 = trainCarCandidatesForDelete[j];
                        if (trainCar2 == null) {
                            trainCarCandidatesForDelete.RemoveAt(j);
                        } else if (AreDeleteConditionsFulfilled(unusedTrainCarDeleter, trainCar2)) {
                            trainCarCandidatesForDelete.RemoveAt(j);
                            trainCarsToDelete.Add(trainCar2);
                        } else {
                            Debug.LogWarning(
                                $"Returning {trainCar2.name} to unusedTrainCarsMarkedForDelete list. PlayerTransform was outside" +
                                " of DELETE_SQR_DISTANCE_FROM_TRAINCAR range of train car, but after short period it" +
                                " was back in range!");
                            trainCarCandidatesForDelete.RemoveAt(j);
                            unusedTrainCarsMarkedForDelete.Add(trainCar2);
                        }
                    }

                    var locosToDelete = trainCarsToDelete.Where(tc => CarTypes.IsAnyLocomotiveOrTender(tc.carLivery)).ToList();
                    var carsToDelete = trainCarsToDelete.Where(tc => !CarTypes.IsAnyLocomotiveOrTender(tc.carLivery)).ToList();

                    if (trainCarsToDelete.Count != 0) {
                        SingletonBehaviour<CarSpawner>.Instance.DeleteTrainCars(new List<TrainCar>(trainCarsToDelete), false);
                    }

                    Main._modEntry.Logger.Log($"deleted {locosToDelete.Count} locomotives or tenders (coroutine)");
                    Main._modEntry.Logger.Log($"deleted {carsToDelete.Count} cars (coroutine)");
                } catch (Exception e) {
                    Main._modEntry.Logger.Error(
                        $"Exception thrown during TrainCarsCreateJobOrDeleteCheck car deletion:\n{e.ToString()}");
                    Main.OnCriticalFailure();
                }
            }
        }
    }
}