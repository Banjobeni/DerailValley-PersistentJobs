using DV.Printers;
using HarmonyLib;
using PersistentJobsMod.Optimization;
using System.Linq;
using UnityEngine;

namespace PersistentJobsMod.HarmonyPatches.JobValidators
{
    public static class JobValidator_ValidateJob_Patch
    {
        [HarmonyPatch(typeof(JobValidator), nameof(JobValidator.ValidateJob))]
        public static bool Prefix(JobValidator __instance, JobBooklet jobBooklet, PrinterController ___bookletPrinter)
        {
            if (___bookletPrinter.IsOnCooldown)
            {
                ___bookletPrinter.PlayErrorSound();
                return false;
            }

            var jcc = StationController.allStations.FirstOrDefault(st => st.logicStation.availableJobs.Contains(jobBooklet.job) || st.logicStation.takenJobs.Contains(jobBooklet.job))?.ProceduralJobsController?.GetCurrentJobChains()?.FirstOrDefault(jcc => jcc.currentJobInChain == jobBooklet.job);
            if (FarCarOpt.SuspendedCarGUIDToJobChainController.ContainsValue(jcc ??= new JobChainController(new()))) //the new is just a fallthrough case instead of null
            {
                UnityEngine.Debug.LogWarning("[PersistentJobsMod] The cars for the job are still suspended!");
                var carGuids = FarCarOpt.SuspendedCarGUIDToJobChainController.Where(kvp => kvp.Value == jcc).Select(kvp => kvp.Key).ToArray();
                _ = FarCarOpt.RunResumeCars(carGuids, "job validating", (success) => { ___bookletPrinter.IsOnCooldown = false; if (success) __instance.ValidateJob(jobBooklet); else { Debug.LogWarning($"[PersistentJobsMod] {jobBooklet.job.ID} couldn't be taken"); __instance.StartCoroutine(JobValidator_ProcessJobOverview_Patch.HandleJobAcceptanceFailure(___bookletPrinter, false)); } });
                return false;
            }
            else return true;
        }
    }
}
