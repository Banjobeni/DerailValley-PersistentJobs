using DV.UI;
using HarmonyLib;
using PersistentJobsMod.ModInteraction;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PersistentJobsMod.HarmonyPatches.Pausing
{
    [HarmonyPatch]
    public static class PauseMenuController_Patches
    {
        [HarmonyPatch(typeof(DV.UI.PauseMenuController), "SetupListeners")]
        [HarmonyPostfix]
        public static void SetupListeners_Postfix(bool on)
        {
            if (MultiplayerShim._isHost != null)
            {
                if (on)
                {
                    Main._modEntry.Logger.Log("Pausing PJ coroutines");
                    Main.Pause = true;
                }
                else
                {
                    Main._modEntry.Logger.Log("Unpausing PJ coroutines");
                    Main.Pause = false;
                }
            }
        }
    }
}
