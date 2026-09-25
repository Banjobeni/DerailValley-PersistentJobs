using DV.Utils;
using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;
using VLB;

namespace PersistentJobsMod.HarmonyPatches.Optimization
{
    [HarmonyPatch]
    public static class RailTrackEx_BogiesOnTrack_Patch
    {
        [HarmonyPatch(typeof(RailTrackOnTrackBogiesExtensions), nameof(RailTrackOnTrackBogiesExtensions.BogiesOnTrack))]
        [HarmonyPostfix]
        public static void Postfix(RailTrack rt, HashSet<Bogie> __result)
        {
            if (PersistentJobsMod.ModInteraction.MultiplayerShim.IsHost && Main.Settings.DummyBogiesForTracksOfSuspendedCars && WorldStreamingInit.IsLoaded)
            {
                if (PersistentJobsMod.Optimization.FarCarOpt.AllTracks == null || PersistentJobsMod.Optimization.FarCarOpt.AllTracks.Length == 0) PersistentJobsMod.Optimization.FarCarOpt.AllTracks = SingletonBehaviour<RailTrackRegistryBase>.Instance.OrderedRailtracks;

                int railTrackIndex = Array.IndexOf(PersistentJobsMod.Optimization.FarCarOpt.AllTracks, rt);
                if (railTrackIndex != -1 && PersistentJobsMod.Optimization.FarCarOpt.OccupiedRailTrackIndexesToFakeBogies.TryGetValue(railTrackIndex, out Bogie fakeBogie))
                {
                    if (fakeBogie is null)
                    {
                        string tempName = $"PersistentJobs_PlaceholderBogieHolder_{rt.gameObject.name}";
                        GameObject tempGO = new(tempName);
                        tempGO.SetActive(false);
                        fakeBogie = tempGO.GetOrAddComponent<Bogie>();
                        __result.Add(fakeBogie);
                        Main._modEntry.Logger.Log($"fake bogie on {rt.gameObject.name} created");
                        PersistentJobsMod.Optimization.FarCarOpt.OccupiedRailTrackIndexesToFakeBogies[railTrackIndex] = fakeBogie;
                    }

                    Main._modEntry.Logger.Log($"signals checking {rt.gameObject.name} see {__result.Count} bogies now");
                }
            }
        }
    }
}
