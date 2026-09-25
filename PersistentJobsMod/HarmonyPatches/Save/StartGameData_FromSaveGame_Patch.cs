using DV.Utils;
using HarmonyLib;
using PersistentJobsMod.Optimization;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PersistentJobsMod.HarmonyPatches.Save
{
    [HarmonyPatch(typeof(StartGameData_FromSaveGame))]
    public static class StartGameData_FromSaveGame_Patch
    {
        [HarmonyPatch("GetPostLoadMessage")]
        [HarmonyPostfix]
        public static void GetPostLoadMessage_Postfix(string __result)
        {
            if(__result == "tutorial/trains_were_reset")
            {
                CarsSaveManager_Load_Patches.ResetJobsAndCarsState();
            }
        }

        /*[HarmonyPatch("LoadingNonBlockingCoro")]
        [HarmonyPostfix]
        public static IEnumerator LoadingNonBlockingCoro_Postfix(IEnumerator __result)
        {
            while (__result.MoveNext()) yield return __result.Current;

            FarCarOpt.RunSuspendCars();
        }*/
    }
}
