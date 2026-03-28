using DV;
using DV.Booklets;
using DV.Logic.Job;
using DV.ThingTypes;
using DV.RenderTextureSystem.BookletRender;
using HarmonyLib;
using MessageBox;
using PersistentJobsMod.Extensions;
using PersistentJobsMod.JobGenerators;
using PersistentJobsMod.Utilities;
using PersistentJobsMod.Persistence;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using static PersistentJobsMod.Utilities.ReflectionUtilities;
using static PersistentJobsMod.HarmonyPatches.JobGeneration.UnusedTrainCarDeleter_Patches;
using static PersistentJobsMod.ModInteraction.PaxJobsCompat.Tags;
using Random = System.Random;

#region RefType using
using ExpressStationsChainDataRef = PersistentJobsMod.Utilities.ReflectionUtilities.Foreign<PersistentJobsMod.ModInteraction.PaxJobsCompat.Tags.ExpressStationsChainData>;
using IPassDestinationRef = PersistentJobsMod.Utilities.ReflectionUtilities.Foreign<PersistentJobsMod.ModInteraction.PaxJobsCompat.Tags.IPassDestination>;
using PassengerChainControllerRef = PersistentJobsMod.Utilities.ReflectionUtilities.Foreign<PersistentJobsMod.ModInteraction.PaxJobsCompat.Tags.PassengerChainController>;
using PassengerHaulJobDefinitionRef = PersistentJobsMod.Utilities.ReflectionUtilities.Foreign<PersistentJobsMod.ModInteraction.PaxJobsCompat.Tags.PassengerHaulJobDefinition>;
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
        private static Type _ExpressJobDefinitionData;
        private static Type _JobSaveManagerPatch;
        private static Type _PassengerChainSaveData;
        private static Type _PJMain;
        private static Type _PJModSettings;
        private static Type _PJExtensions;
        private static Type _CityLoadingTask;
        private static Type _RuralLoadingTask;

        private static ConstructorInfo _RouteTrackCtor;
        private static ConstructorInfo _PassConsistInfoCtor;
        private static ConstructorInfo _PassengerJccCtor;
        private static ConstructorInfo _ExpressStChainDataCtor;

        private static MethodInfo _TryGetInstance;
        private static MethodInfo _GetAllPassengerCars;
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
        private static MethodInfo _CreateCoupleTaskPage;
        private static MethodInfo _CreateCoupleTaskPaperData;
        private static MethodInfo _LoadPassengerChain;
        private static MethodInfo _GetTimeMultiplier;
        private static MethodInfo _GetTimeForStops;
        private static MethodInfo _GetUnusedRouteTracks;
        private static MethodInfo _LocalGetRoute;
        private static MethodInfo _OnLastJobInChainCompletedOverride;
        private static MethodInfo _OnLastJobInChainCompletedBase;

        private static PropertyInfo _AllTracksProperty;
        private static PropertyInfo _RouteTrackLengthProp;
        private static PropertyInfo _RouteTrackIsSegmentProp;
        private static PropertyInfo _TrainCarsToTransportProp;
        private static PropertyInfo _PaxStYardIdProperty;
        private static PropertyInfo _RTPlatformIdProp;
        private static PropertyInfo _PJMainSettingsProp;

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
        private static FieldInfo _BaseWageScale;
        private static FieldInfo _CustomWagesField;

        private static Random _Random;

        private static TaskType _CityLoadingTaskType;
        private static TaskType _RuralLoadingTaskType;

        private static JobType _PassengerExpress;
        private static JobType _PassengerLocal;

        private static object _RouteTypeExpress;
        private static object _RouteTypeLocal;

        private static double _YTO_ReserveThreshold;

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
                _ExpressJobDefinitionData = CompatAccess.Type("PassengerJobs.Generation.ExpressJobDefinitionData");
                _JobSaveManagerPatch = CompatAccess.Type("PassengerJobs.Patches.JobSaveManagerPatch");
                _PassengerChainSaveData = CompatAccess.Type("PassengerJobs.Generation.PassengerChainSaveData");
                _PJMain = CompatAccess.Type("PassengerJobs.PJMain");
                _PJModSettings = CompatAccess.Type("PassengerJobs.PJModSettings");
                _PJExtensions = CompatAccess.Type("PassengerJobs.Extensions");
                _CityLoadingTask = CompatAccess.Type("PassengerJobs.Platforms.CityLoadingTask");
                _RuralLoadingTask = CompatAccess.Type("PassengerJobs.Platforms.RuralLoadingTask");

                _TryGetInstance = CompatAccess.Method(_PassengerJobGenerator, "TryGetInstance");
                _GetAllPassengerCars = CompatAccess.Method(_ConsistManager, "GetAllPassengerCars");
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
                _CreateCoupleTaskPage = CompatAccess.Method(_BookletUtility, "CreateCoupleTaskPage", new[] { _PassengerJobData, typeof(int), typeof(int), typeof(int) });
                _CreateCoupleTaskPaperData = CompatAccess.Method(typeof(BookletCreator_Job), "CreateCoupleTaskPaperData", new[] { typeof(int), typeof(string), typeof(Color), typeof(string), typeof(List<Car_data>), typeof(List<CargoType>), typeof(int), typeof(int) });
                _LoadPassengerChain = CompatAccess.Method(_JobSaveManagerPatch, "LoadPassengerChain", new[] { _PassengerChainSaveData });
                _GetTimeMultiplier = CompatAccess.Method(_PassengerJobGenerator, "GetTimeMultiplier", new[] { typeof(JobType) });
                _GetTimeForStops = CompatAccess.Method(_PassengerJobGenerator, "GetTimeForStops", new[] { _RouteResult });
                _GetUnusedRouteTracks = CompatAccess.Method(_PJExtensions, "GetUnusedTracks", new[] { CompatAccess.IEnumerableOf(_RouteTrack) });
                _LocalGetRoute = CompatAccess.Method(typeof(PaxJobsCompat), "GetRoute");
                _OnLastJobInChainCompletedOverride = CompatAccess.Method(_PassengerChainController, "OnLastJobInChainCompleted", new[] { typeof(Job) });
                _OnLastJobInChainCompletedBase = CompatAccess.Method(typeof(JobChainController), "OnLastJobInChainCompleted", new[] { typeof(Job) });

                _AllTracksProperty = CompatAccess.Property(_IPassDestination, "AllTracks");
                _RouteTrackLengthProp = CompatAccess.Property(_RouteTrack, "Length");
                _RouteTrackIsSegmentProp = CompatAccess.Property(_RouteTrack, "IsSegment");
                _TrainCarsToTransportProp = CompatAccess.Property(_PassengerHaulJobDefinition, "TrainCarsToTransport");
                _PaxStYardIdProperty = CompatAccess.Property(_IPassDestination, "YardID");
                _RTPlatformIdProp = CompatAccess.Property(_RouteTrack, "PlatformID");
                _PJMainSettingsProp = CompatAccess.Property(_PJMain, "Settings");

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
                _BaseWageScale = CompatAccess.Field(_PassengerJobGenerator, "BASE_WAGE_SCALE");
                _CustomWagesField = CompatAccess.Field(_PJModSettings, "UseCustomWages");

                _RouteTrackCtor = CompatAccess.Ctor(_RouteTrack, new[] { _IPassDestination, typeof(Track) });
                _PassConsistInfoCtor = CompatAccess.Ctor(_PassConsistInfo, new[] { _RouteTrack, typeof(List<Car>) });
                _PassengerJccCtor = CompatAccess.Ctor(_PassengerChainController, new[] { typeof(GameObject) });
                _ExpressStChainDataCtor = CompatAccess.Ctor(_ExpressStationsChainData, new[] { typeof(string), Type.GetType("System.String[]") });

                _Random = new Random();

                _CityLoadingTaskType = Traverse.Create(_CityLoadingTask).Field("TaskType").GetValue<TaskType>();
                _RuralLoadingTaskType = Traverse.Create(_RuralLoadingTask).Field("TaskType").GetValue<TaskType>();

                _PassengerExpress = Traverse.Create(_PassJobType).Field("Express").GetValue<JobType>();
                _PassengerLocal = Traverse.Create(_PassJobType).Field("Local").GetValue<JobType>();

                _RouteTypeExpress = Enum.Parse(_RouteType, "Express");
                _RouteTypeLocal = Enum.Parse(_RouteType, "Local");

                _YTO_ReserveThreshold = (float)CompatAccess.Field(typeof(YardTracksOrganizer), "END_OF_TRACK_OFFSET_RESERVATION").GetValue(YardTracksOrganizer.Instance) + (float)CompatAccess.Field(typeof(YardTracksOrganizer), "FLOATING_POINT_IMPRECISION_THRESHOLD").GetValue(YardTracksOrganizer.Instance);

                PatchPrefix(_PHJD_GenerateJob, typeof(PaxJobsCompat), nameof(GenerateJob_Prefix));

                PatchPrefix(_PaxJGeneratorStartGenerationAsync, typeof(PaxJobsCompat), nameof(StartGenerationAsync_Prefix));

                PatchPrefix(_ExtractPassengerJobData, typeof(PaxJobsCompat), nameof(ExtractPassengerJobData_Prefix));

                PatchPostfix(_GetBookletTemplateData, typeof(PaxJobsCompat), nameof(GetBookletTemplateData_Postfix));

                PatchPrefix(_CreateLoadTaskPage, typeof(PaxJobsCompat), nameof(CreateLoadTaskPage_Prefix));

                PatchPrefix(_CreateCoupleTaskPage, typeof(PaxJobsCompat), nameof(CreateCoupleTaskPage_Prefix));

                PatchPostfix(_LoadPassengerChain, typeof(PaxJobsCompat), nameof(LoadPassengerChain_Postfix));

                PatchPrefix(_GetUnusedRouteTracks, typeof(PaxJobsCompat), nameof(GetUnusedRouteTracks_Prefix));

                PatchPrefix(_OnLastJobInChainCompletedOverride, typeof(PaxJobsCompat), nameof(OnLastJobInChainCompletedOverride_Prefix));

                PatchReverse(_OnLastJobInChainCompletedBase, typeof(PaxJobsCompat), nameof(OnLastJobInChainCompletedReverse));

                //removing a PaxJobs patch that deletes cars on job abandonment
                Main.Harmony.Unpatch(CompatAccess.Method(typeof(JobChainController), "OnAnyJobFromChainAbandoned"), HarmonyPatchType.Prefix, Main.PaxJobs.Info.Id);

            }
            catch (Exception e)
            {
                Main._modEntry.Logger.LogException("Failed to initilize PaxJobsCompat when resolving types and methods, trying to unpatch", e);

                StringBuilder s = new();
                foreach (var (target, patchContainer, patchMethodName) in ReflectionUtilities.patchRecord)
                {
                    try
                    {
                        Main.Harmony.Unpatch(target, HarmonyPatchType.All, Main.Harmony.Id);
                    }
                    catch (Exception ex)
                    {
                        Main._modEntry.Logger.LogException($"Error when unpatching {target.Name}!", ex);
                        s.AppendLine(target.Name + ": " + ex.Message);
                    }
                }
                if (s.Length > 0) PopupAPI.ShowOk("State is not clean, there might be problems. \n" + s);

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

            public class PaxJobModifiedFlag : MonoBehaviour { };
            public class PaxJobJobTakenFlag : MonoBehaviour { public JobChainSaveData jobChainSaveData; };
        }
        #endregion

        public static bool OverrideSpawnFlagForPaxJ = false;
        public static bool BypassUnusedTracksFilter = false;

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

        private static bool TryGenerateJob(StationController station, JobType jobType, RouteTrackRef startingRouteTrack, List<TrainCar> trainCars, out JobChainController passengerChainController)
        {
            passengerChainController = null;
            if (!AStartGameData.carsAndJobsLoadingFinished) return false;
            if (trainCars.Any(tc => tc.LoadedCargoAmount > 0.001f)) Main._modEntry.Logger.Log("Cars have cargo...");
            Main._modEntry.Logger.Log($"Attempting to generate job of type {jobType} in {YardIdFromPaxStation(GetRouteTrackStationField(startingRouteTrack))}");

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
            var carLiveries = (IEnumerable<TrainCarLivery>)_GetAllPassengerCars.Invoke(null, null);
            return carLiveries != null && car.carLivery != null && carLiveries.Contains(car.carLivery);
        }

        public static float GetConsistLength(List<TrainCar> trainCars) => CarSpawner.Instance.GetTotalCarsLength(TrainCar.ExtractLogicCars(trainCars), true);

        private static float GetTimeMultiplier(JobType jobType) => (float)(_GetTimeMultiplier.Invoke(null, new object[] { jobType }));

        private static float GetTimeForStops(RouteResultRef route) => (float)(_GetTimeForStops.Invoke(null, new object[] { route.Value }));

        private static bool IsPassengerStation(string yardId) => (bool)(_IsPassengerStation?.Invoke(null, new object[] { yardId }));

        public static List<StationController> AllPaxStations() => StationController.allStations.Where(st => IsPassengerStation(st.stationInfo.YardID)).ToList();

        private static IPassDestinationRef GetStationData(string yardId) => new(_GetStationData.Invoke(null, new object[] { yardId })); // output is IPassDestination : PassStationData

        private static RouteResultRef GetRoute(string yardId, object routeType, IEnumerable<string> existingDests, double minLength = 0)
        {
            BypassUnusedTracksFilter = true;
            try
            {
                return new(_GetRoute?.Invoke(null, new object[] { GetStationData(yardId).Value, routeType, existingDests, minLength }));
            }
            finally
            {
                BypassUnusedTracksFilter = false;
            }
        }

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

        private static PlatformControllerRef GetPlatformControllerForTrack(string id) => new(_PlatformControllerForTrack.Invoke(null, new object[] { id }));

        private static string GetRouteTrackPlatformIdField(RouteTrackRef routeTrack) => (string)_RTPlatformIdProp.GetValue(routeTrack.Value);

        private static double GetRouteTrackLength(RouteTrackRef routeTrack) => (double)_RouteTrackLengthProp.GetValue(routeTrack.Value);

        private static bool IsRouteTrackASegment(RouteTrackRef routeTrack) => (bool)_RouteTrackIsSegmentProp.GetValue(routeTrack.Value);

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
            var currentDests = startingStation.logicStation.availableJobs
                .Where(j => (j.jobType == _PassengerExpress) || (j.jobType == _PassengerLocal))
                .Select(j => j.chainData.chainDestinationYardId);

            RouteResultRef destinations = GetRoute(startingStation.stationInfo.YardID, GetRouteType(jobType).Value, currentDests, GetConsistLength(trainCars));
            if (destinations.Value == null)
            {
                Main._modEntry.Logger.Error("destinations are null");
                return false;
            }

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
            float bonusLimit = JobPaymentCalculator.CalculateHaulBonusTimeLimit(haulDistance, false) * GetTimeMultiplier(jobType);
            bonusLimit += GetTimeForStops(destinations);
            float transportPayment = JobPaymentCalculator.CalculateJobPayment(JobType.Transport, haulDistance, transportPaymentData);
            float? wageScale = 1;
            if (TryGetGenerator(startingStation.logicStation.ID, out object generator))
            {
                wageScale = (float)((bool)_CustomWagesField?.GetValue(_PJMainSettingsProp.GetValue(null)) ? _BaseWageScale?.GetValue(generator) : 1);
            }
            transportPayment = Mathf.Round((float)(transportPayment * wageScale));

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

        internal readonly struct PaxJobContext
        {
#nullable enable
            public readonly object JobDefinitionInstance;
            public readonly StaticJobDefinition BaseJobDefinition;
            public readonly JobChainSaveData? JobChainSaveData;

            public readonly JobType JobType;
            public readonly string ForcedJobId;
            public readonly bool FromSave;
            public readonly bool JobTaken;

            public readonly Station OriginStation;
            public readonly float TimeLimit;
            public readonly float InitialWage;
            public readonly JobLicenses RequiredLicenses;

            public readonly IReadOnlyList<Car> Cars;
            public readonly float TotalCapacity;
            public readonly bool CarsLoaded;

            public readonly RouteTrackRef StartingRouteTrack;
            public readonly Track StartingTrack;
            public readonly Track CarsCurrentTrack;

            public readonly IReadOnlyList<RouteTrackRef> DestinationRouteTracks;
            public readonly IReadOnlyList<Track> DestinationTracks;

            public readonly bool WasModifiedAtSave;

            public PaxJobContext(object jobDefinitionInstance, JobChainSaveData jobChainSaveData, Station originStation, float timeLimit, float initialWage, string forcedJobId, JobLicenses requiredLicenses, IReadOnlyList<Car> cars, RouteTrackRef startingRouteTrack, IReadOnlyList<RouteTrackRef> destinationRouteTracks, bool jobTaken, bool wasModifiedAtSave)
            {
                JobDefinitionInstance = jobDefinitionInstance;
                BaseJobDefinition = (StaticJobDefinition)jobDefinitionInstance;
                JobChainSaveData = jobChainSaveData;

                OriginStation = originStation;
                TimeLimit = timeLimit;
                InitialWage = initialWage;
                ForcedJobId = forcedJobId;
                FromSave = !string.IsNullOrEmpty(forcedJobId);
                JobTaken = jobTaken;
                WasModifiedAtSave = wasModifiedAtSave;
                RequiredLicenses = requiredLicenses;

                JobType = _PHJD_RouteTypeField.GetValue(JobDefinitionInstance).Equals(_RouteTypeExpress) ? _PassengerExpress : _PassengerLocal;

                Cars = cars;
                CarsLoaded = cars.Any(c => c.CurrentCargoTypeInCar != CargoType.None);
                TotalCapacity = cars.Sum(c => c.capacity);

                StartingRouteTrack = startingRouteTrack;
                StartingTrack = GetRouteTrackTrackField(startingRouteTrack);
                CarsCurrentTrack = CarTrackAssignment.FindNearestNamedTrackOrNull(TrainCar.ExtractTrainCars((List<Car>)cars));

                DestinationRouteTracks = destinationRouteTracks;
                DestinationTracks = destinationRouteTracks.Select(rt => GetRouteTrackTrackField(rt)).ToArray();
            }
#nullable disable
        }

        internal enum PaxJobGenerationMode
        {
            None,
            Empty_OnPlatform,
            Empty_OffPlatform,
            Loaded,
            Loaded_Taken_From_Empty,
            Loaded_Taken_From_Loaded,
            Empty_Taken
        }

        private static PaxJobGenerationMode GetPaxJobGenerationMode(PaxJobContext genCtx)
        {
            if (!genCtx.JobTaken)
            {
                if (genCtx.CarsLoaded) return PaxJobGenerationMode.Loaded;
                if (genCtx.CarsCurrentTrack == genCtx.StartingTrack) return PaxJobGenerationMode.Empty_OnPlatform;
                else return PaxJobGenerationMode.Empty_OffPlatform;
            }
            else
            {
                if (genCtx.CarsLoaded)
                {
                    var chainSaveData = genCtx.JobChainSaveData;
                    if (!(chainSaveData == null || chainSaveData.currentJobTaskData == null || chainSaveData.currentJobTaskData.Length == 0))
                    {
                        var headTask = chainSaveData.currentJobTaskData[0];
                        if ((headTask.type == TaskType.Sequential) && (headTask is ComplexTaskSaveData complexData && complexData.tasksData.Length > 0))
                        {
                            //there are two warehouse tasks for each intermediate stations + the initial load + final unload - cahnged in PaxJ v.5.2, logic should still apply tho
                            int numWarehouseTasks = complexData.tasksData.Count(tsd => tsd.type == _CityLoadingTaskType);
                            if (numWarehouseTasks < 1) Main._modEntry.Logger.Error("Unexpected task structure for job " + genCtx.ForcedJobId);
                            if ((numWarehouseTasks % 2) == 0) return PaxJobGenerationMode.Loaded_Taken_From_Empty;
                            else return PaxJobGenerationMode.Loaded_Taken_From_Loaded;
                        }
                    }
                }
                else
                {
                    return PaxJobGenerationMode.Empty_Taken;
                }
            }

            return PaxJobGenerationMode.None;
        }

        private static void DestinationsTasks(ref List<Task> taskList, PaxJobContext genCtx)
        {
            var destinationTracks = genCtx.DestinationRouteTracks;
            for (int i = 0; i < destinationTracks.Count; i++)
            {
                bool isLast = (i == (destinationTracks.Count - 1));
                var unloadTask = CreatePaxBoardingTask(destinationTracks[i], genCtx.TotalCapacity, false, isLast, genCtx.JobDefinitionInstance);
                taskList.Add(unloadTask);

                if (!isLast)
                {
                    var loadTask = CreatePaxBoardingTask(destinationTracks[i], genCtx.TotalCapacity, true, false, genCtx.JobDefinitionInstance);
                    taskList.Add(loadTask);
                }
            }
        }

        private static SequentialTasks BuildUpTasks(PaxJobContext genCtx, PaxJobGenerationMode mode)
        {
            var taskList = new List<Task>();

            switch (mode)
            {
                case PaxJobGenerationMode.Empty_OnPlatform:
                    taskList.Add(CreatePaxBoardingTask(genCtx.StartingRouteTrack, genCtx.TotalCapacity, true, false, genCtx.JobDefinitionInstance));
                    DestinationsTasks(ref taskList, genCtx);
                    break;

                case PaxJobGenerationMode.Empty_OffPlatform:
                    taskList.Add(CreatePaxTransportTask(genCtx.CarsCurrentTrack, genCtx.StartingTrack, genCtx.JobDefinitionInstance));
                    taskList.Add(CreatePaxBoardingTask(genCtx.StartingRouteTrack, genCtx.TotalCapacity, true, false, genCtx.JobDefinitionInstance));
                    DestinationsTasks(ref taskList, genCtx);
                    break;

                case PaxJobGenerationMode.Loaded:
                    taskList.Add(CreatePaxTransportTask(genCtx.CarsCurrentTrack, genCtx.CarsCurrentTrack, genCtx.JobDefinitionInstance));
                    DestinationsTasks(ref taskList, genCtx);
                    break;

                case PaxJobGenerationMode.Loaded_Taken_From_Empty:
                    if (genCtx.WasModifiedAtSave) taskList.Add(CreatePaxTransportTask(genCtx.StartingTrack, genCtx.StartingTrack, genCtx.JobDefinitionInstance));
                    taskList.Add(CreatePaxBoardingTask(genCtx.StartingRouteTrack, genCtx.TotalCapacity, true, false, genCtx.JobDefinitionInstance));
                    DestinationsTasks(ref taskList, genCtx);
                    break;

                case PaxJobGenerationMode.Loaded_Taken_From_Loaded:
                    if (genCtx.WasModifiedAtSave) taskList.Add(CreatePaxTransportTask(genCtx.StartingTrack, genCtx.StartingTrack, genCtx.JobDefinitionInstance));
                    DestinationsTasks(ref taskList, genCtx);
                    break;

                case PaxJobGenerationMode.Empty_Taken:
                    if (genCtx.WasModifiedAtSave) taskList.Add(CreatePaxTransportTask(genCtx.CarsCurrentTrack, genCtx.StartingTrack, genCtx.JobDefinitionInstance));
                    taskList.Add(CreatePaxBoardingTask(genCtx.StartingRouteTrack, genCtx.TotalCapacity, true, false, genCtx.JobDefinitionInstance));
                    DestinationsTasks(ref taskList, genCtx);
                    break;

                default:
                    throw new InvalidOperationException("Task list could not be built! Mode was: " + mode.ToString());
            }

            return new(taskList);
        }

        private static bool GeneratePaxJob(PaxJobContext genCtx)
        {
            try
            {
                var genMode = GetPaxJobGenerationMode(genCtx);
                var chainData = (StationsChainData)CreateExpressStationsChainData(genCtx.OriginStation.ID, genCtx.DestinationRouteTracks.Select(rt => (GetRouteTrackTrackField(rt)).ID.yardId).ToArray()).Value;
                var superTask = BuildUpTasks(genCtx, genMode);
                Job newJob = new(superTask, genCtx.JobType, genCtx.TimeLimit, genCtx.InitialWage, chainData, genCtx.ForcedJobId, genCtx.RequiredLicenses);
                _StaticJobDefJobField.SetValue(genCtx.JobDefinitionInstance, newJob);

                if (genCtx.FromSave) Main._modEntry.Logger.Log($"Loading job {genCtx.ForcedJobId} from save with gen mode {genMode}");

                if (genMode == PaxJobGenerationMode.Empty_OnPlatform || genMode == PaxJobGenerationMode.Empty_OffPlatform || genMode == PaxJobGenerationMode.Empty_Taken)
                {
                    var startPlatController = GetPlatformControllerForTrack(GetRouteTrackPlatformIdField(genCtx.StartingRouteTrack)).Value;
                    _PlatformRegisterOutgoingJob.Invoke(startPlatController, new object[] { newJob, false });
                }

                for (int i = 0; i < genCtx.DestinationRouteTracks.Count - 1; i++)
                {
                    var destPlatController = GetPlatformControllerForTrack(GetRouteTrackPlatformIdField(genCtx.DestinationRouteTracks[i])).Value;
                    newJob.JobTaken += (j, _) => _PlatformRegisterOutgoingJob.Invoke(destPlatController, new object[] { j, false });
                }

                var finalPlatController = GetPlatformControllerForTrack(GetRouteTrackPlatformIdField(genCtx.DestinationRouteTracks.Last())).Value;
                newJob.JobTaken += (j, _) => _PlatformRegisterIncomingJob.Invoke(finalPlatController, new object[] { j, false });

                genCtx.OriginStation.AddJobToStation(genCtx.BaseJobDefinition.job);
                return true;
            }
            catch (Exception ex)
            {
                Main._modEntry.Logger.LogException("Failed to generate pax job due to: ", ex);
                return false;
            }
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
            var instanceBase = (StaticJobDefinition)__instance;
            List<Car> cars = (List<Car>)_TrainCarsToTransportProp.GetValue(__instance);
            RouteTrackRef startingRouteTrack = new(_StartingTrackField.GetValue(__instance));
            if (_DestinationTracksField.GetValue(__instance) is not Array rrTracksArray || rrTracksArray.Length == 0) return false;
            var destinationTracks = rrTracksArray.Cast<object>().Select(o => new RouteTrackRef(o)).ToList();

            var definitionGO = ((MonoBehaviour)__instance).gameObject;
            bool modified = definitionGO.GetComponent<PaxJobModifiedFlag>() != null;
            var takenFlag = definitionGO.GetComponent<PaxJobJobTakenFlag>();
            JobChainSaveData chainSaveData = takenFlag?.jobChainSaveData;
            bool taken = takenFlag != null;

            if ((cars == null) || (cars.Count == 0) || (startingRouteTrack.Value == null) || (destinationTracks == null))
            {
                Main._modEntry.Logger.Log("Failed to generate passengers job, bad data");
                return false;
            }
            bool loaded = cars.Any(c => c.CurrentCargoTypeInCar != CargoType.None);

            PaxJobContext genCtx = new(__instance, chainSaveData, jobOriginStation, timeLimit, initialWage, forcedJobId, requiredLicenses, cars, startingRouteTrack, destinationTracks, taken, modified);

            return !GeneratePaxJob(genCtx);
        }

        private static void LoadPassengerChain_Postfix(object __result, object[] __args)
        {
            if (__result != null)
            {
                Main._modEntry.Logger.Log("Flagg adding postfix runs");
                var jcc = (JobChainController)__result;
                var chainSaveData = (JobChainSaveData)__args[0];

                if (chainSaveData.currentJobTaskData == null || chainSaveData.currentJobTaskData.Length == 0) return;

                if (chainSaveData.jobTaken)
                {
                    Main._modEntry.Logger.Log($"Adding 'JobTaken' flag component to {jcc.jobChainGO.name} at {jcc.jobChain.First().logicStation.ID}");
                    var flag = jcc.jobChainGO.AddComponent<PaxJobJobTakenFlag>();
                    flag.jobChainSaveData = chainSaveData;
                }


                var headTask = chainSaveData.currentJobTaskData[0];
                if (headTask.type != TaskType.Sequential) return;

                if (headTask is ComplexTaskSaveData complexData && complexData.tasksData.Length > 0)
                {
                    var firstChild = complexData.tasksData[0];
                    if (firstChild.type == TaskType.Transport)
                    {
                        Main._modEntry.Logger.Log($"Adding 'modified' flag component to {jcc.jobChainGO.name} at {jcc.jobChain.First().logicStation.ID}");
                        jcc.jobChainGO.AddComponent<PaxJobModifiedFlag>();
                    }
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

        //this is very hackish, we can´t patch the constructor for PassengerJobs.Generation.RouteNode which demands unused tracks, so we patch that method to ignore it but only when the call came from us
        private static bool GetUnusedRouteTracks_Prefix(IEnumerable tracks, ref IEnumerable __result)
        {
            if (!BypassUnusedTracksFilter) return true;

            var validTracks = new List<object>();
            foreach (object rtrack in tracks)
            {
                RouteTrackRef track = new(rtrack);
                if (IsRouteTrackASegment(track))
                {
                    validTracks.Add(rtrack);
                }
                else if (YardTracksOrganizer.Instance.GetReservedSpace(GetRouteTrackTrackField(track)) <= _YTO_ReserveThreshold)
                {
                    validTracks.Add(rtrack);
                }
            }

            Array typedArray = Array.CreateInstance(_RouteTrack, validTracks.Count);
            for (int i = 0; i < validTracks.Count; i++)
            {
                typedArray.SetValue(validTracks[i], i);
            }
            __result = (IEnumerable)typedArray;
            return false;
        }

        private static void OnLastJobInChainCompletedReverse(JobChainController instance, Job lastJobInChain)
        {
            throw new NotImplementedException();
        }

        private static bool OnLastJobInChainCompletedOverride_Prefix(JobChainController __instance, Job lastJobInChain)
        {
            Main._modEntry.Logger.Log("Last pax job in chain completed");
            PersistentJobsMod.HarmonyPatches.JobChainControllers.JobChainController_OnLastJobInChainCompleted_Patch.DecideForConsistAfterJobChainCompletion(__instance, __instance.jobChain, lastJobInChain);
            OnLastJobInChainCompletedReverse(__instance, lastJobInChain);
            return false;
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
                if (taskData.instanceTaskType != _CityLoadingTaskType) continue;
                if (taskData.warehouseTaskType == WarehouseTaskType.Loading)
                {
                    loadingFound = true;
                    continue;
                }
                if (taskData.warehouseTaskType == WarehouseTaskType.Unloading && !loadingFound)
                {
                    var carsTrack = firstNestedTask.startTrackID;
                    if (carsTrack == null) break;

                    var destTrack = GetRouteTrackTrackField(GetPlatforms(GetStationData(carsTrack.yardId)).First());
                    var taskList = taskSequence.nestedTasks.ToList();
                    taskList.Insert(1, new Task_data(_CityLoadingTaskType, _CityLoadingTaskType, TaskState.InProgress, 0, 0, taskData.cars, carsTrack, destTrack.ID, WarehouseTaskType.None, new List<CargoType>(), 0, false, false, Array.Empty<Task_data>()));
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

        private static bool CreateCoupleTaskPage_Prefix(ref TemplatePaperData __result, object[] __args)
        {
            PassengerJobDataRef jobData = new(__args[0]);
            var baseJobData = (TransportJobData)(jobData.Value);
            var station = baseJobData.job.chainOriginStationInfo;
            var startingTrack = baseJobData.job.tasksData.FirstOrDefault(t => t.instanceTaskType == TaskType.Sequential)?.nestedTasks.FirstOrDefault(t => t.instanceTaskType == TaskType.Transport)?.startTrackID ?? baseJobData.startingTrack;
            __result = (TemplatePaperData)_CreateCoupleTaskPaperData.Invoke(null, new object[] { (int)__args[1], station.YardID, station.StationColor, startingTrack.TrackPartOnly, baseJobData.transportingCars, baseJobData.transportedCargoPerCar, (int)__args[2], (int)__args[3] });
            return false;
        }

        private static void GetBookletTemplateData_Postfix(Job_data job, ref List<TemplatePaperData> __result)
        {
            __result = __result.Where(t => t != null).ToList();
        }

#pragma warning restore IDE0060 // Remove unused parameter
        #endregion
    }
}