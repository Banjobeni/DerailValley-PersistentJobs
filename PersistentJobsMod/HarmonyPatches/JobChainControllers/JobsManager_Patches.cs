using DV.Logic.Job;
using HarmonyLib;
using PersistentJobsMod.Optimization;
using System.Linq;
using UnityEngine;

namespace PersistentJobsMod.HarmonyPatches.JobChainControllers
{
    [HarmonyPatch]
    public static class JobsManager_Patches
    {
        [HarmonyPatch(typeof(JobsManager), nameof(JobsManager.AbandonJob))]
        [HarmonyPrefix]
        public static bool Prefix(Job job, JobsManager __instance)
        {
            var jcc = FarCarOpt.SuspendedCarGUIDToJobChainController.Values.FirstOrDefault(jcc => jcc?.jobChain.Any(sjd => sjd.job == job) == true);
            if (jcc != null)
            {
                Debug.LogWarning("[PersistentJobsMod] Can't abandon a job whose cars are still suspended, will wait for resume");
                var carGuids = FarCarOpt.SuspendedCarGUIDToJobChainController.Where(kvp => kvp.Value == jcc).Select(kvp => kvp.Key).ToArray();
                _ = FarCarOpt.RunResumeCars(carGuids, "job abandoning", (success) => { if (success) __instance.AbandonJob(job); else Debug.LogWarning($"[PersistentJobsMod] {job.ID} couldn't be abandoned"); });
                return false;
            }
            else return true;
        }
    }
}
