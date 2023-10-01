using System;
using System.Collections.Generic;
using System.Linq;
using DV.Logic.Job;
using DV.ThingTypes;
using DV.Utils;
using HarmonyLib;
using PersistentJobsMod.ModInteraction;
using UnityEngine;

namespace PersistentJobsMod.HarmonyPatches.JobValidators {
    /// <summary>expires a job if none of its cars are in range of the starting station on job start attempt</summary>
    [HarmonyPatch(typeof(JobValidator), "ProcessJobOverview")]
    public static class JobValidator_ProcessJobOverview_Patch {
        public static bool Prefix(DV.Printers.PrinterController ___bookletPrinter,
            JobOverview jobOverview) {
            try {
                if (!Main._modEntry.Active) {
                    return true;
                }

                var job = jobOverview.job;
                var allStations = UnityEngine.Object.FindObjectsOfType<StationController>();
                var stationController = allStations.FirstOrDefault(
                    st => st.logicStation.availableJobs.Contains(job)
                );

                if (___bookletPrinter.IsOnCooldown || job.State != JobState.Available || stationController == null) {
                    return true;
                }

                // for shunting (un)load jobs, require cars to not already be on the warehouse track
                if (job.jobType == JobType.ShuntingLoad || job.jobType == JobType.ShuntingUnload) {
                    var wt = job.tasks.Aggregate(
                        null as Task,
                        (found, outerTask) => found == null
                            ? Utilities.TaskFindDfs(outerTask, innerTask => innerTask is WarehouseTask)
                            : found) as WarehouseTask;
                    var wm = wt != null ? wt.warehouseMachine : null;
                    if (wm != null && job.tasks.Any(
                            outerTask => Utilities.TaskAnyDfs(
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
                        outerTask => Utilities.TaskAnyDfs(
                            outerTask,
                            innerTask => AreTaskCarsInRange(innerTask, stationRange)))) {
                    job.ExpireJob();
                    return true;
                }

                // reserve space for this job
                var stationJobControllers = UnityEngine.Object.FindObjectsOfType<StationProceduralJobsController>();

                var jobChainController = stationJobControllers.SelectMany(sjc => sjc.GetCurrentJobChains()).FirstOrDefault(jcc => jcc.currentJobInChain == job);

                if (jobChainController == null) {
                    Debug.LogWarning($"[PersistentJobs] could not find JobChainController for Job[{job.ID}]");
                } else if (job.jobType == JobType.ShuntingLoad) {
                    // shunting load jobs don't need to reserve space
                    // their destination track task will be changed to the warehouse track
                    Main._modEntry.Logger.Log($"skipping track reservation for Job[{job.ID}] because it's a shunting load job");
                } else {
                    var didAnyTrackChange = ReserveOrReplaceRequiredTracks(jobChainController);
                    if (didAnyTrackChange) {
                        PersistentJobsModInteractionFeatures.InvokeJobTrackChanged(job);
                    }
                }

                // for shunting load jobs, don't require player to spot the train on a track after loading
                if (job.jobType == JobType.ShuntingLoad) {
                    ReplaceShuntingLoadDestination(job);
                    PersistentJobsModInteractionFeatures.InvokeJobTrackChanged(job);
                }
            } catch (Exception e) {
                Main._modEntry.Logger.Error($"Exception thrown during JobValidator.ProcessJobOverview prefix patch:\n{e}");
                Main.OnCriticalFailure();
            }
            return true;
        }

        private static void ReplaceShuntingLoadDestination(Job job) {
            Main._modEntry.Logger.Log("attempting to replace destination track with warehouse track...");
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

            while (cursor != null && Utilities.TaskAnyDfs(
                       cursor.Value,
                       t => t.InstanceTaskType != TaskType.Warehouse)) {
                Main._modEntry.Logger.Log("    searching for warehouse task...");
                cursor = cursor.Next;
            }

            if (cursor == null) {
                Debug.LogError("    couldn't find warehouse task!");
                return;
            }

            // cursor points at the parallel task of warehouse tasks
            // replace the destination track of all following tasks with the warehouse track
            var wt = (Utilities.TaskFindDfs(
                cursor.Value,
                t => t.InstanceTaskType == TaskType.Warehouse) as WarehouseTask);
            var wm = wt != null ? wt.warehouseMachine : null;

            if (wm == null) {
                Debug.LogError("    couldn't find warehouse machine!");
                return;
            }

            while ((cursor = cursor.Next) != null) {
                Main._modEntry.Logger.Log("    replace destination tracks...");
                Utilities.TaskDoDfs(
                    cursor.Value,
                    t => Traverse.Create(t).Field("destinationTrack").SetValue(wm.WarehouseTrack));
            }

            Main._modEntry.Logger.Log("    done!");
        }

        private static bool AreTaskCarsInRange(Task task, StationJobGenerationRange stationRange) {
            var cars = Traverse.Create(task).Field("cars").GetValue<List<Car>>();
            var carInRangeOfStation = cars.FirstOrDefault(c => (SingletonBehaviour<IdGenerator>.Instance.logicCarToTrainCar[c].transform.position - stationRange.stationCenterAnchor.position).sqrMagnitude <= Main._initialDistanceRegular);
            return carInRangeOfStation != null;
        }

        private static bool IsAnyTaskCarOnTrack(Task task, Track track) {
            var cars = Traverse.Create(task).Field("cars").GetValue<List<Car>>();
            return cars.Any(car => car.FrontBogieTrack == track);
        }

        private static bool ReserveOrReplaceRequiredTracks(JobChainController jobChainController) {
            var jobChain = Traverse.Create(jobChainController).Field("jobChain").GetValue<List<StaticJobDefinition>>();
            var jobDefToCurrentlyReservedTracks = Traverse.Create(jobChainController).Field("jobDefToCurrentlyReservedTracks").GetValue<Dictionary<StaticJobDefinition, List<TrackReservation>>>();

            bool didAnyTrackChange = false;

            for (var i = 0; i < jobChain.Count; i++) {
                var key = jobChain[i];
                if (jobDefToCurrentlyReservedTracks.TryGetValue(key, out var trackReservations)) {
                    for (var j = 0; j < trackReservations.Count; j++) {
                        var reservedTrack = trackReservations[j].track;
                        var reservedLength = trackReservations[j].reservedLength;
                        if (YardTracksOrganizer.Instance.GetFreeSpaceOnTrack(reservedTrack) >= reservedLength) {
                            YardTracksOrganizer.Instance.ReserveSpace(reservedTrack, reservedLength, false);
                        } else {
                            // not enough space to reserve; find a different track with enough space & update job data
                            var replacementTrack = GetReplacementTrack(reservedTrack, reservedLength);
                            if (replacementTrack == null) {
                                Debug.LogWarning($"[PersistentJobs] Can't find track with enough free space for Job[{key.job.ID}]. Skipping track reservation!");
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
                                    Debug.LogError($"[PersistentJobs] Unaccounted for JobType[{key.job.jobType}] encountered while reserving track space for Job[{key.job.ID}].");
                                }
                            }

                            // update task data
                            foreach (var task in key.job.tasks) {
                                Utilities.TaskDoDfs(task, t => {
                                    if (t is TransportTask) {
                                        var destinationTrack = Traverse.Create(t).Field("destinationTrack");
                                        if (destinationTrack.GetValue<Track>() == reservedTrack) {
                                            destinationTrack.SetValue(replacementTrack);
                                        }
                                    }
                                });
                            }

                            didAnyTrackChange = true;
                        }
                    }
                } else {
                    Debug.LogError(
                        $"[PersistentJobs] No reservation data for {"jobChain"}[{i}] found!" + $" Reservation data can be empty, but it needs to be in {"jobDefToCurrentlyReservedTracks"}.",
                        jobChain[i]);
                }
            }

            return didAnyTrackChange;
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
                preferredTracks = new[] {
                    stationYard.StorageTracks,
                    stationYard.TransferOutTracks,
                    stationYard.TransferInTracks
                };
            } else if (stationYard.TransferInTracks.Contains(oldTrack)) {
                // freight haul
                preferredTracks = new[] {
                    stationYard.TransferInTracks,
                    stationYard.TransferOutTracks,
                    stationYard.StorageTracks
                };
            } else if (stationYard.TransferOutTracks.Contains(oldTrack)) {
                // shunting load
                preferredTracks = new[] {
                    stationYard.TransferOutTracks,
                    stationYard.StorageTracks,
                    stationYard.TransferInTracks
                };
            } else {
                Debug.LogError($"[PersistentJobs] Cant't find track group for Track[{oldTrack.ID}] in Station[{stationController.logicStation.ID}]. Skipping reservation!");
                return null;
            }

            // find track with enough free space
            Track targetTrack = null;
            var yto = YardTracksOrganizer.Instance;
            for (var p = 0; targetTrack == null && p < preferredTracks.Length; p++) {
                var trackGroup = preferredTracks[p];
                targetTrack = Utilities.GetTrackThatHasEnoughFreeSpace(yto, trackGroup, trainLength, new System.Random());
            }

            if (targetTrack == null) {
                Debug.LogWarning($"[PersistentJobs] Cant't find any track to replace Track[{oldTrack.ID}] in Station[{stationController.logicStation.ID}]. Skipping reservation!");
            }

            return targetTrack;
        }
    }
}