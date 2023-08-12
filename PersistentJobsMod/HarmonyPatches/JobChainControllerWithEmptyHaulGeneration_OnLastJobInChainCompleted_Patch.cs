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
    [HarmonyPatch(typeof(JobChainControllerWithEmptyHaulGeneration), "OnLastJobInChainCompleted")]
    class JobChainControllerWithEmptyHaulGeneration_OnLastJobInChainCompleted_Patch {
        static void Prefix(JobChainControllerWithEmptyHaulGeneration __instance,
            List<StaticJobDefinition> ___jobChain,
            Job lastJobInChain) {
            Main._modEntry.Logger.Log("last job chain empty haul gen");
            try {
                var lastJobDef = ___jobChain[___jobChain.Count - 1];
                if (lastJobDef.job != lastJobInChain) {
                    Debug.LogError($"[PersistentJobs] lastJobInChain ({lastJobInChain.ID}) does not match lastJobDef.job ({lastJobDef.job.ID})");
                } else if (lastJobInChain.jobType == JobType.ShuntingUnload) {
                    Main._modEntry.Logger.Log("checking static definition type");
                    var unloadJobDef = lastJobDef as StaticShuntingUnloadJobDefinition;
                    if (unloadJobDef != null) {
                        var station = SingletonBehaviour<LogicController>.Instance
                            .YardIdToStationController[lastJobInChain.chainData.chainDestinationYardId];
                        var availableCargoGroups = station.proceduralJobsRuleset.outputCargoGroups;

                        Main._modEntry.Logger.Log("diverting trainCars");
                        var countCarsDiverted = 0;

                        // if a trainCar set can be reused at the current station, keep them there
                        for (var i = unloadJobDef.carsPerDestinationTrack.Count - 1; i >= 0; i--) {
                            var cpt = unloadJobDef.carsPerDestinationTrack[i];

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
                                unloadJobDef.carsPerDestinationTrack.Remove(cpt);
                            }
                        }
                        Main._modEntry.Logger.Log($"diverted {countCarsDiverted} trainCars");
                    } else {
                        Debug.LogError("[PersistentJobs] Couldn't convert lastJobDef to " +
                            "StaticShuntingUnloadJobDefinition. EmptyHaul jobs won't be generated.");
                    }
                } else if (lastJobInChain.jobType == JobType.ShuntingLoad) {
                    var loadJobDef = lastJobDef as StaticShuntingLoadJobDefinition;
                    if (loadJobDef != null) {
                        var startingStation = SingletonBehaviour<LogicController>.Instance
                            .YardIdToStationController[loadJobDef.logicStation.ID];
                        var destStation = SingletonBehaviour<LogicController>.Instance
                            .YardIdToStationController[loadJobDef.chainData.chainDestinationYardId];
                        var startingTrack = loadJobDef.destinationTrack;
                        var trainCars = new List<TrainCar>(__instance.trainCarsForJobChain);
                        var rng = new System.Random(Environment.TickCount);
                        JobChainController jobChainController
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
                    } else {
                        Debug.LogError(
                            "[PersistentJobs] Couldn't convert lastJobDef to StaticShuntingLoadDefinition." +
                            " Transport jobs won't be generated."
                        );
                    }
                } else if (lastJobInChain.jobType == JobType.Transport) {
                    var loadJobDef = lastJobDef as StaticTransportJobDefinition;
                    if (loadJobDef != null) {
                        var startingStation = SingletonBehaviour<LogicController>.Instance
                            .YardIdToStationController[loadJobDef.logicStation.ID];
                        var destStation = SingletonBehaviour<LogicController>.Instance
                            .YardIdToStationController[loadJobDef.chainData.chainDestinationYardId];
                        var startingTrack = loadJobDef.destinationTrack;
                        var trainCars = new List<TrainCar>(__instance.trainCarsForJobChain);
                        var rng = new System.Random(Environment.TickCount);
                        JobChainController jobChainController
                            = ShuntingUnloadJobProceduralGenerator.GenerateShuntingUnloadJobWithExistingCars(
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
                    } else {
                        Debug.LogError(
                            "[PersistentJobs] Couldn't convert lastJobDef to StaticTransportDefinition." +
                            " ShuntingUnload jobs won't be generated."
                        );
                    }
                } else {
                    Debug.LogError($"[PersistentJobs] Unexpected job type: {lastJobInChain.jobType}. The last job in chain must be " + "ShuntingLoad, Transport, or ShuntingUnload for JobChainControllerWithEmptyHaulGeneration patch! " + "New jobs won't be generated.");
                }
            } catch (Exception e) {
                Main._modEntry.Logger.Error($"Exception thrown during {"JobChainControllerWithEmptyHaulGeneration"}.{"OnLastJobInChainCompleted"} {"prefix"} patch:\n{e.ToString()}");
                Main.OnCriticalFailure();
            }
        }
    }
}