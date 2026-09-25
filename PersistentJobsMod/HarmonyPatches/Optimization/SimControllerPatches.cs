using DV.Simulation.Cars;
using DV.ThingTypes;
using HarmonyLib;
using PersistentJobsMod.ModInteraction;
using PersistentJobsMod.Optimization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PersistentJobsMod.HarmonyPatches.Optimization
{
    [HarmonyPatch]
    public static class SimControllerPatches
    {
        //Pax cars with MU have a SimController on them and via this "become locos" for the debt system, we must prevent this
        [HarmonyPatch(typeof(SimController), "OnLogicCarInitialized")]
        [HarmonyPrefix]
        public static bool OnLogicCarInitialized_Prefix(SimController __instance)
        {
            if (Main.PaxJobsPresent)
            {
                if (PaxJobsCompat.IsPaxCar(__instance.train) /*!CarTypes.IsAnyLocoSlugTender(__instance.train.carLivery) || !__instance.train.IsLoco || !(__instance.train.ID == FarCarOpt.CurrentCarIDToResume)*/)
                {
                    Main._modEntry.Logger.Log($"Skipping simCont setup for car {__instance.train.ID}");
                    __instance.train.LogicCarInitialized -= __instance.OnLogicCarInitialized;
                    return false;
                }
                else
                {
                    Main._modEntry.Logger.Log($"Car {__instance.train.ID} {__instance.train.name} {__instance.train.carLivery.prefab.name} {(__instance.train.IsLoco ? "which is a loco" : "")} has normal setup");
                    return true;
                }
            }
            else return true;
        }
    }
}
