using DV.Logic.Job;
using DV.Printers;
using DV.ThingTypes;
using HarmonyLib;
using MessageBox;
using PersistentJobsMod.Extensions;
using PersistentJobsMod.ModInteraction;
using PersistentJobsMod.Optimization;
using PersistentJobsMod.Utilities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Jobs;
using UnityEngine;
using UnityModManagerNet;
using Random = System.Random;

namespace PersistentJobsMod.HarmonyPatches.JobValidators {
    /// <summary>expires a job if none of its cars are in range of the starting station on job start attempt</summary>
    [HarmonyPatch(typeof(JobValidator), nameof(JobValidator.ProcessJobOverview))]
    public static class JobValidator_ProcessJobOverview_Patch {
        public static bool Prefix(JobValidator __instance, PrinterController ___bookletPrinter,
            JobOverview jobOverview) {
            try {
                if (!Main._modEntry.Active || !MultiplayerShim.IsHost) return true;

                if (___bookletPrinter.IsOnCooldown)
                {
                    ___bookletPrinter.PlayErrorSound();
                    return false;
                }

                var job = jobOverview.job;
                var allStations = StationController.allStations;
                var stationController = allStations.FirstOrDefault(st => st.logicStation.availableJobs.Contains(job));

                if (___bookletPrinter.IsOnCooldown || job.State != JobState.Available || stationController == null) return true;

                var jobChainController = stationController.ProceduralJobsController.GetCurrentJobChains().FirstOrDefault(jcc => jcc.currentJobInChain == job);
                if (FarCarOpt.SuspendedCarGUIDToJobChainController.ContainsValue(jobChainController ??= new JobChainController(new()))) //the new is just a fallthrough case instead of null
                {
                    Debug.LogWarning("[PersistentJobsMod] The cars for the job are still suspended!");
                    ___bookletPrinter.IsOnCooldown = true;
                    var carGuids = FarCarOpt.SuspendedCarGUIDToJobChainController.Where(kvp => kvp.Value == jobChainController).Select(kvp => kvp.Key).ToArray();
                    _ = FarCarOpt.RunResumeCars(carGuids, "job validating", (success) => { ___bookletPrinter.IsOnCooldown = false; if (success) __instance.ProcessJobOverview(jobOverview); else { Debug.LogWarning($"[PersistentJobsMod] {job.ID} couldn't be abandoned"); __instance.StartCoroutine(HandleJobAcceptanceFailure(___bookletPrinter, false)); } });
                    return false;
                }

                //let the mod handle it on its own
                if (Main.yardMasterPresent) return true;

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

                // reserve space for job and for shunting (un)load jobs, require cars to not already be on the warehouse track
                if (!ReserveSpacePJ(job, out bool shuntingJobOnWarehouseTrack))
                {
                    __instance.StartCoroutine(HandleJobAcceptanceFailure(___bookletPrinter, shuntingJobOnWarehouseTrack));
                    return false;
                }                

            } catch (Exception e) {
                Main.HandleUnhandledException(e, nameof(JobValidator_ProcessJobOverview_Patch) + "." + nameof(Prefix));
            }
            return true;
        }

        public static bool ReserveSpacePJ(Job job, out bool shuntingJobOnWarehouseTrack)
        {
            shuntingJobOnWarehouseTrack = false;
            try
            {

                // for shunting (un)load jobs, require cars to not already be on the warehouse track
                if (!CanShuntingJobBeAccepted(job))
                {
                    shuntingJobOnWarehouseTrack = true;
                    return false;
                }

                // reserve space for this job
                var stationJobControllers = UnityEngine.Object.FindObjectsOfType<StationProceduralJobsController>();

                var jobChainController = stationJobControllers.SelectMany(sjc => sjc.GetCurrentJobChains()).FirstOrDefault(jcc => jcc.currentJobInChain == job);

                if (jobChainController == null)
                {
                    Debug.LogWarning($"[PersistentJobsMod] could not find JobChainController for Job[{job.ID}]");
                }
                else if (job.jobType == JobType.ShuntingLoad)
                {
                    // shunting load jobs don't need to reserve space
                    // their destination track task will be changed to the warehouse track
                    ReplaceShuntingLoadDestination(job);
                    PersistentJobsModInteractionFeatures.InvokeJobTrackChanged(job);
                    Main._modEntry.Logger.Log($"skipping track reservation for Job[{job.ID}] because it's a shunting load job");
                }
                else
                {
                    var didAnyTrackChange = ReserveOrReplaceRequiredTracks(jobChainController);
                    if (didAnyTrackChange)
                    {
                        PersistentJobsModInteractionFeatures.InvokeJobTrackChanged(job);
                    }
                }
            }
            catch (Exception e)
            {
                Main.HandleUnhandledException(e, nameof(JobValidator_ProcessJobOverview_Patch) + "." + nameof(ReserveSpacePJ));
                return false;
            }
            return true;
        }

        public static bool CanShuntingJobBeAccepted(Job job)
        {
            if (Main.Settings.PreventStartingShuntingJobForCarsOnWarehouseTrack) {
                if (job.jobType == JobType.ShuntingLoad || job.jobType == JobType.ShuntingUnload) 
                {
                    var wt = job.tasks.Aggregate(
                        null as Task,
                        (found, outerTask) => found ?? TaskUtilities.TaskFindDfs(outerTask, innerTask => innerTask is WarehouseTask)) as WarehouseTask;
                    var wm = wt?.warehouseMachine;

                    if (wm != null && job.tasks.Any(outerTask => TaskUtilities.TaskAnyDfs(outerTask, innerTask => IsAnyTaskCarOnTrack(innerTask, wm.WarehouseTrack)))) return false;
                }
            }
            return true;
        }

        private static void ReplaceShuntingLoadDestination(Job job) {
            Main._modEntry.Logger.Log("attempting to replace destination track with warehouse track...");
            if (job.tasks[0] is not SequentialTasks sequence)
            {
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
            var wm = wt?.warehouseMachine;

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

            // When changing destination track for SL job, update also StaticJobDefinition so the change persist through save/load
            var jobChainController = UnityEngine.Object.FindObjectsOfType<StationProceduralJobsController>()
                .SelectMany(sjc => sjc.GetCurrentJobChains())
                .FirstOrDefault(jcc => jcc.currentJobInChain == job);

            if (jobChainController != null) {
                var staticJobDef = jobChainController.jobChain.FirstOrDefault(jd => jd.job == job);
                if (staticJobDef != null) {
                    if (staticJobDef is StaticShuntingLoadJobDefinition shuntingLoadJobDef) {
                        shuntingLoadJobDef.destinationTrack = wm.WarehouseTrack;
                    } else {
                        Traverse.Create(staticJobDef).Field("destinationTrack").SetValue(wm.WarehouseTrack);
                    }
                }
            }

            Main._modEntry.Logger.Log("    done!");
        }

        private static bool AreTaskCarsInRange(Task task, StationJobGenerationRange stationRange) {
            // this is rather hackish - we use a traverse to access the Task.cars field, but actually that field is only defined on TransportTask and WarehouseTask
            var cars = Traverse.Create(task).Field("cars").GetValue<List<Car>>();
            var carInRangeOfStation = cars.FirstOrDefault(c => (c.TrainCar().transform.position - stationRange.stationCenterAnchor.position).sqrMagnitude <= Main._initialDistanceRegular);
            return carInRangeOfStation != null;
        }

        private static bool IsAnyTaskCarOnTrack(Task task, Track track) {
            // this is rather hackish - we use a traverse to access the Task.cars field, but actually that field is only defined on TransportTask and WarehouseTask
            var cars = Traverse.Create(task).Field("cars").GetValue<List<Car>>();
            return cars.Any(car => car.FrontBogieTrack == track);
        }

        private static bool ReserveOrReplaceRequiredTracks(JobChainController jobChainController) {
            var jobChain = jobChainController.jobChain;

            bool didAnyTrackChange = false;

            var random = new Random();

            for (var i = 0; i < jobChain.Count; i++) {
                var staticJobDefinition = jobChain[i];
                if (staticJobDefinition == null) {
                    var message = $"[PersistentJobsMod] The jobChain of a JobChainController contains a null value at index {i}. List of jobs in the jobChain: {string.Join(", ", jobChain.Select(sjd => sjd?.job?.ID ?? "null"))}";
                    Debug.LogError(message);
                    throw new InvalidOperationException(message);
                }

                var job = staticJobDefinition.job;

                if (job == null) {
                    var message = $"[PersistentJobsMod] The job of a StaticJobDefinition ({staticJobDefinition.GetType()}) at index {i} in the jobChain of a JobChainController is null. List of jobs in the jobChain: {string.Join(", ", jobChain.Select(sjd => sjd?.job?.ID ?? "null"))}";
                    Debug.LogWarning(message);
                } else if (Main.PaxJobsPresent && ((job.jobType == PaxJobsCompat._PassengerExpress) || (job.jobType == PaxJobsCompat._PassengerLocal))) {
                    var message = $"[PersistentJobsMod] Skipping track reservations as {job.ID} is a passenger job";
                    Debug.Log(message);
                } else {
                    if (jobChainController.jobDefToCurrentlyReservedTracks[staticJobDefinition].Count > 0)
                    {
                        Debug.Log($"[PersistentJobsMod] Space already reserved for {job.ID} from a previous attempt. Skipping.");
                        continue;
                    }
                    List<TrackReservation> trackReservations = staticJobDefinition.GetRequiredTrackReservations();
                    if (trackReservations.Any()) {
                        for (var j = 0; j < trackReservations.Count; j++) {
                            var intendedDestinationTrack = trackReservations[j].track;
                            var lengthToBeReserved = trackReservations[j].reservedLength;
                            if (YardTracksOrganizer.Instance.GetFreeSpaceOnTrack(intendedDestinationTrack) >= lengthToBeReserved) {
                                jobChainController.jobDefToCurrentlyReservedTracks[staticJobDefinition].Add(trackReservations[j]);
                                YardTracksOrganizer.Instance.ReserveSpace(intendedDestinationTrack, lengthToBeReserved, false);
                            } else {
                                if (Main.Settings.DestinationTrackChange || (job.jobType == JobType.ShuntingUnload)) { 
                                    // not enough space to reserve; find a different track with enough space & update job data
                                    var replacementTrack = GetReplacementTrack(intendedDestinationTrack, lengthToBeReserved, random);
                                    if (replacementTrack == null) {
                                        Debug.LogWarning($"[PersistentJobsMod] Can't find track with enough free space for Job[{job.ID}]. Skipping track reservation!");
                                    } else {
                                        jobChainController.jobDefToCurrentlyReservedTracks[staticJobDefinition].Add(new (replacementTrack, lengthToBeReserved));
                                        YardTracksOrganizer.Instance.ReserveSpace(replacementTrack, lengthToBeReserved, false);

                                        // Query the cars from original shunting unload Job information to be used as additional information for subsequent track change
                                        var intendedCarsOrNull = (staticJobDefinition as StaticShuntingUnloadJobDefinition)?.carsPerDestinationTrack[j].cars;

                                        ReplaceDestinationTrackInStaticJobDefinition(staticJobDefinition, intendedDestinationTrack, replacementTrack, intendedCarsOrNull);

                                        // update reservation data
                                        trackReservations[j] = new TrackReservation(replacementTrack, lengthToBeReserved);

                                        didAnyTrackChange = true;
                                    }
                                }
                            }
                        }
                    } else {                        
                        Debug.LogWarning($"[PersistentJobsMod] No reservation data for jobChain[{i}] gotten! Job either doesn't reserve anything, or something has broken");
                    }
                }
            }

            return didAnyTrackChange;
        }

        private static void ReplaceDestinationTrackInStaticJobDefinition(StaticJobDefinition staticJobDefinition, Track intendedDestinationTrack, Track replacementTrack, IReadOnlyList<Car> relatedCarsOrNull) {
            // update static job definition data
            if (staticJobDefinition is StaticEmptyHaulJobDefinition emptyHaulJobDefinition) {
                emptyHaulJobDefinition.destinationTrack = replacementTrack;
            } else if (staticJobDefinition is StaticShuntingLoadJobDefinition shuntingLoadJobDefinition) {
                shuntingLoadJobDefinition.destinationTrack = replacementTrack;
            } else if (staticJobDefinition is StaticTransportJobDefinition transportJobDefinition) {
                transportJobDefinition.destinationTrack = replacementTrack;
            } else if (staticJobDefinition is StaticShuntingUnloadJobDefinition shuntingUnloadJobDefinition) {
                // We change the track information if and only if both track ID and car ID fit,
                // as the set of the cars should always remain the same even if the destination track changes
                shuntingUnloadJobDefinition.carsPerDestinationTrack = shuntingUnloadJobDefinition.carsPerDestinationTrack.Select(cpt => DoTrackAndCarsMatch(cpt.track, cpt.cars, intendedDestinationTrack, relatedCarsOrNull) ? new CarsPerTrack(replacementTrack, cpt.cars) : cpt).ToList();
            } else {
                // attempt to replace track via Traverse for unknown job types
                var replacedDestination = false;
                try {
                    var destinationTrackField = Traverse.Create(staticJobDefinition).Field("destinationTrack");
                    var carsPerDestinationTrackField = Traverse.Create(staticJobDefinition).Field("carsPerDestinationTrack");
                    if (destinationTrackField.FieldExists()) {
                        destinationTrackField.SetValue(replacementTrack);
                        replacedDestination = true;
                    } else if (carsPerDestinationTrackField.FieldExists()) {
                        carsPerDestinationTrackField.SetValue(
                            carsPerDestinationTrackField.GetValue<List<CarsPerTrack>>()
                                .Select(cpt => cpt.track == intendedDestinationTrack ? new CarsPerTrack(replacementTrack, cpt.cars) : cpt)
                                .ToList());
                        replacedDestination = true;
                    }
                } catch (Exception e) {
                    Debug.LogError(e);
                }
                if (!replacedDestination) {
                    Debug.LogError($"[PersistentJobsMod] Unaccounted for JobType[{staticJobDefinition.job.jobType}] encountered while reserving track space for Job[{staticJobDefinition.job.ID}].");
                }
            }

            // update task data
            foreach (var task in staticJobDefinition.job.tasks) {
                TaskUtilities.TaskDoDfs(
                task, t => {
                    if (t is TransportTask transportTask) {
                        // Extract corresponding car information to be used as the identification in conjunction with the intended track
                        var carData = transportTask.GetTaskData().cars;
                        // We only change the destination track if the set of cars is identical to our previous modifications
                        if (DoTrackAndCarsMatch(transportTask.destinationTrack, carData, intendedDestinationTrack, relatedCarsOrNull)) {
                            transportTask.destinationTrack = replacementTrack;
                        }
                    }
                });
            }
        }

        private static bool DoTrackAndCarsMatch(Track track, List<Car> cars, Track intendedDestinationTrack, IReadOnlyList<Car> intendedCarsOrNull) {
            if (track != intendedDestinationTrack) {
                return false;
            }

            return intendedCarsOrNull == null || cars.SequenceEqual(intendedCarsOrNull);
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
                Debug.LogError($"[PersistentJobsMod] Cant't find track group for Track[{oldTrack.ID}] in Station[{stationController.logicStation.ID}]. Skipping reservation!");
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
                Debug.LogWarning($"[PersistentJobsMod] Cant't find any track to replace Track[{oldTrack.ID}] in Station[{stationController.logicStation.ID}]. Skipping reservation!");
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

        public static IEnumerator HandleJobAcceptanceFailure(PrinterController printerController, bool shuntingJobOnWarehouseTrack) 
        {
            printerController.PlayErrorSound();
            if (shuntingJobOnWarehouseTrack) {
                yield return WaitFor.SecondsRealtime(0.4f);
                printerController.PlayErrorSound();

                if (!Main.Settings.PreventStartingShuntingJobForCarsOnWarehouseTrackMessageWasShown) {
                    Main.Settings.PreventStartingShuntingJobForCarsOnWarehouseTrackMessageWasShown = true;
                    yield return WaitFor.SecondsRealtime(1);

                    PopupAPI.ShowOk($"This shunting job cannot be accepted because some of the cars are already on the loading track - and that's why the job validator just beeped twice at you.{Environment.NewLine}You can disable this in the mod settings if you like.{Environment.NewLine}{Environment.NewLine}This message is only shown once.", "PersistentJobs");
                }
            }
        }
    }
}