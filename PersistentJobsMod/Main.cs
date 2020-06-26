﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Harmony12;
using UnityEngine;
using UnityModManagerNet;
using DV;
using DV.Logic.Job;

namespace PersistentJobsMod
{
	static class Main
	{
		private static UnityModManager.ModEntry thisModEntry;
		private static bool isModBroken = false;
		private static float initialDistanceRegular = 0f;
		private static float initialDistanceAnyJobTaken = 0f;
		private static float PERIOD = 5f * 60f;
		public static float DVJobDestroyDistanceRegular { get { return initialDistanceRegular; } }

		static void Load(UnityModManager.ModEntry modEntry)
		{
			var harmony = HarmonyInstance.Create(modEntry.Info.Id);
			harmony.PatchAll(Assembly.GetExecutingAssembly());
			modEntry.OnToggle = OnToggle;
			thisModEntry = modEntry;
		}

		static bool OnToggle(UnityModManager.ModEntry modEntry, bool isTogglingOn)
		{
			if (SingletonBehaviour<UnusedTrainCarDeleter>.Instance == null)
			{
				// delay initialization
				modEntry.OnUpdate = (entry, delta) =>
				{
					if (SingletonBehaviour<UnusedTrainCarDeleter>.Instance != null)
					{
						modEntry.OnUpdate = null;
						ReplaceCoroutine(isTogglingOn);
					}
				};
				return true;
			}
			else
			{
				ReplaceCoroutine(isTogglingOn);
			}

			if (isModBroken)
			{
				return !isTogglingOn;
			}

			return true;
		}

		static void ReplaceCoroutine(bool isTogglingOn)
		{
			float? carsCheckPeriod = Traverse.Create(SingletonBehaviour<UnusedTrainCarDeleter>.Instance)
				.Field("DELETE_CARS_CHECK_PERIOD")
				.GetValue<float>();
			if (carsCheckPeriod == null)
			{
				carsCheckPeriod = 0.5f;
			}
			SingletonBehaviour<UnusedTrainCarDeleter>.Instance.StopAllCoroutines();
			if (isTogglingOn && !isModBroken)
			{
				thisModEntry.Logger.Log("Injected mod coroutine.");
				SingletonBehaviour<UnusedTrainCarDeleter>.Instance
					.StartCoroutine(TrainCarsCreateJobOrDeleteCheck(PERIOD, Mathf.Max(carsCheckPeriod.Value, 1.0f)));
			}
			else
			{
				thisModEntry.Logger.Log("Restored game coroutine.");
				SingletonBehaviour<UnusedTrainCarDeleter>.Instance.StartCoroutine(
					SingletonBehaviour<UnusedTrainCarDeleter>.Instance.TrainCarsDeleteCheck(carsCheckPeriod.Value)
				);
			}
		}

		static void OnCriticalFailure()
		{
			isModBroken = true;
			thisModEntry.Active = false;
			thisModEntry.Logger.Critical("Deactivating mod PersistentJobs due to critical failure!");
			thisModEntry.Logger.Warning("You can reactivate PersistentJobs by restarting the game, but this failure " +
				"type likely indicates an incompatibility between the mod and a recent game update. Please search the " +
				"mod's Github issue tracker for a relevant report. If none is found, please open one. Include the " +
				"exception message printed above and your game's current build number.");
		}

		// prevents jobs from expiring due to the player's distance from the station
		[HarmonyPatch(typeof(StationController), "ExpireAllAvailableJobsInStation")]
		class StationController_ExpireAllAvailableJobsInStation_Patch
		{
			static bool Prefix()
			{
				// skips the original method entirely when this mod is active
				return !thisModEntry.Active;
			}
		}

		// expands the distance at which the job generation trigger is rearmed
		[HarmonyPatch(typeof(StationJobGenerationRange))]
		[HarmonyPatchAll]
		class StationJobGenerationRange_AllMethods_Patch
		{
			static void Prefix(StationJobGenerationRange __instance, MethodBase __originalMethod)
			{
				try
				{
					// backup existing values before overwriting
					if (initialDistanceRegular < 1f)
					{
						initialDistanceRegular = __instance.destroyGeneratedJobsSqrDistanceRegular;
					}
					if (initialDistanceAnyJobTaken < 1f)
					{
						initialDistanceAnyJobTaken = __instance.destroyGeneratedJobsSqrDistanceAnyJobTaken;
					}

					if (thisModEntry.Active)
					{
						if (__instance.destroyGeneratedJobsSqrDistanceAnyJobTaken < 4000000f)
						{
							__instance.destroyGeneratedJobsSqrDistanceAnyJobTaken = 4000000f;
						}
						__instance.destroyGeneratedJobsSqrDistanceRegular =
							__instance.destroyGeneratedJobsSqrDistanceAnyJobTaken;
					}
					else
					{
						__instance.destroyGeneratedJobsSqrDistanceRegular = initialDistanceRegular;
						__instance.destroyGeneratedJobsSqrDistanceAnyJobTaken = initialDistanceAnyJobTaken;
					}
				}
				catch (Exception e)
				{
					thisModEntry.Logger.Error(string.Format(
						"Exception thrown during StationJobGenerationRange.{0} prefix patch:\n{1}",
						__originalMethod.Name,
						e.ToString()
					));
					OnCriticalFailure();
				}
			}
		}

		// expires a job if none of its cars are in range of the starting station on job start attempt
		[HarmonyPatch(typeof(JobValidator), "ProcessJobOverview")]
		class JobValidator_ProcessJobOverview_Patch
		{
			static void Prefix(
				List<StationController> ___allStations,
				DV.Printers.PrinterController ___bookletPrinter,
				JobOverview jobOverview)
			{
				try
				{
					if (!thisModEntry.Active)
					{
						return;
					}

					Job job = jobOverview.job;
					StationController stationController = ___allStations.FirstOrDefault(
						(StationController st) => st.logicStation.availableJobs.Contains(job)
					);

					if (___bookletPrinter.IsOnCooldown || job.State != JobState.Available || stationController == null)
					{
						return;
					}

					// expire the job if all associated cars are outside the job destruction range
					// the base method's logic will handle generating the expired report
					StationJobGenerationRange stationRange = Traverse.Create(stationController)
						.Field("stationRange")
						.GetValue<StationJobGenerationRange>();
					Task taskWithCarsInRangeOfStation = job.tasks.FirstOrDefault((Task t) =>
					{
						List<Car> cars = Traverse.Create(t).Field("cars").GetValue<List<Car>>();
						Car carInRangeOfStation = cars.FirstOrDefault((Car c) =>
						{
							TrainCar trainCar = TrainCar.GetTrainCarByCarGuid(c.carGuid);
							float distance =
								(trainCar.transform.position - stationRange.stationCenterAnchor.position).sqrMagnitude;
							return trainCar != null && distance <= initialDistanceRegular;
						});
						return carInRangeOfStation != null;
					});
					if (taskWithCarsInRangeOfStation == null)
					{
						job.ExpireJob();
					}
				}
				catch (Exception e)
				{
					thisModEntry.Logger.Error(string.Format(
						"Exception thrown during JobValidator.ProcessJobOverview prefix patch:\n{0}",
						e.ToString()
					));
					OnCriticalFailure();
				}
			}
		}

		// simply copied from the patched method
		// may help keep mod stable across game updates
		[HarmonyPatch(typeof(StationProceduralJobGenerator), "GenerateJobChain")]
		class StationProceduralJobGenerator_GenerateJobChain_Patch
		{
			static bool Prefix(
				System.Random rng,
				bool forceJobWithLicenseRequirementFulfilled,
				ref StationProceduralJobGenerator __instance,
				ref JobChainController __result,
				StationProceduralJobsRuleset ___generationRuleset,
				YardTracksOrganizer ___yto,
				Yard ___stYard)
			{
				if (thisModEntry.Active)
				{
					try
					{
						thisModEntry.Logger.Log("gen job chain setup");
						Traverse currentRng = Traverse.Create(__instance).Property("currentRng");
						if (
							!___generationRuleset.loadStartingJobSupported &&
							!___generationRuleset.haulStartingJobSupported &&
							!___generationRuleset.unloadStartingJobSupported &&
							!___generationRuleset.emptyHaulStartingJobSupported)
						{
							return true;
						}
						currentRng.SetValue(rng);
						thisModEntry.Logger.Log("collecting job types");
						List<JobType> spawnableJobTypes = new List<JobType>();
						if (___generationRuleset.loadStartingJobSupported)
						{
							spawnableJobTypes.Add(JobType.ShuntingLoad);
						}
						if (___generationRuleset.emptyHaulStartingJobSupported)
						{
							spawnableJobTypes.Add(JobType.EmptyHaul);
						}
						int countOutTracksAvailable = ___yto.FilterOutOccupiedTracks(___stYard.TransferOutTracks).Count;
						if (___generationRuleset.haulStartingJobSupported && countOutTracksAvailable > 0)
						{
							spawnableJobTypes.Add(JobType.Transport);
						}
						int countInTracksAvailable = ___yto.FilterOutReservedTracks(
							___yto.FilterOutOccupiedTracks(___stYard.TransferInTracks)
						).Count;
						if (___generationRuleset.unloadStartingJobSupported && countInTracksAvailable > 0)
						{
							spawnableJobTypes.Add(JobType.ShuntingUnload);
						}
						JobChainController jobChainController = null;
						if (forceJobWithLicenseRequirementFulfilled)
						{
							if (
								spawnableJobTypes.Contains(JobType.Transport) &&
								LicenseManager.IsJobLicenseAcquired(JobLicenses.FreightHaul))
							{
								thisModEntry.Logger.Log("A");
								jobChainController = Traverse.Create(__instance)
									.Method("GenerateOutChainJob", new Type[] { typeof(JobType), typeof(bool) })
									.GetValue<JobChainController>(JobType.Transport, true);
								if (jobChainController != null)
								{
									__result = jobChainController;
									return false;
								}
							}
							if (
								spawnableJobTypes.Contains(JobType.EmptyHaul) &&
								LicenseManager.IsJobLicenseAcquired(JobLicenses.LogisticalHaul))
							{
								thisModEntry.Logger.Log("B");
								jobChainController = Traverse.Create(__instance)
									.Method("GenerateEmptyHaul", new Type[] { typeof(bool) })
									.GetValue<JobChainController>(true);
								if (jobChainController != null)
								{
									__result = jobChainController;
									return false;
								}
							}
							if (
								spawnableJobTypes.Contains(JobType.ShuntingLoad) &&
								LicenseManager.IsJobLicenseAcquired(JobLicenses.Shunting))
							{
								thisModEntry.Logger.Log("C");
								jobChainController = Traverse.Create(__instance)
									.Method("GenerateOutChainJob", new Type[] { typeof(JobType), typeof(bool) })
									.GetValue<JobChainController>(JobType.ShuntingLoad, true);
								if (jobChainController != null)
								{
									__result = jobChainController;
									return false;
								}
							}
							if (
								spawnableJobTypes.Contains(JobType.ShuntingUnload) &&
								LicenseManager.IsJobLicenseAcquired(JobLicenses.Shunting))
							{
								thisModEntry.Logger.Log("D");
								jobChainController = Traverse.Create(__instance)
									.Method("GenerateInChainJob", new Type[] { typeof(JobType), typeof(bool) })
									.GetValue<JobChainController>(JobType.ShuntingUnload, true);
								if (jobChainController != null)
								{
									__result = jobChainController;
									return false;
								}
							}
							__result = null;
							return false;
						}
						if (
							spawnableJobTypes.Contains(JobType.Transport) &&
							countOutTracksAvailable > Mathf.FloorToInt(0.399999976f * (float)___stYard.TransferOutTracks.Count))
						{
							thisModEntry.Logger.Log("E");
							JobType startingJobType = JobType.Transport;
							jobChainController = Traverse.Create(__instance)
								.Method("GenerateOutChainJob", new Type[] { typeof(JobType), typeof(bool) })
								.GetValue<JobChainController>(startingJobType, false);
						}
						else
						{
							if (spawnableJobTypes.Count == 0)
							{
								__result = null;
								return false;
							}
							thisModEntry.Logger.Log("F");
							JobType startingJobType = Utilities.GetRandomFromEnumerable(spawnableJobTypes, rng);
							/* TODO: how to Traverse generic methods?
							JobType startingJobType = Traverse.Create(__instance)
								.Method("GetRandomFromList", new Type[] { typeof(List<T>) })
								.GetValue<JobType>(spawnableJobTypes);*/
							switch (startingJobType)
							{
								case JobType.ShuntingLoad:
								case JobType.Transport:
									thisModEntry.Logger.Log("G");
									jobChainController = Traverse.Create(__instance)
										.Method("GenerateOutChainJob", new Type[] { typeof(JobType), typeof(bool) })
										.GetValue<JobChainController>(startingJobType, false);
									break;
								case JobType.ShuntingUnload:
									thisModEntry.Logger.Log("H");
									jobChainController = Traverse.Create(__instance)
										.Method("GenerateInChainJob", new Type[] { typeof(JobType), typeof(bool) })
										.GetValue<JobChainController>(startingJobType, false);
									break;
								case JobType.EmptyHaul:
									thisModEntry.Logger.Log("I");
									jobChainController = Traverse.Create(__instance)
										.Method("GenerateEmptyHaul", new Type[] { typeof(bool) })
										.GetValue<JobChainController>(false);
									break;
							}
						}
						currentRng.SetValue(null);
						__result = jobChainController;
						return false;
					}
					catch (Exception e)
					{
						thisModEntry.Logger.Error(string.Format(
							"Exception thrown during {0}.{1} {2} patch:\n{3}",
							"StationProceduralJobGenerator",
							"GenerateJobChain",
							"prefix",
							e.ToString()
						));
						OnCriticalFailure();
					}
				}
				return true;
			}
		}

		// generates shunting unload jobs
		[HarmonyPatch(typeof(StationProceduralJobGenerator), "GenerateInChainJob")]
		class StationProceduralJobGenerator_GenerateInChainJob_Patch
		{
			static bool Prefix(
				ref JobChainController __result,
				StationController ___stationController,
				JobType startingJobType,
				bool forceFulfilledLicenseRequirements = false)
			{
				if (thisModEntry.Active)
				{
					try
					{
						if (startingJobType == JobType.ShuntingUnload)
						{
							thisModEntry.Logger.Log("gen in shunting unload");
							__result = ShuntingUnloadJobProceduralGenerator.GenerateShuntingUnloadJobWithCarSpawning(
								___stationController,
								forceFulfilledLicenseRequirements,
								new System.Random(Environment.TickCount));
							return false;
						}
						thisModEntry.Logger.Warning(string.Format(
							"Got unexpected JobType.{0} in {1}.{2} {3} patch. Falling back to base method.",
							startingJobType.ToString(),
							"StationProceduralJobGenerator",
							"GenerateInChainJob",
							"prefix"
						));
					}
					catch (Exception e)
					{
						thisModEntry.Logger.Error(string.Format(
							"Exception thrown during {0}.{1} {2} patch:\n{3}",
							"StationProceduralJobGenerator",
							"GenerateInChainJob",
							"prefix",
							e.ToString()
						));
						OnCriticalFailure();
					}
				}
				return true;
			}
		}

		// generates shunting load jobs & freight haul jobs
		[HarmonyPatch(typeof(StationProceduralJobGenerator), "GenerateOutChainJob")]
		class StationProceduralJobGenerator_GenerateOutChainJob_Patch
		{
			static bool Prefix(
				ref JobChainController __result,
				StationController ___stationController,
				JobType startingJobType,
				bool forceFulfilledLicenseRequirements = false)
			{
				if (thisModEntry.Active)
				{
					try
					{
						if (startingJobType == JobType.ShuntingLoad)
						{
							thisModEntry.Logger.Log("gen out shunting load");
							__result = ShuntingLoadJobProceduralGenerator.GenerateShuntingLoadJobWithCarSpawning(
								___stationController,
								forceFulfilledLicenseRequirements,
								new System.Random(Environment.TickCount));
							return false;
						}
						else if (startingJobType == JobType.Transport)
						{
							thisModEntry.Logger.Log("gen out transport");
							__result = TransportJobProceduralGenerator.GenerateTransportJobWithCarSpawning(
								___stationController,
								forceFulfilledLicenseRequirements,
								new System.Random(Environment.TickCount));
							return false;
						}
						thisModEntry.Logger.Warning(string.Format(
							"Got unexpected JobType.{0} in {1}.{2} {3} patch. Falling back to base method.",
							startingJobType.ToString(),
							"StationProceduralJobGenerator",
							"GenerateOutChainJob",
							"prefix"
						));
					}
					catch (Exception e)
					{
						thisModEntry.Logger.Error(string.Format(
							"Exception thrown during {0}.{1} {2} patch:\n{3}",
							"StationProceduralJobGenerator",
							"GenerateOutChainJob",
							"prefix",
							e.ToString()
						));
						OnCriticalFailure();
					}
				}
				return true;
			}
		}

		// generates logistical haul jobs
		// patch currently skipped
		[HarmonyPatch(typeof(StationProceduralJobGenerator), "GenerateEmptyHaul")]
		class StationProceduralJobGenerator_GenerateEmptyHaul_Patch
		{
			static bool Prefix()
			{
				if (thisModEntry.Active && false)
				{
					try
					{
						// TODO: implement this!
					}
					catch (Exception e)
					{
						thisModEntry.Logger.Error(string.Format(
							"Exception thrown during {0}.{1} {2} patch:\n{3}",
							"StationProceduralJobGenerator",
							"GenerateEmptyHaul",
							"prefix",
							e.ToString()
						));
						OnCriticalFailure();
					}
				}
				return true;
			}
		}

		// divert cars that can be loaded at the current station for later generation of ShuntingLoad jobs
		[HarmonyPatch(typeof(JobChainControllerWithEmptyHaulGeneration), "OnLastJobInChainCompleted")]
		class JobChainControllerWithEmptyHaulGeneration_OnLastJobInChainCompleted_Patch
		{
			static void Prefix(
				JobChainControllerWithEmptyHaulGeneration __instance,
				List<StaticJobDefinition> ___jobChain,
				Job lastJobInChain)
			{
				thisModEntry.Logger.Log("last job chain empty haul gen");
				try
				{
					StaticJobDefinition lastJobDef = ___jobChain[___jobChain.Count - 1];
					if (lastJobDef.job == lastJobInChain && lastJobInChain.jobType == JobType.ShuntingUnload)
					{
						thisModEntry.Logger.Log("checking static definition type");
						StaticShuntingUnloadJobDefinition unloadJobDef = lastJobDef as StaticShuntingUnloadJobDefinition;
						if (unloadJobDef != null)
						{
							StationController station = SingletonBehaviour<LogicController>.Instance
								.YardIdToStationController[lastJobInChain.chainData.chainDestinationYardId];
							List<CargoGroup> availableCargoGroups = station.proceduralJobsRuleset.outputCargoGroups;

							thisModEntry.Logger.Log("diverting trainCars");
							// if a trainCar set can be reused at the current station, keep them there
							foreach (CarsPerTrack cpt in unloadJobDef.carsPerDestinationTrack)
							{
								// check if there is any cargoGroup that satisfies all the cars
								if (availableCargoGroups.Any(
									cg => cpt.cars.All(
										c => Utilities.GetCargoTypesForCarType(c.carType).Intersect(cg.cargoTypes).Any())))
								{
									// removing the trainCars prevents generating an EmptyHaul job for them
									// they will be candidates for new jobs after the player leaves the area
									cpt.cars.ForEach(
										c => __instance.trainCarsForJobChain.Remove(TrainCar.logicCarToTrainCar[c]));
								}
							}
						}
					}
				}
				catch (Exception e)
				{
					// TODO: log & handle exception
				}
			}
		}

		// tries to generate new shunting load jobs for the train cars marked for deletion
		// failing that, the train cars are deleted
		[HarmonyPatch(typeof(UnusedTrainCarDeleter), "InstantConditionalDeleteOfUnusedCars")]
		class UnusedTrainCarDeleter_InstantConditionalDeleteOfUnusedCars_Patch
		{
			static bool Prefix(
				UnusedTrainCarDeleter __instance,
				List<TrainCar> ___unusedTrainCarsMarkedForDelete,
				Dictionary<TrainCar, CarVisitChecker> ___carVisitCheckersMap)
			{
				if (thisModEntry.Active)
				{
					try
					{
						if (___unusedTrainCarsMarkedForDelete.Count == 0)
						{
							return false;
						}

						thisModEntry.Logger.Log("collecting deletion candidates");
						List<TrainCar> trainCarsToDelete = new List<TrainCar>();
						for (int i = ___unusedTrainCarsMarkedForDelete.Count - 1; i >= 0; i--)
						{
							TrainCar trainCar = ___unusedTrainCarsMarkedForDelete[i];
							if (trainCar == null)
							{
								___unusedTrainCarsMarkedForDelete.RemoveAt(i);
								continue;
							}
							bool areDeleteConditionsFulfilled = Traverse.Create(__instance)
								.Method("AreDeleteConditionsFulfilled", new Type[] { typeof(TrainCar) })
								.GetValue<bool>(trainCar);
							if (areDeleteConditionsFulfilled)
							{
								___unusedTrainCarsMarkedForDelete.RemoveAt(i);
								trainCarsToDelete.Add(trainCar);
								___carVisitCheckersMap.Remove(trainCar);
							}
						}
						if (trainCarsToDelete.Count == 0)
						{
							return false;
						}

						// ------ BEGIN JOB GENERATION ------
						// group trainCars by trainset
						thisModEntry.Logger.Log("grouping trainCars by trainSet");
						Dictionary<Trainset, List<TrainCar>> trainCarsPerTrainSet
								= ShuntingLoadJobProceduralGenerator.GroupTrainCarsByTrainset(trainCarsToDelete);

						// group trainCars sets by nearest stationController
						thisModEntry.Logger.Log("grouping trainSets by station");
						Dictionary<StationController, List<(List<TrainCar>, List<CargoGroup>)>> cgsPerTcsPerSc
							= ShuntingLoadJobProceduralGenerator.GroupTrainCarSetsByNearestStation(trainCarsPerTrainSet);

						// populate possible cargoGroups per group of trainCars
						thisModEntry.Logger.Log("populating cargoGroups");
						ShuntingLoadJobProceduralGenerator.PopulateCargoGroupsPerTrainCarSet(cgsPerTcsPerSc);

						// pick new jobs for the trainCars at each station
						thisModEntry.Logger.Log("picking jobs");
						System.Random rng = new System.Random(Environment.TickCount);
						List<(StationController, List<CarsPerTrack>, StationController, List<TrainCar>, List<CargoType>)>
							jobInfos = ShuntingLoadJobProceduralGenerator
								.ComputeJobInfosFromCargoGroupsPerTrainCarSetPerStation(cgsPerTcsPerSc, rng);

						// try to generate jobs
						thisModEntry.Logger.Log("generating jobs");
						IEnumerable<(List<TrainCar>, JobChainController)> trainCarListJobChainControllerPairs
							= ShuntingLoadJobProceduralGenerator.doJobGeneration(jobInfos, rng);

						// preserve trainCars for which a new job was generated
						thisModEntry.Logger.Log("preserving cars");
						foreach ((List<TrainCar> trainCars, JobChainController jcc) in trainCarListJobChainControllerPairs)
						{
							if (jcc != null)
							{
								trainCars.ForEach(tc => trainCarsToDelete.Remove(tc));
							}
						}
						// ------ END JOB GENERATION ------

						SingletonBehaviour<CarSpawner>.Instance.DeleteTrainCars(trainCarsToDelete, true);
						return false;
					}
					catch (Exception e)
					{
						thisModEntry.Logger.Error(string.Format(
							"Exception thrown during {0}.{1} {2} patch:\n{3}",
							"UnusedTrainCarDeleter",
							"InstantConditionalDeleteOfUnusedCars",
							"prefix",
							e.ToString()
						));
						OnCriticalFailure();
					}
				}
				return true;
			}
		}

		// override/replacement for UnusedTrainCarDeleter.TrainCarsDeleteCheck coroutine
		// tries to generate new shunting load jobs for the train cars marked for deletion
		// failing that, the train cars are deleted
		public static IEnumerator TrainCarsCreateJobOrDeleteCheck(float period, float interopPeriod)
		{
			List<TrainCar> trainCarsToDelete = null;
			List<TrainCar> trainCarCandidatesForDelete = null;
			Traverse unusedTrainCarDeleterTraverser = null;
			List<TrainCar> unusedTrainCarsMarkedForDelete = null;
			Dictionary<TrainCar, DV.CarVisitChecker> carVisitCheckersMap = null;
			Traverse AreDeleteConditionsFulfilledMethod = null;
			try
			{
				trainCarsToDelete = new List<TrainCar>();
				trainCarCandidatesForDelete = new List<TrainCar>();
				unusedTrainCarDeleterTraverser = Traverse.Create(SingletonBehaviour<UnusedTrainCarDeleter>.Instance);
				unusedTrainCarsMarkedForDelete = unusedTrainCarDeleterTraverser
					.Field("unusedTrainCarsMarkedForDelete")
					.GetValue<List<TrainCar>>();
				carVisitCheckersMap = unusedTrainCarDeleterTraverser
					.Field("carVisitCheckersMap")
					.GetValue<Dictionary<TrainCar, DV.CarVisitChecker>>();
				AreDeleteConditionsFulfilledMethod
					= unusedTrainCarDeleterTraverser.Method("AreDeleteConditionsFulfilled", new Type[] { typeof(TrainCar) });
			}
			catch (Exception e)
			{
				thisModEntry.Logger.Error(string.Format(
					"Exception thrown during TrainCarsCreateJobOrDeleteCheck setup:\n{0}",
					e.ToString()
				));
				OnCriticalFailure();
			}
			for (; ; )
			{
				yield return WaitFor.SecondsRealtime(period);

				try
				{
					if (PlayerManager.PlayerTransform == null || FastTravelController.IsFastTravelling)
					{
						continue;
					}

					if (unusedTrainCarsMarkedForDelete.Count == 0)
					{
						if (carVisitCheckersMap.Count != 0)
						{
							carVisitCheckersMap.Clear();
						}
						continue;
					}
				}
				catch (Exception e)
				{
					thisModEntry.Logger.Error(string.Format(
						"Exception thrown during TrainCarsCreateJobOrDeleteCheck skip checks:\n{0}",
						e.ToString()
					));
					OnCriticalFailure();
				}

				thisModEntry.Logger.Log("collecting deletion candiates (coroutine)");
				try
				{
					trainCarCandidatesForDelete.Clear();
					for (int i = unusedTrainCarsMarkedForDelete.Count - 1; i >= 0; i--)
					{
						TrainCar trainCar = unusedTrainCarsMarkedForDelete[i];
						if (trainCar == null)
						{
							unusedTrainCarsMarkedForDelete.RemoveAt(i);
						}
						else if (AreDeleteConditionsFulfilledMethod.GetValue<bool>(trainCar))
						{
							unusedTrainCarsMarkedForDelete.RemoveAt(i);
							trainCarCandidatesForDelete.Add(trainCar);
						}
					}
					if (trainCarCandidatesForDelete.Count == 0)
					{
						continue;
					}
				}
				catch (Exception e)
				{
					thisModEntry.Logger.Error(string.Format(
						"Exception thrown during TrainCarsCreateJobOrDeleteCheck delete candidate collection:\n{0}",
						e.ToString()
					));
					OnCriticalFailure();
				}

				yield return WaitFor.SecondsRealtime(interopPeriod);

				// ------ BEGIN JOB GENERATION ------
				// group trainCars by trainset
				thisModEntry.Logger.Log("grouping trainCars by trainSet (coroutine)");
				Dictionary<Trainset, List<TrainCar>> trainCarsPerTrainSet = null;
				try
				{
					trainCarsPerTrainSet
						= ShuntingLoadJobProceduralGenerator.GroupTrainCarsByTrainset(trainCarCandidatesForDelete);
				}
				catch (Exception e)
				{
					thisModEntry.Logger.Error(string.Format(
						"Exception thrown during TrainCarsCreateJobOrDeleteCheck trainset grouping:\n{0}",
						e.ToString()
					));
					OnCriticalFailure();
				}

				yield return WaitFor.SecondsRealtime(interopPeriod);

				// group trainCars sets by nearest stationController
				thisModEntry.Logger.Log("grouping trainSets by station (coroutine)");
				Dictionary<StationController, List<(List<TrainCar>, List<CargoGroup>)>> cgsPerTcsPerSc = null;
				try
				{
					cgsPerTcsPerSc
						= ShuntingLoadJobProceduralGenerator.GroupTrainCarSetsByNearestStation(trainCarsPerTrainSet);
				}
				catch (Exception e)
				{
					thisModEntry.Logger.Error(string.Format(
						"Exception thrown during TrainCarsCreateJobOrDeleteCheck station grouping:\n{0}",
						e.ToString()
					));
					OnCriticalFailure();
				}

				yield return WaitFor.SecondsRealtime(interopPeriod);

				// populate possible cargoGroups per group of trainCars
				thisModEntry.Logger.Log("populating cargoGroups (coroutine)");
				try
				{
					ShuntingLoadJobProceduralGenerator.PopulateCargoGroupsPerTrainCarSet(cgsPerTcsPerSc);
				}
				catch (Exception e)
				{
					thisModEntry.Logger.Error(string.Format(
						"Exception thrown during TrainCarsCreateJobOrDeleteCheck cargoGroup population:\n{0}",
						e.ToString()
					));
					OnCriticalFailure();
				}

				yield return WaitFor.SecondsRealtime(interopPeriod);

				// pick new jobs for the trainCars at each station
				thisModEntry.Logger.Log("picking jobs (coroutine)");
				System.Random rng = new System.Random(Environment.TickCount);
				List<(StationController, List<CarsPerTrack>, StationController, List<TrainCar>, List<CargoType>)>
					jobInfos = null;
				try
				{
					jobInfos = ShuntingLoadJobProceduralGenerator
						.ComputeJobInfosFromCargoGroupsPerTrainCarSetPerStation(cgsPerTcsPerSc, rng);
				}
				catch (Exception e)
				{
					thisModEntry.Logger.Error(string.Format(
						"Exception thrown during TrainCarsCreateJobOrDeleteCheck job info selection:\n{0}",
						e.ToString()
					));
					OnCriticalFailure();
				}

				yield return WaitFor.SecondsRealtime(interopPeriod);

				// try to generate jobs
				thisModEntry.Logger.Log("generating jobs (coroutine)");
				IEnumerable<(List<TrainCar>, JobChainController)> trainCarListJobChainControllerPairs = null;
				try
				{
					trainCarListJobChainControllerPairs
						= ShuntingLoadJobProceduralGenerator.doJobGeneration(jobInfos, rng);
				}
				catch (Exception e)
				{
					thisModEntry.Logger.Error(string.Format(
						"Exception thrown during TrainCarsCreateJobOrDeleteCheck job generation:\n{0}",
						e.ToString()
					));
					OnCriticalFailure();
				}

				yield return WaitFor.SecondsRealtime(interopPeriod);

				// preserve trainCars for which a new job was generated
				thisModEntry.Logger.Log("preserving cars (coroutine)");
				try
				{
					foreach ((List<TrainCar> trainCars, JobChainController jcc) in trainCarListJobChainControllerPairs)
					{
						if (jcc != null)
						{
							trainCars.ForEach(tc => trainCarCandidatesForDelete.Remove(tc));
						}
					}
				}
				catch (Exception e)
				{
					thisModEntry.Logger.Error(string.Format(
						"Exception thrown during TrainCarsCreateJobOrDeleteCheck trainCar preservation:\n{0}",
						e.ToString()
					));
					OnCriticalFailure();
				}
				// ------ END JOB GENERATION ------

				yield return WaitFor.SecondsRealtime(interopPeriod);

				thisModEntry.Logger.Log("deleting cars (coroutine)");
				try
				{
					trainCarsToDelete.Clear();
					for (int j = trainCarCandidatesForDelete.Count - 1; j >= 0; j--)
					{
						TrainCar trainCar2 = trainCarCandidatesForDelete[j];
						if (trainCar2 == null)
						{
							trainCarCandidatesForDelete.RemoveAt(j);
						}
						else if (AreDeleteConditionsFulfilledMethod.GetValue<bool>(trainCar2))
						{
							trainCarCandidatesForDelete.RemoveAt(j);
							carVisitCheckersMap.Remove(trainCar2);
							trainCarsToDelete.Add(trainCar2);
						}
						else
						{
							Debug.LogWarning(string.Format(
								"Returning {0} to unusedTrainCarsMarkedForDelete list. PlayerTransform was outside" +
								" of DELETE_SQR_DISTANCE_FROM_TRAINCAR range of train car, but after short period it" +
								" was back in range!",
								trainCar2.name
							));
							trainCarCandidatesForDelete.RemoveAt(j);
							unusedTrainCarsMarkedForDelete.Add(trainCar2);
						}
					}
					if (trainCarsToDelete.Count != 0)
					{
						SingletonBehaviour<CarSpawner>.Instance
							.DeleteTrainCars(new List<TrainCar>(trainCarsToDelete), false);
					}
				}
				catch (Exception e)
				{
					thisModEntry.Logger.Error(string.Format(
						"Exception thrown during TrainCarsCreateJobOrDeleteCheck car deletion:\n{0}",
						e.ToString()
					));
					OnCriticalFailure();
				}
			}
		}

		// chooses the shortest track with enough space (instead of the first track found)
		[HarmonyPatch(typeof(YardTracksOrganizer), "GetTrackThatHasEnoughFreeSpace")]
		class YardTracksOrganizer_GetTrackThatHasEnoughFreeSpace_Patch
		{
			static bool Prefix(YardTracksOrganizer __instance, ref Track __result, List<Track> tracks, float requiredLength)
			{
				if (thisModEntry.Active)
				{
					thisModEntry.Logger.Log("getting track with free space");
					try
					{
						__result = null;
						SortedList<double, Track> tracksSortedByLength = new SortedList<double, Track>();
						foreach (Track track in tracks)
						{
							double freeSpaceOnTrack = __instance.GetFreeSpaceOnTrack(track);
							if (freeSpaceOnTrack > (double)requiredLength)
							{
								tracksSortedByLength.Add(freeSpaceOnTrack, track);
							}
						}
						if (tracksSortedByLength.Count > 0)
						{
							__result = tracksSortedByLength.First().Value;
						}
						return false;
					}
					catch (Exception e)
					{
						thisModEntry.Logger.Warning(string.Format(
							"Exception thrown during {0}.{1} {2} patch:\n{3}\nFalling back on base method.",
							"YardTracksOrganizer",
							"GetTrackThatHasEnoughFreeSpace",
							"prefix",
							e.ToString()
						));
						// OnCriticalFailure();
					}
				}
				return true;
			}
		}
	}
}