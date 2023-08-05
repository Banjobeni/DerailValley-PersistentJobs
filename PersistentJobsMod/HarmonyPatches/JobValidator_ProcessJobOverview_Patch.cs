using System;
using System.Collections.Generic;
using System.Linq;
using DV.Logic.Job;
using DV.ThingTypes;
using DV.Utils;
using HarmonyLib;
using UnityEngine;

namespace PersistentJobsMod.HarmonyPatches {
    /// <summary>expires a job if none of its cars are in range of the starting station on job start attempt</summary>
    [HarmonyPatch(typeof(JobValidator), "ProcessJobOverview")]
    class JobValidator_ProcessJobOverview_Patch {
        static bool Prefix(DV.Printers.PrinterController ___bookletPrinter,
            JobOverview jobOverview) {
            try {
                if (!Main.modEntry.Active) {
                    return true;
                }

                var job = jobOverview.job;
                var allStations = UnityEngine.Object.FindObjectsOfType<StationController>();
                var stationController = allStations.FirstOrDefault(
                    (StationController st) => st.logicStation.availableJobs.Contains(job)
                );

                if (___bookletPrinter.IsOnCooldown || job.State != JobState.Available || stationController == null) {
                    return true;
                }

                // for shunting (un)load jobs, require cars to not already be on the warehouse track
                if (job.jobType == JobType.ShuntingLoad || job.jobType == JobType.ShuntingUnload) {
                    var wt = job.tasks.Aggregate(
                        null as Task,
                        (found, outerTask) => found == null
                            ? Utilities.TaskFindDFS(outerTask, innerTask => innerTask is WarehouseTask)
                            : found) as WarehouseTask;
                    var wm = wt != null ? wt.warehouseMachine : null;
                    if (wm != null && job.tasks.Any(
                            outerTask => Utilities.TaskAnyDFS(
                                outerTask,
                                innerTask => IsAnyTaskCarOnTrack(innerTask, wm.WarehouseTrack)))) {
                        ___bookletPrinter.PlayErrorSound();
                        return false;
                    }
                }

                // expire the job if all associated cars are outside the job destruction range
                // the base method's logic will handle generating the expired report
                var stationRange = Traverse.Create(stationController)
                    .Field("stationRange")
                    .GetValue<StationJobGenerationRange>();
                if (!job.tasks.Any(
                        outerTask => Utilities.TaskAnyDFS(
                            outerTask,
                            innerTask => AreTaskCarsInRange(innerTask, stationRange)))) {
                    job.ExpireJob();
                    return true;
                }

                // reserve space for this job
                var stationJobControllers
                    = UnityEngine.Object.FindObjectsOfType<StationProceduralJobsController>();
                JobChainController jobChainController = null;
                for (var i = 0; i < stationJobControllers.Length && jobChainController == null; i++) {
                    foreach (var jcc in stationJobControllers[i].GetCurrentJobChains()) {
                        if (jcc.currentJobInChain == job) {
                            jobChainController = jcc;
                            break;
                        }
                    }
                }
                if (jobChainController == null) {
                    Debug.LogWarning(string.Format(
                        "[PersistentJobs] could not find JobChainController for Job[{0}]",
                        job.ID));
                } else if (job.jobType == JobType.ShuntingLoad) {
                    // shunting load jobs don't need to reserve space
                    // their destination track task will be changed to the warehouse track
                    Debug.Log(string.Format(
                        "[PersistentJobs] skipping track reservation for Job[{0}] because it's a shunting load job",
                        job.ID));
                } else {
                    ReserveOrReplaceRequiredTracks(jobChainController);
                }

                // for shunting load jobs, don't require player to spot the train on a track after loading
                if (job.jobType == JobType.ShuntingLoad) {
                    ReplaceShuntingLoadDestination(job);
                }
            } catch (Exception e) {
                Main.modEntry.Logger.Error(string.Format(
                    "Exception thrown during JobValidator.ProcessJobOverview prefix patch:\n{0}",
                    e.ToString()
                ));
                Main.OnCriticalFailure();
            }
            return true;
        }

        private static void ReplaceShuntingLoadDestination(Job job) {
            Debug.Log("[PersistentJobs] attempting to replace destination track with warehouse track...");
            var sequence = job.tasks[0] as SequentialTasks;
            if (sequence == null) {
                Debug.LogError("    couldn't find sequential task!");
                return;
            }

            var tasks = Traverse.Create(sequence)
                .Field("tasks")
                .GetValue<LinkedList<Task>>();

            if (tasks == null) {
                Debug.LogError("    couldn't find child tasks!");
                return;
            }

            var cursor = tasks.First;

            if (cursor == null) {
                Debug.LogError("    first task in sequence was null!");
                return;
            }

            while (cursor != null && Utilities.TaskAnyDFS(
                       cursor.Value,
                       t => t.InstanceTaskType != TaskType.Warehouse)) {
                Debug.Log("    searching for warehouse task...");
                cursor = cursor.Next;
            }

            if (cursor == null) {
                Debug.LogError("    couldn't find warehouse task!");
                return;
            }

            // cursor points at the parallel task of warehouse tasks
            // replace the destination track of all following tasks with the warehouse track
            var wt = (Utilities.TaskFindDFS(
                cursor.Value,
                t => t.InstanceTaskType == TaskType.Warehouse) as WarehouseTask);
            var wm = wt != null ? wt.warehouseMachine : null;

            if (wm == null) {
                Debug.LogError("    couldn't find warehouse machine!");
                return;
            }

            while ((cursor = cursor.Next) != null) {
                Debug.Log("    replace destination tracks...");
                Utilities.TaskDoDFS(
                    cursor.Value,
                    t => Traverse.Create(t).Field("destinationTrack").SetValue(wm.WarehouseTrack));
            }

            Debug.Log("    done!");
        }

        private static bool AreTaskCarsInRange(Task task, StationJobGenerationRange stationRange) {
            var cars = Traverse.Create(task).Field("cars").GetValue<List<Car>>();
            var carInRangeOfStation = cars.FirstOrDefault((Car c) => {
                var trainCar = SingletonBehaviour<IdGenerator>.Instance.logicCarToTrainCar[c];
                var distance =
                    (trainCar.transform.position - stationRange.stationCenterAnchor.position).sqrMagnitude;
                return trainCar != null && distance <= Main.initialDistanceRegular;
            });
            return carInRangeOfStation != null;
        }

        private static bool IsAnyTaskCarOnTrack(Task task, Track track) {
            var cars = Traverse.Create(task).Field("cars").GetValue<List<Car>>();
            return cars.Any(car => car.CurrentTrack == track);
        }

        private static void ReserveOrReplaceRequiredTracks(JobChainController jobChainController) {
            var jobChain = Traverse.Create(jobChainController)
                .Field("jobChain")
                .GetValue<List<StaticJobDefinition>>();
            var jobDefToCurrentlyReservedTracks
                = Traverse.Create(jobChainController)
                    .Field("jobDefToCurrentlyReservedTracks")
                    .GetValue<Dictionary<StaticJobDefinition, List<TrackReservation>>>();
            for (var i = 0; i < jobChain.Count; i++) {
                var key = jobChain[i];
                if (jobDefToCurrentlyReservedTracks.ContainsKey(key)) {
                    var trackReservations = jobDefToCurrentlyReservedTracks[key];
                    for (var j = 0; j < trackReservations.Count; j++) {
                        var reservedTrack = trackReservations[j].track;
                        var reservedLength = trackReservations[j].reservedLength;
                        if (YardTracksOrganizer.Instance.GetFreeSpaceOnTrack(reservedTrack) >= reservedLength) {
                            YardTracksOrganizer.Instance.ReserveSpace(reservedTrack, reservedLength, false);
                        } else {
                            // not enough space to reserve; find a different track with enough space & update job data
                            var replacementTrack = GetReplacementTrack(reservedTrack, reservedLength);
                            if (replacementTrack == null) {
                                Debug.LogWarning(string.Format(
                                    "[PersistentJobs] Can't find track with enough free space for Job[{0}]. Skipping track reservation!",
                                    key.job.ID));
                                continue;
                            }

                            YardTracksOrganizer.Instance.ReserveSpace(replacementTrack, reservedLength, false);

                            // update reservation data
                            trackReservations.RemoveAt(j);
                            trackReservations.Insert(j, new TrackReservation(replacementTrack, reservedLength));

                            // update static job definition data
                            if (key is StaticEmptyHaulJobDefinition) {
                                (key as StaticEmptyHaulJobDefinition).destinationTrack = replacementTrack;
                            } else if (key is StaticShuntingLoadJobDefinition) {
                                (key as StaticShuntingLoadJobDefinition).destinationTrack = replacementTrack;
                            } else if (key is StaticTransportJobDefinition) {
                                (key as StaticTransportJobDefinition).destinationTrack = replacementTrack;
                            } else if (key is StaticShuntingUnloadJobDefinition) {
                                (key as StaticShuntingUnloadJobDefinition).carsPerDestinationTrack
                                    = (key as StaticShuntingUnloadJobDefinition).carsPerDestinationTrack
                                    .Select(cpt => cpt.track == reservedTrack ? new CarsPerTrack(replacementTrack, cpt.cars) : cpt)
                                    .ToList();
                            } else {
                                // attempt to replace track via Traverse for unknown job types
                                var replacedDestination = false;
                                try {
                                    var destinationTrackField = Traverse.Create(key).Field("destinationTrack");
                                    var carsPerDestinationTrackField = Traverse.Create(key).Field("carsPerDestinationTrack");
                                    if (destinationTrackField.FieldExists()) {
                                        destinationTrackField.SetValue(replacementTrack);
                                        replacedDestination = true;
                                    } else if (carsPerDestinationTrackField.FieldExists()) {
                                        carsPerDestinationTrackField.SetValue(
                                            carsPerDestinationTrackField.GetValue<List<CarsPerTrack>>()
                                                .Select(cpt => cpt.track == reservedTrack ? new CarsPerTrack(replacementTrack, cpt.cars) : cpt)
                                                .ToList());
                                        replacedDestination = true;
                                    }
                                } catch (Exception e) {
                                    Debug.LogError(e);
                                }
                                if (!replacedDestination) {
                                    Debug.LogError(string.Format(
                                        "[PersistentJobs] Unaccounted for JobType[{1}] encountered while reserving track space for Job[{0}].",
                                        key.job.ID,
                                        key.job.jobType));
                                }
                            }

                            // update task data
                            foreach (var task in key.job.tasks) {
                                Utilities.TaskDoDFS(task, t => {
                                    if (t is TransportTask) {
                                        var destinationTrack = Traverse.Create(t).Field("destinationTrack");
                                        if (destinationTrack.GetValue<Track>() == reservedTrack) {
                                            destinationTrack.SetValue(replacementTrack);
                                        }
                                    }
                                });
                            }
                        }
                    }
                } else {
                    Debug.LogError(
                        string.Format(
                            "[PersistentJobs] No reservation data for {0}[{1}] found!" +
                            " Reservation data can be empty, but it needs to be in {2}.",
                            "jobChain",
                            i,
                            "jobDefToCurrentlyReservedTracks"),
                        jobChain[i]);
                }
            }
        }

        private static Track GetReplacementTrack(Track oldTrack, float trainLength) {
            // find station controller for track
            var allStations = UnityEngine.Object.FindObjectsOfType<StationController>();
            var stationController
                = allStations.ToList().Find(sc => sc.stationInfo.YardID == oldTrack.ID.yardId);

            // setup preferred tracks
            List<Track>[] preferredTracks;
            var stationYard = stationController.logicStation.yard;
            if (stationYard.StorageTracks.Contains(oldTrack)) {
                // shunting unload, logistical haul
                preferredTracks = new List<Track>[] {
                    stationYard.StorageTracks,
                    stationYard.TransferOutTracks,
                    stationYard.TransferInTracks
                };
            } else if (stationYard.TransferInTracks.Contains(oldTrack)) {
                // freight haul
                preferredTracks = new List<Track>[] {
                    stationYard.TransferInTracks,
                    stationYard.TransferOutTracks,
                    stationYard.StorageTracks
                };
            } else if (stationYard.TransferOutTracks.Contains(oldTrack)) {
                // shunting load
                preferredTracks = new List<Track>[] {
                    stationYard.TransferOutTracks,
                    stationYard.StorageTracks,
                    stationYard.TransferInTracks
                };
            } else {
                Debug.LogError(string.Format(
                    "[PersistentJobs] Cant't find track group for Track[{0}] in Station[{1}]. Skipping reservation!",
                    oldTrack.ID,
                    stationController.logicStation.ID));
                return null;
            }

            // find track with enough free space
            Track targetTrack = null;
            var yto = YardTracksOrganizer.Instance;
            for (var p = 0; targetTrack == null && p < preferredTracks.Length; p++) {
                var trackGroup = preferredTracks[p];
                targetTrack = Utilities.GetTrackThatHasEnoughFreeSpace(yto, trackGroup, trainLength);
            }

            if (targetTrack == null) {
                Debug.LogWarning(string.Format(
                    "[PersistentJobs] Cant't find any track to replace Track[{0}] in Station[{1}]. Skipping reservation!",
                    oldTrack.ID,
                    stationController.logicStation.ID));
            }

            return targetTrack;
        }
    }
}