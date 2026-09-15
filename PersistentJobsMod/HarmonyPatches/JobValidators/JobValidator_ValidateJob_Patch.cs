using DV.Logic.Job;
using DV.Printers;
using HarmonyLib;
using PersistentJobsMod.Optimization;
using PersistentJobsMod.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.Jobs;
using UnityEngine;

namespace PersistentJobsMod.HarmonyPatches.JobValidators
{
    public static class JobValidator_ValidateJob_Patch
    {
        [HarmonyPatch(typeof(JobValidator), nameof(JobValidator.ValidateJob))]
        public static bool Prefix(JobValidator __instance, JobBooklet jobBooklet, PrinterController ___bookletPrinter)
        {
            var jobChainController = StationController.allStations.FirstOrDefault(st => st.logicStation.availableJobs.Contains(jobBooklet.job)).ProceduralJobsController.GetCurrentJobChains().FirstOrDefault(jcc => jcc.currentJobInChain == jobBooklet.job);
            if (FarCarOpt.SuspendedCarGUIDToJobChainController.ContainsValue(jobChainController ??= new JobChainController(new()))) //the new is just a fallthrough case instead of null
            {
                UnityEngine.Debug.LogWarning("[PersistentJobsMod] The cars for the job are still suspended!");

                IEnumerator<(string NextStageName, object Result)> WaitAndRetryProcessCoro()
                {
                    string location = "job turn-in validation";
                    bool stationDoneResuming = false;
                    bool breakOut = false;
                    void OnResumeCompleted(string id)
                    {
                        if (id == location) stationDoneResuming = true;
                    }

                    FarCarOpt.ResumeCompleted += OnResumeCompleted;
                    try
                    {
                        if (!FarCarOpt.RunResumeCars(jobChainController?.carsForJobChain?.Select(c => c.carGuid).ToList(), location))
                        {
                            Main._modEntry.Logger.Log($"failure or not resumed anything");
                            breakOut = true;
                            stationDoneResuming = true;
                        }
                        if (!breakOut)
                        {
                            yield return ("waiting for car resume", new WaitUntil(() => stationDoneResuming));
                            yield return ("safety wait", WaitFor.SecondsRealtime(0.5f));

                            if (!ReflectionUtilities.IsInCallers(nameof(WaitAndRetryProcessCoro), log: true)) __instance.ValidateJob(jobBooklet);
                            else Debug.LogError("[PersistentJobsMod] Cars somehow didn't resume before job validation reattempt, breaking to avoid recursion loop!");
                        }
                    }
                    finally
                    {
                        FarCarOpt.ResumeCompleted -= OnResumeCompleted;
                    }
                }

                CoroutineManager.Instance.Run(WaitAndRetryProcessCoro());
                return false;
            }
            else return true;
        }
    }
}
