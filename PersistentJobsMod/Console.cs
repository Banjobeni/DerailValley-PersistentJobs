using CommandTerminal;
using UnityEngine;

namespace PersistentJobsMod {
    public static class Console {
        [RegisterCommand("PJClearStationSpawnFlag", Help = "PersistentJobsMod: Clear the flag for a station such that it may spawn cars again", MaxArgCount = 1, MinArgCount = 1)]
        public static void Controls_ShowBinds(CommandArg[] args) {
            var success = Main.StationIdSpawnBlockList.Remove(args[0].String);
            if (success) {
                Debug.Log("Cleared station spawn flag of " + args[0].String);
            } else {
                Debug.Log("Station spawn flag was not set");
            }
        }
    }
}