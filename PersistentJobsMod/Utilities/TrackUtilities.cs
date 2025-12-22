using System;
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

        public static Track FindNearestNamedTrackOrNull(Track track, double trackPosition, Vector3 searchStartPosition) {
            var (trackOrNull, distance, steps, totalIterations) = SearchTrackByDistance(track, trackPosition, 800, (t, d) => DoesTrackMatch(t, d, searchStartPosition));

            var formattedSearchResult = FormatSearchResult(trackOrNull, distance, steps, totalIterations, searchStartPosition);

            Debug.Log($"[PersistentJobsMod] Result of FindNearestNamedTrackOrNull({track.ID}, {trackPosition:F2}, {searchStartPosition.ToString()}): {formattedSearchResult}");

            return trackOrNull;
        }

        private static string FormatSearchResult(Track trackOrNull, double? distance, int? steps, int totalIterations, Vector3 searchStartPosition) {
            var stationControllerDistance = (trackOrNull != null && !trackOrNull.ID.IsGeneric()) ? (StationController.GetStationByYardID(trackOrNull.ID.yardId).transform.position - searchStartPosition).magnitude : (float?)null;

            return $"{trackOrNull?.ID.FullDisplayID ?? "-"} dist:{stationControllerDistance?.ToString("F2") ?? "-"} path:{distance:F2} steps:{steps?.ToString() ?? "-"} iters:{totalIterations}";
        }

        private static bool DoesTrackMatch(Track track, double distance, Vector3 searchStartPosition) {
            if (track.ID.IsGeneric()) {
                return false;
            }

            if (IsSubstationMilitaryTrack(track) && distance > 400) {
                return false;
            }

            var stationControllerDistance = (StationController.GetStationByYardID(track.ID.yardId).transform.position - searchStartPosition).magnitude;
            if (stationControllerDistance > 1000) {
                return false;
            }

            return true;
        }

        private static bool IsSubstationMilitaryTrack(Track track) {
            var yardID = track.ID.yardId;
            if (yardID == "MB") {
                return false;
            }

            if (yardID.EndsWith("MB")) {
                return true;
            }

            return false;
        }

        private static (Track TrackOrNull, double? Distance, int? steps, int totalIterations) SearchTrackByDistance(Track track, double trackPosition, double maxTrackDistance, Func<Track, double, bool> doesTrackMatch) {
            if (doesTrackMatch(track, trackPosition)) {
                return (track, 0, 0, 0);
            }

            var fakeMinHeap = new List<(TrackSide TrackSide, double Distance, int Steps)>();
            var visited = new HashSet<TrackSide>();

            if (trackPosition < maxTrackDistance) {
                fakeMinHeap.Add((new TrackSide { Track = track, IsStart = true }, trackPosition, 1));
            }
            if (track.length - trackPosition < maxTrackDistance) {
                fakeMinHeap.Add((new TrackSide { Track = track, IsStart = false }, track.length - trackPosition, 1));
            }
            fakeMinHeap.Sort((a, b) => a.Distance.CompareTo(b.Distance));

            int iterations = 0;
            while (fakeMinHeap.Any()) {
                iterations++;
                var (currentTrackSide, currentDistance, currentSteps) = fakeMinHeap.First();
                fakeMinHeap.RemoveAt(0);
                if (!visited.Contains(currentTrackSide)) {
                    visited.Add(currentTrackSide);

                    if (doesTrackMatch(currentTrackSide.Track, currentDistance)) {
                        return (currentTrackSide.Track, currentDistance, currentSteps, iterations);
                    }

                    var connectedTrackSides = GetConnectedTrackSides(currentTrackSide);
                    foreach (var connectedTrackSide in connectedTrackSides) {
                        fakeMinHeap.Add((connectedTrackSide, currentDistance, currentSteps + 1));
                    }
                    if (currentDistance + currentTrackSide.Track.length < maxTrackDistance) {
                        fakeMinHeap.Add((currentTrackSide with { IsStart = !currentTrackSide.IsStart }, currentDistance + currentTrackSide.Track.length, currentSteps + 1));
                    }
                    fakeMinHeap.Sort((a, b) => a.Distance.CompareTo(b.Distance));
                }
            }

            return (null, null, null, iterations);
        }

        private static List<TrackSide> GetConnectedTrackSides(TrackSide currentTrackSide) {
            var railTrack = currentTrackSide.Track.RailTrack();
            var branches = currentTrackSide.IsStart ? railTrack.GetAllInBranches() : railTrack.GetAllOutBranches();
            if (branches == null) {
                return new List<TrackSide>();
            }
            return branches.Select(b => new TrackSide { Track = b.track.LogicTrack(), IsStart = b.first }).ToList();
        }


        public record TrackSide {
            public Track Track { get; set; }
            public bool IsStart { get; set; }
        }
    }
}