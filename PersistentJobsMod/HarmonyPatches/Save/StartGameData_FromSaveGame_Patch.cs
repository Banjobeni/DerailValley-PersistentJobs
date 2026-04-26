using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PersistentJobsMod.HarmonyPatches.Save
{
    [HarmonyPatch(typeof(StartGameData_FromSaveGame), "GetPostLoadMessage")]
    public static class StartGameData_FromSaveGame_Patch
    {
        public static void Postfix(string __result)
        {
            if(__result == "tutorial/trains_were_reset")
            {
                CarsSaveManager_Patches.ResetJobsAndCarsState();
            }
        }
    }
}
