using DV.Logic.Job;
using DV.ThingTypes;
using DV.Utils;
using HarmonyLib;
using PersistentJobsMod.JobGenerators;
using PersistentJobsMod.ModInteraction;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static PersistentJobsMod.ModInteraction.PaxJobsCompat.Tags;
using static System.Collections.Specialized.BitVector32;
using PassengerHaulJobDefinitionRef = PersistentJobsMod.Utilities.ReflectionUtilities.Foreign<PersistentJobsMod.ModInteraction.PaxJobsCompat.Tags.PassengerHaulJobDefinition>;

namespace PersistentJobsMod.HarmonyPatches.JobChainControllers {
    /// <summary>
    /// unload: divert cars that can be loaded at the current station for later generation of ShuntingLoad jobs
    /// load: generates a corresponding transport job
    /// transport: generates a corresponding unload job
    /// </summary>
    [HarmonyPatch(typeof(JobChainController), "OnLastJobInChainCompleted")]
    static class JobChainController_OnLastJobInChainCompleted_Patch {
        public static void Prefix(JobChainController __instance,
                List<StaticJobDefinition> ___jobChain,
                Job lastJobInChain) {
            if (!Main._modEntry.Active) {
                return;
            }

            if (!__instance.carsForJobChain.Any()) {
                // passenger jobs may generate a subsequent job by themselves, thereby clearing trainCarsForJobChain
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
                    if (Main.PaxJobsPresent && PaxJobsCompat.IsPaxCars(__instance.carsForJobChain.First().TrainCar()))
                    {
                        CreateSubsequentPaxJobs(__instance, transportJobDefinition, lastJobInChain);
                    }
                    else
                    {
                        var subsequentJobChainController = CreateSubsequentShuntingUnloadJob(__instance, transportJobDefinition);

                        FinishSubsequentJobChainControllerAndRemoveTrainCarsFromCurrentJobChain(subsequentJobChainController, __instance, lastJobInChain);
                    }
                } else if (lastJobInChain.jobType == JobType.ShuntingUnload && lastJobDefinition is StaticShuntingUnloadJobDefinition) {
                    // nothing to do. JobChainController will register the cars as jobless and they may then be chosen for further jobs
                    Debug.Log($"[PersistentJobsMod] Skipped creating a subsequent job after completing a shunting unload job.");
                } else if (lastJobInChain.jobType == JobType.EmptyHaul && lastJobDefinition is StaticEmptyHaulJobDefinition) {
                    if (Main.PaxJobsPresent)
                    {
                        CreateSubsequentPaxJobs(__instance, lastJobDefinition, lastJobInChain);
                    }
                    else
                    {
                        // nothing to do. JobChainController will register the cars as jobless and they may then be chosen for further jobs
                        Debug.Log($"[PersistentJobsMod] Skipped creating a subsequent job after completing an empty haul job.");
                    }
                } else if (Main.PaxJobsPresent && PaxJobsCompat.IsPaxJobDefinition(lastJobDefinition, out PassengerHaulJobDefinitionRef passengerHaulJobDefinition)) {
                    CreateSubsequentPaxJobs(__instance, (StaticJobDefinition)passengerHaulJobDefinition.Value, lastJobInChain);
                } else {
                    Debug.Log($"[PersistentJobsMod] Skipped creating a subsequent job for job type {lastJobInChain.jobType} and job definition type {lastJobDefinition.GetType()}.");
                }
            } catch (Exception e) {
                Main.HandleUnhandledException(e, nameof(JobChainController_OnLastJobInChainCompleted_Patch) + "." + nameof(Prefix));
            }
        }

        private static JobChainController CreateSubsequentTransportJob(JobChainController __instance, StaticShuntingLoadJobDefinition shuntingLoadJobDefinition) {
            var startingStation = SingletonBehaviour<LogicController>.Instance.YardIdToStationController[shuntingLoadJobDefinition.logicStation.ID];
            var destStation = SingletonBehaviour<LogicController>.Instance.YardIdToStationController[shuntingLoadJobDefinition.chainData.chainDestinationYardId];
            var startingTrack = shuntingLoadJobDefinition.destinationTrack;
            var trainCars = new List<TrainCar>(TrainCar.ExtractTrainCars(__instance.carsForJobChain));
            var rng = new System.Random(Environment.TickCount);
            var transportedCargoPerCar = trainCars.Select(tc => tc.logicCar.CurrentCargoTypeInCar).ToList();
            return TransportJobGenerator.TryGenerateJobChainController(startingStation, startingTrack, destStation, trainCars, transportedCargoPerCar, rng);
        }

        private static JobChainController CreateSubsequentShuntingUnloadJob(JobChainController __instance, StaticTransportJobDefinition transportJobDefinition) {
            var startingStation = SingletonBehaviour<LogicController>.Instance.YardIdToStationController[transportJobDefinition.logicStation.ID];
            var destinationStation = SingletonBehaviour<LogicController>.Instance.YardIdToStationController[transportJobDefinition.chainData.chainDestinationYardId];

            var startingTrack = transportJobDefinition.destinationTrack;

            var trainCars = new List<TrainCar>(TrainCar.ExtractTrainCars(__instance.carsForJobChain));
            var rng = new System.Random(Environment.TickCount);

            return ShuntingUnloadJobGenerator.TryGenerateJobChainController(startingStation, startingTrack, destinationStation, trainCars, rng);
        }

        private static void CreateSubsequentPaxJobs(JobChainController __instance, StaticJobDefinition preceedingJobDefinition, Job lastJobInChain)
        {
            List<JobChainController> subsequentJobChainControllers = new();

            var trainCars = new List<TrainCar>(TrainCar.ExtractTrainCars(__instance.carsForJobChain));
            if (!(trainCars.Any(tc => !PaxJobsCompat.IsPaxCars(tc))))
            {
                var destinationStation = SingletonBehaviour<LogicController>.Instance.YardIdToStationController[(preceedingJobDefinition.chainData.chainDestinationYardId)];
                subsequentJobChainControllers.AddRange(PaxJobsCompat.DecideForPaxCarGroups((new List<IReadOnlyList<TrainCar>> { trainCars }), destinationStation));
                if (subsequentJobChainControllers.Any() && !subsequentJobChainControllers.Any(jcc => jcc == null))
                {
                    foreach (var subsequentJobChainController in subsequentJobChainControllers) FinishSubsequentJobChainControllerAndRemoveTrainCarsFromCurrentJobChain(subsequentJobChainController, __instance, lastJobInChain, finalize: false);
                }
                else Main._modEntry.Logger.Log($"Could not generate subsequent passanger jobs for {lastJobInChain.ID}");
            }
        }

        private static void FinishSubsequentJobChainControllerAndRemoveTrainCarsFromCurrentJobChain(JobChainController subsequentJobChainController, JobChainController previousJobChainController, Job previousJob, bool finalize = true) {
            if (subsequentJobChainController != null) {
                foreach (var tc in subsequentJobChainController.carsForJobChain) {
                    previousJobChainController.carsForJobChain.Remove(tc);
                }

                if (finalize) subsequentJobChainController.FinalizeSetupAndGenerateFirstJob();

                if (subsequentJobChainController.currentJobInChain is Job job) {
                    Main._modEntry.Logger.Log($"Completion of job {previousJob.ID} generated subsequent {job.jobType} job {job.ID} ({subsequentJobChainController.jobChainGO.name})");
                } else {
                    Main._modEntry.Logger.Log($"Completion of job {previousJob.ID} generated subsequent job chain but could not generate first job from it {subsequentJobChainController.jobChainGO.name}");
                }
            }
        }
    }
}