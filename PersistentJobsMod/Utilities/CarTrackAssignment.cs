using System;
using System.Collections.Generic;
using System.Linq;
using DV.Logic.Job;
using UnityEngine;

namespace PersistentJobsMod.Utilities {
    public static class CarTrackAssignment {
        public static Track FindNearestNamedTrackOrNull(IReadOnlyList<TrainCar> trainCars) {
            var medianCarCount = trainCars.Count / 2;

            var nonDerailedBogies = trainCars.SelectMany((tc, index) => new[] { (MedianDistance: Math.Abs(index - medianCarCount), Bogie: tc.FrontBogie), (MedianDistance: Math.Abs(index - medianCarCount), Bogie: tc.RearBogie) })
                .Where(t => t.Bogie.track != null)
                .OrderBy(t => t.MedianDistance)
                .ToList();

            if (nonDerailedBogies.Count == 0) {
                // all bogies are derailed
                return null;
            }

            var bogieNamedTrackOrNull = nonDerailedBogies.Select(b => b.Bogie.track).Distinct().FirstOrDefault(t => !t.LogicTrack().ID.IsGeneric());
            if (bogieNamedTrackOrNull != null) {
                return bogieNamedTrackOrNull.LogicTrack();
            }

            var (_, bogie) = nonDerailedBogies.First();
            var track = bogie.track.LogicTrack();

            var (foundTrackOrNull, distance, steps, totalIterations) = SearchTrackByDistance(track, bogie.traveller.Span, 800, (t, d) => DoesTrackMatch(t, d, bogie.transform.position));

            var formattedSearchResult = FormatSearchResult(foundTrackOrNull, distance, steps, totalIterations, bogie.transform.position);

            Debug.Log($"[PersistentJobsMod] Result of FindNearestNamedTrackOrNull({track.ID}, {bogie.traveller.Span:F2}, {bogie.transform.position.ToString()}): {formattedSearchResult}");

            if (foundTrackOrNull != null) {
                return foundTrackOrNull;
            }

            Debug.Log($"[PersistentJobsMod] Could not determine a nice-looking starting track for train cars {string.Join(", ", trainCars.Select(tc => tc.ID))} by searching for nearest named track");

            return null;
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
            var railTrack = RailTrackExtensions.RailTrack(currentTrackSide.Track);
            var branches = currentTrackSide.IsStart ? railTrack.GetAllInBranches() : railTrack.GetAllOutBranches();
            if (branches == null) {
                return new List<TrackSide>();
            }
            return branches.Select(b => new TrackSide { Track = RailTrackExtensions.LogicTrack(b.track), IsStart = b.first }).ToList();
        }

        public record TrackSide {
            public Track Track { get; set; }
            public bool IsStart { get; set; }
        }
    }
}