using DV;
using DV.Booklets;
using DV.Logic.Job;
using DV.ThingTypes;
using DV.RenderTextureSystem.BookletRender;
using HarmonyLib;
using PersistentJobsMod.Extensions;
using PersistentJobsMod.JobGenerators;
using PersistentJobsMod.Utilities;
using PersistentJobsMod.Persistence;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using static PersistentJobsMod.Utilities.ReflectionUtilities;
using static PersistentJobsMod.HarmonyPatches.JobGeneration.UnusedTrainCarDeleter_Patches;
using static PersistentJobsMod.ModInteraction.PaxJobsCompat.Tags;
using Random = System.Random;

#region RefType using
using ConsistManagerRef = PersistentJobsMod.Utilities.ReflectionUtilities.Foreign<PersistentJobsMod.ModInteraction.PaxJobsCompat.Tags.ConsistManager>;
using ExpressStationsChainDataRef = PersistentJobsMod.Utilities.ReflectionUtilities.Foreign<PersistentJobsMod.ModInteraction.PaxJobsCompat.Tags.ExpressStationsChainData>;
using IPassDestinationRef = PersistentJobsMod.Utilities.ReflectionUtilities.Foreign<PersistentJobsMod.ModInteraction.PaxJobsCompat.Tags.IPassDestination>;
using PassConsistInfoRef = PersistentJobsMod.Utilities.ReflectionUtilities.Foreign<PersistentJobsMod.ModInteraction.PaxJobsCompat.Tags.PassConsistInfo>;
using PassengerChainControllerRef = PersistentJobsMod.Utilities.ReflectionUtilities.Foreign<PersistentJobsMod.ModInteraction.PaxJobsCompat.Tags.PassengerChainController>;
using PassengerHaulJobDefinitionRef = PersistentJobsMod.Utilities.ReflectionUtilities.Foreign<PersistentJobsMod.ModInteraction.PaxJobsCompat.Tags.PassengerHaulJobDefinition>;
using PassengerJobGeneratorRef = PersistentJobsMod.Utilities.ReflectionUtilities.Foreign<PersistentJobsMod.ModInteraction.PaxJobsCompat.Tags.PassengerJobGenerator>;
using PassJobTypeRef = PersistentJobsMod.Utilities.ReflectionUtilities.Foreign<PersistentJobsMod.ModInteraction.PaxJobsCompat.Tags.PassJobType>;
using RouteManagerRef = PersistentJobsMod.Utilities.ReflectionUtilities.Foreign<PersistentJobsMod.ModInteraction.PaxJobsCompat.Tags.RouteManager>;
using RouteResultRef = PersistentJobsMod.Utilities.ReflectionUtilities.Foreign<PersistentJobsMod.ModInteraction.PaxJobsCompat.Tags.RouteResult>;
using RouteTrackRef = PersistentJobsMod.Utilities.ReflectionUtilities.Foreign<PersistentJobsMod.ModInteraction.PaxJobsCompat.Tags.RouteTrack>;
using RouteTypeRef = PersistentJobsMod.Utilities.ReflectionUtilities.Foreign<PersistentJobsMod.ModInteraction.PaxJobsCompat.Tags.RouteType>;
using PlatformControllerRef = PersistentJobsMod.Utilities.ReflectionUtilities.Foreign<PersistentJobsMod.ModInteraction.PaxJobsCompat.Tags.PlatformController>;
using PassengerJobDataRef = PersistentJobsMod.Utilities.ReflectionUtilities.Foreign<PersistentJobsMod.ModInteraction.PaxJobsCompat.Tags.PassengerJobData>;
#endregion

namespace PersistentJobsMod.ModInteraction
{
    public static class PaxJobsCompat
    {
        #region Reflection setup
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
        private static Type _RouteResult;
        private static Type _RouteType;
        private static Type _ExpressStationsChainData;
        private static Type _PlatformController;
        private static Type _BookletUtility;
        private static Type _BookletCreator_JobPatch;
        private static Type _PassengerJobData;
        private static Type _PassStopInfo;

        private static ConstructorInfo _RouteTrackCtor;
        private static ConstructorInfo _PassConsistInfoCtor;
        private static ConstructorInfo _PassengerJccCtor;
        private static ConstructorInfo _ExpressStChainDataCtor;

        private static MethodInfo _TryGetInstance;
        private static MethodInfo _GenerateJob;
        private static MethodInfo _GetPassengerCars;
        private static MethodInfo _IsPassengerStation;
        private static MethodInfo _GetStationData;
        private static MethodInfo _GetRouteTrackById;
        private static MethodInfo _GetPlatforms;
        private static MethodInfo _PHJD_GenerateJob;
        private static MethodInfo _GetRoute;
        private static MethodInfo _GetRouteType;
        private static MethodInfo _GetJobPaymentData;
        private static MethodInfo _GetTotalHaulDistance;
        private static MethodInfo _PopulateExpressJobExistingCars;
        private static MethodInfo _PaxJGeneratorStartGenerationAsync;
        private static MethodInfo _PHJD_CreateBoardingTask;
        private static MethodInfo _PHJD_CreateTransportTask;
        private static MethodInfo _PlatformControllerForTrack;
        private static MethodInfo _PlatformRegisterOutgoingJob;
        private static MethodInfo _PlatformRegisterIncomingJob;
        private static MethodInfo _ExtractPassengerJobData;
        private static MethodInfo _GetBookletTemplateData;
        private static MethodInfo _CreateLoadTaskPage;

        private static PropertyInfo _AllTracksProperty;
        private static PropertyInfo _RouteTrackLengthProp;
        private static PropertyInfo _TrainCarsToTransportProp;
        private static PropertyInfo _PaxStYardIdProperty;
        private static PropertyInfo _RTPlatformIdProp;

        private static FieldInfo _RouteTrackTrackField;
        private static FieldInfo _StartingTrackField;
        private static FieldInfo _DestinationTracksField;
        private static FieldInfo _TaskStartingTrackField;
        private static FieldInfo _RouteResultTracksField;
        private static FieldInfo _RouteTrackStationField;
        private static FieldInfo _PaxJGeneratorStContField;
        private static FieldInfo _PHJD_RouteTypeField;
        private static FieldInfo _StaticJobDefJobField;
        private static FieldInfo _InitialStopField;

        private static Random _Random;

        private static JobType _PassengerExpress;
        private static JobType _PassengerLocal;

        private static object _RouteTypeExpress;
        private static object _RouteTypeLocal;

        //private static UnityEngine.Object[] _AllPlatformControllers;

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
                _RouteResult = CompatAccess.Type("PassengerJobs.Generation.RouteResult");
                _RouteType = CompatAccess.Type("PassengerJobs.Generation.RouteType");
                _ExpressStationsChainData = CompatAccess.Type("PassengerJobs.Generation.ExpressStationsChainData");
                _PlatformController = CompatAccess.Type("PassengerJobs.Platforms.PlatformController");
                _BookletUtility = CompatAccess.Type("PassengerJobs.BookletUtility");
                _BookletCreator_JobPatch = CompatAccess.Type("PassengerJobs.Patches.BookletCreator_JobPatch");
                _PassengerJobData = CompatAccess.Type("PassengerJobs.PassengerJobData");
                _PassStopInfo = CompatAccess.Type("PassengerJobs.PassStopInfo");

                _TryGetInstance = CompatAccess.Method(_PassengerJobGenerator, "TryGetInstance");
                _GenerateJob = CompatAccess.Method(_PassengerJobGenerator, "GenerateJob", new[] { typeof(JobType), _PassConsistInfo });
                _GetPassengerCars = CompatAccess.Method(_ConsistManager, "GetPassengerCars");
                _IsPassengerStation = CompatAccess.Method(_RouteManager, "IsPassengerStation");
                _GetStationData = CompatAccess.Method(_RouteManager, "GetStationData");
                _GetRouteTrackById = CompatAccess.Method(_RouteManager, "GetRouteTrackById");
                _GetPlatforms = CompatAccess.Method(_IPassDestination, "GetPlatforms", new[] { typeof(bool) });
                _PHJD_GenerateJob = CompatAccess.Method(_PassengerHaulJobDefinition, "GenerateJob", new Type[] { typeof(Station), typeof(float), typeof(float), typeof(string), typeof(JobLicenses) });
                _GetRoute = CompatAccess.Method(_RouteManager, "GetRoute");
                _GetRouteType = CompatAccess.Method(_PassJobType, "GetRouteType");
                _GetJobPaymentData = CompatAccess.Method(_PassengerJobGenerator, "GetJobPaymentData", new[] { typeof(IEnumerable<TrainCarLivery>), typeof(bool) });
                _GetTotalHaulDistance = CompatAccess.Method(_PassengerJobGenerator, "GetTotalHaulDistance", new[] { typeof(StationController), CompatAccess.IEnumerableOf(_RouteTrack) });
                _PopulateExpressJobExistingCars = CompatAccess.Method(_PassengerJobGenerator, "PopulateExpressJobExistingCars", new[] { typeof(JobChainController), typeof(Station), _RouteTrack, _RouteResult, typeof(List<Car>), typeof(StationsChainData), typeof(float), typeof(float) });
                _PaxJGeneratorStartGenerationAsync = CompatAccess.Method(_PassengerJobGenerator, "StartGenerationAsync");
                _PHJD_CreateBoardingTask = CompatAccess.Method(_PassengerHaulJobDefinition, "CreateBoardingTask", new[] { _RouteTrack, typeof(float), typeof(bool), typeof(bool) });
                _PHJD_CreateTransportTask = CompatAccess.Method(_PassengerHaulJobDefinition, "CreateTransportLeg", new[] { typeof(Track), typeof(Track) });
                _PlatformControllerForTrack = CompatAccess.Method(_PlatformController, "GetControllerForTrack", new[] { typeof(string) });
                _PlatformRegisterOutgoingJob = CompatAccess.Method(_PlatformController, "RegisterOutgoingJob", new[] { typeof(Job), typeof(bool) });
                _PlatformRegisterIncomingJob = CompatAccess.Method(_PlatformController, "RegisterIncomingJob", new[] { typeof(Job), typeof(bool) });
                _ExtractPassengerJobData = CompatAccess.Method(_BookletUtility, "ExtractPassengerJobData", new[] { typeof(Job_data) });
                _GetBookletTemplateData = CompatAccess.Method(_BookletCreator_JobPatch, "GetBookletTemplateData", new[] { typeof(Job_data) });
                _CreateLoadTaskPage = CompatAccess.Method(_BookletUtility, "CreateLoadTaskPage", new[] { _PassengerJobData, _PassStopInfo, typeof(int), typeof(int), typeof(int) });

                _AllTracksProperty = CompatAccess.Property(_IPassDestination, "AllTracks");
                _RouteTrackLengthProp = CompatAccess.Property(_RouteTrack, "Length");
                _TrainCarsToTransportProp = CompatAccess.Property(_PassengerHaulJobDefinition, "TrainCarsToTransport");
                _PaxStYardIdProperty = CompatAccess.Property(_IPassDestination, "YardID");
                _RTPlatformIdProp = CompatAccess.Property(_RouteTrack, "PlatformID");

                _RouteTrackTrackField = CompatAccess.Field(_RouteTrack, "Track");
                _StartingTrackField = CompatAccess.Field(_PassengerHaulJobDefinition, "StartingTrack");
                _DestinationTracksField = CompatAccess.Field(_PassengerHaulJobDefinition, "DestinationTracks");
                _TaskStartingTrackField = CompatAccess.Field(typeof(TransportTask), "startingTrack");
                _RouteResultTracksField = CompatAccess.Field(_RouteResult, "Tracks");
                _RouteTrackStationField = CompatAccess.Field(_RouteTrack, "Station");
                _PaxJGeneratorStContField = CompatAccess.Field(_PassengerJobGenerator, "Controller");
                _PHJD_RouteTypeField = CompatAccess.Field(_PassengerHaulJobDefinition, "RouteType");
                _StaticJobDefJobField = CompatAccess.Field(typeof(StaticJobDefinition), "<job>k__BackingField");
                _InitialStopField = CompatAccess.Field(_PassengerJobData, "initialStop");

                _RouteTrackCtor = CompatAccess.Ctor(_RouteTrack, new[] { _IPassDestination, typeof(Track) });
                _PassConsistInfoCtor = CompatAccess.Ctor(_PassConsistInfo, new[] { _RouteTrack, typeof(List<Car>) });
                _PassengerJccCtor = CompatAccess.Ctor(_PassengerChainController, new[] { typeof(GameObject) });
                _ExpressStChainDataCtor = CompatAccess.Ctor(_ExpressStationsChainData, new[] { typeof(string), Type.GetType("System.String[]") });

                _Random = new Random();

                _PassengerExpress = Traverse.Create(_PassJobType).Field("Express").GetValue<JobType>();
                _PassengerLocal = Traverse.Create(_PassJobType).Field("Local").GetValue<JobType>();

                _RouteTypeExpress = Enum.Parse(_RouteType, "Express");
                _RouteTypeLocal = Enum.Parse(_RouteType, "Local");

                //_AllPlatformControllers ??= UnityEngine.Object.FindObjectsOfType(_PlatformController) ?? throw new KeyNotFoundException("No platform controllesr present, this shouldn´t happen");

                PatchPrefix(_PHJD_GenerateJob, typeof(PaxJobsCompat), nameof(GenerateJob_Prefix));
                PatchPostfix(_PHJD_GenerateJob, typeof(PaxJobsCompat), nameof(GenerateJob_Postfix));

                PatchPrefix(_PaxJGeneratorStartGenerationAsync, typeof(PaxJobsCompat), nameof(StartGenerationAsync_Prefix));

                PatchPrefix(_ExtractPassengerJobData, typeof(PaxJobsCompat), nameof(ExtractPassengerJobData_Prefix));

                PatchPostfix(_GetBookletTemplateData, typeof(PaxJobsCompat), nameof(GetBookletTemplateData_Postfix));

                PatchPrefix(_CreateLoadTaskPage, typeof(PaxJobsCompat), nameof(CreateLoadTaskPage_Prefix));

            }
            catch (Exception e)
            {
                Main._modEntry.Logger.LogException("Failed to initilize PaxJobsCompat when resolving types and methods", e);
                return false;
            }

            return true;
        }

        public class Tags
        {
            public sealed class RouteManager { };
            public sealed class ConsistManager { };
            public sealed class RouteTrack { };
            public sealed class PassConsistInfo { };
            public sealed class PassengerJobGenerator { };
            public sealed class IPassDestination { };
            public sealed class PassJobType { };
            public sealed class PassengerChainController { };
            public sealed class PassengerHaulJobDefinition { };
            public sealed class RouteResult { };
            public sealed class RouteType { };
            public sealed class ExpressStationsChainData { };
            public sealed class PlatformController { };
            public sealed class PassengerJobData { };
        }
        #endregion

        public static bool OverrideSpawnFlagForPaxJ = false;

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

        public static void PaxJobsOrigGenJobsInStation(string yardId)
        {
            if (!TryGetGenerator(yardId, out object generator))
            {
                Main._modEntry.Logger.Error($"PaxJobsGenerator for {yardId} was null, this shouldn´t happen!");
                return;
            }
            _PaxJGeneratorStartGenerationAsync.Invoke(generator, new object[0]);
            Main._modEntry.Logger.Log($"Started PaxJ generation with cars in {yardId}");
        }

        //public static bool TryGenerateJob(string yardId, JobType jobType, object passConsistInfo, out JobChainController passengerChainController)
        private static bool TryGenerateJob(StationController station, JobType jobType, RouteTrackRef startingRouteTrack, List<TrainCar> trainCars, out JobChainController passengerChainController)
        {
            passengerChainController = null;
            if (!AStartGameData.carsAndJobsLoadingFinished) return false;
            if (trainCars.Any(tc => tc.LoadedCargoAmount > 0.001f)) Main._modEntry.Logger.Log("Cars have cargo...");
            Main._modEntry.Logger.Log($"Attempting to generate job of type {jobType} in {YardIdFromPaxStation(GetRouteTrackStationField(startingRouteTrack))}");

            /*if (!TryGetGenerator(yardId, out object generator))
            {
                Main._modEntry.Logger.Error($"PaxJobsGenerator for {yardId} was null, this shouldn´t happen!");
                return false;
            }

            passengerChainController = (JobChainController)_GenerateJob.Invoke(generator, new object[] { jobType, passConsistInfo });*/

            if (!SetupAndGenerateJob(station, startingRouteTrack, trainCars, jobType, out passengerChainController))
            {
                Main._modEntry.Logger.Error("Couldn´t generate PaxJob - problem in build-up");
                return false;
            }

            if (passengerChainController == null || passengerChainController.currentJobInChain == null) Main._modEntry.Logger.Error("JobChainController or its job is null, this shouldn´t happen!"); ;
            return passengerChainController != null;
        }

        private static RouteTrackRef CreateRouteTrack(IPassDestinationRef IPassDestination, Track terminalTrack) => new(_RouteTrackCtor.Invoke(new object[] { IPassDestination.Value, terminalTrack }));

        private static object CreatePassConsistInfo(RouteTrackRef routeTrack, List<Car> cars) => _PassConsistInfoCtor.Invoke(new object[] { routeTrack.Value, cars });

        private static PassengerChainControllerRef CreatePassJcc(GameObject jobChainGameObject) => new(_PassengerJccCtor.Invoke(new object[] { jobChainGameObject }));

        private static ExpressStationsChainDataRef CreateExpressStationsChainData(string chainOriginYardId, string[] chainDestinationYardIds) => new(_ExpressStChainDataCtor.Invoke(new object[] { chainOriginYardId, chainDestinationYardIds }));

        public static bool IsPaxJobDefinition(StaticJobDefinition jobDefinition, out PassengerHaulJobDefinitionRef passengerHaulJobDefinition)
        {
            passengerHaulJobDefinition = new(null);
            if (jobDefinition == null) return false;
            var job = jobDefinition.job;
            if (job == null) return false;
            bool correctType = (job.jobType == _PassengerExpress || job.jobType == _PassengerLocal);
            bool correctDef = _PassengerHaulJobDefinition.IsInstanceOfType(jobDefinition);
            passengerHaulJobDefinition = new(jobDefinition);
            return correctType && correctDef;
        }

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

        public static List<StationController> AllPaxStations() => StationController.allStations.Where(st => IsPassengerStation(st.stationInfo.YardID)).ToList();

        private static IPassDestinationRef GetStationData(string yardId) => new(_GetStationData.Invoke(null, new object[] { yardId })); // output is IPassDestination : PassStationData

        private static RouteResultRef GetRoute(string yardId, object routeType, IEnumerable<string> existingDests, double minLength = 0) => new(_GetRoute?.Invoke(null, new object[] { GetStationData(yardId).Value, routeType, existingDests, minLength }));

        private static RouteTrackRef GetRouteTrackByIdOrNull(string trackFullDisplayId)
        {
            RouteTrackRef routeTrack = new(_GetRouteTrackById?.Invoke(null, new object[] { trackFullDisplayId }));
            if (routeTrack.Value is null) return new(null);
            Track trackField = GetRouteTrackTrackField(routeTrack);
            if (trackField == null || trackField.RailTrack() == null || trackField.ID == null || trackField.ID.FullDisplayID == null) return new(null);
            return routeTrack;
        }

        private static List<Track> AllPaxTracksForStationData(string yardId) => ((IEnumerable<Track>)_AllTracksProperty.GetValue(GetStationData(yardId).Value)).ToList();

        private static string YardIdFromPaxStation(IPassDestinationRef passStationData) => (string)_PaxStYardIdProperty.GetValue(passStationData.Value);

        private static IEnumerable<RouteTrackRef> GetPlatforms(IPassDestinationRef stationData, bool onlyTerminusTracks = false)
        {
            var result = _GetPlatforms.Invoke(stationData.Value, new object[] { onlyTerminusTracks });
            if (result is System.Collections.IEnumerable enumerable) foreach (var item in enumerable) yield return new(item);
        }

        private static List<Track> GetStorageTracks(string yardId) => AllPaxTracksForStationData(yardId).Except(GetPlatforms(GetStationData(yardId)).Select(rt => GetRouteTrackTrackField(rt)).ToList()).ToList();

        private static Track GetRouteTrackTrackField(RouteTrackRef routeTrack)
        {
            if (routeTrack.Value == null) return null;
            var track = (Track)_RouteTrackTrackField.GetValue(routeTrack.Value);
            if (track == null || track.RailTrack() == null || track.ID == null || track.ID.FullDisplayID == null) return null;
            return track;
        }

        private static IPassDestinationRef GetRouteTrackStationField(RouteTrackRef routeTrack)
        {
            if (routeTrack.Value == null) return new(null);
            var iPassDest = _RouteTrackStationField.GetValue(routeTrack.Value);
            if (iPassDest == null) return new(null);
            return new(iPassDest);
        }

        //private static PlatformControllerRef GetPlatformControllerForTrack(string id) => new(_PlatformControllerForTrack.Invoke(_AllPlatformControllers.ToList().WhereNotNull().First(), new object[] {id}));
        private static PlatformControllerRef GetPlatformControllerForTrack(string id) => new(_PlatformControllerForTrack.Invoke(null, new object[] { id }));
            
        private static string GetRouteTrackPlatformIdField(RouteTrackRef routeTrack) => (string)_RTPlatformIdProp.GetValue(routeTrack.Value);

        private static double GetRouteTrackLength(RouteTrackRef routeTrack) => (double)_RouteTrackLengthProp.GetValue(routeTrack.Value);

        private static PaymentCalculationData GetJobPaymentData(IEnumerable<TrainCarLivery> carTypes, bool empty = false) => (PaymentCalculationData)_GetJobPaymentData.Invoke(null, new object[] { carTypes, empty });

        private static bool CanFitInStation(IPassDestinationRef stationData, List<TrainCar> trainCars) => GetPlatforms(stationData).Any(rt => (float)GetConsistLength(trainCars) <= GetRouteTrackLength(rt));

        private static RouteTypeRef GetRouteType(JobType type) => new(_GetRouteType.Invoke(null, new object[] { type }));

        private static object GetRouteResultTracksArray(RouteResultRef routeResult) => _RouteResultTracksField.GetValue(routeResult.Value);

        private static float GetTotalHaulDistance(StationController startStation, Array destinations) => (float)_GetTotalHaulDistance.Invoke(null, new object[] { startStation, destinations });

        private static JobType PickPassengerJobType(int carCount)
        {
            if (carCount <= 4)
                return (JobType)_Random.Next(101, 103); // Express or Local

            return _PassengerExpress;
        }

        private static PassengerHaulJobDefinitionRef PopulateExpressJobExistingCars(JobChainController chainController, Station startStation, RouteTrackRef startTrack, RouteResultRef routeResult, List<Car> logicCars, StationsChainData chainData, float timeLimit, float initialPay) => new(_PopulateExpressJobExistingCars?.Invoke(null, new object[] { chainController, startStation, startTrack.Value, routeResult.Value, logicCars, chainData, timeLimit, initialPay }));

        private static Task CreatePaxBoardingTask(RouteTrackRef platform, float totalCapacity, bool loading, bool isFinal, object __instance) => (Task)(_PHJD_CreateBoardingTask.Invoke(__instance, new object[] { platform.Value, totalCapacity, loading, isFinal }));

        private static Task CreatePaxTransportTask(Track startingTrack, Track endTrack, object __instance) => (Task)_PHJD_CreateTransportTask.Invoke(__instance, new object[] { startingTrack, endTrack });

        private static RouteTrackRef SelectStartPlatformRouteTrack(Track currentTrack, List<RouteTrackRef> fittingPlatforms)
        {
            if (!fittingPlatforms.Any()) return new(null);

            if (currentTrack != null)
            {
                string currentTrackId = currentTrack.ID.FullDisplayID;
                var matchingPlatform = fittingPlatforms.FirstOrDefault(rt =>
                {
                    var track = GetRouteTrackTrackField(rt);
                    return track != null && track.ID.FullDisplayID == currentTrackId;
                });

                if (matchingPlatform.Value != null)
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

        private static List<JobChainController> TryGeneratePassengerJob(StationController station, List<TrainCar> trainCars, List<RouteTrackRef> fittingPlatforms, JobType jobType)
        {
            List<JobChainController> result = new();
            if (trainCars.Count() > 20)
            {
                result.AddRange(HandleSplitOrFail(trainCars, station));
                return result;
            }

            Track currentTrack = CarTrackAssignment.FindNearestNamedTrackOrNull(trainCars);
            var preferredRouteTrack = SelectStartPlatformRouteTrack(currentTrack, fittingPlatforms);
            if (preferredRouteTrack.Value == null || GetRouteTrackTrackField(preferredRouteTrack).ID == null)
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

            //if (TryGenerateJob(station.stationInfo.YardID, jobType, CreatePassConsistInfo(preferredRouteTrack, TrainCar.ExtractLogicCars(cars)), out JobChainController passangerChainController))
            if (TryGenerateJob(station, jobType, preferredRouteTrack, trainCars, out JobChainController passangerChainController))
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
                    var (first, second) = emptyTrainCars.SplitInHalf();
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
                    var (first, second) = loadedTrainCars.SplitInHalf();
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

        private static void AddTransportTaskToTop(StaticJobDefinition staticPaxJobDefinition, Track currentTrack, Track destinatiionTrack, List<Car> cars)
        {
            if (staticPaxJobDefinition != null)
            {
                if (staticPaxJobDefinition.job == null)
                {
                    Main._modEntry.Logger.Error("staticJobDefinition.job is null");
                    return;
                }
                var job = staticPaxJobDefinition.job;
                Main._modEntry.Logger.Log($"Attempting to add task to {job.ID}");
                var headSequentialTask = (SequentialTasks)job.tasks.FirstOrDefault(t => t.InstanceTaskType == TaskType.Sequential);
                if (headSequentialTask != null)
                {
                    var newTransportTask = JobsGenerator.CreateTransportTask(cars, destinatiionTrack, currentTrack);
                    newTransportTask.SetJobBelonging(job);
                    headSequentialTask.tasks.AddFirst(newTransportTask);
                }
                else Main._modEntry.Logger.Error($"Couldn´t extract head sequential task of {job.ID}");
            }
            else Main._modEntry.Logger.Error($"Couldn´t get job definition");

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

            return result;
        }

        public static List<IReadOnlyList<TrainCar>> FilterTrainCarGroups(List<IReadOnlyList<TrainCar>> trainCarGroups)
        {
            trainCarGroups.RemoveAll(trainCars =>
            {
                if (trainCars == null || trainCars.Count == 0)
                {
                    Main._modEntry.Logger.Error("[FilterTrainCarGroups] Invalid cars input thrown out");
                    return true;
                }
                if (CarTrackAssignment.FindNearestNamedTrackOrNull(trainCars) == null) return true;

                return trainCars.All(tc => tc.derailed);
            });
            return trainCarGroups;
        }

        private static Track GetRandomFittingStorageTrack(StationController targetStation, List<TrainCar> trainCars)
        {
            var fittingSPTracks = GetStationData(targetStation.stationInfo.YardID).Value != null ? (GetStorageTracks(targetStation.stationInfo.YardID).Where(t => GetConsistLength(trainCars) <= t.length).ToList()) : new List<Track>();
            return !fittingSPTracks.Any() ? null : fittingSPTracks.GetRandomElement();
        }

        private static List<RouteTrackRef> GetFittingPlatformsForStation(StationController station, List<TrainCar> trainCars, bool onlyTerminusTracks = false)
        {
            var stationData = GetStationData(station.stationInfo.YardID);
            return stationData.Value == null ? new List<RouteTrackRef>() : (GetPlatforms(stationData, onlyTerminusTracks).Where(rt => GetConsistLength(trainCars) <= GetRouteTrackLength(rt)).ToList());
        }

        private static float CalculateCrowDistanceBetweenThings(Vector3 thing1, Vector3 thing2) => (thing1 - thing2).sqrMagnitude;

        private static StationController FindDestinationStation(StationController origin, List<TrainCar> trainCars) => AllPaxStations().Distinct().Where(st => st != origin).Where(st => CanFitInStation(GetStationData(st.stationInfo.YardID), trainCars)).OrderBy(sc => CalculateCrowDistanceBetweenThings(trainCars.First().transform.position, sc.stationRange.transform.position)).FirstOrDefault();

        private static bool FilterJobDataForModPaxJob(ref Job_data job, out Task_data taskSequence, out Task_data firstNestedTask, out bool exit)
        {
            firstNestedTask = null;
            taskSequence = null;

            if (job.tasksData == null || job.tasksData.Length == 0)
            {
                exit = true;
                return false;
            }

            taskSequence = job.tasksData[0];
            if (taskSequence.type != TaskType.Sequential || taskSequence.nestedTasks?.Length < 1)
            {
                exit = true;
                return false;
            }

            firstNestedTask = taskSequence.nestedTasks[0];
            if (firstNestedTask.instanceTaskType != TaskType.Transport)
            {
                exit = true;
                return true;
            }

            exit = false;
            return false;
        }

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

        private static bool SetupAndGenerateJob(StationController startingStation, RouteTrackRef startingRouteTrack, List<TrainCar> trainCars, JobType jobType, out JobChainController passengerChainController)
        {
            passengerChainController = null;
            //object startingRouteTrack = GetRouteTrackByIdOrNull(startingTrack.ID.FullDisplayID);
            var currentDests = startingStation.logicStation.availableJobs
                .Where(j => (j.jobType == _PassengerExpress) || (j.jobType == _PassengerLocal))
                .Select(j => j.chainData.chainDestinationYardId);

            var destinations = GetRoute(startingStation.stationInfo.YardID, GetRouteType(jobType).Value, currentDests, GetConsistLength(trainCars));
            if (destinations.Value == null) return false;

            var jobCarTypes = TrainCar.ExtractLogicCars(trainCars).Select(c => c.carType).ToList();

            if (GetRouteResultTracksArray(destinations) is not Array rrTracksArray || rrTracksArray.Length == 0) return false;
            var destinationTracks = rrTracksArray.Cast<object>().Select(o => new Foreign<RouteTrackRef>(o)).Select(rt => (Track)_RouteTrackTrackField.GetValue(rt.Value)).ToList();
            var destinationRouteTracksYardIDs = rrTracksArray.Cast<object>().Select(d => YardIdFromPaxStation(GetRouteTrackStationField(new RouteTrackRef(d))));

            // create job chain controller
            string destString = string.Join(" - ", destinationRouteTracksYardIDs);
            var chainJobObject = new GameObject($"ChainJob[Passenger]: {startingStation.stationInfo.YardID} - {destString}");
            chainJobObject.transform.SetParent(startingStation.transform);
            var chainController = (JobChainController)CreatePassJcc(chainJobObject).Value;

            var chainData = (StationsChainData)CreateExpressStationsChainData(startingStation.stationInfo.YardID, destinationRouteTracksYardIDs.ToArray()).Value;
            PaymentCalculationData transportPaymentData = GetJobPaymentData(jobCarTypes);

            float haulDistance = GetTotalHaulDistance(startingStation, rrTracksArray);
            float bonusLimit = JobPaymentCalculator.CalculateHaulBonusTimeLimit(haulDistance, false);
            float transportPayment = JobPaymentCalculator.CalculateJobPayment(JobType.Transport, haulDistance, transportPaymentData);

            chainController.carsForJobChain = TrainCar.ExtractLogicCars(trainCars);

            var jobDefinition = (StaticJobDefinition)PopulateExpressJobExistingCars(chainController, startingStation.logicStation, startingRouteTrack, destinations, TrainCar.ExtractLogicCars(trainCars), chainData, bonusLimit, transportPayment).Value;
            if (jobDefinition == null)
            {
                Main._modEntry.Logger.Warning($"Failed to generate transport job definition for {chainController.jobChainGO.name}");
                chainController.DestroyChain();
                return false;
            }

            chainController.AddJobDefinitionToChain(jobDefinition);

            // Finalize job
            chainController.FinalizeSetupAndGenerateFirstJob();
            Main._modEntry.Logger.Log($"Generated new passenger haul job: {chainJobObject.name} ({chainController.currentJobInChain.ID})");
            passengerChainController = chainController;
            return true;
        }

        private static bool GeneratePaxJobOverride(List<Car> cars, RouteTrackRef startingRouteTrack, List<RouteTrackRef> destinationTracks, object __instance, Station jobOriginStation, float timeLimit = 0, float initialWage = 0, string forcedJobId = null, JobLicenses requiredLicenses = JobLicenses.Basic, bool loaded = false, bool tracksMismatch = false)
        {
            // taken from PaxJobs - mostly original method code - in order to avoid using transpiler
            float totalCapacity = 0;
            foreach (var car in cars) totalCapacity += car.capacity;
            var taskList = new List<Task>();
            Track currentCarsTrack = CarTrackAssignment.FindNearestNamedTrackOrNull(TrainCar.ExtractTrainCars(cars));
            Track initialTransportDestTrack = null;

            if (!loaded)
            {
                var initialLoad = CreatePaxBoardingTask(startingRouteTrack, totalCapacity, true, false, __instance);
                taskList.Add(initialLoad);
            }
            if (tracksMismatch) initialTransportDestTrack = GetRouteTrackTrackField(startingRouteTrack);
            //there has to be some task based in the originating station to get couple page to exist
            var initialTransportTask = CreatePaxTransportTask(currentCarsTrack, initialTransportDestTrack ?? currentCarsTrack, __instance);
            taskList.Insert(0, initialTransportTask);

            List<string> destinationRouteTracksYardIDs = new();

            for (int i = 0; i < destinationTracks.Count; i++)
            {
                bool isLast = (i == (destinationTracks.Count - 1));
                var unloadTask = CreatePaxBoardingTask(destinationTracks[i], totalCapacity, false, isLast, __instance);
                taskList.Add(unloadTask);

                if (!isLast)
                {
                    var loadTask = CreatePaxBoardingTask(destinationTracks[i], totalCapacity, true, false, __instance);
                    taskList.Add(loadTask);
                }

                destinationRouteTracksYardIDs.Add(GetRouteTrackTrackField(destinationTracks[i]).ID.yardId);
            }

            var chainData = (StationsChainData)CreateExpressStationsChainData(jobOriginStation.ID, destinationRouteTracksYardIDs.ToArray()).Value;
            var superTask = new SequentialTasks(taskList);
            var currentRouteType = _PHJD_RouteTypeField.GetValue(__instance);
            var jobType = currentRouteType.Equals(_RouteTypeExpress) ? _PassengerExpress : _PassengerLocal;
            StaticJobDefinition baseJobDef = (StaticJobDefinition)__instance;
            var newJob = new Job(superTask, jobType, timeLimit, initialWage, chainData, forcedJobId, requiredLicenses);
            _StaticJobDefJobField.SetValue(__instance, newJob);

            if (!loaded)
            {
                var startPlatController = GetPlatformControllerForTrack(GetRouteTrackPlatformIdField(startingRouteTrack)).Value;
                newJob.JobTaken += (j, _) => _PlatformRegisterOutgoingJob.Invoke(startPlatController, new object[] { j, false });
            }

            for (int i = 0; i < destinationTracks.Count - 1; i++)
            {
                var destPlatController = GetPlatformControllerForTrack(GetRouteTrackPlatformIdField(destinationTracks[i])).Value;
                newJob.JobTaken += (j, _) => _PlatformRegisterOutgoingJob.Invoke(destPlatController, new object[] { j, false });
            }

            var finalPlatController = GetPlatformControllerForTrack(GetRouteTrackPlatformIdField(destinationTracks.Last())).Value;
            newJob.JobTaken += (j, _) => _PlatformRegisterIncomingJob.Invoke(finalPlatController, new object[] { j, false });

            jobOriginStation.AddJobToStation(baseJobDef.job);
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
                if (station.stationInfo.YardID == "CS" && jobType == _PassengerExpress) jobType = _PassengerLocal;
                jobChainControllers.AddRange(TryGeneratePassengerJob(station, trainCars, fittingPlatforms, jobType));
                return;
            }
            else
            {
                Main._modEntry.Logger.Log($"Loaded consist of {trainCars.Count()} pax cars starting with {trainCars.First().ID} on track {startingTrack.ID.FullID} needs to be transported to a pax jobs station to get unloaded");
                //generate FH job to random pax station: use already existing mod logic elswhere
                StationController viableDestStation = FindDestinationStation(station, trainCars);
                if (viableDestStation != null)
                {
                    Track possibleDestinationTrack = GetRandomFittingStorageTrack(viableDestStation, trainCars);
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
                    Main._modEntry.Logger.Error($"Loaded consist of {trainCars.Count()} pax cars starting with {trainCars.First().ID} can´t be reassigned a FH to any pax station, attempting splitting");
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
                if (station.stationInfo.YardID == "CS" && jobType == _PassengerExpress) jobType = _PassengerLocal; //we have to do this since City South doesn´t have any valid outgoing express routes <-- do this dynamically from PaxJobs routes list?
                jobChainControllers.AddRange(TryGeneratePassengerJob(station, trainCars, fittingPlatforms, jobType));
                return;
            }
            else
            {
                Main._modEntry.Logger.Log($"Empty consist of {trainCars.Count()} pax cars starting with {trainCars.First().ID} on track {startingTrack.ID.FullID} needs to be transported to a pax jobs station to get reassigned a pax job");
                //generate LH job to random pax station: use already existing mod logic elswhere
                StationController viableDestStation = FindDestinationStation(station, trainCars);
                if (viableDestStation != null)
                {
                    Track possibleDestinationTrack = GetRandomFittingStorageTrack(viableDestStation, trainCars);
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


        #region PaxJobs Patches

#pragma warning disable IDE0060 // Remove unused parameter

        private static bool GenerateJob_Prefix(object __instance, Station jobOriginStation, float timeLimit, float initialWage, string forcedJobId, JobLicenses requiredLicenses)
        {
            List<Car> cars = (List<Car>)_TrainCarsToTransportProp.GetValue(__instance);
            RouteTrackRef startingRouteTrack = new(_StartingTrackField.GetValue(__instance));
            if (_DestinationTracksField.GetValue(__instance) is not Array rrTracksArray || rrTracksArray.Length == 0) return false;
            var destinationTracks = rrTracksArray.Cast<object>().Select(o => new RouteTrackRef(o)).ToList();

            if ((cars == null) || (cars.Count == 0) || (startingRouteTrack.Value == null) || (destinationTracks == null))
            {
                Main._modEntry.Logger.Log("Failed to generate passengers job, bad data");
                return false;
            }

            if (cars.Any(c => c.CurrentCargoTypeInCar != CargoType.None))
            {
                return !GeneratePaxJobOverride(cars, startingRouteTrack, destinationTracks, __instance, jobOriginStation, timeLimit, initialWage, forcedJobId, requiredLicenses, loaded: true);
            }

            if (GetRouteTrackTrackField(startingRouteTrack) != CarTrackAssignment.FindNearestNamedTrackOrNull(TrainCar.ExtractTrainCars(cars)))
            {
                return !GeneratePaxJobOverride(cars, startingRouteTrack, destinationTracks, __instance, jobOriginStation, timeLimit, initialWage, forcedJobId, requiredLicenses, loaded: false, tracksMismatch: true);
            }

            return true;
        }

        private static void GenerateJob_Postfix(object __instance, Station jobOriginStation, float timeLimit, float initialWage, string forcedJobId, JobLicenses requiredLicenses)
        {
            RouteTrackRef startingRouteTrack = new(_StartingTrackField.GetValue(__instance));
            List<Car> cars = (List<Car>)_TrainCarsToTransportProp.GetValue(__instance);
            var carsTrack = CarTrackAssignment.FindNearestNamedTrackOrNull(TrainCar.ExtractTrainCars(cars));

            if (GetRouteTrackTrackField(startingRouteTrack).ID.FullDisplayID != carsTrack.ID.FullDisplayID)
            {
                var jobs = jobOriginStation.availableJobs;
                jobs.Reverse();

                foreach (var job in jobs)
                {
                    if (job.tasks.FirstOrDefault(t => t.InstanceTaskType == TaskType.Sequential) is not SequentialTasks sequential) continue;

                    if (sequential.tasks.FirstOrDefault(t => t.InstanceTaskType == TaskType.Transport) is not TransportTask transport) continue;

                    if ((bool)!transport.GetTaskData().cars.SequenceEqual(cars)) continue;

                    var startTrack = (Track)_TaskStartingTrackField.GetValue(transport);
                    if (startTrack == null || startTrack.ID.FullDisplayID == carsTrack.ID.FullDisplayID) continue;

                    _TaskStartingTrackField.SetValue(transport, carsTrack);
                    Main._modEntry.Logger.Log($"Changed start strack for job {job.ID}");
                    break;
                }
            }
        }

        private static bool StartGenerationAsync_Prefix(object __instance)
        {
            StationController generatingStation = (StationController)_PaxJGeneratorStContField.GetValue(__instance);
            if (StationIdCarSpawningPersistence.Instance.GetHasStationSpawnedCarsFlag(generatingStation) && !OverrideSpawnFlagForPaxJ)
            {
                Main._modEntry.Logger.Log($"Station {generatingStation.logicStation.ID} has already spawned cars, skipping passanger jobs with new cars generation");
                return false;
            }
            else
            {
                Main._modEntry.Logger.Log($"Station {generatingStation.logicStation.ID} is generating passanger jobs with cars");
                OverrideSpawnFlagForPaxJ = false;
                return true;
            }
        }

        private static bool ExtractPassengerJobData_Prefix(ref Job_data job)
        {
            //adding a dummy load task data if there is none in origin station

            bool result = FilterJobDataForModPaxJob(ref job, out Task_data taskSequence, out Task_data firstNestedTask, out bool exit);
            if (exit) return result;

            Task_data[] newNestedTasks = null;
            bool loadingFound = false;
            foreach (var taskData in taskSequence.nestedTasks)
            {
                if (taskData.instanceTaskType != TaskType.Warehouse) continue;
                if (taskData.warehouseTaskType == WarehouseTaskType.Loading)
                {
                    loadingFound = true;
                    continue;
                }
                if (taskData.warehouseTaskType == WarehouseTaskType.Unloading && !loadingFound)
                {
                    //var carsTrack = CarTrackAssignment.FindNearestNamedTrackOrNull(CarTrackAssignment.TrainCarsByGuid(taskData.cars.Select(c => c.ID)));
                    var carsTrack = firstNestedTask.startTrackID;
                    if (carsTrack == null) break;

                    var destTrack = GetRouteTrackTrackField(GetPlatforms(GetStationData(carsTrack.yardId)).First());
                    var taskList = taskSequence.nestedTasks.ToList();
                    taskList.Insert(1, new Task_data(TaskType.Warehouse, TaskType.Warehouse, TaskState.InProgress, 0, 0, taskData.cars, carsTrack, destTrack.ID, WarehouseTaskType.None, new List<CargoType>(), 0, false, false, Array.Empty<Task_data>()));
                    newNestedTasks = taskList.ToArray();
                    break;
                }
            }

            if (newNestedTasks?.Length > 1) taskSequence.nestedTasks = newNestedTasks;
            job.tasksData[0] = taskSequence;
            return true;
        }

        private static bool CreateLoadTaskPage_Prefix(ref TemplatePaperData __result, object[] __args)
        {
            //skip creation of load page for our modified jobs (starting with transport) that are from loaded cars

            PassengerJobDataRef jobData = new(__args[0]);
            object initialStopInfo = __args[1];
            if (_InitialStopField.GetValue(jobData.Value) == initialStopInfo)
            {
                var baseJobData = ((TransportJobData)jobData.Value).job;
                bool result = FilterJobDataForModPaxJob(ref baseJobData, out _, out Task_data firstNestedTask, out bool exit);
                if (exit) return result;

                if (firstNestedTask.startTrackID == firstNestedTask.destinationTrackID)
                {
                    //from context we now know (hopefully?) that the cars were loaded 
                    __result = null;
                    return false;
                }
            }
            return true;
        }

        private static void GetBookletTemplateData_Postfix(Job_data job, ref List<TemplatePaperData> __result)
        {
            __result = __result.Where(t => t != null).ToList();
        }

#pragma warning restore IDE0060 // Remove unused parameter
        #endregion
    }
}