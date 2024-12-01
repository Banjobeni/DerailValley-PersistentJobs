using System.Collections.Generic;
using System.Linq;
using DV.ThingTypes;

namespace PersistentJobsMod.Licensing {
    public static class LicensesUtilities {
        public static JobLicenses GetRequiredJobLicenses(JobType jobType, IReadOnlyList<TrainCarType_v2> trainCarTypes, IReadOnlyList<CargoType> cargoTypes, int trainCarCount) {
            var licenses = LicenseManager.Instance.GetRequiredLicensesForJobType(jobType).ToList();

            var requiredLicensesForCarTypes = LicenseManager.Instance.GetRequiredLicensesForCarTypes(trainCarTypes);
            licenses.AddRange(requiredLicensesForCarTypes);

            var requiredLicensesForCargoTypes = LicenseManager.Instance.GetRequiredLicensesForCargoTypes(cargoTypes);
            licenses.AddRange(requiredLicensesForCargoTypes);

            if (LicenseManager.Instance.GetRequiredLicenseForNumberOfTransportedCars(trainCarCount) is var license && license != null) {
                licenses.Add(license);
            }

            return JobLicenseType_v2.ListToFlags(licenses);
        }
    }
}