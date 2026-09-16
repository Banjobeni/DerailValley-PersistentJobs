using DV.JObjectExtstensions;
using DV.Logic.Job;
using DV.ServicePenalty;
using DV.ThingTypes;
using DV.Utils;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using PersistentJobsMod.Extensions;
using PersistentJobsMod.ModInteraction;
using PersistentJobsMod.Utilities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;

namespace PersistentJobsMod.Optimization
{
    public static class FarCarOpt
    {
        public static RailTrack[] AllTracks;
        private static int SuspendIteration;

        public static TrainCar CurrentTrainCarToSuspend;
        public static string CurrentCarIDToResume;
        public static bool SuspendCoroRunning;
        public static bool ResumeCoroRunning;
        public static Stopwatch ResumeStopwatch;

        private static (Coroutine, List<StationController>) SuspendCoroutine;
        private static (Coroutine, IEnumerable<string>, string, Action<bool>) ResumeCoroutine;

        private static readonly Queue<(List<StationController>, List<TrainCar>)> PendingSuspends = new();
        private static readonly Queue<(IEnumerable<string>, string, Action<bool>)> PendingResumes = new();

        //key is carGUID (not trainCar ID!), value is the save format for a trainCar
        public static readonly Dictionary<string, JObject> SuspendedCarObjects = [];
        public static readonly Dictionary<string, string> SuspendedCarIDToCarGUID = [];
        public static readonly Dictionary<string, string> SuspendedCarGUIDToCarID = [];
        public static readonly Dictionary<string, JobChainController> SuspendedCarGUIDToJobChainController = [];
        public static readonly Dictionary<string, (DebtTrackerBase, CarDebtData)> SuspendedCarGUIDToDebtTracker = [];
        public static readonly Dictionary<string, List<string>> StationIDtoSuspendedCarGUID = [];

        public static readonly Dictionary<int, Bogie> OccupiedRailTrackIndexesToFakeBogies = [];

        public static readonly Dictionary<Track, float> TracksToSpaceOccupiedBySuspendedCars = [];
        public static readonly Dictionary<TrainCarType, float> TrainCarTypeToInterCouplerDistance = [];

        public static event Action<string> ResumeCompleted;
        public static event Action SuspendCompleted;

        private static Exception ContextException;

        public static bool SuspendCar(TrainCar trainCar, JObject carObj = null)
        {
            CurrentTrainCarToSuspend = null;
            bool returnBool = false;
            string carGUID = string.Empty;
            try
            {
                if (trainCar is null) return returnBool;
                if (!trainCar.isEligibleForSleep) UnityEngine.Debug.LogWarning($"[PersistentJobsMod] Car {trainCar.ID} not eligible for sleep");
                if (trainCar.logicCar is null) return returnBool;
                var st = Stopwatch.StartNew();

                if (AllTracks == null || AllTracks.Length == 0) AllTracks = SingletonBehaviour<RailTrackRegistryBase>.Instance.OrderedRailtracks;
                var allTracks = AllTracks;

                PlayerSpawnedCarUtilities.ConvertPlayerSpawnedTrainCar(trainCar);

                CurrentTrainCarToSuspend = trainCar;
                Car logicCar = trainCar.logicCar;
                carGUID = logicCar.carGuid;
                string carID = logicCar.ID;

                if (SuspendedCarObjects.ContainsKey(carGUID))
                {
                    UnityEngine.Debug.LogError($"[PersistentJobsMod] Car with GUID {carGUID} already suspended!");
                    return returnBool;
                }

                CarsSaveManager.SetBrakesOnSpawn(trainCar);

                carObj ??= CarsSaveManager.GetCarSaveData(trainCar, allTracks);

                int bog1TrackChildInd = carObj.GetInt("bog1TrackChildInd") ?? -1;
                int bog2TrackChildInd = carObj.GetInt("bog2TrackChildInd") ?? -1;
                if (bog1TrackChildInd == -1 || bog2TrackChildInd == -1)
                {
                    UnityEngine.Debug.LogError($"[PersistentJobsMod] Car with GUID {carGUID} has invalid track {trainCar.logicCar.CurrentTrack.ID} saved!");
                    return returnBool;
                }
                if (!OccupiedRailTrackIndexesToFakeBogies.ContainsKey(bog1TrackChildInd)) OccupiedRailTrackIndexesToFakeBogies.Add(bog1TrackChildInd, null);
                if (!OccupiedRailTrackIndexesToFakeBogies.ContainsKey(bog2TrackChildInd)) OccupiedRailTrackIndexesToFakeBogies.Add(bog2TrackChildInd, null);
                SignalOccupation.OccupyTrack(carID, AllTracks[bog1TrackChildInd]);
                SignalOccupation.OccupyTrack(carID, AllTracks[bog2TrackChildInd]);

                var carJccOrNull = CarTrackAssignment.GetControllerOfCarOrNull(logicCar);
                var yardID = (CarTrackAssignment.FindNearestNamedTrackOrNull([trainCar]))?.ID.yardId;
                DebtTrackerBase tracker = null;
                CarDebtData frozenCarDebtData = null;

                var debtController = trainCar.GetComponent<CarDebtController>();
                if (debtController != null)
                {
                    tracker = debtController.CarDebtTracker;
                    if (tracker != null)
                    {
                        tracker.UpdateDebtValues();
                        frozenCarDebtData = new(tracker.GetDebtData());
                    }
                }
                trainCar.UpdateJobIdOnCarPlates(string.Empty);

                SuspendedCarObjects.Add(carGUID, carObj);
                SuspendedCarIDToCarGUID.Add(carID, carGUID);
                SuspendedCarGUIDToCarID.Add(carGUID, carID);
                SuspendedCarGUIDToJobChainController.Add(carGUID, carJccOrNull);
                SuspendedCarGUIDToDebtTracker.Add(carGUID, (tracker, CarDebtData.LoadCarDebtFromSaveData(frozenCarDebtData.GetCarDebtSaveData())));

                var tct = trainCar.carType;
                if (!TrainCarTypeToInterCouplerDistance.ContainsKey(tct)) TrainCarTypeToInterCouplerDistance[tct] = trainCar.InterCouplerDistance;

                if (StationIDtoSuspendedCarGUID.TryGetValue(yardID ?? "#Y", out var carGuids)) carGuids.Add(carGUID);
                else StationIDtoSuspendedCarGUID.Add(yardID ?? "#Y", [carGUID]);

                var carLength = CarSpawner.Instance.GetTotalTrainCarsLength([logicCar], true);
                var logicTrack = logicCar.CurrentTrack;
                if (logicTrack != null)
                {
                    if (TracksToSpaceOccupiedBySuspendedCars.ContainsKey(logicTrack)) TracksToSpaceOccupiedBySuspendedCars[logicTrack] += carLength;
                    else TracksToSpaceOccupiedBySuspendedCars.Add(logicTrack, carLength);
                }

                SingletonBehaviour<IdGenerator>.Instance.carGuidToCar.Remove(carGUID);
                SingletonBehaviour<CarSpawner>.Instance.DeleteCar(trainCar);
                SingletonBehaviour<UnusedTrainCarDeleter>.Instance.ClearInvalidCarReferencesAfterManualDelete();

                st.Stop();
                Main._modEntry.Logger.Log($"Suspended trainCar {carID} (carGUID: {carGUID}) in {st.Elapsed}");
                returnBool = true;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[PersistentJobsMod] Problem when suspending trainCar {trainCar.ID} (carGUID: {carGUID})");
                UnityEngine.Debug.LogException(ex);
                returnBool = false;

                if (SingletonBehaviour<IdGenerator>.Instance.carGuidToCar.ContainsKey(carGUID))
                {
                    Main._modEntry.Logger.Log("Exception thrown before deleting train car, removing residual suspend data from dicts");
                    RemoveFromDicts(carGUID);
                }
                else
                {
                    UnityEngine.Debug.LogError("[PersistentJobsMod] Exception thrown after or while deleting train car, this really should not have happened, inject the car data to the save manually to recover it.");
                    UnityEngine.Debug.Log(carObj);
                    Traverse.Create(ex).Property("Message").SetValue(ex.Message.Insert(0, "Exception thrown after or while deleting train car, this really should not have happened! \n"));
                    throw new AdditionalInformationException($"Exception thrown after or while deleting car {trainCar.ID} with guid {trainCar.CarGUID}", ex);
                }
            }
            finally
            {
                CurrentTrainCarToSuspend = null;
            }

            return returnBool;
        }

        public static bool ResumeCar(string carGUID, out JObject carObject)
        {
            carObject = null;
            CurrentCarIDToResume = null;
            try
            {
                if (carGUID == null) return false;
                SuspendedCarObjects.TryGetValue(carGUID, out var carObj);
                if (carObj is null) return false;
                Stopwatch st = Stopwatch.StartNew();

                if (AllTracks == null || AllTracks.Length == 0) AllTracks = SingletonBehaviour<RailTrackRegistryBase>.Instance.OrderedRailtracks;
                var allTracks = AllTracks;

                int bog1TrackChildInd = carObj.GetInt("bog1TrackChildInd").Value;
                int bog2TrackChildInd = carObj.GetInt("bog2TrackChildInd").Value;
                if (bog1TrackChildInd == -1 || bog2TrackChildInd == -1)
                {
                    UnityEngine.Debug.LogError($"[PersistentJobsMod] Car with GUID {carGUID} has invalid track saved!");
                    return false;
                }

                string oldCarID = SuspendedCarGUIDToCarID[carGUID];
                CurrentCarIDToResume = oldCarID;
                TrainCar trainCar = CarsSaveManager.InstantiateCarFromSavegame(carObj, allTracks);
                if (trainCar is null)
                {
                    UnityEngine.Debug.LogError($"[PersistentJobsMod] Car {oldCarID} (with GUID {carGUID} is already present in the world and thus won't be resumed. This might be due to something in the resume process breaking or something messed with car IDs. The game is ok to continue running, though you should be vigilant (and potentially report this if it happens again). ");
                    RemoveFromDicts(carGUID);
                    return true;
                }

                Car logicCar = trainCar.logicCar;
                string newCarGUID = logicCar.carGuid;
                if (!(oldCarID == logicCar.ID && carGUID == newCarGUID)) throw new Exception("Restored trainCar does not match");
                IdGenerator.Instance.carGuidToCar[carGUID] = logicCar;

                RemoveFakeBogies(bog1TrackChildInd);
                RemoveFakeBogies(bog2TrackChildInd);
                SignalOccupation.FreeTrack(logicCar.ID, AllTracks[bog1TrackChildInd]);
                SignalOccupation.FreeTrack(logicCar.ID, AllTracks[bog2TrackChildInd]);

                CarsSaveManager.SetBrakesOnSpawn(trainCar);
                carObject = carObj;
                //CarsSaveManager.RestoreCarConnections(carObject);

                var carLength = CarSpawner.Instance.GetTotalTrainCarsLength([logicCar], true);
                var logicTrack = logicCar.CurrentTrack;
                if (logicTrack != null && TracksToSpaceOccupiedBySuspendedCars.ContainsKey(logicTrack))
                {
                    TracksToSpaceOccupiedBySuspendedCars[logicTrack] -= carLength;
                    if (TracksToSpaceOccupiedBySuspendedCars[logicTrack] < 0.1f) TracksToSpaceOccupiedBySuspendedCars.Remove(logicTrack);
                }

                SuspendedCarGUIDToJobChainController.TryGetValue(carGUID, out var jcc);

                if (jcc is not null)
                {
                    ReplaceCarInJcc(jcc, oldCarID, logicCar);
                    trainCar.UpdateJobIdOnCarPlates(jcc.currentJobInChain.ID);
                }
                else trainCar.UpdateJobIdOnCarPlates(string.Empty);

                var newDebtController = trainCar.GetComponent<CarDebtController>();
                if (newDebtController != null && SuspendedCarGUIDToDebtTracker.TryGetValue(carGUID, out var tuple))
                {
                    newDebtController.ignoreCarDamageDebt = false;
                    var (oldTracker, frozenCarDebtData) = tuple;
                    var newTracker = newDebtController.CarDebtTracker;
                    if (newTracker != null && oldTracker != null)
                    {
                        TransferDebtValues(oldTracker, newTracker, frozenCarDebtData);
                        ReplaceTrackerInSystems(oldTracker, newTracker);
                    }
                }

                RemoveFromDicts(carGUID);
                CurrentCarIDToResume = null;

                Main.Pause = false;
                st.Stop();
                Main._modEntry.Logger.Log($"Resumed trainCar {logicCar.ID} (carGUID: {newCarGUID}) in {st.Elapsed}");
                return true;
            }
            catch (Exception ex)
            {
                Main._modEntry.Logger.LogException($"Problem when resuming trainCar {carGUID}", ex);
                ContextException = ex;
                return false;
            }
            finally
            {
                CurrentCarIDToResume = null;
            }
        }

        private static void RemoveFromDicts(string carGUID)
        {
            SuspendedCarObjects.TryRemove(carGUID, out var _);
            SuspendedCarIDToCarGUID.TryRemove(SuspendedCarGUIDToCarID[carGUID], out var _);
            SuspendedCarGUIDToCarID.TryRemove(carGUID, out var _);
            SuspendedCarGUIDToJobChainController.TryRemove(carGUID, out var _);
            SuspendedCarGUIDToDebtTracker.TryRemove(carGUID, out var _);
            foreach (var cars in StationIDtoSuspendedCarGUID.Values) if (cars.Remove(carGUID)) break;
        }

        private static void ReplaceCarInJcc(JobChainController jcc, string oldCarID, Car newLogicCar)
        {
            var oldCar = jcc.carsForJobChain.First(c => c.ID == oldCarID);
            jcc.carsForJobChain.Replace(oldCar, newLogicCar);

            foreach (var sjd in jcc.jobChain)
            {
                Job job = sjd.job;

                if (job != null)
                {
                    var jobToCarsDict = SingletonBehaviour<JobsManager>.Instance.jobToJobCars;
                    if (jobToCarsDict.TryGetValue(job, out var cars)) jobToCarsDict[job] = (cars?.Replace(oldCar, newLogicCar).ToHashSet());

                    //if (job.tasks[0] is not SequentialTasks sequence) continue;
                    TaskUtilities.TaskDoLeafDfs(job.tasks[0], task =>
                    {
                        var cars = Traverse.Create(task).Field("cars").GetValue<IList<Car>>();
                        if (cars == null) return;
                        if (cars.Replace(oldCar, newLogicCar) != -1) PersistentJobsModInteractionFeatures.InvokeJobCarsChanged(job, newLogicCar);
                    });
                }
                else
                {
                    UnityEngine.Debug.LogWarning($"[PersistentJobsMod] JobChainController of car {oldCarID} has a {sjd.GetType().Name} with a null job with apparent id {Traverse.Create(sjd)?.Field("forcedJobId")?.GetValue<string>()} at index {jcc.jobChain.IndexOf(sjd)}, this shouldn't happen!");
                }

                switch (sjd)
                {
                    case StaticEmptyHaulJobDefinition ehjd:
                        ehjd.carsToTransport.Replace(oldCar, newLogicCar);
                        break;

                    case StaticTransportJobDefinition tjd:
                        tjd.carsToTransport.Replace(oldCar, newLogicCar);
                        break;

                    case StaticShuntingLoadJobDefinition sljd:
                        sljd.carsPerStartingTrack.ForEach(cpt => cpt.cars.Replace(oldCar, newLogicCar));
                        sljd.loadData.ForEach(ld => ld.cars.Replace(oldCar, newLogicCar));
                        break;

                    case StaticShuntingUnloadJobDefinition sujd:
                        sujd.carsPerDestinationTrack.ForEach(cpt => cpt.cars.Replace(oldCar, newLogicCar));
                        sujd.unloadData.ForEach(ld => ld.cars.Replace(oldCar, newLogicCar));
                        break;

                    default:
                        if (Main.PaxJobsPresent)
                        {
                            if (PaxJobsCompat.IsPaxJobDefinition(sjd, out var phjd))
                            {
                                var jobCars = PaxJobsCompat.GetCarsFromPaxJobDef(phjd);
                                jobCars.Replace(oldCar, newLogicCar);
                                PaxJobsCompat.SetCarsInPaxJobDef(phjd, jobCars);
                                break;
                            }
                        }
                        Main._modEntry.Logger.Warning("Unknown StaticJobDefinition type encountered, won't be updated!");
                        break;
                }
            }
        }

        private static void TransferDebtValues(DebtTrackerBase oldTracker, DebtTrackerBase newTracker, CarDebtData frozenCarDebtData)
        {
            CarDebtData oldData = oldTracker.GetDebtData();
            CarDebtData newData = newTracker.GetDebtData();

            if (oldData == null || newData == null || frozenCarDebtData == null) return;

            //var oldCarDebtSer = oldData.GetCarDebtSaveData();
            //var frozenCarDebtSer = frozenCarDebtData.GetCarDebtSaveData();
            //bool changed = !(oldCarDebtSer.ToString() == frozenCarDebtSer.ToString());

            //Main._modEntry.Logger.Log($"old d: ({((oldTracker as SimulatedCarDebtTracker) != null ? "simTracker" : "carTracker")}) \n{oldCarDebtSer} \nfrozen d: {frozenCarDebtSer}");
            //Main._modEntry.Logger.Log($"equal: {!changed}");

            var oldComponents = oldData.GetTrackedDebts();
            var newComponents = newData.GetTrackedDebts();

            if (oldComponents != null && newComponents != null)
            {
                foreach (var oldComp in oldComponents)
                {
                    var oldType = oldComp.Type;

                    foreach (var newComp in newComponents)
                    {
                        if ((newComp.Type).Equals(oldType))
                        {
                            newComp.type = oldComp.type;
                            newComp.startValue = oldComp.startValue;
                            newComp.snapshotValue = oldComp.snapshotValue;
                            newComp.endValue = oldComp.endValue;
                            break;
                        }
                    }
                }
                //Main._modEntry.Logger.Log($"After replace d: ({((oldTracker as SimulatedCarDebtTracker) != null ? "simTracker" : "carTracker")}) \n{newTracker.GetDebtData().GetCarDebtSaveData()}");
            }
        }

        private static void ReplaceTrackerInSystems(DebtTrackerBase oldTracker, DebtTrackerBase newTracker)
        {
            var trackedJobs = JobDebtController.Instance.existingTrackedJobs;
            if (trackedJobs != null)
            {
                foreach (var existingJobDebt in trackedJobs)
                {
                    var carsDebtTrackers = existingJobDebt.jobDebtTracker.carsDebtTrackers;
                    if (carsDebtTrackers != null)
                    {
                        for (int i = 0; i < carsDebtTrackers.Length; i++)
                        {
                            if (carsDebtTrackers.GetValue(i) == oldTracker) carsDebtTrackers.SetValue(newTracker, i);
                        }
                    }
                }
            }

            var joblessTrackers = Traverse.Create(JobDebtController.Instance.existingJoblessCarDebts).Field("joblessCarsTrackers").GetValue<System.Collections.IList>();
            if (joblessTrackers != null)
            {
                for (int i = 0; i < joblessTrackers.Count; i++)
                {
                    if (joblessTrackers[i] == oldTracker)
                    {
                        joblessTrackers[i] = newTracker;
                    }
                }
            }

            var trackedLocos = LocoDebtController.Instance.trackedLocosDebts;
            if (trackedLocos != null)
            {
                foreach (var locoDebt in trackedLocos) if (locoDebt.locoDebtTracker == oldTracker) Traverse.Create(locoDebt).Field("locoDebtTracker").SetValue(newTracker as LocoDebtTrackerBase);
            }

            var ownedCarStates = OwnedCarsStateController.Instance.existingOwnedCarStates;
            if (ownedCarStates != null)
            {
                foreach (var ownedCarDebt in ownedCarStates) if (ownedCarDebt.carDebtTrackerBase == oldTracker) Traverse.Create(ownedCarDebt).Field("carDebtTrackerBase").SetValue(newTracker);
            }
        }

        private static void RemoveFakeBogies(int railTrackIndex)
        {
            if (Main.Settings.DummyBogiesForTracksOfSuspendedCars && OccupiedRailTrackIndexesToFakeBogies.TryGetValue(railTrackIndex, out var fakeBogie))
            {
                var rt = AllTracks[railTrackIndex];
                if (rt == null) return;
                if (rt.GetComponent<RailTrackBogiesOnTrack>()?.bogiesOnTrack.Remove(fakeBogie) is true)
                {
                    Main._modEntry.Logger.Log($"Removed fake bogie from {rt} on car resumption");
                    OccupiedRailTrackIndexesToFakeBogies.Remove(railTrackIndex);
                    return;
                }
                else
                {
                    Main._modEntry.Logger.Error($"Something failed when removing fake bogies from {AllTracks[railTrackIndex]}");
                }
            }
        }

        private static IEnumerator SuspendCars(List<TrainCar> trainCars)
        {
            if (trainCars is null || !trainCars.Any()) yield break;
            if (!WorldStreamingInit.IsLoaded) yield break;

            var st = Stopwatch.StartNew();
            var fst = Stopwatch.StartNew();

            if (AllTracks == null || AllTracks.Length == 0) AllTracks = SingletonBehaviour<RailTrackRegistryBase>.Instance.OrderedRailtracks;
            //var viableTrainCars = (trainCars.Where(tc => !(tc is null || tc.uniqueCar || tc.IsLoco || tc.IsCaboose || tc.preventDelete || tc.logicCar is null))).ToList();
            var viableTrainCars = (trainCars.Where(tc => !(tc is null || tc.uniqueCar || CarTypes.IsAnyLocoSlugTender(tc.carLivery) || tc.IsCaboose || tc.derailed))).ToList();
            var trainCarObjects = viableTrainCars.Select(tc => CarsSaveManager.GetCarSaveData(tc, AllTracks)).ToList();
            var diff = trainCars.Except(viableTrainCars).ToList();
            if (diff.Any()) Main._modEntry.Logger.Warning($"cars excepted from suspend {string.Join(", ", diff)}");

            for (int i = 0; i < viableTrainCars.Count; i++)
            {
                if (!SuspendCar(viableTrainCars[i], trainCarObjects[i])) UnityEngine.Debug.LogError($"[PersistentJobsMod] Error suspending {viableTrainCars[i].name} index: {i}");

                if (fst.ElapsedMilliseconds > 12)
                {
                    Main._modEntry.Logger.Log($"time ran out after {viableTrainCars[i].name} index: {i}");
                    yield return null;
                    fst.Restart();
                }
            }

            SingletonBehaviour<UnusedTrainCarDeleter>.Instance.ClearInvalidCarReferencesAfterManualDelete();
            st.Stop();
            Main._modEntry.Logger.Log($"Suspending {viableTrainCars.Count} cars took {st.Elapsed}");
            yield break;
        }

        public static bool RunSuspendCars(bool immediately = false, List<StationController> where = null, List<TrainCar> optCars = null)
        {
            if (!MultiplayerShim.IsHost) return false;
            SuspendIteration++;
            if (!immediately && (SuspendIteration % 2 > 0)) return false;
            if (!Main.Settings.SuspendFarAwayCars) return false;

            if (AllTracks == null || AllTracks.Length == 0 || AllTracks.Any(rt => rt is null)) AllTracks = SingletonBehaviour<RailTrackRegistryBase>.Instance.OrderedRailtracks;
            where ??= StationController.allStations.Where(sc => sc.stationRange.IsPlayerOutOfJobDestroyZone(sc.stationRange.PlayerSqrDistanceFromStationCenter * 2, true)).ToList();
            if (where.Any(sc => sc?.gameObject == null)) return false;

            if (SuspendCoroRunning && SuspendCoroutine.Item1 != null)
            {
                if (where.All(sc => SuspendCoroutine.Item2.Contains(sc))) return false;
                Main._modEntry.Logger.Error($"SuspendCarsCoro already running on {string.Join(", ", SuspendCoroutine.Item2.Select(sc => sc.stationInfo.YardID))}, new Coro on {string.Join(", ", where.Select(sc => sc.stationInfo.YardID))} enqueued");
                PendingSuspends.Enqueue((where, optCars));
                return false;
            }

            if (ResumeCoroRunning)
            {
                Main._modEntry.Logger.Error("ResumeCoro is running now, suspension will be enqueued");
                PendingSuspends.Enqueue((where, optCars));
                return false;
            }

            Main.Pause = true;
            SuspendCoroRunning = true;
            SuspendCoroutine = (SingletonBehaviour<CoroutineManager>.Instance.Run(new ExceptionCatchingCoroutineIterator(SuspendCarsCoro(where, optCars?.ToHashSet()), nameof(FarCarOpt) + "." + nameof(SuspendCarsCoro), new StackTrace(true))), where);

            return true;
        }

        private static IEnumerator<(string NextStageName, object Result)> SuspendCarsCoro(List<StationController> viableSCs, HashSet<TrainCar> cars = null)
        {
            Main._modEntry.Logger.Log(nameof(SuspendCarsCoro));
            while (!WorldStreamingInit.IsLoaded) yield return ("waiting for WorldStreamingInit.IsLoaded", null);

            bool waitForResumeToFinish = false;
            void OnResumeCompleted(string id)
            {
                Main._modEntry.Logger.Log($"Won't suspend cars in {id} as they just got resumed");
                viableSCs.RemoveAll(sc => sc.logicStation.ID == id);
                ResumeCompleted -= OnResumeCompleted;
                waitForResumeToFinish = false;
            }

            ResumeCompleted += OnResumeCompleted;

            if (ResumeCoroRunning)
            {
                waitForResumeToFinish = true;
                Main._modEntry.Logger.Error("ResumeCoro is running now somehow");
            }
            else
            {
                ResumeCompleted -= OnResumeCompleted;
            }

            //while (ResumeCoroRunning) yield return ("waiting for trainCar resuming to finish", null);
            yield return ("waiting for car resume", new WaitUntil(() => !waitForResumeToFinish));
            yield return ("safety wait", WaitFor.SecondsRealtime(0.5f));

            var st = Stopwatch.StartNew();
            var fst = Stopwatch.StartNew();
            cars ??= [];
            try
            {
                if (cars.Any()) yield return ("actually suspending cars", SuspendCars([.. cars.ToList()]));
                cars.Clear();
                yield return ("done - resetting", null);

                foreach (var sc in viableSCs)
                {
                    var scst = Stopwatch.StartNew();
                    Main._modEntry.Logger.Log("Suspending cars in " + sc.stationInfo.YardID);
                    var stationTracks = sc.ExtractLogicTracks(sc.AllStationTracks);
                    if (Main.PaxJobsPresent && PaxJobsCompat.IsPassengerStation(sc.stationInfo.YardID)) stationTracks.AddRange(PaxJobsCompat.AllPaxTracksForStationData(sc.stationInfo.YardID));

                    foreach (var track in stationTracks)
                    {
                        cars.UnionWith(GetTrainCarsToSuspendOnTrack(track));

                        if (fst.ElapsedMilliseconds > 2)
                        {
                            yield return ("frame time elapsed", null);
                            fst.Restart();
                        }
                    }

                    scst.Stop();
                    Main._modEntry.Logger.Log($"Gather took {scst.Elapsed}");

                    if (fst.ElapsedMilliseconds > 5)
                    {
                        yield return ("frame time elapsed", null);
                        fst.Restart();
                    }

                    if (cars.Any()) yield return ("actually suspending cars", SuspendCars([.. cars.ToList()]));
                    cars.Clear();
                    yield return ("done - resetting", null);
                }
            }
            finally
            {
                SuspendCoroRunning = false;
                SuspendCompleted?.Invoke();
                Main.Pause = false;
                st.Stop();
                Main._modEntry.Logger.Log($"SuspendCarsCoro took {st.Elapsed} to run");

                if (PendingSuspends.Count > 0)
                {
                    var next = PendingSuspends.Dequeue();
                    RunSuspendCars(true, next.Item1, next.Item2);
                }
            }

            yield break;
        }

        private static IEnumerable<TrainCar> GetTrainCarsToSuspendOnTrack(Track track)
        {
            var result = new HashSet<TrainCar>();
            IEnumerable<Car> allCars = (track.GetCarsFullyOnTrack() ?? Enumerable.Empty<Car>()).Concat(track.GetCarsPartiallyOnTrack() ?? Enumerable.Empty<Car>());

            foreach (var car in allCars)
            {
                var trainCar = car.TrainCar();
                if (trainCar == null || result.Contains(trainCar)) continue;

                var trainset = trainCar.trainset?.cars;
                if (trainset == null || trainset.Count == 0) continue;

                if (IsTrainsetValid(trainset)) result.UnionWith(trainset);
            }
            return result;
        }

        private static bool IsTrainsetValid(IEnumerable<TrainCar> trainset)
        {
            foreach (var trainCar in trainset) if (trainCar == null || trainCar.uniqueCar || CarTypes.IsAnyLocoSlugTender(trainCar.carLivery) || trainCar.IsCaboose || trainCar.preventDelete || trainCar.logicCar?.ID == null || trainCar.derailed || !trainCar.isEligibleForSleep || trainCar.SimController != null) return false;
            return true;
        }

        public static bool ResumeCarsInStation(string stationID, Action<bool> OnComplete = null)
        {
            if (!MultiplayerShim.IsHost || stationID is null || stationID == string.Empty) return false;
            if (!WorldStreamingInit.IsLoaded) return false;

            stationID = stationID.Trim().ToUpper();

            UnityEngine.Debug.Log("[PersistentJobsMod] Resuming cars in " + stationID);

            StationIDtoSuspendedCarGUID.TryGetValue(stationID, out var carGUIDS);
            if (stationID is "ALL" or "*") carGUIDS = StationIDtoSuspendedCarGUID.Values.SelectMany(x => x).ToList();
            if (carGUIDS is null || !carGUIDS.Any()) return false;

            //SingletonBehaviour<CoroutineManager>.Instance.Run(CarsSaveManager.IgnoreTrainStressForLoadedCarsUntilCouplingIsSettled());
            if (!RunResumeCars(carGUIDS.ToList(), stationID, OnComplete))
            {
                UnityEngine.Debug.LogWarning($"[PersistentJobsMod] Car resuming in {stationID} failed");
                if (Debugger.IsAttached) Debugger.Break();
                return false;
            }
            return true;
        }

        public static bool RunResumeCars(IEnumerable<string> guids, string location, Action<bool> OnComplete = null)
        {
            if (!MultiplayerShim.IsHost) return false;

            if (ResumeCoroRunning)
            {
                if (ResumeCoroutine.Item3 != location || location.Length > 4) PendingResumes.Enqueue((guids, location, OnComplete));
                return true;
            }

            ResumeCoroRunning = true;
            Main.Pause = true;
            ResumeStopwatch = Stopwatch.StartNew();
            ResumeCoroutine = (SingletonBehaviour<CoroutineManager>.Instance.Run(new ExceptionCatchingCoroutineIterator(ResumeCarsCoro(guids, location, OnComplete), nameof(FarCarOpt) + "." + nameof(ResumeCarsCoro), new StackTrace(true))), guids, location, OnComplete);

            return true;
        }

        private static IEnumerator<(string NextStageName, object Result)> ResumeCarsCoro(IEnumerable<string> guids, string location, Action<bool> OnComplete = null)
        {
            List<JObject> successfulCars = [];
            if (guids is not null && guids.Any())
            {
                var fst = Stopwatch.StartNew();
                var allCars = CarSpawner.Instance.AllCars.Select(tc => tc.CarGUID).ToHashSet();
                TrainStress.globalIgnoreStressCalculation = true;
                try
                {
                    Main._modEntry.Logger.Log($"about to resume {guids.Count()} cars in {location}");
                    foreach (var guid in guids)
                    {
                        if (ResumeStopwatch.Elapsed.TotalMinutes > 8) throw new TimeoutException($"{nameof(ResumeCarsCoro)} has ran for too long!");

                        if (allCars.Contains(guid)) continue;

                        if (!ResumeCar(guid, out JObject carData))
                        {
                            TrainStress.globalIgnoreStressCalculation = false;
                            AdditionalInformationException ex = new("Failed to resume trainCar with guid " + guid, ContextException);
                            ContextException = null;
                            OnComplete?.Invoke(false);
                            throw ex;
                        }
                        else if (carData != null) successfulCars.Add(carData);

                        if (fst.ElapsedMilliseconds > 8)
                        {
                            Main._modEntry.Logger.Log($"time ran out after {guid} index {successfulCars.Count}, time: {fst.Elapsed}");
                            yield return ("frame time elapsed", null);
                            fst.Restart();
                        }
                    }

                    foreach (var carData in successfulCars) CarsSaveManager.RestoreCarConnections(carData);

                    UnityEngine.Debug.Log($"[PersistentJobsMod] Successfully resumed {successfulCars.Count} cars {(location.Length > 0 ? ("in " + location) : "")} in {ResumeStopwatch.Elapsed}");
                    ResumeStopwatch.Reset();
                    OnComplete?.Invoke(successfulCars.Count > 0);
                }
                finally
                {
                    TrainStress.globalIgnoreStressCalculation = false;
                    ResumeCoroRunning = false;
                    Main.Pause = false;
                    ResumeStopwatch.Stop();
                    ResumeCompleted?.Invoke(location);

                    if (PendingResumes.Count > 0)
                    {
                        var next = PendingResumes.Dequeue();
                        RunResumeCars(next.Item1, next.Item2, next.Item3);
                    }
                }
            }
            // call completed immediately even when nothing to resume
            else
            {
                ResumeCompleted?.Invoke(location);
                OnComplete?.Invoke(false);
            }

            yield break;
        }

        public static void ClearRecords()
        {
            SuspendedCarObjects.Clear();
            SuspendedCarIDToCarGUID.Clear();
            SuspendedCarGUIDToCarID.Clear();
            SuspendedCarGUIDToJobChainController.Clear();
            SuspendedCarGUIDToDebtTracker.Clear();
            StationIDtoSuspendedCarGUID.Clear();
            OccupiedRailTrackIndexesToFakeBogies.Clear();
            TracksToSpaceOccupiedBySuspendedCars.Clear();
            SuspendIteration = 0;
            AllTracks = null;
        }
    }
}