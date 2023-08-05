using System;
using DV.ThingTypes;
using HarmonyLib;
using UnityEngine;

namespace PersistentJobsMod.HarmonyPatches {
    /// <summary>reserves tracks for taken jobs when loading save file</summary>
    [HarmonyPatch(typeof(JobSaveManager), "LoadJobChain")]
    class JobSaveManager_LoadJobChain_Patch {
        static void Postfix(JobChainSaveData chainSaveData) {
            try {
                if (chainSaveData.jobTaken) {
                    // reserve space for this job
                    StationProceduralJobsController[] stationJobControllers
                        = UnityEngine.Object.FindObjectsOfType<StationProceduralJobsController>();
                    JobChainController jobChainController = null;
                    for (int i = 0; i < stationJobControllers.Length && jobChainController == null; i++) {
                        foreach (JobChainController jcc in stationJobControllers[i].GetCurrentJobChains()) {
                            if (jcc.currentJobInChain.ID == chainSaveData.firstJobId) {
                                jobChainController = jcc;
                                break;
                            }
                        }
                    }
                    if (jobChainController == null) {
                        Debug.LogWarning(string.Format(
                            "[PersistentJobs] could not find JobChainController for Job[{0}]; skipping track reservation!",
                            chainSaveData.firstJobId));
                    } else if (jobChainController.currentJobInChain.jobType == JobType.ShuntingLoad) {
                        Debug.Log(string.Format(
                            "[PersistentJobs] skipping track reservation for Job[{0}] because it's a shunting load job",
                            jobChainController.currentJobInChain.ID));
                    } else {
                        Main.overrideTrackReservation = true;
                        Traverse.Create(jobChainController).Method("ReserveRequiredTracks", new[] { typeof(bool) }).GetValue(true);
                        Main.overrideTrackReservation = false;
                    }
                }
            } catch (Exception e) {
                // TODO: what to do if reserving tracks fails?
                Main.modEntry.Logger.Warning(string.Format("Reserving track space for Job[{1}] failed with exception:\n{0}", e, chainSaveData.firstJobId));
            }
        }
    }
}