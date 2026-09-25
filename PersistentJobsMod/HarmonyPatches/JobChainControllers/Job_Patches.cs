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
                    var carGuids = FarCarOpt.SuspendedCarGUIDToJobChainController.Where(kvp => kvp.Value == jcc).Select(kvp => kvp.Key).ToArray();
                    _ = FarCarOpt.RunResumeCars(carGuids, "job expiring", (success) => { if (success) __instance.ExpireJob(); else Debug.LogWarning($"[PersistentJobsMod] {__instance.ID} couldn't be expired"); });
                    return false;
                }
            }
            return true;
        }
    }
}
