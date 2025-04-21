using DV.Damage;
using DV.Logic.Job;
using HarmonyLib;

namespace PersistentJobsMod.Utilities {
    public static class PlayerSpawnedCarUtilities {

        public static void ModifyReadonlyField(Car instance, bool value)
        {
            Traverse.Create(instance)
            .Field("playerSpawnedCar")
            .SetValue(value);
        }

        public static void ConvertPlayerSpawnedTrainCar(TrainCar trainCar) {
            if (!trainCar.playerSpawnedCar) {
                return;
            }

            trainCar.playerSpawnedCar = false;
            ModifyReadonlyField(trainCar.logicCar, false);

            var carStateSave = trainCar.carStateSave;
            if (carStateSave.debtTrackerCar != null) {
                return;
            }

            var trainPlatesController = trainCar.trainPlatesCtrl;

            var carDamageModel = GetOrCreateCarDamageModel(trainCar, trainPlatesController);

            var cargoDamageModelOrNull = GetOrCreateCargoDamageModelOrNull(trainCar, trainPlatesController);

            var carDebtController = trainCar.carDebtController;
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

            trainCar.CarDamage = carDamageModel;
            carDamageModel.OnCreated(trainCar);

            trainPlatesController.UpdateCarHealthData(carDamageModel.EffectiveHealthPercentage100Notation);
            carDamageModel.CarEffectiveHealthStateUpdate += trainPlatesController.UpdateCarHealthData;

            return carDamageModel;
        }

        private static CargoDamageModel GetOrCreateCargoDamageModelOrNull(TrainCar trainCar, TrainCarPlatesController trainPlatesCtrl) {
            if (trainCar.CargoDamage != null || trainCar.IsLoco) {
                return trainCar.CargoDamage;
            }

            Main._modEntry.Logger.Log($"Creating CargoDamageModel for TrainCar[{trainCar.logicCar.ID}]...");

            var cargoDamageModel = trainCar.gameObject.AddComponent<CargoDamageModel>();

            trainCar.CargoDamage = cargoDamageModel;
            cargoDamageModel.OnCreated(trainCar);

            trainPlatesCtrl.UpdateCargoHealthData(cargoDamageModel.EffectiveHealthPercentage100Notation);
            cargoDamageModel.CargoEffectiveHealthStateUpdate += trainPlatesCtrl.UpdateCargoHealthData;

            return cargoDamageModel;
        }
    }
}