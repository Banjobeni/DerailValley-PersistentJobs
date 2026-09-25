using HarmonyLib;
using DV.Logic.Job;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PersistentJobsMod.Optimization;
using System.Diagnostics;

namespace PersistentJobsMod.HarmonyPatches.Optimization
{
    [HarmonyPatch]
    public static class IdGenerator_Patches
    {
        [HarmonyPatch(typeof(IdGenerator), "UnregisterCarId")]
        [HarmonyPrefix]
        public static bool UnregisterCarId_Prefix(string carId)
        {
            //sometimes coroutines line up just right and CurrentTrainCarToSuspend still has a value when a loco is getting rightfully deleted
            if (carId.StartsWith("L")) return true;
            if (carId == FarCarOpt.CurrentTrainCarToSuspend?.ID) return false;
            else return true;
        }

        [HarmonyPatch(typeof(IdGenerator), "RegisterCarId")]
        [HarmonyPrefix]
        public static bool RegisterCarId_Prefix(string carId)
        {
            if (carId == FarCarOpt.CurrentCarIDToResume) return false;
            else return true;
        }
    }
}
