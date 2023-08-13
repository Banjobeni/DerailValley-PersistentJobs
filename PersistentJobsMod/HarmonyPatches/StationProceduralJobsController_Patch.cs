using System;
using System.Collections;
using System.Collections.Generic;
using DV.Logic.Job;
using DV.ThingTypes;
using DV.ThingTypes.TransitionHelpers;
using DV.Utils;
using HarmonyLib;
using JetBrains.Annotations;
using PersistentJobsMod.CarSpawningJobGenerators;
using UnityEngine;
using Random = System.Random;

namespace PersistentJobsMod.HarmonyPatches {
    [UsedImplicitly]
    [HarmonyPatch(typeof(StationProceduralJobsController))]
    public static class StationProceduralJobsController_Patch {
        [HarmonyPatch(nameof(StationProceduralJobsController.TryToGenerateJobs))]
        [HarmonyPrefix]
        static bool TryToGenerateJobs_Prefix(StationProceduralJobsController __instance, StationProceduralJobsRuleset ___generationRuleset, ref Coroutine ___generationCoro) {
            if (!Main._modEntry.Active) {
                return true;
            }

            if (!Main.StationIdSpawnBlockList.Contains(__instance.stationController.logicStation.ID)) {
                Main.StationIdSpawnBlockList.Add(__instance.stationController.logicStation.ID);

                __instance.StopJobGeneration();
                ___generationCoro = __instance.StartCoroutine(GenerateProceduralJobsCoroutine(__instance, ___generationRuleset));
            }

            return false;
        }

        private static IEnumerator GenerateProceduralJobsCoroutine(StationProceduralJobsController instance, StationProceduralJobsRuleset stationProceduralJobsRuleset) {
            var generateJobsAttempts = 0;
            var forcePlayerLicensedJobGeneration = true;
            Main._modEntry.Logger.Log($"{instance.stationController.stationInfo.YardID} job generation started. At most {stationProceduralJobsRuleset.jobsCapacity} job chains will be generated.");
            while (instance.stationController.logicStation.availableJobs.Count < stationProceduralJobsRuleset.jobsCapacity && generateJobsAttempts < 30) {
                yield return WaitFor.FixedUpdate;
                if (generateJobsAttempts > 10 & forcePlayerLicensedJobGeneration) {
                    Main._modEntry.Logger.Log("Couldn't generate any player licensed job");
                    forcePlayerLicensedJobGeneration = false;
                }
                var tickCount = Environment.TickCount;
                var jobChain = GenerateJobChain(stationProceduralJobsRuleset, instance.stationController, new Random(tickCount), forcePlayerLicensedJobGeneration);
                var generationAttempt = (Action)Traverse.Create(instance).Field("JobGenerationAttempt").GetValue();
                generationAttempt?.Invoke();
                if (jobChain != null) {
                    if (forcePlayerLicensedJobGeneration) {
                        forcePlayerLicensedJobGeneration = false;
                    }
                    Main._modEntry.Logger.Log($"Generated job {jobChain.currentJobInChain.ID} (rng seed: {tickCount})");
                    for (var i = 0; i < 12; ++i) {
                        yield return null;
                    }
                } else {
                    ++generateJobsAttempts;
                    yield return null;
                }
            }

            Main._modEntry.Logger.Log($"{instance.stationController.stationInfo.YardID} job generation ended. {instance.stationController.logicStation.availableJobs.Count} jobs were generated with {generateJobsAttempts} job generation attempts");

            Traverse.Create(instance).Field("generationCoro").SetValue(null);
        }

        private static JobChainController GenerateJobChain(StationProceduralJobsRuleset generationRuleset, StationController stationController, Random random, bool forceJobWithLicenseRequirementFulfilled) {
            Yard yard = stationController.logicStation.yard;
            if (!generationRuleset.loadStartingJobSupported && !generationRuleset.haulStartingJobSupported && !generationRuleset.unloadStartingJobSupported && !generationRuleset.emptyHaulStartingJobSupported) {
                return null;
            }

            var allowedJobTypes = new List<JobType>();
            if (generationRuleset.loadStartingJobSupported) {
                allowedJobTypes.Add(JobType.ShuntingLoad);
            }
            if (generationRuleset.emptyHaulStartingJobSupported) {
                allowedJobTypes.Add(JobType.EmptyHaul);
            }
            var unoccuppiedTransferOutTracks = SingletonBehaviour<YardTracksOrganizer>.Instance.FilterOutOccupiedTracks(yard.TransferOutTracks).Count;
            if (generationRuleset.haulStartingJobSupported && unoccuppiedTransferOutTracks > 0) {
                allowedJobTypes.Add(JobType.Transport);
            }

            if (allowedJobTypes.Count == 0) {
                return null;
            }

            var licenseManager = SingletonBehaviour<LicenseManager>.Instance;

            if (forceJobWithLicenseRequirementFulfilled) {
                // generate a job that the player can actually take. this flag will not be set after the first licensable job was successfully generated.

                if (allowedJobTypes.Contains(JobType.Transport) && licenseManager.IsJobLicenseAcquired(JobLicenses.FreightHaul.ToV2())) {
                    var transportJob = GenerateAndFinalizeTransportJob(stationController, true, random);
                    if (transportJob != null) {
                        return transportJob;
                    }
                }
                if (allowedJobTypes.Contains(JobType.EmptyHaul) && licenseManager.IsJobLicenseAcquired(JobLicenses.LogisticalHaul.ToV2())) {
                    var emptyHaulJob = GenerateAndFinalizeEmptyHaulJob(stationController, true, random);
                    if (emptyHaulJob != null) {
                        return emptyHaulJob;
                    }
                }
                if (allowedJobTypes.Contains(JobType.ShuntingLoad) && licenseManager.IsJobLicenseAcquired(JobLicenses.Shunting.ToV2())) {
                    var shuntingLoadJob = GenerateAndFinalizeShuntingLoadJob(stationController, true, random);
                    if (shuntingLoadJob != null) {
                        return shuntingLoadJob;
                    }
                }
                return null;
            }

            if (allowedJobTypes.Contains(JobType.Transport) && unoccuppiedTransferOutTracks > Mathf.FloorToInt(0.399999976f * yard.TransferOutTracks.Count)) {
                var jobChainController = GenerateAndFinalizeTransportJob(stationController, false, random);
                if (jobChainController != null) {
                    return jobChainController;
                }
            } else {
                var jobType = random.GetRandomElement(allowedJobTypes);
                if (jobType == JobType.ShuntingLoad) {
                    return GenerateAndFinalizeShuntingLoadJob(stationController, false, random);
                } else if (jobType == JobType.EmptyHaul) {
                    return GenerateAndFinalizeEmptyHaulJob(stationController, false, random);
                }
            }
            return null;
        }

        private static JobChainController GenerateAndFinalizeShuntingLoadJob(StationController stationController, bool requirePlayerLicensesCompatible, Random random) {
            Main._modEntry.Logger.Log("trying to generate SL job");
            var result = ShuntingLoadJobWithCarsGenerator.TryGenerateJobChainController(stationController, requirePlayerLicensesCompatible, random);
            if (result != null) {
                result.FinalizeSetupAndGenerateFirstJob();
            }
            return result;
        }

        private static JobChainController GenerateAndFinalizeTransportJob(StationController stationController, bool requirePlayerLicensesCompatible, Random random) {
            Main._modEntry.Logger.Log("trying to generate FH job");
            var result = TransportJobWithCarsGenerator.TryGenerateJobChainController(stationController, requirePlayerLicensesCompatible, random);
            if (result != null) {
                result.FinalizeSetupAndGenerateFirstJob();
            }
            return result;
        }

        private static JobChainController GenerateAndFinalizeEmptyHaulJob(StationController startingStation, bool requirePlayerLicensesCompatible, Random random) {
            Main._modEntry.Logger.Log("trying to generate LH job");
            var result = EmptyHaulJobWithCarsGenerator.TryGenerateJobChainController(startingStation, requirePlayerLicensesCompatible, random);
            if (result != null) {
                result.FinalizeSetupAndGenerateFirstJob();
            }
            return result;
        }
    }
}