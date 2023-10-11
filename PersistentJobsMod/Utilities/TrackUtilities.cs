using System.Collections.Generic;
using System.Linq;
using DV.Logic.Job;
using PersistentJobsMod.Extensions;
using UnityEngine;
using Random = System.Random;

namespace PersistentJobsMod.Utilities {
    static class TrackUtilities {
        public static Track GetRandomHavingSpaceOrLongEnoughTrackOrNull(YardTracksOrganizer yto, List<Track> tracks, float requiredLength, Random random) {
            var longEnoughTracks = tracks.Where(t => t.GetTotalUsableTrackLength() > requiredLength).ToList();
            var havingSpaceTracks = yto.FilterOutTracksWithoutRequiredFreeSpace(longEnoughTracks, requiredLength);
            if (havingSpaceTracks.Any()) {
                return random.GetRandomElement(havingSpaceTracks);
            }
            if (longEnoughTracks.Any()) {
                var track = random.GetRandomElement(longEnoughTracks);

                Debug.LogWarning($"[PersistentJobsMod] None of the queried tracks have {requiredLength:F1}m of free space: {string.Join(", ", tracks.Select(t => $"{t.ID} ({yto.GetFreeSpaceOnTrack(t):F1}m)"))}. Choosing {track.ID} that is long enough.");

                return track;
            }

            Debug.LogWarning($"[PersistentJobsMod] None of the queried tracks are long enough for a consist of {requiredLength:F1}m: {string.Join(", ", tracks.Select(t => $"{t.ID} ({t.GetTotalUsableTrackLength():F1}m)"))}.");

            return null;
        }
    }
}