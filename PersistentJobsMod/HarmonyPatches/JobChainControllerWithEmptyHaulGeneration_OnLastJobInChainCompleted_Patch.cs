using System;
using System.Collections.Generic;
using System.Linq;
using DV;
using DV.Logic.Job;
using DV.ServicePenalty;
using DV.ThingTypes;
using DV.ThingTypes.TransitionHelpers;
using DV.Utils;
using Harmony12;
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
            Debug.Log("[PersistentJobs] last job chain empty haul gen");
            try {
                StaticJobDefinition lastJobDef = ___jobChain[___jobChain.Count - 1];
                if (lastJobDef.job != lastJobInChain) {
                    Debug.LogError(string.Format(
                        "[PersistentJobs] lastJobInChain ({0}) does not match lastJobDef.job ({1})",
                        lastJobInChain.ID,
                        lastJobDef.job.ID));
                } else if (lastJobInChain.jobType == JobType.ShuntingUnload) {
                    Debug.Log("[PersistentJobs] checking static definition type");
                    StaticShuntingUnloadJobDefinition unloadJobDef = lastJobDef as StaticShuntingUnloadJobDefinition;
                    if (unloadJobDef != null) {
                        StationController station = SingletonBehaviour<LogicController>.Instance
                            .YardIdToStationController[lastJobInChain.chainData.chainDestinationYardId];
                        List<CargoGroup> availableCargoGroups = station.proceduralJobsRuleset.outputCargoGroups;

                        Debug.Log("[PersistentJobs] diverting trainCars");
                        int countCarsDiverted = 0;

                        // if a trainCar set can be reused at the current station, keep them there
                        for (int i = unloadJobDef.carsPerDestinationTrack.Count - 1; i >= 0; i--) {
                            CarsPerTrack cpt = unloadJobDef.carsPerDestinationTrack[i];

                            // check if there is any cargoGroup that satisfies all the cars
                            if (availableCargoGroups.Any(
                                    cg => cpt.cars.All(
                                        c => Globals.G.Types.CarTypeToLoadableCargo[c.carType.parentType]
                                            .Intersect(cg.cargoTypes.Select(ct => TransitionHelpers.ToV2((CargoType)ct)))
                                            .Any()))) {
                                // registering the cars as jobless & removing them from carsPerDestinationTrack
                                // prevents base method from generating an EmptyHaul job for them
                                // they will be candidates for new jobs once the player leaves the area
                                List<TrainCar> tcsToDivert = new List<TrainCar>();
                                foreach (Car c in cpt.cars) {
                                    tcsToDivert.Add(SingletonBehaviour<IdGenerator>.Instance.logicCarToTrainCar[c]);
                                    tcsToDivert[tcsToDivert.Count - 1].UpdateJobIdOnCarPlates(string.Empty);
                                }
                                SingletonBehaviour<JobDebtController>.Instance.RegisterJoblessCars(tcsToDivert);
                                countCarsDiverted += tcsToDivert.Count;
                                unloadJobDef.carsPerDestinationTrack.Remove(cpt);
                            }
                        }
                        Debug.Log(string.Format("[PersistentJobs] diverted {0} trainCars", countCarsDiverted));
                    } else {
                        Debug.LogError("[PersistentJobs] Couldn't convert lastJobDef to " +
                            "StaticShuntingUnloadJobDefinition. EmptyHaul jobs won't be generated.");
                    }
                } else if (lastJobInChain.jobType == JobType.ShuntingLoad) {
                    StaticShuntingLoadJobDefinition loadJobDef = lastJobDef as StaticShuntingLoadJobDefinition;
                    if (loadJobDef != null) {
                        StationController startingStation = SingletonBehaviour<LogicController>.Instance
                            .YardIdToStationController[loadJobDef.logicStation.ID];
                        StationController destStation = SingletonBehaviour<LogicController>.Instance
                            .YardIdToStationController[loadJobDef.chainData.chainDestinationYardId];
                        Track startingTrack = loadJobDef.destinationTrack;
                        List<TrainCar> trainCars = new List<TrainCar>(__instance.trainCarsForJobChain);
                        System.Random rng = new System.Random(Environment.TickCount);
                        JobChainController jobChainController
                            = TransportJobProceduralGenerator.GenerateTransportJobWithExistingCars(
                                startingStation,
                                startingTrack,
                                destStation,
                                trainCars,
                                trainCars.Select<TrainCar, CargoType>(tc => tc.logicCar.CurrentCargoTypeInCar)
                                    .ToList(),
                                rng
                            );
                        if (jobChainController != null) {
                            foreach (TrainCar tc in jobChainController.trainCarsForJobChain) {
                                __instance.trainCarsForJobChain.Remove(tc);
                            }
                            jobChainController.FinalizeSetupAndGenerateFirstJob();
                            Debug.Log(string.Format(
                                "[PersistentJobs] Generated job chain [{0}]: {1}",
                                jobChainController.jobChainGO.name,
                                jobChainController.jobChainGO));
                        }
                    } else {
                        Debug.LogError(
                            "[PersistentJobs] Couldn't convert lastJobDef to StaticShuntingLoadDefinition." +
                            " Transport jobs won't be generated."
                        );
                    }
                } else if (lastJobInChain.jobType == JobType.Transport) {
                    StaticTransportJobDefinition loadJobDef = lastJobDef as StaticTransportJobDefinition;
                    if (loadJobDef != null) {
                        StationController startingStation = SingletonBehaviour<LogicController>.Instance
                            .YardIdToStationController[loadJobDef.logicStation.ID];
                        StationController destStation = SingletonBehaviour<LogicController>.Instance
                            .YardIdToStationController[loadJobDef.chainData.chainDestinationYardId];
                        Track startingTrack = loadJobDef.destinationTrack;
                        List<TrainCar> trainCars = new List<TrainCar>(__instance.trainCarsForJobChain);
                        System.Random rng = new System.Random(Environment.TickCount);
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
                            foreach (TrainCar tc in jobChainController.trainCarsForJobChain) {
                                __instance.trainCarsForJobChain.Remove(tc);
                            }
                            jobChainController.FinalizeSetupAndGenerateFirstJob();
                            Debug.Log(string.Format(
                                "[PersistentJobs] Generated job chain [{0}]: {1}",
                                jobChainController.jobChainGO.name,
                                jobChainController.jobChainGO));
                        }
                    } else {
                        Debug.LogError(
                            "[PersistentJobs] Couldn't convert lastJobDef to StaticTransportDefinition." +
                            " ShuntingUnload jobs won't be generated."
                        );
                    }
                } else {
                    Debug.LogError(string.Format(
                        "[PersistentJobs] Unexpected job type: {0}. The last job in chain must be " +
                        "ShuntingLoad, Transport, or ShuntingUnload for JobChainControllerWithEmptyHaulGeneration patch! " +
                        "New jobs won't be generated.",
                        lastJobInChain.jobType));
                }
            } catch (Exception e) {
                Main.modEntry.Logger.Error(string.Format(
                    "Exception thrown during {0}.{1} {2} patch:\n{3}",
                    "JobChainControllerWithEmptyHaulGeneration",
                    "OnLastJobInChainCompleted",
                    "prefix",
                    e.ToString()));
                Main.OnCriticalFailure();
            }
        }
    }
}