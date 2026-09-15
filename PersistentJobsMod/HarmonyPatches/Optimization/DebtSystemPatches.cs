using DV.Damage;
using DV.Logic.Job;
using DV.ServicePenalty;
using DV.Utils;
using HarmonyLib;
using PersistentJobsMod.Optimization;
using System.Collections.Generic;
using System.Linq;

namespace PersistentJobsMod.HarmonyPatches.Optimization
{
    [HarmonyPatch(typeof(JobDebtController), nameof(JobDebtController.StageJoblessCarDebtOnCarDestroy))]
    public static class JobDebtController_StageJoblessCarDebtOnCarDestroy_Patch
    {
        public static bool Prefix()
        {
            if (FarCarOpt.CurrentTrainCarToSuspend != null) return false;
            else return true;
        }
    }

    [HarmonyPatch(typeof(LocoDebtController), nameof(LocoDebtController.StageLocoDebtOnLocoDestroy))]
    public static class LocoDebtController_StageLocoDebtOnLocoDestroy_Patch
    {
        public static bool Prefix()
        {
            if (FarCarOpt.CurrentTrainCarToSuspend != null) return false;
            else return true;
        }
    }

    [HarmonyPatch(typeof(OwnedCarsStateController), nameof(OwnedCarsStateController.StageCarStateTrackerOnDestroy))]
    public static class OwnedCarsStateController_StageCarStateTrackerOnDestroy_Patch
    {
        public static bool Prefix()
        {
            if (FarCarOpt.CurrentTrainCarToSuspend != null) return false;
            else return true;
        }
    }

    [HarmonyPatch(typeof(DebtTrackerCar), nameof(DebtTrackerCar.UpdateDebtValues))]
    public static class DebtTrackerCar_UpdateDebtValues_Patch
    {
        public static bool Prefix(DebtTrackerCar __instance)
        {
            if (FarCarOpt.SuspendedCarGUIDToDebtTracker.Values.Any(t => t.Item1 == __instance as DebtTrackerBase)) return false;
            else return true;
        }
    }

    [HarmonyPatch(typeof(CarDebtController), nameof(CarDebtController.SetDebtTracker))]
    public static class CarDebtController_SetDebtTracker_Patch
    {
        public static bool Prefix(CarDamageModel carDmg, CargoDamageModel cargoDmg, CarDebtController __instance)
        {
            return PreSetTracker(carDmg, cargoDmg, __instance);
        }

        public static bool PreSetTracker(CarDamageModel carDmg, CargoDamageModel cargoDmg, CarDebtController __instance)
        {
            if (__instance.ignoreCarDamageDebt && !__instance.trainCar.IsLoco)
            {
                if (carDmg != null || cargoDmg != null)
                {
                    Main._modEntry.Logger.Log($"Setting functional debt tracker on {__instance.trainCar.ID}");
                    Traverse.Create(__instance).Property("CarDebtTracker").SetValue(new DebtTrackerCar(carDmg, cargoDmg, __instance.trainCar.ID, __instance.trainCar.carType));
                }
                else
                {
                    Main._modEntry.Logger.Error("Car is missing both damage components: CarDamageModel or CargoDamageModel. CarDebtTracker will be set to null!");
                    return true;
                }
                return false;
            }
            else return true;
        }
    }

    [HarmonyPatch(typeof(JobDebtController), nameof(JobDebtController.RegisterGeneratedJob))]
    public static class JobDebtController_Patch
    {
        public static void Prefix(Job job, List<Car> cars, JobDebtController __instance)
        {
            SingletonBehaviour<CareerManagerDebtController>.Instance.RefreshExistingDebtsState();
            var debtHandler = __instance.existingJoblessCarDebts;
            debtHandler.UpdateDebtState();
            var joblessCarDebtTrackers = debtHandler.joblessCarsTrackers;
            var carsToStageDebtsFor = joblessCarDebtTrackers.Select(jdt => jdt.GetDebtData()).Select(cdd => cdd.id).ToList().Intersect(cars.Select(c => c.ID)).ToList();
            foreach (var carID in carsToStageDebtsFor) foreach (var debt in joblessCarDebtTrackers.Where(jdt => jdt.GetDebtData().id == carID).ToList()) StageJoblessCarDebtAndFreeze(debt);
        }

        public static void StageJoblessCarDebtAndFreeze(DebtTrackerCar debtTrackerCar)
        {
            var controller = SingletonBehaviour<JobDebtController>.Instance;

            if (!controller.existingJoblessCarDebts.RemoveJoblessCarTracker(debtTrackerCar))
            {
                UnityEngine.Debug.LogError("Unexpected error: DebtTrackerCar" + debtTrackerCar.GetDebtData().id + " is not part of the existingJoblessCarDebts!");
                return;
            }

            if (controller.existingJoblessCarDebts.NumberOfDebts == 0) SingletonBehaviour<CareerManagerDebtController>.Instance.UnregisterDebt(controller.existingJoblessCarDebts);

            debtTrackerCar.UpdateDebtValues();
            CarDebtData carDebtData = debtTrackerCar.GetDebtData();
            if (carDebtData.GetTotalPriceOfDebt(false, false) > 0f)
            {
                carDebtData = CarDebtData.FilterOutUnchangedComponents(carDebtData, false);
                if (carDebtData == null) return;

                controller.deletedJoblessCarDebts.AddJoblessCarDebt(CarDebtData.LoadCarDebtFromSaveData(carDebtData.GetCarDebtSaveData()));

                if (controller.deletedJoblessCarDebts.NumberOfDebts == 1) SingletonBehaviour<CareerManagerDebtController>.Instance.RegisterDebt(controller.deletedJoblessCarDebts);
            }
            debtTrackerCar.UpdateStartValueToEndValue();
        }
    }
}
