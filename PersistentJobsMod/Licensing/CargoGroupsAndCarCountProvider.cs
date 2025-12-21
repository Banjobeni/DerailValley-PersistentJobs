using System.Collections.Generic;
using System;
using System.Linq;
using DV.ThingTypes;

namespace PersistentJobsMod.Licensing {
    public static class CargoGroupsAndCarCountProvider {
        public enum CargoGroupLicenseKind {
            Cargo,
            Cars,
        }

        public static (List<CargoGroup> availableCargoGroups, int countTrainCars)? GetOrNull(List<CargoGroup> cargoGroups, StationProceduralJobsRuleset carCountRuleset, bool requirePlayerLicensesCompatible, CargoGroupLicenseKind licenseKind, Random rng) {
            var maxCarsPerJob = requirePlayerLicensesCompatible ? Math.Min(carCountRuleset.maxCarsPerJob, LicenseManager.Instance.GetMaxNumberOfCarsPerJobWithAcquiredJobLicenses()) : carCountRuleset.maxCarsPerJob;

            var trainCarCount = rng.Next(carCountRuleset.minCarsPerJob, maxCarsPerJob + 1);

            if (trainCarCount < 1) {
                // really shouldn't happen, but just in case
                return null;
            }

            if (!requirePlayerLicensesCompatible) {
                return (cargoGroups, trainCarCount);
            }

            Main._modEntry.Logger.Log("CargoGroupsAndCarCountProvider: forcing license requirements");

            var licensedCargoGroups = GetLicensedCargoGroups(cargoGroups, licenseKind);

            if (licensedCargoGroups.Count > 0) {
                return (licensedCargoGroups, trainCarCount);
            }

            return null;
        }

        private static List<CargoGroup> GetLicensedCargoGroups(List<CargoGroup> cargoGroups, CargoGroupLicenseKind licenseKind) {
            if (licenseKind == CargoGroupLicenseKind.Cargo) {
                return cargoGroups.Where(cg => LicenseManager.Instance.IsLicensedForJob(JobLicenseType_v2.ToV2List(cg.CargoRequiredLicenses))).ToList();
            } else {
                return cargoGroups.Where(cg => LicenseManager.Instance.IsLicensedForJob(JobLicenseType_v2.ToV2List(cg.CarRequiredLicenses))).ToList();
            }
        }
    }
}