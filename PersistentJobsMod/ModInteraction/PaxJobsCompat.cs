using DV;
using DV.Logic.Job;
using DV.ThingTypes;
using HarmonyLib;
using PersistentJobsMod.Extensions;
using PersistentJobsMod.JobGenerators;
using PersistentJobsMod.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using static PersistentJobsMod.HarmonyPatches.JobGeneration.UnusedTrainCarDeleter_Patches;
using Random = System.Random;

namespace PersistentJobsMod.ModInteraction
{
    public static class PaxJobsCompat
    {
        private static Assembly asm;

        private static Type _RouteManager;
        private static Type _ConsistManager;
        private static Type _RouteTrack;
        private static Type _PassConsistInfo;
        private static Type _PassengerJobGenerator;
        private static Type _IPassDestination;
        private static Type _PassJobType;
        private static Type _PassengerChainController;
        private static Type _PassengerHaulJobDefinition;

        private static ConstructorInfo _RouteTrackCtor;
        private static ConstructorInfo _PassConsistInfoCtor;
        private static MethodInfo _TryGetInstance;
        private static MethodInfo _GenerateJob;
        private static MethodInfo _GetPassengerCars;
        private static MethodInfo _IsPassengerStation;
        private static MethodInfo _GetStationData;
        private static MethodInfo _GetRouteTrackById;
        private static MethodInfo _GetPlatforms;

        private static PropertyInfo _AllTracksProperty;
        private static PropertyInfo _RouteTrackLengthProp;
        private static PropertyInfo _TrainCarsToTransportProp;

        private static FieldInfo _RouteTrackTrackField;
        private static FieldInfo _StartingTrackField;

        private static Random _Random;

        private static JobType _PassengerExpress;
        private static JobType _PassengerLocal;

        public static bool Initialize()
        {
            try
            {
                asm = Main.PaxJobs.Assembly;

                _RouteManager = CompatAccess.Type("PassengerJobs.Generation.RouteManager");
                _ConsistManager = CompatAccess.Type("PassengerJobs.Generation.ConsistManager");
                _RouteTrack = CompatAccess.Type("PassengerJobs.Generation.RouteTrack");
                _PassConsistInfo = CompatAccess.Type("PassengerJobs.Generation.PassConsistInfo");
                _PassengerJobGenerator = CompatAccess.Type("PassengerJobs.Generation.PassengerJobGenerator");
                _IPassDestination = CompatAccess.Type("PassengerJobs.Generation.IPassDestination");
                _PassJobType = CompatAccess.Type("PassengerJobs.Generation.PassJobType");
                _PassengerChainController = CompatAccess.Type("PassengerJobs.Generation.PassengerChainController");
                _PassengerHaulJobDefinition = CompatAccess.Type("PassengerJobs.Generation.PassengerHaulJobDefinition");

                _TryGetInstance = CompatAccess.Method(_PassengerJobGenerator, "TryGetInstance");
                _GenerateJob = CompatAccess.Method(_PassengerJobGenerator, "GenerateJob", new[] { typeof(JobType), _PassConsistInfo });
                _GetPassengerCars = CompatAccess.Method(_ConsistManager, "GetPassengerCars");
                _IsPassengerStation = CompatAccess.Method(_RouteManager, "IsPassengerStation");
                _GetStationData = CompatAccess.Method(_RouteManager, "GetStationData");
                _GetRouteTrackById = CompatAccess.Method(_RouteManager, "GetRouteTrackById");
                _GetPlatforms = CompatAccess.Method(_IPassDestination, "GetPlatforms", new[] { typeof(bool) });

                _AllTracksProperty = CompatAccess.Property(_IPassDestination, "AllTracks");
                _RouteTrackLengthProp = CompatAccess.Property(_RouteTrack, "Length");
                _TrainCarsToTransportProp = CompatAccess.Property(_PassengerHaulJobDefinition, "TrainCarsToTransport");

                _RouteTrackTrackField = CompatAccess.Field(_RouteTrack, "Track");
                _StartingTrackField = CompatAccess.Field(_PassengerHaulJobDefinition, "StartingTrack");

                _RouteTrackCtor = CompatAccess.Ctor(_RouteTrack, new[] { _IPassDestination, typeof(Track) });
                _PassConsistInfoCtor = CompatAccess.Ctor(_PassConsistInfo, new[] { _RouteTrack, typeof(List<Car>) });

                _Random = new Random();

                _PassengerExpress = Traverse.Create(_PassJobType).Field("Express").GetValue<JobType>();
                _PassengerLocal = Traverse.Create(_PassJobType).Field("Local").GetValue<JobType>();
            }
            catch (Exception e)
            {
                Main._modEntry.Logger.LogException("Failed to initilize PaxJobsCompat when resolving types and methods", e);
                return false;
            }
            return true;
        }

        internal static class CompatAccess
        {
            public static Type Type(string fullName) => AccessTools.TypeByName(fullName) ?? throw new TypeLoadException($"Type not found: {fullName}");
            public static MethodInfo Method(Type type, string name, Type[] args = null) => (args == null ? AccessTools.Method(type, name) : AccessTools.Method(type, name, args)) ?? throw new MissingMethodException(type.FullName, name);
            public static ConstructorInfo Ctor(Type type, Type[] args) => AccessTools.Constructor(type, args) ?? throw new MissingMethodException(type.FullName, ".ctor");
            public static PropertyInfo Property(Type type, string name) => AccessTools.Property(type, name) ?? throw new MissingMemberException(type.FullName, name);
            public static FieldInfo Field(Type type, string name) => AccessTools.Field(type, name) ?? throw new MissingFieldException(type.FullName, name);
        }

        private static bool TryGetGenerator(string yardId, out object generator)
        {
            generator = null;
            var args = new object[] { yardId, null };
            if (!(bool)_TryGetInstance.Invoke(null, args))
            {
                Main._modEntry.Logger.Error($"Couldn´t get instance of PaxJobsGenerator for {yardId}");
                return false;
            }

            generator = args[1];
            return generator != null;
        }

        public static bool TryGenerateJob(string yardId, JobType jobType, object passConsistInfo, out JobChainController passengerChainController)
        {
            passengerChainController = null;
            if (!AStartGameData.carsAndJobsLoadingFinished) return false;
            Main._modEntry.Logger.Log($"[TryGenerateJob] Attempting to generate job of type {jobType} in {yardId}");

            if (!TryGetGenerator(yardId, out object generator))
            {
                Main._modEntry.Logger.Error($"PaxJobsGenerator for {yardId} was null, this shouldn´t happen!");
                return false;
            }

            passengerChainController = (JobChainController)_GenerateJob.Invoke(generator, new object[] { jobType, passConsistInfo });
            if (passengerChainController == null) Main._modEntry.Logger.Error("Couldn´t generate PaxJob - null from there");
            return passengerChainController != null;
        }

        private static object CreateRouteTrack(object IPassDestination, Track terminalTrack) => _RouteTrackCtor.Invoke(new object[] { IPassDestination, terminalTrack });

        private static object CreatePassConsistInfo(object routeTrack, List<Car> cars) => _PassConsistInfoCtor.Invoke(new object[] { routeTrack, cars });

        public static bool IsPaxCars(TrainCar car)
        {
            var carLiveries = (IEnumerable<TrainCarLivery>)_GetPassengerCars.Invoke(null, null);
            return carLiveries != null && car.carLivery != null && carLiveries.Contains(car.carLivery);
        }

        public static float GetConsistLength(List<TrainCar> trainCars)
        {
            return CarSpawner.Instance.GetTotalCarsLength(
                TrainCar.ExtractLogicCars(trainCars),
                true
            );
        }

        private static bool IsPassengerStation(string yardId) => (bool)(_IsPassengerStation?.Invoke(null, new object[] { yardId }));

        private static List<StationController> AllPaxStations() => StationController.allStations.Where(st => IsPassengerStation(st.stationInfo.YardID)).ToList();

        private static object GetStationData(string yardId) => _GetStationData?.Invoke(null, new object[] { yardId }); // output is IPassDestination : PassStationData

        private static object GetRouteTrackByIdOrNull(string trackFullDisplayId)
        {
            object routeTrackObj = _GetRouteTrackById?.Invoke(null, new object[] { trackFullDisplayId });
            if (routeTrackObj is null) return null;
            Track trackField = GetRouteTrackTrackField(routeTrackObj);
            if (trackField == null || trackField.RailTrack() == null || trackField.ID == null || trackField.ID.FullDisplayID == null) return null;
            return routeTrackObj;
        }

        private static List<Track> AllPaxTracksForStationData(string yardId) => ((IEnumerable<Track>)_AllTracksProperty.GetValue(GetStationData(yardId))).ToList();

        private static List<Car> GetTrainCarsForPaxJobDefinition(StaticJobDefinition staticPaxJobDefinition) => (List<Car>)_TrainCarsToTransportProp.GetValue(staticPaxJobDefinition);

        private static IEnumerable<object> GetPlatforms(object stationData, bool onlyTerminusTracks = false)
        {
            var result = _GetPlatforms.Invoke(stationData, new object[] { onlyTerminusTracks });
            if (result is System.Collections.IEnumerable enumerable) foreach (var item in enumerable) yield return item;
        }

        private static Track GetRouteTrackTrackField(object routeTrack)
        {
            if (routeTrack == null) return null;
            var track = (Track)_RouteTrackTrackField.GetValue(routeTrack);
            if (track == null || track.RailTrack() == null || track.ID == null || track.ID.FullDisplayID == null) return null;
            return track;
        }

        private static object GetPaxHaulJobDefStartingRouteTrackField(StaticJobDefinition staticPaxJobDefinition) => _StartingTrackField.GetValue(staticPaxJobDefinition);

        private static double GetRouteTrackLength(object routeTrack) => (double)_RouteTrackLengthProp.GetValue(routeTrack);

        private static bool CanFitInStation(object stationData, List<TrainCar> trainCars) => GetPlatforms(stationData).Any(rt => (float)GetConsistLength(trainCars) <= GetRouteTrackLength(rt));

        private static JobType PickPassengerJobType(int carCount)
        {
            if (carCount <= 4)
                return (JobType)_Random.Next(101, 103); // Express or Local

            return _PassengerExpress;
        }

        private static object SelectStartPlatformRouteTrack(Track currentTrack, List<object> fittingPlatforms)
        {
            if (!fittingPlatforms.Any()) return null;

            if (currentTrack != null)
            {
                string currentTrackId = currentTrack.ID.FullDisplayID;
                var matchingPlatform = fittingPlatforms.FirstOrDefault(rt =>
                {
                    var track = GetRouteTrackTrackField(rt);
                    return track != null && track.ID.FullDisplayID == currentTrackId;
                });

                if (matchingPlatform != null)
                {
                    Main._modEntry.Logger.Log($"Using current platform track {currentTrackId} as PaxJobs start platform");
                    return matchingPlatform;
                }
                Main._modEntry.Logger.Log($"Current track {currentTrackId} is not a PaxJobs platform, selecting random free platform");
            }
            else Main._modEntry.Logger.Log("Could not determine current track, selecting random free platform");

            return fittingPlatforms.OrderByDescending(p =>
            {
                var track = GetRouteTrackTrackField(p) ?? new Track(0);
                return track.length - track.OccupiedLength;
            }).FirstOrDefault();
        }

        private static List<JobChainController> TryGeneratePassengerJob(StationController station, List<TrainCar> trainCars, List<object> fittingPlatforms, JobType jobType)
        {
            List<JobChainController> result = new();
            if (trainCars.Count() > 20)
            {
                result.AddRange(HandleSplitOrFail(trainCars, station));
                return result;
            }

            Track currentTrack = CarTrackAssignment.FindNearestNamedTrackOrNull(trainCars);
            object preferredRouteTrack = SelectStartPlatformRouteTrack(currentTrack, fittingPlatforms);
            if (preferredRouteTrack == null || GetRouteTrackTrackField(preferredRouteTrack).ID == null)
            {
                Main._modEntry.Logger.Log($"No fitting platforms found for consist starting with {trainCars.First().ID}");
                result.AddRange(HandleSplitOrFail(trainCars, station));
                return result;
            }

            if (!fittingPlatforms.Contains(preferredRouteTrack))
            {
                Main._modEntry.Logger.Error("Selected RouteTrack is not in fitting platforms list");
                return result;
            }

            Main._modEntry.Logger.Log($"Picked platform {GetRouteTrackTrackField(preferredRouteTrack).ID.FullDisplayID} ");

            if (TryGenerateJob(station.stationInfo.YardID, jobType, CreatePassConsistInfo(preferredRouteTrack, TrainCar.ExtractLogicCars(trainCars)), out JobChainController passangerChainController))
            {
                Main._modEntry.Logger.Log($"Successfully reassigned pax consist starting with {trainCars.First().ID} to job {passangerChainController.currentJobInChain.ID}");
                result.Add(passangerChainController);
                return result;
            }

            result.AddRange(HandleSplitOrFail(trainCars, station));
            return result;
        }

        private static List<JobChainController> HandleSplitOrFail(List<TrainCar> trainCars, StationController station)
        {
            List<JobChainController> result = new();
            if (trainCars.Count < 2)
            {
                Main._modEntry.Logger.Error($"Single pax car {trainCars.First().ID} cannot be reassigned");
                return result;
            }

            var (emptyConsecutiveTrainCarGroups, loadedConsecutiveTrainCarGroups) = SortCGIntoEmptyAndLoaded(new List<IReadOnlyList<TrainCar>> { trainCars });
            if (emptyConsecutiveTrainCarGroups.Any())
            {
                foreach (var emptyTrainCars in emptyConsecutiveTrainCarGroups)
                {
                    Main._modEntry.Logger.Log($"Spilitting consist starting with car {emptyTrainCars.First().ID}");
                    var (first, second) = SplitInHalf((List<TrainCar>)emptyTrainCars);
                    HandleEmptyPaxCars(first, station, out List<JobChainController> outJobChainControllers);
                    result.AddRange(outJobChainControllers);
                    HandleEmptyPaxCars(second, station, out outJobChainControllers);
                    result.AddRange(outJobChainControllers);
                }
            }

            if (loadedConsecutiveTrainCarGroups.Any())
            {
                foreach (var loadedTrainCars in loadedConsecutiveTrainCarGroups)
                {
                    Main._modEntry.Logger.Log($"Spilitting consist starting with car {loadedTrainCars.First().ID}");
                    var (first, second) = SplitInHalf((List<TrainCar>)loadedTrainCars);
                    HandleLoadedPaxCars(first, station, out List<JobChainController> outJobChainControllers);
                    result.AddRange(outJobChainControllers);
                    HandleLoadedPaxCars(second, station, out outJobChainControllers);
                    result.AddRange(outJobChainControllers);
                }
            }

            return result;
        }

        private static (List<IReadOnlyList<TrainCar>>, List<IReadOnlyList<TrainCar>>) SortCGIntoEmptyAndLoaded(List<IReadOnlyList<TrainCar>> paxConsecutiveTrainCarGroups)
        {
            var statusTrainCarGroups = paxConsecutiveTrainCarGroups.SelectMany(cars => cars.GroupConsecutiveBy(tc => GetTrainCarReassignStatus(tc))).ToList();
            var emptyConsecutiveTrainCarGroups = statusTrainCarGroups.Where(s => s.Key == TrainCarReassignStatus.Empty).Select(s => s.Items).ToList();
            var loadedConsecutiveTrainCarGroups = statusTrainCarGroups.Where(s => s.Key == TrainCarReassignStatus.Loaded).Select(s => s.Items).ToList();

            Main._modEntry.Logger.Log($"Found {emptyConsecutiveTrainCarGroups.Count} empty pax train car groups with a total of {emptyConsecutiveTrainCarGroups.SelectMany(g => g).Count()} cars");
            Main._modEntry.Logger.Log($"Found {loadedConsecutiveTrainCarGroups.Count} loaded pax train car groups with a total of {loadedConsecutiveTrainCarGroups.SelectMany(g => g).Count()} cars");
            return (emptyConsecutiveTrainCarGroups, loadedConsecutiveTrainCarGroups);
        }

        public static List<JobChainController> DecideForPaxCarGroups(List<IReadOnlyList<TrainCar>> paxConsecutiveTrainCarGroups, StationController station)
        {
            List<JobChainController> result = new();
            Main._modEntry.Logger.Log($"Reassigning passanger cars to jobs in station {station.logicStation.ID}");

            EnsureTrainCarsAreConvertedToNonPlayerSpawned(FilterTrainCarGroups(paxConsecutiveTrainCarGroups).SelectMany(tcg => tcg).ToList());

            var (emptyConsecutiveTrainCarGroups, loadedConsecutiveTrainCarGroups) = SortCGIntoEmptyAndLoaded(paxConsecutiveTrainCarGroups);
            foreach (List<TrainCar> tcs in emptyConsecutiveTrainCarGroups.Cast<List<TrainCar>>())
            {
                HandleEmptyPaxCars(tcs, station, out List<JobChainController> jobChainControllers);
                result.AddRange(jobChainControllers);
            }
            foreach (List<TrainCar> tcs in loadedConsecutiveTrainCarGroups.Cast<List<TrainCar>>())
            {
                Main._modEntry.Logger.Log($"Loaded consist of {tcs.Count()} pax cars starting with {tcs.First().ID} is in station {station.stationInfo.Name}");
                //handling complicated - no inbuilt methods for job generation from already loaded cars

                HandleLoadedPaxCars(tcs, station, out List<JobChainController> jobChainControllers);
                result.AddRange(jobChainControllers);
            }

            //foreach (var jcc in result) if (_PassengerChainController.IsInstanceOfType(jcc)) FinalizeJobChainControllerAndGenerateFirstJob(jcc);
            return result;
        }

        public static (List<T> first, List<T> second) SplitInHalf<T>(IList<T> source)
        {
            if (source.Count() < 2) return (null, null);
            int mid = (source.Count + 1) / 2;
            var first = source.Take(mid).ToList();
            var second = source.Skip(mid).ToList();
            return (first, second);
        }

        public static List<IReadOnlyList<TrainCar>> FilterTrainCarGroups(List<IReadOnlyList<TrainCar>> trainCarGroups)
        {
            trainCarGroups.RemoveAll(trainCars =>
            {
                if (trainCars == null || trainCars.Count < 1)
                {
                    Main._modEntry.Logger.Error("[FilterTrainCarGroups] Invalid trainCars input thrown out");
                    return true;
                }
                var startingTrack = CarTrackAssignment.FindNearestNamedTrackOrNull(trainCars);
                return startingTrack == null;
            });
            return trainCarGroups;
        }

        private static Track GetRandomFittingPlatform(StationController targetStation, List<TrainCar> trainCars, bool onlyTerminusTracks = false)
        {
            var fittingPlatforms = GetFittingPlatformsForStation(targetStation, trainCars, onlyTerminusTracks);

            if (!fittingPlatforms.Any()) return null;

            var randomRouteTrack = fittingPlatforms.GetRandomElement();
            return GetRouteTrackTrackField(randomRouteTrack);
        }

        private static List<object> GetFittingPlatformsForStation(StationController station, List<TrainCar> trainCars, bool onlyTerminusTracks = false)
        {
            var stationData = GetStationData(station.stationInfo.YardID);
            return stationData == null ? new List<object>() : (GetPlatforms(stationData, onlyTerminusTracks).Where(rt => GetConsistLength(trainCars) <= GetRouteTrackLength(rt)).ToList());
        }

        private static float CalculateCrowDistanceBetweenThings(Vector3 thing1, Vector3 thing2) => (thing1 - thing2).sqrMagnitude;

        private static StationController FindDestinationStation(StationController origin, List<TrainCar> trainCars) => AllPaxStations().Distinct().Where(st => st != origin).Where(st => CanFitInStation(GetStationData(st.stationInfo.YardID), trainCars)).OrderBy(sc => CalculateCrowDistanceBetweenThings(trainCars.First().transform.position, sc.stationRange.transform.position)).FirstOrDefault();

        private static bool TryGetStationContext(List<TrainCar> trainCars, StationController station, out Track startingTrack)
        {
            startingTrack = CarTrackAssignment.FindNearestNamedTrackOrNull(trainCars);
            if (startingTrack == null)
            {
                Main._modEntry.Logger.Error("No starting track found");
                return false;
            }

            if (startingTrack.ID.yardId != station.stationInfo.YardID)
            {
                Main._modEntry.Logger.Error("Station mismatch");
                return false;
            }
            return true;
        }

        private static void HandleLoadedPaxCars(List<TrainCar> trainCars, StationController station, out List<JobChainController> jobChainControllers)
        {
            jobChainControllers = new();

            if (!TryGetStationContext(trainCars, station, out Track startingTrack)) return;

            if (IsPassengerStation(station.stationInfo.YardID))
            {
                var fittingPlatforms = GetFittingPlatformsForStation(station, trainCars);

                if (!fittingPlatforms.Any())
                {
                    jobChainControllers.AddRange(HandleSplitOrFail(trainCars, station));
                    return;
                }

                JobType jobType = PickPassengerJobType(trainCars.Count);
                //jobChainControllers.AddRange(TryGeneratePartialPassengerJob(station, trainCars, fittingPlatforms, jobType)); - not implemented now, need to skip first/second task (pax loading in start station), have to go deeper than PassangerJobGenerator.GenerateJob(...)
                return;
            }
            else
            {
                Main._modEntry.Logger.Log($"Loaded consist of {trainCars.Count()} pax cars starting with {trainCars.First().ID} on track {startingTrack.ID.FullID} needs to be transported to a pax jobs station to get unloaded");
                //generate FH job to random pax station: use already existing mod logic elswhere -- potentially chnge to use SP tracks which PaxJobs currently doesn´t - wait for update?
                StationController viableDestStation = FindDestinationStation(station, trainCars);
                if (viableDestStation != null)
                {
                    Track possibleDestinationTrack = GetRandomFittingPlatform(viableDestStation, trainCars, true);
                    JobChainController transportJobChainController = TransportJobGenerator.TryGenerateJobChainController(station, startingTrack, viableDestStation, trainCars, trainCars.Select(tc => tc.LoadedCargo).ToList(), _Random, false, possibleDestinationTrack);
                    if (transportJobChainController != null)
                    {
                        transportJobChainController.FinalizeSetupAndGenerateFirstJob();
                        jobChainControllers.Add(transportJobChainController);
                        return;
                    }
                }
                else
                {
                    Main._modEntry.Logger.Error($"Loaded consist of {trainCars.Count()} pax cars starting with {trainCars.First().ID} can´t be reassigned a LH to any pax station, attempting splitting");
                    jobChainControllers.AddRange(HandleSplitOrFail(trainCars, station));
                    return;
                }
            }

            Main._modEntry.Logger.Error("[HandleLoadedPaxCars] End of function reached possibly without reassigning, this shouldn´t happen!");
        }

        private static void HandleEmptyPaxCars(List<TrainCar> trainCars, StationController station, out List<JobChainController> jobChainControllers)
        {
            jobChainControllers = new();

            if (!TryGetStationContext(trainCars, station, out Track startingTrack)) return;

            if (IsPassengerStation(station.stationInfo.YardID))
            {
                var fittingPlatforms = GetFittingPlatformsForStation(station, trainCars);

                if (!fittingPlatforms.Any())
                {
                    jobChainControllers.AddRange(HandleSplitOrFail(trainCars, station));
                    return;
                }

                JobType jobType = PickPassengerJobType(trainCars.Count);
                if (station.stationInfo.YardID == "CS" && jobType == _PassengerExpress) jobType = _PassengerLocal; //we have to do this since City South doesn´t have any valid outgoing express routes 
                jobChainControllers.AddRange(TryGeneratePassengerJob(station, trainCars, fittingPlatforms, jobType));
                return;
            }
            else
            {
                Main._modEntry.Logger.Log($"Empty consist of {trainCars.Count()} pax cars starting with {trainCars.First().ID} on track {startingTrack.ID.FullID} needs to be transported to a pax jobs station to get reassigned a pax job");
                //generate LH job to random pax station: use already existing mod logic elswhere -- potentially chnge to use SP tracks which PaxJobs currently doesn´t - wait for update?
                StationController viableDestStation = FindDestinationStation(station, trainCars);
                if (viableDestStation != null)
                {
                    Track possibleDestinationTrack = GetRandomFittingPlatform(viableDestStation, trainCars);
                    JobChainController emptyHaulJobChainController = EmptyHaulJobGenerator.GenerateEmptyHaulJobWithExistingCarsOrNull(station, viableDestStation, startingTrack, trainCars, _Random, possibleDestinationTrack);
                    if (emptyHaulJobChainController != null)
                    {
                        emptyHaulJobChainController.FinalizeSetupAndGenerateFirstJob();
                        jobChainControllers.Add(emptyHaulJobChainController);
                        return;
                    }
                }
                else
                {
                    Main._modEntry.Logger.Error($"Empty consist of {trainCars.Count()} pax cars starting with {trainCars.First().ID} can´t be reassigned a LH to any pax station, attempting splitting");
                    jobChainControllers.AddRange(HandleSplitOrFail(trainCars, station));
                    return;
                }
            }

            Main._modEntry.Logger.Error("[HandleEmptyPaxCars] End of function reached possibly without reassigning, this shouldn´t happen!");
        }
    }
}