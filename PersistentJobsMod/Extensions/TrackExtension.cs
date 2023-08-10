using System.Collections.Generic;
using System.Linq;
using DV.Logic.Job;

namespace PersistentJobsMod.Extensions {
    public static class TrackExtension {
        public static List<Job> GetJobsOfCarsFullyOnTrack(this Track track) {
            var cars = track.GetCarsFullyOnTrack();
            var jobs = cars.Select(c => JobsManager.Instance.GetJobOfCar(IdGenerator.Instance.logicCarToTrainCar[c])).Distinct().ToList();
            return jobs;
        }
    }
}