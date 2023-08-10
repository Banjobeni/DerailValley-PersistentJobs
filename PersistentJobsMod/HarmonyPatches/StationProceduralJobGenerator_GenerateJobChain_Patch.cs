using System;
using DV.ThingTypes.TransitionHelpers;
using System.Collections.Generic;
using DV.ThingTypes;
using HarmonyLib;
using UnityEngine;
using DV.Logic.Job;
using Random = System.Random;
using PersistentJobsMod.CarSpawningJobGenerators;

namespace PersistentJobsMod.HarmonyPatches {
    [HarmonyPatch(typeof(StationProceduralJobGenerator), nameof(StationProceduralJobGenerator.GenerateJobChain))]
    public static class StationProceduralJobGenerator_GenerateJobChain_Patch {
        public static bool Prefix(
                StationProceduralJobGenerator __instance,
                ref JobChainController __result,
                StationProceduralJobsRuleset ___generationRuleset,
                StationController ___stationController,
                LicenseManager ___licenseManager,
                Yard ___stYard,
                YardTracksOrganizer ___yto,
                Random rng,
                bool forceJobWithLicenseRequirementFulfilled) {
            if (!Main._modEntry.Active) {
                return true;
            } else {
                Main._modEntry.Logger.Log("StationProceduralJobGenerator_GenerateJobChain_Patch");
                try {
                    __result = GenerateJobChain(___generationRuleset, ___stationController, ___licenseManager, ___stYard, ___yto, rng, forceJobWithLicenseRequirementFulfilled);
                } catch (Exception e) {
                    Main._modEntry.Logger.Error($"Exception thrown during {nameof(StationProceduralJobGenerator_GenerateJobChain_Patch)} {nameof(Prefix)} patch:\n{e}");
                    Main.OnCriticalFailure();
                }
                return false;
            }
        }

        private static JobChainController GenerateJobChain(
            StationProceduralJobsRuleset generationRuleset,
                StationController stationController,
                LicenseManager licenseManager,
                Yard yard,
                YardTracksOrganizer yardTracksOrganizer,
                Random random,
                bool forceJobWithLicenseRequirementFulfilled) {
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
            var unoccuppiedTransferOutTracks = yardTracksOrganizer.FilterOutOccupiedTracks(yard.TransferOutTracks).Count;
            if (generationRuleset.haulStartingJobSupported && unoccuppiedTransferOutTracks > 0) {
                allowedJobTypes.Add(JobType.Transport);
            }
            ////var unoccuppiedAndUnreservedTransferInTracks = yardTracksOrganizer.FilterOutReservedTracks(yardTracksOrganizer.FilterOutOccupiedTracks(yard.TransferInTracks)).Count;
            ////if (generationRuleset.unloadStartingJobSupported && unoccuppiedAndUnreservedTransferInTracks > 0) {
            ////    allowedJobTypes.Add(JobType.ShuntingUnload);
            ////}

            if (allowedJobTypes.Count == 0) {
                return null;
            }

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
                ////if (allowedJobTypes.Contains(JobType.ShuntingUnload) && licenseManager.IsJobLicenseAcquired(JobLicenses.Shunting.ToV2())) {
                ////    return null;
                ////    JobChainController inChainJob = this.GenerateInChainJob(JobType.ShuntingUnload, true);
                ////    if (inChainJob != null) {
                ////        return inChainJob;
                ////    }
                ////}
                return null;
            }

            if (allowedJobTypes.Contains(JobType.Transport) && unoccuppiedTransferOutTracks > Mathf.FloorToInt(0.399999976f * (float)yard.TransferOutTracks.Count)) {
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
                    ////} else if (jobType == JobType.ShuntingUnload) {
                    ////    // nothing
                }
            }
            return null;
        }

        private static JobChainController GenerateAndFinalizeShuntingLoadJob(StationController stationController, bool requirePlayerLicensesCompatible, Random random) {
            Main._modEntry.Logger.Log("trying to generate SL job");
            var result = ShuntingLoadJobProceduralGenerator.GenerateShuntingLoadJobWithCarSpawning(stationController, requirePlayerLicensesCompatible, random);
            if (result != null) {
                result.FinalizeSetupAndGenerateFirstJob();
                Main._modEntry.Logger.Log($"Generated job {result.currentJobInChain?.ID}");
            }
            return result;
        }

        private static JobChainController GenerateAndFinalizeTransportJob(StationController stationController, bool requirePlayerLicensesCompatible, Random random) {
            Main._modEntry.Logger.Log("trying to generate FH job");
            var result = TransportJobProceduralGenerator.GenerateTransportJobWithCarSpawning(stationController, requirePlayerLicensesCompatible, random);
            if (result != null) {
                result.FinalizeSetupAndGenerateFirstJob();
                Main._modEntry.Logger.Log($"Generated job {result.currentJobInChain?.ID}");
            }
            return result;
        }

        private static JobChainController GenerateAndFinalizeEmptyHaulJob(StationController startingStation, bool requirePlayerLicensesCompatible, Random random) {
            Main._modEntry.Logger.Log("trying to generate LH job");
            var result = EmptyHaulJobWithCarsGenerator.TryGenerateJobChain(startingStation, requirePlayerLicensesCompatible, random);
            if (result != null) {
                result.FinalizeSetupAndGenerateFirstJob();
                Main._modEntry.Logger.Log($"Generated job {result.currentJobInChain?.ID}");
            }
            return result;
        }
    }
}