using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using DV;
using DV.Logic.Job;
using DV.ThingTypes;
using DV.Utils;
using HarmonyLib;

namespace PersistentJobsMod {
    static class UnusedTrainCarDeleterPatch {
        /// <summary>tries to generate new jobs for the train cars marked for deletion</summary>
        [HarmonyPatch(typeof(UnusedTrainCarDeleter), "InstantConditionalDeleteOfUnusedCars")]
        class UnusedTrainCarDeleter_InstantConditionalDeleteOfUnusedCars_Patch {
            static bool Prefix(UnusedTrainCarDeleter __instance,
                List<TrainCar> ___unusedTrainCarsMarkedForDelete,
                Dictionary<TrainCar, CarVisitChecker> ___carVisitCheckersMap) {
                if (Main.modEntry.Active) {
                    try {
                        if (___unusedTrainCarsMarkedForDelete.Count == 0) {
                            return false;
                        }

                        Debug.Log("[PersistentJobs] collecting deletion candidates...");
                        var trainCarsToDelete = new List<TrainCar>();
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
                                ___unusedTrainCarsMarkedForDelete.RemoveAt(i);
                                trainCarsToDelete.Add(trainCar);
                            }
                        }
                        Debug.Log(
                            $"[PersistentJobs] found {trainCarsToDelete.Count} cars marked for deletion");
                        if (trainCarsToDelete.Count == 0) {
                            return false;
                        }

                        // ------ BEGIN JOB GENERATION ------
                        // group trainCars by trainset
                        Debug.Log("[PersistentJobs] grouping trainCars by trainSet...");
                        var nonLocoOrPaxTrainCars = trainCarsToDelete
                            .Where(tc => !CarTypes.IsAnyLocomotiveOrTender(tc.carLivery) && !Utilities.IsPassengerCar(tc.carType))
                            .ToList();
                        var emptyFreightCars = nonLocoOrPaxTrainCars
                            .Where(tc => tc.logicCar.CurrentCargoTypeInCar == CargoType.None
                                || tc.logicCar.LoadedCargoAmount < 0.001f)
                            .ToList();
                        var loadedFreightTrainCars = nonLocoOrPaxTrainCars
                            .Where(tc => tc.logicCar.CurrentCargoTypeInCar != CargoType.None
                                && tc.logicCar.LoadedCargoAmount >= 0.001f)
                            .ToList();
                        var emptyTrainCarsPerTrainSet = JobProceduralGenerationUtilities.GroupTrainCarsByTrainset(emptyFreightCars);
                        var loadedTrainCarsPerTrainSet = JobProceduralGenerationUtilities.GroupTrainCarsByTrainset(loadedFreightTrainCars);
                        Debug.Log(
                            $"[PersistentJobs]\n" +
                            $"    found {emptyTrainCarsPerTrainSet.Count} empty trainSets,\n" +
                            $"    and {loadedTrainCarsPerTrainSet.Count} loaded trainSets");

                        // group trainCars sets by nearest stationController
                        Debug.Log("[PersistentJobs] grouping trainSets by nearest station...");
                        var emptyCgsPerTcsPerSc
                            = JobProceduralGenerationUtilities.GroupTrainCarSetsByNearestStation(emptyTrainCarsPerTrainSet);
                        var loadedCgsPerTcsPerSc
                            = JobProceduralGenerationUtilities.GroupTrainCarSetsByNearestStation(loadedTrainCarsPerTrainSet);
                        Debug.Log(
                            $"[PersistentJobs]\n" +
                            $"    found {emptyCgsPerTcsPerSc.Count} stations for empty trainSets,\n" +
                            $"    and {loadedCgsPerTcsPerSc.Count} stations for loaded trainSets");

                        // populate possible cargoGroups per group of trainCars
                        Debug.Log("[PersistentJobs] populating cargoGroups...");
                        JobProceduralGenerationUtilities.PopulateCargoGroupsPerTrainCarSet(emptyCgsPerTcsPerSc);
                        JobProceduralGenerationUtilities.PopulateCargoGroupsPerLoadedTrainCarSet(loadedCgsPerTcsPerSc);
                        var emptyTcsPerSc
                            = JobProceduralGenerationUtilities.ExtractEmptyHaulTrainSets(emptyCgsPerTcsPerSc);

                        // pick new jobs for the trainCars at each station
                        Debug.Log("[PersistentJobs] picking jobs...");
                        var rng = new System.Random(Environment.TickCount);
                        var
                            shuntingLoadJobInfos = ShuntingLoadJobProceduralGenerator
                                .ComputeJobInfosFromCargoGroupsPerTrainCarSetPerStation(emptyCgsPerTcsPerSc, rng);
                        var
                            transportJobInfos = TransportJobProceduralGenerator
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
                        var
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
                        Debug.Log(
                            $"[PersistentJobs]\n" +
                            $"    chose {shuntingLoadJobInfos.Count} shunting load jobs,\n" +
                            $"    {transportJobInfos.Count} transport jobs,\n" +
                            $"    {shuntingUnloadJobInfos.Count} shunting unload jobs,\n" +
                            $"    and {emptyTcsPerSc.Aggregate(0, (acc, kv) => acc + kv.Value.Count)} empty haul jobs");

                        // try to generate jobs
                        Debug.Log("[PersistentJobs] generating jobs...");
                        var shuntingLoadJobChainControllers
                            = ShuntingLoadJobProceduralGenerator.doJobGeneration(shuntingLoadJobInfos, rng);
                        var transportJobChainControllers
                            = TransportJobProceduralGenerator.doJobGeneration(transportJobInfos, rng);
                        var shuntingUnloadJobChainControllers
                            = ShuntingUnloadJobProceduralGenerator.doJobGeneration(shuntingUnloadJobInfos, rng);
                        IEnumerable<JobChainController> emptyHaulJobChainControllers = emptyTcsPerSc.Aggregate(
                            new List<JobChainController>(),
                            (list, kv) => {
                                list.AddRange(
                                    kv.Value.Select(tcs => EmptyHaulJobProceduralGenerator
                                        .GenerateEmptyHaulJobWithExistingCars(kv.Key, tcs[0].logicCar.CurrentTrack, tcs, rng)));
                                return list;
                            });
                        Debug.Log(
                            $"[PersistentJobs]\n" +
                            $"    generated {shuntingLoadJobChainControllers.Where(jcc => jcc != null).Count()} shunting load jobs,\n" +
                            $"    {transportJobChainControllers.Where(jcc => jcc != null).Count()} transport jobs,\n" +
                            $"    {shuntingUnloadJobChainControllers.Where(jcc => jcc != null).Count()} shunting unload jobs,\n" +
                            $"    and {emptyHaulJobChainControllers.Where(jcc => jcc != null).Count()} empty haul jobs");

                        // finalize jobs & preserve job train cars
                        Debug.Log("[PersistentJobs] finalizing jobs...");
                        var totalCarsPreserved = 0;
                        foreach (var jcc in shuntingLoadJobChainControllers) {
                            if (jcc != null) {
                                jcc.trainCarsForJobChain.ForEach(tc => {
                                    // force job's train cars to not be treated as player spawned
                                    // DV will complain if we don't do this
                                    Utilities.ConvertPlayerSpawnedTrainCar(tc);
                                    trainCarsToDelete.Remove(tc);
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
                                    trainCarsToDelete.Remove(tc);
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
                                    trainCarsToDelete.Remove(tc);
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
                                    trainCarsToDelete.Remove(tc);
                                });
                                totalCarsPreserved += jcc.trainCarsForJobChain.Count;
                                jcc.FinalizeSetupAndGenerateFirstJob();
                            }
                        }

                        // preserve all trainCars that are not locos
                        Debug.Log("[PersistentJobs] preserving cars...");
                        foreach (var tc in new List<TrainCar>(trainCarsToDelete)) {
                            if (tc.playerSpawnedCar || !CarTypes.IsAnyLocomotiveOrTender(tc.carLivery)) {
                                trainCarsToDelete.Remove(tc);
                                ___unusedTrainCarsMarkedForDelete.Add(tc);
                                totalCarsPreserved += 1;
                            }
                        }
                        Debug.Log($"[PersistentJobs] preserved {totalCarsPreserved} cars");

                        // ------ END JOB GENERATION ------

                        Debug.Log("[PersistentJobs] deleting cars...");
                        foreach (var tc in trainCarsToDelete) {
                            ___unusedTrainCarsMarkedForDelete.Remove(tc);
                            ___carVisitCheckersMap.Remove(tc);
                        }
                        SingletonBehaviour<CarSpawner>.Instance.DeleteTrainCars(trainCarsToDelete, true);
                        Debug.Log($"[PersistentJobs] deleted {trainCarsToDelete.Count} cars");
                        return false;
                    } catch (Exception e) {
                        Main.modEntry.Logger.Error(
                            $"Exception thrown during {"UnusedTrainCarDeleter"}.{"InstantConditionalDeleteOfUnusedCars"} {"prefix"} patch:" +
                            $"\n{e.ToString()}");
                        Main.OnCriticalFailure();
                    }
                }
                return true;
            }
        }

        // override/replacement for UnusedTrainCarDeleter.TrainCarsDeleteCheck coroutine
        // tries to generate new jobs for the train cars marked for deletion
        public static IEnumerator TrainCarsCreateJobOrDeleteCheck(float period, float interopPeriod) {
            List<TrainCar> trainCarsToDelete = null;
            List<TrainCar> trainCarCandidatesForDelete = null;
            Traverse unusedTrainCarDeleterTraverser = null;
            List<TrainCar> unusedTrainCarsMarkedForDelete = null;
            Dictionary<TrainCar, DV.CarVisitChecker> carVisitCheckersMap = null;
            Traverse AreDeleteConditionsFulfilledMethod = null;
            try {
                trainCarsToDelete = new List<TrainCar>();
                trainCarCandidatesForDelete = new List<TrainCar>();
                unusedTrainCarDeleterTraverser = Traverse.Create(SingletonBehaviour<UnusedTrainCarDeleter>.Instance);
                unusedTrainCarsMarkedForDelete = unusedTrainCarDeleterTraverser
                    .Field("unusedTrainCarsMarkedForDelete")
                    .GetValue<List<TrainCar>>();
                carVisitCheckersMap = unusedTrainCarDeleterTraverser
                    .Field("carVisitCheckersMap")
                    .GetValue<Dictionary<TrainCar, DV.CarVisitChecker>>();
                AreDeleteConditionsFulfilledMethod
                    = unusedTrainCarDeleterTraverser.Method("AreDeleteConditionsFulfilled", new Type[] { typeof(TrainCar) });
            } catch (Exception e) {
                Main.modEntry.Logger.Error(
                    $"Exception thrown during TrainCarsCreateJobOrDeleteCheck setup:\n{e.ToString()}");
                Main.OnCriticalFailure();
            }
            for (;;) {
                yield return WaitFor.SecondsRealtime(period);

                try {
                    if (PlayerManager.PlayerTransform == null || FastTravelController.IsFastTravelling) {
                        continue;
                    }

                    if (unusedTrainCarsMarkedForDelete.Count == 0) {
                        if (carVisitCheckersMap.Count != 0) {
                            carVisitCheckersMap.Clear();
                        }
                        continue;
                    }
                } catch (Exception e) {
                    Main.modEntry.Logger.Error(
                        $"Exception thrown during TrainCarsCreateJobOrDeleteCheck skip checks:\n{e.ToString()}");
                    Main.OnCriticalFailure();
                }

                Debug.Log("[PersistentJobs] collecting deletion candidates... (coroutine)");
                try {
                    trainCarCandidatesForDelete.Clear();
                    for (var i = unusedTrainCarsMarkedForDelete.Count - 1; i >= 0; i--) {
                        var trainCar = unusedTrainCarsMarkedForDelete[i];
                        if (trainCar == null) {
                            unusedTrainCarsMarkedForDelete.RemoveAt(i);
                        } else if (AreDeleteConditionsFulfilledMethod.GetValue<bool>(trainCar)) {
                            unusedTrainCarsMarkedForDelete.RemoveAt(i);
                            trainCarCandidatesForDelete.Add(trainCar);
                        }
                    }
                    Debug.Log(
                        $"[PersistentJobs] found {trainCarCandidatesForDelete.Count} cars marked for deletion (coroutine)");
                    if (trainCarCandidatesForDelete.Count == 0) {
                        continue;
                    }
                } catch (Exception e) {
                    Main.modEntry.Logger.Error(
                        $"Exception thrown during TrainCarsCreateJobOrDeleteCheck delete candidate collection:\n{e.ToString()}");
                    Main.OnCriticalFailure();
                }

                yield return WaitFor.SecondsRealtime(interopPeriod);

                // ------ BEGIN JOB GENERATION ------
                // group trainCars by trainset
                Debug.Log("[PersistentJobs] grouping trainCars by trainSet... (coroutine)");
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
                    Main.modEntry.Logger.Error(
                        $"Exception thrown during TrainCarsCreateJobOrDeleteCheck trainset grouping:\n{e.ToString()}");
                    Main.OnCriticalFailure();
                }
                Debug.Log(
                    $"[PersistentJobs]\n" +
                    $"    found {paxTrainCarsPerTrainSet.Count} passenger trainSets,\n" +
                    $"    {emptyTrainCarsPerTrainSet.Count} empty trainSets,\n" +
                    $"    and {loadedTrainCarsPerTrainSet.Count} loaded trainSets (coroutine)");

                yield return WaitFor.SecondsRealtime(interopPeriod);

                // group trainCars sets by nearest stationController
                Debug.Log("[PersistentJobs] grouping trainSets by nearest station... (coroutine)");
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
                    Main.modEntry.Logger.Error(
                        $"Exception thrown during TrainCarsCreateJobOrDeleteCheck station grouping:\n{e.ToString()}");
                    Main.OnCriticalFailure();
                }
                Debug.Log(
                    $"[PersistentJobs]\n" +
                    $"    found {paxTcsPerSc.Count} stations for passenger trainSets\n," +
                    $"    {emptyCgsPerTcsPerSc.Count} stations for empty trainSets\n," +
                    $"    and {loadedCgsPerTcsPerSc.Count} stations for loaded trainSets (coroutine)");

                yield return WaitFor.SecondsRealtime(interopPeriod);

                // populate possible cargoGroups per group of trainCars
                Dictionary<StationController, List<List<TrainCar>>> emptyTcsPerSc = null;
                Debug.Log("[PersistentJobs] populating cargoGroups... (coroutine)");
                try {
                    JobProceduralGenerationUtilities.PopulateCargoGroupsPerTrainCarSet(emptyCgsPerTcsPerSc);
                    JobProceduralGenerationUtilities.PopulateCargoGroupsPerLoadedTrainCarSet(loadedCgsPerTcsPerSc);
                    emptyTcsPerSc = JobProceduralGenerationUtilities.ExtractEmptyHaulTrainSets(emptyCgsPerTcsPerSc);
                } catch (Exception e) {
                    Main.modEntry.Logger.Error(
                        $"Exception thrown during TrainCarsCreateJobOrDeleteCheck cargoGroup population:\n{e.ToString()}");
                    Main.OnCriticalFailure();
                }

                yield return WaitFor.SecondsRealtime(interopPeriod);

                // pick new jobs for the trainCars at each station
                Debug.Log("[PersistentJobs] picking jobs... (coroutine)");
                var rng = new System.Random(Environment.TickCount);
                List<(StationController, List<CarsPerTrack>, StationController, List<TrainCar>, List<CargoType>)>
                    shuntingLoadJobInfos = null;
                List<(StationController, Track, StationController, List<TrainCar>, List<CargoType>)>
                    transportJobInfos = null;
                List<(StationController, Track, StationController, List<TrainCar>, List<CargoType>)>
                    shuntingUnloadJobInfos = null;
                try {
                    shuntingLoadJobInfos = ShuntingLoadJobProceduralGenerator
                        .ComputeJobInfosFromCargoGroupsPerTrainCarSetPerStation(emptyCgsPerTcsPerSc, rng);

                    transportJobInfos = TransportJobProceduralGenerator
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
                    Main.modEntry.Logger.Error(
                        $"Exception thrown during TrainCarsCreateJobOrDeleteCheck job info selection:\n{e.ToString()}");
                    Main.OnCriticalFailure();
                }
                Debug.Log(
                    $"[PersistentJobs]\n" +
                    $"    chose {shuntingLoadJobInfos.Count} shunting load jobs,\n" +
                    $"    {transportJobInfos.Count} transport jobs,\n" +
                    $"    {shuntingUnloadJobInfos.Count} shunting unload jobs,\n" +
                    $"    and {emptyTcsPerSc.Aggregate(0, (acc, kv) => acc + kv.Value.Count)} empty haul jobs (coroutine)");

                yield return WaitFor.SecondsRealtime(interopPeriod);

                // try to generate jobs
                Debug.Log("[PersistentJobs] generating jobs... (coroutine)");
                IEnumerable<JobChainController> shuntingLoadJobChainControllers = null;
                IEnumerable<JobChainController> transportJobChainControllers = null;
                IEnumerable<JobChainController> shuntingUnloadJobChainControllers = null;
                IEnumerable<JobChainController> emptyHaulJobChainControllers = null;
                try {
                    shuntingLoadJobChainControllers
                        = ShuntingLoadJobProceduralGenerator.doJobGeneration(shuntingLoadJobInfos, rng);
                    transportJobChainControllers
                        = TransportJobProceduralGenerator.doJobGeneration(transportJobInfos, rng);
                    shuntingUnloadJobChainControllers
                        = ShuntingUnloadJobProceduralGenerator.doJobGeneration(shuntingUnloadJobInfos, rng);
                    emptyHaulJobChainControllers = emptyTcsPerSc.Aggregate(
                        new List<JobChainController>(),
                        (list, kv) => {
                            list.AddRange(
                                kv.Value.Select(tcs => EmptyHaulJobProceduralGenerator
                                    .GenerateEmptyHaulJobWithExistingCars(kv.Key, tcs[0].logicCar.CurrentTrack, tcs, rng)));
                            return list;
                        });
                } catch (Exception e) {
                    Main.modEntry.Logger.Error(
                        $"Exception thrown during TrainCarsCreateJobOrDeleteCheck job generation:\n{e.ToString()}");
                    Main.OnCriticalFailure();
                }
                Debug.Log(
                    $"[PersistentJobs]\n" +
                    $"    generated {shuntingLoadJobChainControllers.Where(jcc => jcc != null).Count()} shunting load jobs,\n" +
                    $"    {transportJobChainControllers.Where(jcc => jcc != null).Count()} transport jobs,\n" +
                    $"    {shuntingUnloadJobChainControllers.Where(jcc => jcc != null).Count()} shunting unload jobs,\n" +
                    $"    and {emptyHaulJobChainControllers.Where(jcc => jcc != null).Count()} empty haul jobs (coroutine)");

                yield return WaitFor.SecondsRealtime(interopPeriod);

                // finalize jobs & preserve job train cars
                Debug.Log("[PersistentJobs] finalizing jobs... (coroutine)");
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
                    Main.modEntry.Logger.Error(
                        $"Exception thrown during TrainCarsCreateJobOrDeleteCheck trainCar preservation:\n{e.ToString()}");
                    Main.OnCriticalFailure();
                }

                yield return WaitFor.SecondsRealtime(interopPeriod);

                // preserve all trainCars that are not locomotives
                Debug.Log("[PersistentJobs] preserving cars... (coroutine)");
                try {
                    foreach (var tc in new List<TrainCar>(trainCarCandidatesForDelete)) {
                        if (tc.playerSpawnedCar || !CarTypes.IsAnyLocomotiveOrTender(tc.carLivery)) {
                            trainCarCandidatesForDelete.Remove(tc);
                            unusedTrainCarsMarkedForDelete.Add(tc);
                            totalCarsPreserved += 1;
                        }
                    }
                    Debug.Log($"[PersistentJobs] preserved {totalCarsPreserved} cars (coroutine)");
                } catch (Exception e) {
                    Main.modEntry.Logger.Error(
                        $"Exception thrown during TrainCarsCreateJobOrDeleteCheck trainCar preservation:\n{e.ToString()}");
                    Main.OnCriticalFailure();
                }

                // ------ END JOB GENERATION ------

                yield return WaitFor.SecondsRealtime(interopPeriod);

                Debug.Log("[PersistentJobs] deleting cars... (coroutine)");
                try {
                    trainCarsToDelete.Clear();
                    for (var j = trainCarCandidatesForDelete.Count - 1; j >= 0; j--) {
                        var trainCar2 = trainCarCandidatesForDelete[j];
                        if (trainCar2 == null) {
                            trainCarCandidatesForDelete.RemoveAt(j);
                        } else if (AreDeleteConditionsFulfilledMethod.GetValue<bool>(trainCar2)) {
                            trainCarCandidatesForDelete.RemoveAt(j);
                            carVisitCheckersMap.Remove(trainCar2);
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
                    if (trainCarsToDelete.Count != 0) {
                        SingletonBehaviour<CarSpawner>.Instance
                            .DeleteTrainCars(new List<TrainCar>(trainCarsToDelete), false);
                    }
                    Debug.Log($"[PersistentJobs] deleted {trainCarsToDelete.Count} cars (coroutine)");
                } catch (Exception e) {
                    Main.modEntry.Logger.Error(
                        $"Exception thrown during TrainCarsCreateJobOrDeleteCheck car deletion:\n{e.ToString()}");
                    Main.OnCriticalFailure();
                }
            }
        }
    }
}