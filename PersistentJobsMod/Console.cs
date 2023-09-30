using System.Collections.Generic;
using System.Linq;
using CommandTerminal;
using DV.Logic.Job;
using DV.ThingTypes;
using HarmonyLib;
using PersistentJobsMod.HarmonyPatches.JobGeneration;
using PersistentJobsMod.Persistence;
using UnityEngine;
using Random = System.Random;

namespace PersistentJobsMod {
    public static class Console {
        [RegisterCommand("PJ.ClearStationSpawnFlag", Help = "PersistentJobsMod: Clear the flag for a station such that it may spawn cars again.", MinArgCount = 1, MaxArgCount = 1)]
        public static void ClearStationSpawnFlag(CommandArg[] args) {
            var stationId = args[0].String;

            if (StationIdCarSpawningPersistence.Instance.GetHasStationSpawnedCarsFlag(stationId)) {
                StationIdCarSpawningPersistence.Instance.SetHasStationSpawnedCarsFlag(stationId, false);
                Debug.Log($"Cleared station spawn flag of {stationId}.");
            } else {
                Debug.Log("Station spawn flag was not set. See PJ.ListStationSpawnFlag for alist of currently set flags.");
            }
        }

        [RegisterCommand("PJ.ListStationSpawnFlag", Help = "PersistentJobsMod: List stations that have already and will not spawn cars again.", MinArgCount = 0, MaxArgCount = 0)]
        public static void ListStationSpawnFlag(CommandArg[] args) {

            var stationIds = StationIdCarSpawningPersistence.Instance.GetAllSetStationSpawnedCarFlags();
            if (!stationIds.Any()) {
                Debug.Log("The list of station spawn flags is empty.");
            } else {
                Debug.Log(string.Join(", ", stationIds));
            }
        }

        [RegisterCommand("PJ.RegenerateJobsImmediately", Help = "PersistentJobsMod: Regenerate jobs immediately for train cars that are registered to be deleted by vanilla.", MinArgCount = 0, MaxArgCount = 0)]
        public static void RegenerateJobsImmediately(CommandArg[] args) {
            UnusedTrainCarDeleter_Patches.ReassignRegularTrainCarsAndDeleteNonPlayerSpawnedCars(UnusedTrainCarDeleter.Instance, Traverse.Create(UnusedTrainCarDeleter.Instance).Field("unusedTrainCarsMarkedForDelete").GetValue<List<TrainCar>>(), true);
            UnusedTrainCarDeleter.Instance.InstantConditionalDeleteOfUnusedCars();
        }

        [RegisterCommand("PJ.RegenerateJobsForConsistOfCar", Help = "PersistentJobsMod: Regenerate jobs for the consist of a specific car immediately. To identify the car, use the ID on the car plate.", MinArgCount = 1, MaxArgCount = 1)]
        public static void RegenerateJobsForConsistOfCar(CommandArg[] args) {
            var trainCarID = args[0].String;
            var trainCar = CarSpawner.Instance.AllCars.FirstOrDefault(tc => tc.ID == trainCarID);
            if (trainCar == null) {
                Debug.Log($"Could not find train car with ID {trainCarID}");
                return;
            }

            var reassignedTrainCars = UnusedTrainCarDeleter_Patches.ReassignJoblessRegularTrainCarsToJobs(new[] { trainCar.trainset }, new Random());

            if (reassignedTrainCars.Any()) {
                var unusedTrainCarsMarkedForDelete = Traverse.Create(UnusedTrainCarDeleter.Instance).Field("unusedTrainCarsMarkedForDelete").GetValue<List<TrainCar>>();
                foreach (var reassignedTrainCar in reassignedTrainCars) {
                    // a reassigned train car may or may not be registered for deletion already. the Remove method does not fail if the train car is not in the list, though, so we can safely call it anyway.
                    unusedTrainCarsMarkedForDelete.Remove(reassignedTrainCar);
                }
                Debug.Log($"Assigned {string.Join(", ", reassignedTrainCars.Select(tc => tc.ID))} to new job(s)");
            } else {
                Debug.Log($"None of {string.Join(", ", trainCar.trainset.cars.Select(tc => tc.ID))} could be assigned to new jobs");
            }
        }

        [RegisterCommand("PJ.ListCarsRegisteredForDeletion", Help = "PersistentJobsMod: Lists cars that are registered for deletion. Those cars are candidates for being assigned to new jobs.", MinArgCount = 0, MaxArgCount = 0)]
        public static void ListCarsRegisteredForDeletion(CommandArg[] args) {
            var unusedTrainCarsMarkedForDelete = Traverse.Create(UnusedTrainCarDeleter.Instance).Field("unusedTrainCarsMarkedForDelete").GetValue<List<TrainCar>>();
            Debug.Log(string.Join(", ", unusedTrainCarsMarkedForDelete.Select(tc => tc.ID)));
        }

        [RegisterCommand("PJ.ExpireAllAvailableJobs", Help = "PersistentJobsMod: Expire all available (not accepted) jobs such that the cars of those jobs will be jobless. Use the station ID as argument to restrict it to jobs in that station.", MinArgCount = 0, MaxArgCount = 1)]
        public static void ExpireAllJobs(CommandArg[] args) {
            if (args.Length == 0) {
                var jobs = Traverse.Create(JobsManager.Instance).Field("allJobs").GetValue<List<Job>>().ToList();
                foreach (var job in jobs) {
                    if (job.State == JobState.Available) {
                        job.ExpireJob();
                    }
                }
            } else {
                var stationID = args[0].String;
                var stationController = StationController.allStations.FirstOrDefault(s => s.logicStation.ID == stationID);
                if (stationController == null) {
                    Debug.Log("Could not find station with that ID");
                }

                var jobChainControllers = Traverse.Create(stationController).Field("jobChainControllers").GetValue<List<JobChainController>>().ToList();
                foreach (var jobChainController in jobChainControllers) {
                    var currentJob = jobChainController.currentJobInChain;
                    if (currentJob.State == JobState.Available) {
                        currentJob.ExpireJob();
                    }
                }
            }
        }
    }
}