using System;
using System.Collections.Generic;
using System.Linq;
using DV;
using DV.Logic.Job;
using DV.ServicePenalty;
using DV.ThingTypes;
using DV.ThingTypes.TransitionHelpers;
using DV.Utils;
using HarmonyLib;
using PersistentJobsMod.JobGenerators;
using UnityEngine;

namespace PersistentJobsMod.HarmonyPatches {
    /// <summary>
    /// unload: divert cars that can be loaded at the current station for later generation of ShuntingLoad jobs
    /// load: generates a corresponding transport job
    /// transport: generates a corresponding unload job
    /// </summary>
    [HarmonyPatch(typeof(JobChainController), "OnLastJobInChainCompleted")]
    class JobChainController_OnLastJobInChainCompleted_Patch {
        static void Prefix(JobChainController __instance,
                List<StaticJobDefinition> ___jobChain,
                Job lastJobInChain) {
            if (!Main._modEntry.Active) {
                return;
            }

            if (__instance.GetType() != typeof(JobChainController) || ___jobChain.Count != 1) {
                // we only generate single-job-JobChainControllers in this mod. other types may come from a time where the mod wasn't installed or active.
                return;
            }

            Main._modEntry.Logger.Log("last job chain empty haul gen");
            try {
                var lastJobDefinition = ___jobChain[___jobChain.Count - 1];
                if (lastJobDefinition.job != lastJobInChain) {
                    Debug.LogError($"[PersistentJobs] lastJobInChain ({lastJobInChain.ID}) does not match lastJobDef.job ({lastJobDefinition.job.ID})");
                    return;
                }

                if (lastJobInChain.jobType == JobType.ShuntingLoad && lastJobDefinition is StaticShuntingLoadJobDefinition shuntingLoadJobDefinition) {
                    HandleShuntingLoadJobCompleted(__instance, shuntingLoadJobDefinition);
                } else if (lastJobInChain.jobType == JobType.Transport && lastJobDefinition is StaticTransportJobDefinition transportJobDefinition) {
                    HandleTransportJobCompleted(__instance, transportJobDefinition);
                } else if (lastJobInChain.jobType == JobType.ShuntingUnload && lastJobDefinition is StaticShuntingUnloadJobDefinition) {
                    // nothing to do. JobChainController will register the cars as jobless and they may then be chosen for further jobs
                } else if (lastJobInChain.jobType == JobType.EmptyHaul && lastJobDefinition is StaticEmptyHaulJobDefinition) {
                    // nothing to do. JobChainController will register the cars as jobless and they may then be chosen for further jobs
                } else {
                    Debug.LogError($"[PersistentJobs] Cannot handle unexpected job type {lastJobInChain.jobType} and {lastJobDefinition.GetType()} combination.");
                }
            } catch (Exception e) {
                Main._modEntry.Logger.Error($"Exception thrown during {"JobChainControllerWithEmptyHaulGeneration"}.{"OnLastJobInChainCompleted"} {"prefix"} patch:\n{e.ToString()}");
                Main.OnCriticalFailure();
            }
        }

        private static void HandleTransportJobCompleted(JobChainController __instance, StaticTransportJobDefinition transportJobDefinition) {
            var startingStation = SingletonBehaviour<LogicController>.Instance.YardIdToStationController[transportJobDefinition.logicStation.ID];
            var destinationStation = SingletonBehaviour<LogicController>.Instance.YardIdToStationController[transportJobDefinition.chainData.chainDestinationYardId];

            var startingTrack = transportJobDefinition.destinationTrack;

            var trainCars = new List<TrainCar>(__instance.trainCarsForJobChain);
            var rng = new System.Random(Environment.TickCount);

            var perCarCargoTypes = trainCars.Select(tc => tc.logicCar.CurrentCargoTypeInCar).ToList();

            var subsequentJobChainController = ShuntingUnloadJobProceduralGenerator.TryGenerateJobChainController(startingStation, startingTrack, destinationStation, trainCars, perCarCargoTypes, rng);

            if (subsequentJobChainController != null) {
                foreach (var tc in subsequentJobChainController.trainCarsForJobChain) {
                    __instance.trainCarsForJobChain.Remove(tc);
                }
                subsequentJobChainController.FinalizeSetupAndGenerateFirstJob();
                Main._modEntry.Logger.Log($"Generated job chain [{subsequentJobChainController.jobChainGO.name}]: {subsequentJobChainController.jobChainGO}");
            }
        }

        private static void HandleShuntingLoadJobCompleted(JobChainController __instance, StaticShuntingLoadJobDefinition shuntingLoadJobDefinition) {
            var startingStation = SingletonBehaviour<LogicController>.Instance
                .YardIdToStationController[shuntingLoadJobDefinition.logicStation.ID];
            var destStation = SingletonBehaviour<LogicController>.Instance
                .YardIdToStationController[shuntingLoadJobDefinition.chainData.chainDestinationYardId];
            var startingTrack = shuntingLoadJobDefinition.destinationTrack;
            var trainCars = new List<TrainCar>(__instance.trainCarsForJobChain);
            var rng = new System.Random(Environment.TickCount);
            var jobChainController
                = TransportJobGenerator.TryGenerateJobChainController(
                    startingStation,
                    startingTrack,
                    destStation,
                    trainCars,
                    trainCars.Select<TrainCar, CargoType>(tc => tc.logicCar.CurrentCargoTypeInCar)
                        .ToList(),
                    rng
                );
            if (jobChainController != null) {
                foreach (var tc in jobChainController.trainCarsForJobChain) {
                    __instance.trainCarsForJobChain.Remove(tc);
                }
                jobChainController.FinalizeSetupAndGenerateFirstJob();
                Main._modEntry.Logger.Log($"Generated job chain [{jobChainController.jobChainGO.name}]: {jobChainController.jobChainGO}");
            }
        }

        private static void HandleShuntingUnloadJobFinished(StaticShuntingUnloadJobDefinition shuntingUnloadJobDefinition) {
            var station = SingletonBehaviour<LogicController>.Instance.YardIdToStationController[shuntingUnloadJobDefinition.chainData.chainDestinationYardId];
            var availableCargoGroups = station.proceduralJobsRuleset.outputCargoGroups;

            Main._modEntry.Logger.Log("diverting trainCars");
            var countCarsDiverted = 0;

            // if a trainCar set can be reused at the current station, keep them there
            for (var i = shuntingUnloadJobDefinition.carsPerDestinationTrack.Count - 1; i >= 0; i--) {
                var cpt = shuntingUnloadJobDefinition.carsPerDestinationTrack[i];

                // check if there is any cargoGroup that satisfies all the cars
                if (availableCargoGroups.Any(
                        cg => cpt.cars.All(
                            c => Globals.G.Types.CarTypeToLoadableCargo[c.carType.parentType]
                                .Intersect(cg.cargoTypes.Select(ct => TransitionHelpers.ToV2((CargoType)ct)))
                                .Any()))) {
                    // registering the cars as jobless & removing them from carsPerDestinationTrack
                    // prevents base method from generating an EmptyHaul job for them
                    // they will be candidates for new jobs once the player leaves the area
                    var tcsToDivert = new List<TrainCar>();
                    foreach (var c in cpt.cars) {
                        tcsToDivert.Add(SingletonBehaviour<IdGenerator>.Instance.logicCarToTrainCar[c]);
                        tcsToDivert[tcsToDivert.Count - 1].UpdateJobIdOnCarPlates(string.Empty);
                    }
                    SingletonBehaviour<JobDebtController>.Instance.RegisterJoblessCars(tcsToDivert);
                    countCarsDiverted += tcsToDivert.Count;
                    shuntingUnloadJobDefinition.carsPerDestinationTrack.Remove(cpt);
                }
            }
            Main._modEntry.Logger.Log($"diverted {countCarsDiverted} trainCars");
        }
    }
}