using DV.Logic.Job;
using HarmonyLib;
using PersistentJobsMod.Extensions;
using PersistentJobsMod.Optimization;
using PersistentJobsMod.Utilities;
using System.Collections.Generic;
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
                List<string> carGuids = __instance.jobToJobCars[job]?.Select(c => c.carGuid).ToList();

                IEnumerator<(string NextStageName, object Result)> WaitAndAbandonCoro()
                {
                    string location = "job abandoning";
                    bool stationDoneResuming = false;
                    bool breakOut = false;
                    void OnResumeCompleted(string id)
                    {
                        if (id == location) stationDoneResuming = true;
                    }

                    FarCarOpt.ResumeCompleted += OnResumeCompleted;
                    try
                    {
                        if (!FarCarOpt.RunResumeCars(carGuids, location))
                        {
                            Main._modEntry.Logger.Log($"failure or not resumed anything");
                            breakOut = true;
                            stationDoneResuming = true;
                        }
                        if (!breakOut)
                        {
                            yield return ("waiting for car resume", new WaitUntil(() => stationDoneResuming));
                            yield return ("safety wait", WaitFor.SecondsRealtime(0.5f));

                            if (!ReflectionUtilities.IsInCallers(nameof(WaitAndAbandonCoro), log: true)) __instance.AbandonJob(job);
                            else Debug.LogError("[PersistentJobsMod] Cars somehow didn't resume before expiring, breaking to avoid recursion loop!");
                        }
                    }
                    finally
                    {
                        FarCarOpt.ResumeCompleted -= OnResumeCompleted;
                    }
                }

                CoroutineManager.Instance.Run(WaitAndAbandonCoro());
                return false;
            }
            else return true;
        }
    }
}
