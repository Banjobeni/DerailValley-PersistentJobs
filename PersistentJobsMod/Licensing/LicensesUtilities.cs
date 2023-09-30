using System.Collections.Generic;
using DV.ThingTypes;

namespace PersistentJobsMod.Licensing {
    public static class LicensesUtilities {
        public static JobLicenses GetRequiredJobLicenses(JobType jobType, IReadOnlyList<TrainCarType_v2> trainCarTypes, IReadOnlyList<CargoType> cargoTypes, int trainCarCount) {
            var licenses = LicenseManager.Instance.GetRequiredLicensesForJobType(jobType);
            
            licenses.UnionWith(LicenseManager.Instance.GetRequiredLicensesForCarTypes(trainCarTypes));
            licenses.UnionWith(LicenseManager.Instance.GetRequiredLicensesForCargoTypes(cargoTypes));

            if (LicenseManager.Instance.GetRequiredLicenseForNumberOfTransportedCars(trainCarCount) is var license && license != null) {
                licenses.Add(license);
            }

            return JobLicenseType_v2.ListToFlags(licenses);
        }
    }
}