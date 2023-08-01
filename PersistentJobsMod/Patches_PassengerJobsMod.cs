using Harmony12;
using System.Collections.Generic;
using UnityEngine;

namespace PersistentJobsMod {
    static class PJModSettings_PurgeData_Patch {
        static void Postfix() {
            Debug.Log("Clearing passenger spawning block list...");
            Main.StationIdPassengerBlockList.Clear();
        }
    }

    static class PassengerJobGenerator_StartGenerationAsync_Patch {
        static bool Prefix(object __instance) {
            if (Main.modEntry.Active) {
                StationController controller = Traverse.Create(__instance).Field("Controller").GetValue<StationController>();
                string stationId = controller?.logicStation?.ID;
                return stationId == null || !Main.StationIdPassengerBlockList.Contains(controller.logicStation.ID);
            }
            return true;
        }

        static void Postfix(object __instance) {
            StationController controller = Traverse.Create(__instance).Field("Controller").GetValue<StationController>();
            string stationId = controller?.logicStation?.ID;
            if (stationId != null && !Main.StationIdPassengerBlockList.Contains(stationId)) {
                Main.StationIdPassengerBlockList.Add(stationId);
            }
        }
    }

    static class CarSpawner_DeleteTrainCars_Replacer {
        // DeleteTrainCars is no longer static, thus leading instance argument is needed to soak up the instance from the IL stack
        static void NoOp(CarSpawner instance, List<TrainCar> trainCarsToDelete, bool forceInstantDestroy = false) {
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
            return instructions.MethodReplacer(
                AccessTools.Method(typeof(CarSpawner), "DeleteTrainCars"),
                AccessTools.Method(typeof(CarSpawner_DeleteTrainCars_Replacer), "NoOp"));
        }
    }
}