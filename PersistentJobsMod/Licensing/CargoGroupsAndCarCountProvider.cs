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

            if (!requirePlayerLicensesCompatible) {
                return (cargoGroups, trainCarCount);
            }

            Main._modEntry.Logger.Log("CargoGroupsAndCarCountProvider: forcing license requirements");

            if (licenseKind == CargoGroupLicenseKind.Cargo) {
                var licensedCargoGroups = cargoGroups.Where(cg => LicenseManager.Instance.IsLicensedForJob(JobLicenseType_v2.ToV2List(cg.CargoRequiredLicenses))).ToList();
                if (licensedCargoGroups.Count == 0) {
                    return null;
                }

                return (licensedCargoGroups, trainCarCount);
            } else {
                var licensedCargoGroups = cargoGroups.Where(cg => LicenseManager.Instance.IsLicensedForJob(JobLicenseType_v2.ToV2List(cg.CarRequiredLicenses))).ToList();
                if (licensedCargoGroups.Count == 0) {
                    return null;
                }

                return (licensedCargoGroups, trainCarCount);
            }
        }
    }
}