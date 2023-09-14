using System;
using DV.Logic.Job;

namespace PersistentJobsMod.ModInteraction {
    public static class PersistentJobsModInteractionFeatures {
        public static event Action<Job> JobTracksChanged;

        public static void InvokeJobTrackChanged(Job job) {
            JobTracksChanged?.Invoke(job);
        }
    }
}