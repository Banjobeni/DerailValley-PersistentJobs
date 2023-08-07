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
                        Main._modEntry.Logger.Log("gen out shunting load");
                        __result = ShuntingLoadJobProceduralGenerator.GenerateShuntingLoadJobWithCarSpawning(
                            ___stationController,
                            forceFulfilledLicenseRequirements,
                            new System.Random(Environment.TickCount));
                        if (__result != null) {
                            Main._modEntry.Logger.Log("finalize out shunting load");
                            __result.FinalizeSetupAndGenerateFirstJob();
                        }
                        return false;
                    } else if (startingJobType == JobType.Transport) {
                        Main._modEntry.Logger.Log("gen out transport");
                        __result = TransportJobProceduralGenerator.GenerateTransportJobWithCarSpawning(
                            ___stationController,
                            forceFulfilledLicenseRequirements,
                            new System.Random(Environment.TickCount));
                        if (__result != null) {
                            Main._modEntry.Logger.Log("finalize out transport");
                            __result.FinalizeSetupAndGenerateFirstJob();
                        }
                        return false;
                    }
                    Debug.LogWarning($"[PersistentJobs] Got unexpected JobType.{startingJobType.ToString()} in {"StationProceduralJobGenerator"}.{"GenerateOutChainJob"} {"prefix"} patch. Falling back to base method.");
                } catch (Exception e) {
                    Main._modEntry.Logger.Error($"Exception thrown during {"StationProceduralJobGenerator"}.{"GenerateOutChainJob"} {"prefix"} patch:\n{e.ToString()}");
                    Main.OnCriticalFailure();
                }
            }
            return true;
        }
    }
}