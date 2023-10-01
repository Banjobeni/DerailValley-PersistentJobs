using System;
using System.Linq;
using DV.ThingTypes;
using HarmonyLib;
using UnityEngine;

namespace PersistentJobsMod.HarmonyPatches.Save {
    /// <summary>reserves tracks for taken jobs when loading save file</summary>
    [HarmonyPatch(typeof(JobSaveManager), "LoadJobChain")]
    public static class JobSaveManager_LoadJobChain_Patch {
        public static void Postfix(JobChainSaveData chainSaveData) {
            try {
                if (chainSaveData.jobTaken) {
                    // reserve space for this job
                    var stationJobControllers = UnityEngine.Object.FindObjectsOfType<StationProceduralJobsController>();
                    var jobChainController = stationJobControllers.SelectMany(sjc => sjc.GetCurrentJobChains()).FirstOrDefault(jcc => jcc.currentJobInChain.ID == chainSaveData.firstJobId);

                    if (jobChainController == null) {
                        Debug.LogWarning($"[PersistentJobs] could not find JobChainController for Job[{chainSaveData.firstJobId}]; skipping track reservation!");
                    } else if (jobChainController.currentJobInChain.jobType == JobType.ShuntingLoad) {
                        Main._modEntry.Logger.Log($"skipping track reservation for Job[{jobChainController.currentJobInChain.ID}] because it's a shunting load job");
                    } else {
                        Traverse.Create(jobChainController).Method("ReserveRequiredTracks", new[] { typeof(bool) }).GetValue(true);
                    }
                }
            } catch (Exception e) {
                Main._modEntry.Logger.Warning($"Reserving track space for Job[{chainSaveData.firstJobId}] failed with exception:\n{e}");
            }
        }
    }
}