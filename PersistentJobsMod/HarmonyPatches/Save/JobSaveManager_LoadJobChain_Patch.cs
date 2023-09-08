using System;
using DV.ThingTypes;
using HarmonyLib;
using UnityEngine;

namespace PersistentJobsMod.HarmonyPatches.Save {
    /// <summary>reserves tracks for taken jobs when loading save file</summary>
    [HarmonyPatch(typeof(JobSaveManager), "LoadJobChain")]
    class JobSaveManager_LoadJobChain_Patch {
        static void Postfix(JobChainSaveData chainSaveData) {
            try {
                if (chainSaveData.jobTaken) {
                    // reserve space for this job
                    var stationJobControllers
                        = UnityEngine.Object.FindObjectsOfType<StationProceduralJobsController>();
                    JobChainController jobChainController = null;
                    for (var i = 0; i < stationJobControllers.Length && jobChainController == null; i++) {
                        foreach (var jcc in stationJobControllers[i].GetCurrentJobChains()) {
                            if (jcc.currentJobInChain.ID == chainSaveData.firstJobId) {
                                jobChainController = jcc;
                                break;
                            }
                        }
                    }
                    if (jobChainController == null) {
                        Debug.LogWarning($"[PersistentJobs] could not find JobChainController for Job[{chainSaveData.firstJobId}]; skipping track reservation!");
                    } else if (jobChainController.currentJobInChain.jobType == JobType.ShuntingLoad) {
                        Main._modEntry.Logger.Log($"skipping track reservation for Job[{jobChainController.currentJobInChain.ID}] because it's a shunting load job");
                    } else {
                        Main._overrideTrackReservation = true;
                        Traverse.Create(jobChainController).Method("ReserveRequiredTracks", new[] { typeof(bool) }).GetValue(true);
                        Main._overrideTrackReservation = false;
                    }
                }
            } catch (Exception e) {
                // TODO: what to do if reserving tracks fails?
                Main._modEntry.Logger.Warning($"Reserving track space for Job[{chainSaveData.firstJobId}] failed with exception:\n{e}");
            }
        }
    }
}