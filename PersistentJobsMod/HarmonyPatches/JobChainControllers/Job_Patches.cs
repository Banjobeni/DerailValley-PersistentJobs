using DV.Logic.Job;
using HarmonyLib;
using PersistentJobsMod.Extensions;
using PersistentJobsMod.Optimization;
using PersistentJobsMod.Utilities;
using System.Collections.Generic;
using System.Linq;
using Unity.Jobs;
using UnityEngine;

namespace PersistentJobsMod.HarmonyPatches.JobChainControllers
{
    [HarmonyPatch]
    public static class Job_Patches
    {
        [HarmonyPatch(typeof(Job), nameof(Job.ExpireJob))]
        [HarmonyPrefix]
        public static bool Prefix(Job __instance)
        {
            if (__instance != null)
            {
                var jcc = FarCarOpt.SuspendedCarGUIDToJobChainController.Values.FirstOrDefault(jcc => jcc?.jobChain.Any(sjd => sjd.job == __instance) == true);
                if (jcc != null)
                {
                    Debug.LogWarning("[PersistentJobsMod] Can't expire a job whose cars are still suspended, will wait for resume");

                    IEnumerator<(string NextStageName, object Result)> WaitAndExpireCoro()
                    {
                        string location = "job expiring";
                        bool stationDoneResuming = false;
                        bool breakOut = false;
                        void OnResumeCompleted(string id)
                        {
                            if (id == location) stationDoneResuming = true;
                        }

                        FarCarOpt.ResumeCompleted += OnResumeCompleted;
                        try
                        {
                            if (!FarCarOpt.RunResumeCars(jcc.carsForJobChain?.Select(c => c.carGuid).ToList(), location))
                            {
                                Main._modEntry.Logger.Log($"failure or not resumed anything");
                                breakOut = true;
                                stationDoneResuming = true;
                            }
                            if (!breakOut)
                            {
                                yield return ("waiting for car resume", new WaitUntil(() => stationDoneResuming));
                                yield return ("safety wait", WaitFor.SecondsRealtime(0.5f));

                                if (!ReflectionUtilities.IsInCallers(nameof(WaitAndExpireCoro), log: true)) __instance.ExpireJob();
                                else Debug.LogError("[PersistentJobsMod] Cars somehow didn't resume before expiring, breaking to avoid recursion loop!");
                            }
                        }
                        finally
                        {
                            FarCarOpt.ResumeCompleted -= OnResumeCompleted;
                        }
                    }

                    CoroutineManager.Instance.Run(WaitAndExpireCoro());
                    return false;
                }
            }
            return true;
        }
    }
}
