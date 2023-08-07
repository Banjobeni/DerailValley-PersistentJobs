using System;
using DV.ThingTypes;
using HarmonyLib;
using UnityEngine;

namespace PersistentJobsMod.HarmonyPatches {
    /// <summary>generates shunting load jobs & freight haul jobs</summary>
    [HarmonyPatch(typeof(StationProceduralJobGenerator), "GenerateOutChainJob")]
    class StationProceduralJobGenerator_GenerateOutChainJob_Patch {
        static bool Prefix(ref JobChainController __result,
            StationController ___stationController,
            JobType startingJobType,
            bool forceFulfilledLicenseRequirements = false) {
            if (Main._modEntry.Active) {
                try {
                    if (startingJobType == JobType.ShuntingLoad) {
                        Debug.Log("[PersistentJobs] gen out shunting load");
                        __result = ShuntingLoadJobProceduralGenerator.GenerateShuntingLoadJobWithCarSpawning(
                            ___stationController,
                            forceFulfilledLicenseRequirements,
                            new System.Random(Environment.TickCount));
                        if (__result != null) {
                            Debug.Log("[PersistentJobs] finalize out shunting load");
                            __result.FinalizeSetupAndGenerateFirstJob();
                        }
                        return false;
                    } else if (startingJobType == JobType.Transport) {
                        Debug.Log("[PersistentJobs] gen out transport");
                        __result = TransportJobProceduralGenerator.GenerateTransportJobWithCarSpawning(
                            ___stationController,
                            forceFulfilledLicenseRequirements,
                            new System.Random(Environment.TickCount));
                        if (__result != null) {
                            Debug.Log("[PersistentJobs] finalize out transport");
                            __result.FinalizeSetupAndGenerateFirstJob();
                        }
                        return false;
                    }
                    Debug.LogWarning(string.Format(
                        "[PersistentJobs] Got unexpected JobType.{0} in {1}.{2} {3} patch. Falling back to base method.",
                        startingJobType.ToString(),
                        "StationProceduralJobGenerator",
                        "GenerateOutChainJob",
                        "prefix"
                    ));
                } catch (Exception e) {
                    Main._modEntry.Logger.Error(string.Format(
                        "Exception thrown during {0}.{1} {2} patch:\n{3}",
                        "StationProceduralJobGenerator",
                        "GenerateOutChainJob",
                        "prefix",
                        e.ToString()
                    ));
                    Main.OnCriticalFailure();
                }
            }
            return true;
        }
    }
}