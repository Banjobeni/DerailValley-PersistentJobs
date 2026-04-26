using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PersistentJobsMod.HarmonyPatches.Save
{
    [HarmonyPatch(typeof(SaveGameData), "RemoveData")]
    public static class SaveGameData_RemoveData_Patch
    {
        public static void Postfix(string key)
        {
            if ((key == SaveGameKeys.Cars) || (key == SaveGameKeys.Jobs))
            {
                Main._modEntry.Logger.Log($"SaveGameData_RemoveData_Patch.Postfix: Savegame data reset, possibly due to mod or game update. Resetting all jobs and stations.");
                CarsSaveManager_Patches.ResetJobsAndCarsState();
            }
        }
    }
}
