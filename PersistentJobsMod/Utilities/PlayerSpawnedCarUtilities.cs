using DV.Damage;
using DV.ServicePenalty;
using HarmonyLib;

namespace PersistentJobsMod.Utilities {
    public static class PlayerSpawnedCarUtilities {
        public static void ConvertPlayerSpawnedTrainCar(TrainCar trainCar) {
            if (!trainCar.playerSpawnedCar) {
                return;
            }

            trainCar.playerSpawnedCar = false;

            var carStateSave = Traverse.Create(trainCar).Field("carStateSave").GetValue<CarStateSave>();
            if (Traverse.Create(carStateSave).Field("debtTrackerCar").GetValue<DebtTrackerCar>() != null) {
                return;
            }

            var trainPlatesController = Traverse.Create(trainCar).Field("trainPlatesCtrl").GetValue<TrainCarPlatesController>();

            var carDamageModel = GetOrCreateCarDamageModel(trainCar, trainPlatesController);

            var cargoDamageModelOrNull = GetOrCreateCargoDamageModelOrNull(trainCar, trainPlatesController);

            var carDebtController = Traverse.Create(trainCar).Field("carDebtController").GetValue<CarDebtController>();
            carDebtController.SetDebtTracker(carDamageModel, cargoDamageModelOrNull);

            carStateSave.Initialize(carDamageModel, cargoDamageModelOrNull);
            carStateSave.SetDebtTrackerCar(carDebtController.CarDebtTracker);

            Main._modEntry.Logger.Log($"Converted player spawned TrainCar {trainCar.logicCar.ID}");
        }

        private static CarDamageModel GetOrCreateCarDamageModel(TrainCar trainCar, TrainCarPlatesController trainPlatesController) {
            if (trainCar.CarDamage != null) {
                return trainCar.CarDamage;
            }

            Main._modEntry.Logger.Log($"Creating CarDamageModel for TrainCar[{trainCar.logicCar.ID}]...");

            var carDamageModel = trainCar.gameObject.AddComponent<CarDamageModel>();

            Traverse.Create(trainCar).Field("carDmg").SetValue(carDamageModel);
            carDamageModel.OnCreated(trainCar);

            var updateCarHealthDataMethodTraverse = Traverse.Create(trainPlatesController).Method("UpdateCarHealthData", new[] { typeof(float) });
            updateCarHealthDataMethodTraverse.GetValue(carDamageModel.EffectiveHealthPercentage100Notation);
            carDamageModel.CarEffectiveHealthStateUpdate += carHealthPercentage => updateCarHealthDataMethodTraverse.GetValue(carHealthPercentage);

            return carDamageModel;
        }

        private static CargoDamageModel GetOrCreateCargoDamageModelOrNull(TrainCar trainCar, TrainCarPlatesController trainPlatesCtrl) {
            if (trainCar.CargoDamage != null || trainCar.IsLoco) {
                return trainCar.CargoDamage;
            }

            Main._modEntry.Logger.Log($"Creating CargoDamageModel for TrainCar[{trainCar.logicCar.ID}]...");

            var cargoDamageModel = trainCar.gameObject.AddComponent<CargoDamageModel>();

            Traverse.Create(trainCar).Property("cargoDamage").SetValue(cargoDamageModel);
            cargoDamageModel.OnCreated(trainCar);

            var updateCargoHealthDataMethodTraverse = Traverse.Create(trainPlatesCtrl).Method("UpdateCargoHealthData", new[] { typeof(float) });
            updateCargoHealthDataMethodTraverse.GetValue(cargoDamageModel.EffectiveHealthPercentage100Notation);
            cargoDamageModel.CargoEffectiveHealthStateUpdate += cargoHealthPercentage => updateCargoHealthDataMethodTraverse.GetValue(cargoHealthPercentage);

            return cargoDamageModel;
        }
    }
}