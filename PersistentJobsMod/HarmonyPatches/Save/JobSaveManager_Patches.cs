using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using DV.ThingTypes;
using DV.Utils;
using HarmonyLib;
using UnityEngine;
using DV;
using DV.Logic.Job;

namespace PersistentJobsMod.HarmonyPatches.Save {
    /// <summary>reserves tracks for taken jobs when loading save file</summary>
    [HarmonyPatch]
    public static class JobSaveManager_Patches {
        [HarmonyPatch(typeof(JobSaveManager), "GetYardTrackWithId")]
        [HarmonyPrefix]
        public static bool GetYardTrackWithId_Prefix(string trackId, ref Track __result)
        {
            if (SingletonBehaviour<YardTracksOrganizer>.Instance.yardTrackIdToTrack.TryGetValue(trackId, out var track) && track != null)
            {
                __result = track;
            }
            //vanilla code above, below logic no not null tracks outside of yards, could cause conflicts if original method was used for filtering those tracks out
            else
            {
                Main._modEntry.Logger.Log($"Track {trackId} not found in yard tracks");
                RailTrack RT = RailTrackRegistry.Instance.AllTracks.FirstOrDefault(rt => RailTrackRegistry.RailTrackToLogicTrack[rt].ID.FullID == trackId);
                __result = RailTrackRegistry.RailTrackToLogicTrack[RT] ?? null;
            }
            return false; 
        }

        [HarmonyPatch(typeof(JobSaveManager), "LoadJobChain")]
        [HarmonyPostfix]
        public static void LoadJobChain_Postfix(JobChainSaveData chainSaveData) {
            try {
                if (chainSaveData.jobTaken) {
                    // reserve space for this job
                    var stationJobControllers = UnityEngine.Object.FindObjectsOfType<StationProceduralJobsController>();
                    var jobChainController = stationJobControllers.SelectMany(sjc => sjc.GetCurrentJobChains()).FirstOrDefault(jcc => jcc.currentJobInChain.ID == chainSaveData.firstJobId);

                    if (jobChainController == null) {
                        Debug.LogWarning($"[PersistentJobs] could not find JobChainController for Job[{chainSaveData.firstJobId}]; skipping track reservation!");
                    } else if (jobChainController.currentJobInChain.jobType == JobType.ShuntingLoad) {
                        Main._modEntry.Logger.Log($"skipping track reservation for job {jobChainController.currentJobInChain.ID} because it's a shunting load job");
                    } else {
                        Main._modEntry.Logger.Log($"reserving tracks for loaded job {jobChainController.currentJobInChain.ID}");
                        jobChainController.ReserveRequiredTracks(true);
                    }
                }
            } catch (Exception e) {
                Main._modEntry.Logger.Warning($"Reserving track space for Job[{chainSaveData.firstJobId}] failed with exception:\n{e}");
            }
        }

        [HarmonyPatch(typeof(JobSaveManager), "LoadJobChain")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> LoadJobChain_Transpiler(IEnumerable<CodeInstruction> instructionsEnumerable, MethodBase originalMethod) {
            var instructions = instructionsEnumerable.ToList();

            var index = instructions.FindIndex(i => i.Is(OpCodes.Newobj, typeof(JobChainControllerWithEmptyHaulGeneration).GetConstructors().Single()));

            if (index == -1) {
                throw new InvalidOperationException($"could not find instruction that calls constructor of {typeof(JobChainControllerWithEmptyHaulGeneration)}");
            }

            instructions[index] = new CodeInstruction(OpCodes.Newobj, typeof(JobChainController).GetConstructors().Single());

            Debug.Log($"[PersistentJobsMod] Transpiling {originalMethod.DeclaringType.FullName}:{originalMethod.Name} at index {index}: changed instanciated object to {typeof(JobChainController).FullName}");

            return instructions;
        }
    }
}