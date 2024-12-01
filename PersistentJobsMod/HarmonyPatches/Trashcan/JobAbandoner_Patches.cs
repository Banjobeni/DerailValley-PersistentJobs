using HarmonyLib;
using UnityEngine;
using Collider = UnityEngine.Collider;

namespace PersistentJobsMod.HarmonyPatches.Trashcan {
    [HarmonyPatch]
    public sealed class JobAbandoner_Patches {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(JobAbandoner), "OnTriggerEnter")]
        public static void OnTriggerEnter_Postfix(Collider other) {
            var jobOverview = other.GetComponent<JobOverview>();
            if (jobOverview != null) {
                jobOverview.job.ExpireJob();
                jobOverview.DestroyJobOverview();
            }
        }
    }
}