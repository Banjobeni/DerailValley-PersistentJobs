using System.Collections.Generic;
using System.Linq;
using DV.Logic.Job;

namespace PersistentJobsMod.Extensions {
    public static class TrackExtension {
        public static List<Job> GetJobsOfCarsFullyOnTrack(this Track track)
        {
            var cars = track.GetCarsFullyOnTrack();
            var trainCars = TrainCar.ExtractTrainCars(cars);
            var jobs = trainCars.Select(tc => JobsManager.Instance.GetJobOfCar(TrainCar.ExtractLogicCars(new List<TrainCar> { tc })[0])).Where(j => j != null).Distinct().ToList();
            return jobs;
        }
        public static double GetTotalUsableTrackLength(this Track track) {
            // see YardTracksOrganizer.END_OF_TRACK_OFFSET_RESERVATION. this number of meters is always kept "reserved" when checking for free (reservable) space
            return track.length - 40.0f;
        }
    }
}