using System;
using System.Collections.Generic;
using System.Linq;
using DV.Logic.Job;
using DV.ThingTypes;
using DV.Utils;
using HarmonyLib;
using PersistentJobsMod.Extensions;
using PersistentJobsMod.ModInteraction;
using PersistentJobsMod.Utilities;
using PersistentJobsMod;
using UnityEngine;
using Random = System.Random;
using MessageBox;
using DV.UIFramework;
using DV.Common;

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

                // for shunting (un)load jobs, require cars to not already be on the warehouse track, or allows the player to disable that feature on first encouter
                if (job.jobType == JobType.ShuntingLoad || job.jobType == JobType.ShuntingUnload) {
                    var wt = job.tasks.Aggregate(
                        null as Task,
                        (found, outerTask) => found == null
                            ? TaskUtilities.TaskFindDfs(outerTask, innerTask => innerTask is WarehouseTask)
                            : found) as WarehouseTask;
                    var wm = wt != null ? wt.warehouseMachine : null;
                    bool retBool = false;
                    if (wm != null && job.tasks.Any(
                            outerTask => TaskUtilities.TaskAnyDfs(
                                outerTask,
                                innerTask => IsAnyTaskCarOnTrack(innerTask, wm.WarehouseTrack)))) 
                    {
                        if (Main.Settings.AllowAccOnWarehouseTracks)
                        {
                            return true;
                        }
                        else
                        {
                            ___bookletPrinter.PlayErrorSound();
                            if (!Main.Settings.GetShuntJobInteract())
                            {
                                Main.Settings.SetShuntJobInteract(true); 
                                PopupAPI.ShowYesNo(
                                    message: "You are trying to accept a shunting job that is already on a loading track, that is kinda cheaty... \n Do you want the mod to allow this? ",
                                    onClose: (result) =>
                                    {
                                        if (result.closedBy == PopupClosedByAction.Positive)
                                        {
                                            Main.Settings.AllowAccOnWarehouseTracks = true;
                                            retBool = true;
                                        }
                                    }
                                );
                                Main.Settings.Save(Main._modEntry);
                                return retBool;
                            }
                            return retBool;
                        }
                    }
                }

                // expire the job if all associated cars are outside the job destruction range
                // the base method's logic will handle generating the expired report
                var stationRange = stationController.stationRange;
                if (!job.tasks.Any(
                        outerTask => TaskUtilities.TaskAnyDfs(
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
                    // TODO SL jobs generated by PJ should already have the warehouse track set as destination. decide if this additional handling here can be omitted.
                    ReplaceShuntingLoadDestination(job);
                    PersistentJobsModInteractionFeatures.InvokeJobTrackChanged(job);
                }
            } catch (Exception e) {
                Main.HandleUnhandledException(e, nameof(JobValidator_ProcessJobOverview_Patch) +"." + nameof(Prefix));
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

            // TODO rewrite this using sequence.GetTaskData().nestedTasks
            var tasks = sequence.tasks;

            if (tasks == null) {
                Debug.LogError("    couldn't find child tasks!");
                return;
            }

            var cursor = tasks.First;

            if (cursor == null) {
                Debug.LogError("    first task in sequence was null!");
                return;
            }

            while (cursor != null && TaskUtilities.TaskAnyDfs(
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
            var wt = (TaskUtilities.TaskFindDfs(
                cursor.Value,
                t => t.InstanceTaskType == TaskType.Warehouse) as WarehouseTask);
            var wm = wt != null ? wt.warehouseMachine : null;

            if (wm == null) {
                Debug.LogError("    couldn't find warehouse machine!");
                return;
            }

            while ((cursor = cursor.Next) != null) {
                Main._modEntry.Logger.Log("    replace destination tracks...");
                TaskUtilities.TaskDoDfs(
                    cursor.Value,
                    // this is rather hackish - we use a traverse to access the Task.destinationTrack field, but actually that field is only defined on TransportTask
                    t => Traverse.Create(t).Field("destinationTrack").SetValue(wm.WarehouseTrack));
            }

            Main._modEntry.Logger.Log("    done!");
        }

        private static bool AreTaskCarsInRange(Task task, StationJobGenerationRange stationRange) {
            // this is rather hackish - we use a traverse to access the Task.cars field, but actually that field is only defined on TransportTask and WarehouseTask
            var cars = Traverse.Create(task).Field("cars").GetValue<List<Car>>();
            var carInRangeOfStation = cars.FirstOrDefault(c => (SingletonBehaviour<IdGenerator>.Instance.logicCarToTrainCar[c].transform.position - stationRange.stationCenterAnchor.position).sqrMagnitude <= Main._initialDistanceRegular);
            return carInRangeOfStation != null;
        }

        private static bool IsAnyTaskCarOnTrack(Task task, Track track) {
            // this is rather hackish - we use a traverse to access the Task.cars field, but actually that field is only defined on TransportTask and WarehouseTask
            var cars = Traverse.Create(task).Field("cars").GetValue<List<Car>>();
            return cars.Any(car => car.FrontBogieTrack == track);
        }

        private static bool ReserveOrReplaceRequiredTracks(JobChainController jobChainController)
        {
            var jobChain = jobChainController.jobChain;
            var jobDefToCurrentlyReservedTracks = jobChainController.jobDefToCurrentlyReservedTracks;
            bool didAnyTrackChange = false;
            var random = new System.Random();

            for (var i = 0; i < jobChain.Count; i++)
            {
                var key = jobChain[i];
                Debug.Log($"[PersistentJobs] jobChain[{i}] type: {key?.GetType().Name}, has job: {key?.job != null}");
                if (key == null || key.job == null)
                {
                    Debug.LogError($"[PersistentJobs] job for jobChain[{i}] is null!");
                    SingletonBehaviour<SaveGameManager>.Instance.Save(SaveType.Manual, null, true);
                    PopupAPI.ShowOk("This job is possibly corrupted and problems may arise, so it is recommended to discard it. (You can regerate a job for the cars with 'PJ.RegenerateJobsImmediately') \n You may file a bug report concerning this on the mod´s github, including the output of the Bug Report gotten from the pause screen.");
                    continue;
                }

                if (jobDefToCurrentlyReservedTracks.TryGetValue(key, out var trackReservations))
                {
                    for (var j = 0; j < trackReservations.Count; j++)
                    {
                        var reservation = trackReservations[j];
                        var reservedTrack = reservation.track;
                        var reservedLength = reservation.reservedLength;

                        if (YardTracksOrganizer.Instance.GetFreeSpaceOnTrack(reservedTrack) >= reservedLength)
                        {
                            YardTracksOrganizer.Instance.ReserveSpace(reservedTrack, reservedLength, false);
                        }
                        else
                        {
                            // not enough space to reserve; find a different track with enough space & update job data
                            var replacementTrack = GetReplacementTrack(reservedTrack, reservedLength, random);
                            if (replacementTrack == null)
                            {
                                Debug.LogWarning($"[PersistentJobs] Can't find track with enough free space for Job[{key.job.ID}]. Skipping track reservation!");
                                continue;
                            }

                            YardTracksOrganizer.Instance.ReserveSpace(replacementTrack, reservedLength, false);

                            // update reservation data
                            trackReservations.RemoveAt(j);
                            trackReservations.Insert(j, new TrackReservation(replacementTrack, reservedLength));

                            // update static job definition data
                            try
                            {
                                if (key is StaticEmptyHaulJobDefinition emptyHaulDef)
                                {
                                    emptyHaulDef.destinationTrack = replacementTrack;
                                }
                                else if (key is StaticShuntingLoadJobDefinition loadDef)
                                {
                                    loadDef.destinationTrack = replacementTrack;
                                }
                                else if (key is StaticTransportJobDefinition transportDef)
                                {
                                    transportDef.destinationTrack = replacementTrack;
                                }
                                else if (key is StaticShuntingUnloadJobDefinition unloadDef)
                                {
                                    if (unloadDef.carsPerDestinationTrack != null)
                                    {
                                        unloadDef.carsPerDestinationTrack = unloadDef.carsPerDestinationTrack
                                            .Select(cpt => cpt != null && cpt.track == reservedTrack ?
                                                new CarsPerTrack(replacementTrack, cpt.cars) : cpt)
                                            .ToList();
                                    }
                                    else
                                    {
                                        Debug.LogError($"[PersistentJobs] carsPerDestinationTrack for ShuntingUnloadJobDefinition Job[{key.job.ID}] is null!");
                                    }
                                }
                                else
                                {
                                    // attempt to replace track via Traverse for unknown job types
                                    var replacedDestination = false;
                                    try
                                    {
                                        var destinationTrackField = Traverse.Create(key).Field("destinationTrack");
                                        var carsPerDestinationTrackField = Traverse.Create(key).Field("carsPerDestinationTrack");
                                        if (destinationTrackField.FieldExists())
                                        {
                                            destinationTrackField.SetValue(replacementTrack);
                                            replacedDestination = true;
                                        }
                                        else if (carsPerDestinationTrackField.FieldExists())
                                        {
                                            var existingList = carsPerDestinationTrackField.GetValue<List<CarsPerTrack>>();
                                            if (existingList != null)
                                            {
                                                carsPerDestinationTrackField.SetValue(
                                                    existingList
                                                        .Select(cpt => cpt != null && cpt.track == reservedTrack ?
                                                            new CarsPerTrack(replacementTrack, cpt.cars) : cpt)
                                                        .ToList());
                                                replacedDestination = true;
                                            }
                                            else
                                            {
                                                Debug.LogError($"[PersistentJobs] carsPerDestinationTrack field is null for Job[{key.job.ID}]!");
                                            }
                                        }
                                    }
                                    catch (Exception e)
                                    {
                                        Debug.LogError($"[PersistentJobs] Exception while trying to update tracks via Traverse for Job[{key.job.ID}]: {e}");
                                    }
                                    if (!replacedDestination)
                                    {
                                        Debug.LogError($"[PersistentJobs] Unaccounted for JobType[{key.job.jobType}] encountered while reserving track space for Job[{key.job.ID}].");
                                    }
                                }

                                // update task data
                                if (key.job.tasks != null)
                                {
                                    foreach (var task in key.job.tasks)
                                    {
                                        if (task != null)
                                        {
                                            TaskUtilities.TaskDoDfs(task, t => {
                                                if (t is TransportTask transportTask && transportTask.destinationTrack == reservedTrack)
                                                {
                                                    transportTask.destinationTrack = replacementTrack;
                                                }
                                            });
                                        }
                                    }
                                }
                                else
                                {
                                    Debug.LogError($"[PersistentJobs] tasks for Job[{key.job.ID}] is null!");
                                }

                                didAnyTrackChange = true;
                            }
                            catch (Exception e)
                            {
                                Debug.LogError($"[PersistentJobs] Exception while updating job data for Job[{key.job.ID}]: {e}");
                            }
                        }
                    }
                }
                else
                {
                    Debug.LogError(
                        $"[PersistentJobs] No reservation data for {"jobChain"}[{i}] found!" +
                        $" Reservation data can be empty, but it needs to be in {"jobDefToCurrentlyReservedTracks"}.",
                        jobChain[i]);
                }
            }

            return didAnyTrackChange;
        }
        private static Track GetReplacementTrack(Track oldTrack, float trainLength, Random random) {
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
                targetTrack = GetTrackThatHasEnoughFreeSpace(yto, trackGroup, trainLength, random);
            }

            if (targetTrack == null) {
                Debug.LogWarning($"[PersistentJobs] Cant't find any track to replace Track[{oldTrack.ID}] in Station[{stationController.logicStation.ID}]. Skipping reservation!");
            }

            return targetTrack;
        }

        private static Track GetTrackThatHasEnoughFreeSpace(YardTracksOrganizer yto, List<Track> tracks, float requiredLength, Random rng) {
            Main._modEntry.Logger.Log("getting random track with free space");
            var tracksWithFreeSpace = yto.FilterOutTracksWithoutRequiredFreeSpace(tracks, requiredLength);
            Main._modEntry.Logger.Log($"{tracksWithFreeSpace.Count}/{tracks.Count} tracks have at least {requiredLength}m available");
            if (tracksWithFreeSpace.Count > 0) {
                return rng.GetRandomElement(tracksWithFreeSpace);
            }

            Debug.LogWarning($"[PersistentJobsMod] None of the queried tracks have {requiredLength:F1}m of free space: {string.Join(", ", tracks.Select(t => $"{t.ID} ({yto.GetFreeSpaceOnTrack(t):F1}m)"))}");
            return null;
        }

    }
}