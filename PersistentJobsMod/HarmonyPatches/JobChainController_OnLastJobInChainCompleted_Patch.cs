using System;
using System.Collections.Generic;
using System.Linq;
using DV.Logic.Job;
using DV.ThingTypes;
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

            try {
                var lastJobDefinition = ___jobChain[___jobChain.Count - 1];
                if (lastJobDefinition.job != lastJobInChain) {
                    Debug.LogError($"[PersistentJobs] lastJobInChain ({lastJobInChain.ID}) does not match lastJobDef.job ({lastJobDefinition.job.ID})");
                    return;
                }

                if (lastJobInChain.jobType == JobType.ShuntingLoad && lastJobDefinition is StaticShuntingLoadJobDefinition shuntingLoadJobDefinition) {
                    var subsequentJobChainController = CreateSubsequentTransportJob(__instance, shuntingLoadJobDefinition);

                    FinishSubsequentJobChainControllerAndRemoveTrainCarsFromCurrentJobChain(subsequentJobChainController, __instance, lastJobInChain);
                } else if (lastJobInChain.jobType == JobType.Transport && lastJobDefinition is StaticTransportJobDefinition transportJobDefinition) {
                    var subsequentJobChainController = CreateSubsequentShuntingUnloadJob(__instance, transportJobDefinition);
                    
                    FinishSubsequentJobChainControllerAndRemoveTrainCarsFromCurrentJobChain(subsequentJobChainController, __instance, lastJobInChain);
                } else if (lastJobInChain.jobType == JobType.ShuntingUnload && lastJobDefinition is StaticShuntingUnloadJobDefinition) {
                    // nothing to do. JobChainController will register the cars as jobless and they may then be chosen for further jobs
                } else if (lastJobInChain.jobType == JobType.EmptyHaul && lastJobDefinition is StaticEmptyHaulJobDefinition) {
                    // nothing to do. JobChainController will register the cars as jobless and they may then be chosen for further jobs
                } else {
                    Debug.LogError($"[PersistentJobs] Cannot handle unexpected job type {lastJobInChain.jobType} and {lastJobDefinition.GetType()} combination.");
                }
            } catch (Exception e) {
                Main._modEntry.Logger.Error($"Exception thrown during {nameof(JobChainController_OnLastJobInChainCompleted_Patch)}.{nameof(Prefix)} patch:\n{e.ToString()}");
                Main.OnCriticalFailure();
            }
        }

        private static JobChainController CreateSubsequentTransportJob(JobChainController __instance, StaticShuntingLoadJobDefinition shuntingLoadJobDefinition) {
            var startingStation = SingletonBehaviour<LogicController>.Instance.YardIdToStationController[shuntingLoadJobDefinition.logicStation.ID];
            var destStation = SingletonBehaviour<LogicController>.Instance.YardIdToStationController[shuntingLoadJobDefinition.chainData.chainDestinationYardId];
            var startingTrack = shuntingLoadJobDefinition.destinationTrack;
            var trainCars = new List<TrainCar>(__instance.trainCarsForJobChain);
            var rng = new System.Random(Environment.TickCount);
            var transportedCargoPerCar = trainCars.Select(tc => tc.logicCar.CurrentCargoTypeInCar).ToList();
            return TransportJobGenerator.TryGenerateJobChainController(startingStation, startingTrack, destStation, trainCars, transportedCargoPerCar, rng);
        }

        private static JobChainController CreateSubsequentShuntingUnloadJob(JobChainController __instance, StaticTransportJobDefinition transportJobDefinition) {
            var startingStation = SingletonBehaviour<LogicController>.Instance.YardIdToStationController[transportJobDefinition.logicStation.ID];
            var destinationStation = SingletonBehaviour<LogicController>.Instance.YardIdToStationController[transportJobDefinition.chainData.chainDestinationYardId];

            var startingTrack = transportJobDefinition.destinationTrack;

            var trainCars = new List<TrainCar>(__instance.trainCarsForJobChain);
            var rng = new System.Random(Environment.TickCount);

            var perCarCargoTypes = trainCars.Select(tc => tc.logicCar.CurrentCargoTypeInCar).ToList();

            return ShuntingUnloadJobProceduralGenerator.TryGenerateJobChainController(startingStation, startingTrack, destinationStation, trainCars, perCarCargoTypes, rng);
        }

        private static void FinishSubsequentJobChainControllerAndRemoveTrainCarsFromCurrentJobChain(JobChainController subsequentJobChainController, JobChainController previousJobChainController, Job previousJob) {
            if (subsequentJobChainController != null) {
                foreach (var tc in subsequentJobChainController.trainCarsForJobChain) {
                    previousJobChainController.trainCarsForJobChain.Remove(tc);
                }

                subsequentJobChainController.FinalizeSetupAndGenerateFirstJob();

                if (subsequentJobChainController.currentJobInChain is Job job) {
                    Main._modEntry.Logger.Log($"Completion of job {previousJob.ID} generated subsequent {job.jobType} job {job.ID} ({subsequentJobChainController.jobChainGO.name})");
                } else {
                    Main._modEntry.Logger.Log($"Completion of job {previousJob.ID} generated subsequent job chain but could not generate first job from it {subsequentJobChainController.jobChainGO.name}");
                }
            }
        }
    }
}