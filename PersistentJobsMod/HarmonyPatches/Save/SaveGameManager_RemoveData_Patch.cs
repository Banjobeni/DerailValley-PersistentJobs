using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PersistentJobsMod.HarmonyPatches.Save
{
    [HarmonyPatch(typeof(SaveGameData), "RemoveData")]
    public static class SaveGameManager_RemoveData_Patch
    {
        public static void Postfix(string _result)
        {
            if ((_result == SaveGameKeys.Cars) || (_result == SaveGameKeys.Jobs))
            {
                Main._modEntry.Logger.Log($"Savegame data reset, possibly due to mod or game update. Resetting all jobs and stations.");
                CarsSaveManager_Patches.ResetJobsAndCarsState();
            }
        }
    }
}
