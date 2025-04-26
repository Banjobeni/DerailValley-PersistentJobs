using System;
using System.Collections.Generic;
using System.Linq;
using DV.Logic.Job;
using DV.ThingTypes;
using PersistentJobsMod.Extensions;
using UnityEngine;

namespace PersistentJobsMod.Model {
    public sealed class OutgoingCargoGroup {
        public IReadOnlyList<CargoType> CargoTypes { get; }
        public IReadOnlyList<OutgoingCargoGroupDestination> Destinations { get; }
        public IReadOnlyList<WarehouseMachine> SourceWarehouseMachines { get; }
        public JobLicenses EmptyHaulLicenses { get; }
        public JobLicenses LoadedCargoLicenses { get; }

        public OutgoingCargoGroup(IReadOnlyList<CargoType> cargoTypes, IReadOnlyList<OutgoingCargoGroupDestination> destinations, IReadOnlyList<WarehouseMachine> sourceWarehouseMachines, JobLicenses emptyHaulLicenses, JobLicenses loadedCargoLicenses) {
            CargoTypes = cargoTypes;
            Destinations = destinations;
            SourceWarehouseMachines = sourceWarehouseMachines;
            EmptyHaulLicenses = emptyHaulLicenses;
            LoadedCargoLicenses = loadedCargoLicenses;
        }
    }

    public sealed class OutgoingCargoGroupDestination : IReassignableTrainCarRelationWithMaxTrackLength {
        public StationController Station { get; }
        public IReadOnlyList<WarehouseMachine> WarehouseMachines { get; }
        public double MaxSourceDestinationTrainLength { get; }

        public OutgoingCargoGroupDestination(StationController station, IReadOnlyList<WarehouseMachine> warehouseMachines, double maxSourceDestinationTrainLength) {
            Station = station;
            WarehouseMachines = warehouseMachines;
            MaxSourceDestinationTrainLength = maxSourceDestinationTrainLength;
        }

        public double RelationMaxTrainLength => MaxSourceDestinationTrainLength;
    }

    public sealed class IncomingCargoGroup : IReassignableTrainCarRelationWithMaxTrackLength {
        public IncomingCargoGroup(StationController sourceStation, IReadOnlyList<CargoType> cargoTypes, IReadOnlyList<WarehouseMachine> destinationWarehouseMachines, double warehouseMachineTrackLength) {
            SourceStation = sourceStation;
            CargoTypes = cargoTypes;
            DestinationWarehouseMachines = destinationWarehouseMachines;
            WarehouseMachineTrackLength = warehouseMachineTrackLength;
        }

        public StationController SourceStation { get; }
        public IReadOnlyList<CargoType> CargoTypes { get; }
        public IReadOnlyList<WarehouseMachine> DestinationWarehouseMachines { get; }
        public double WarehouseMachineTrackLength { get; }
        
        public double RelationMaxTrainLength => WarehouseMachineTrackLength;
    }

    public static class DetailedCargoGroups {
        private static IReadOnlyDictionary<StationController, IReadOnlyList<OutgoingCargoGroup>> _station2OutgoingCargoGroups;
        private static IReadOnlyDictionary<StationController, IReadOnlyList<IncomingCargoGroup>> _station2IncomingCargoGroups;
        private static IReadOnlyDictionary<CargoType, IReadOnlyList<OutgoingCargoGroupDestination>> _cargoType2Destinations;

        public static void Initialize() {
            _station2OutgoingCargoGroups = StationController.allStations.ToDictionary(s => s, CreateOutgoingCargoGroups);

            var count = _station2OutgoingCargoGroups.Values.SelectMany(v => v).SelectMany(cg => cg.Destinations).Count();

            Main._modEntry.Logger.Log($"Initialized DetailedCargoGroups with a total of {count} source-destination items");

            _station2IncomingCargoGroups =
                _station2OutgoingCargoGroups.SelectMany(kvp => kvp.Value.SelectMany(ocg => ocg.Destinations.Select(ocgd => (DestinationStation: ocgd.Station, IncomingCargoGroup: new IncomingCargoGroup(kvp.Key, ocg.CargoTypes, ocgd.WarehouseMachines, ocgd.MaxSourceDestinationTrainLength)))))
                    .GroupBy(rel => rel.DestinationStation)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<IncomingCargoGroup>)g.Select(rel => rel.IncomingCargoGroup).ToList());

            _cargoType2Destinations =
                _station2OutgoingCargoGroups.Values.SelectMany(ocgs => ocgs)
                    .SelectMany(ocg => ocg.CargoTypes.Select(ct => (CargoType: ct, ocg.Destinations)))
                    .GroupBy(ctd => ctd.CargoType)
                    .ToDictionary(g => g.Key, g => (IReadOnlyList<OutgoingCargoGroupDestination>)g.SelectMany(ctd => ctd.Destinations).ToList());
        }

        private static IReadOnlyList<OutgoingCargoGroup> CreateOutgoingCargoGroups(StationController station) {
            return station.proceduralJobsRuleset.outputCargoGroups.Select(cg => CreateOutgoingCargoGroup(station, cg)).ToList();
        }

        private static OutgoingCargoGroup CreateOutgoingCargoGroup(StationController station, CargoGroup cargoGroup) {
            var sourceWarehouseMachines = station.logicStation.yard.GetWarehouseMachinesThatSupportCargoTypes(cargoGroup.cargoTypes).ToList();

            if (!sourceWarehouseMachines.Any()) {
                throw new InvalidOperationException($"Source station {station.logicStation.ID} does not have any warehouse machine that supports all cargo types of a cargo group: {string.Join(", ", cargoGroup.cargoTypes)}");
            }

            var sourceWarehouseMachineTrackLength = sourceWarehouseMachines.Select(wh => wh.WarehouseTrack.GetTotalUsableTrackLength()).Aggregate(Math.Min);

            var cargoGroupDestinations = cargoGroup.stations.Select(ds => CreateOutgoingCargoGroupDestionation(station, ds, cargoGroup, sourceWarehouseMachineTrackLength)).ToList();

            return new OutgoingCargoGroup(cargoGroup.cargoTypes.ToList(), cargoGroupDestinations, sourceWarehouseMachines, cargoGroup.CarRequiredLicenses, cargoGroup.CargoRequiredLicenses);
        }

        private static OutgoingCargoGroupDestination CreateOutgoingCargoGroupDestionation(StationController sourceStation, StationController destinationStation, CargoGroup cargoGroup, double sourceWarehouseMachineTrackLength) {
            var destinationWarehouseMachines = destinationStation.logicStation.yard.GetWarehouseMachinesThatSupportCargoTypes(cargoGroup.cargoTypes).ToList();

            if (!destinationWarehouseMachines.Any()) {
                throw new InvalidOperationException($"Destination station {destinationStation.logicStation.ID} does not have any warehouse machine that supports all cargo types of a cargo group: {string.Join(", ", cargoGroup.cargoTypes)}");
            }

            var destinationWarehouseMachineTrackLength = destinationWarehouseMachines.Select(wh => wh.WarehouseTrack.GetTotalUsableTrackLength()).Aggregate(Math.Min);

            if (!destinationStation.logicStation.yard.TransferInTracks.Any()) {
                throw new InvalidOperationException($"Station {destinationStation.logicStation.ID} is used as a target station in cargo group {string.Join(", ", cargoGroup.cargoTypes)}, but does not have any input tracks");
            }

            var inputTracksMaxLength = destinationStation.logicStation.yard.TransferInTracks.Select(t => t.GetTotalUsableTrackLength()).Aggregate(Math.Max);

            var maxSourceDestinationTrainLength = new[] { sourceWarehouseMachineTrackLength, destinationWarehouseMachineTrackLength, inputTracksMaxLength }.Aggregate(Math.Min);

            return new OutgoingCargoGroupDestination(destinationStation, destinationWarehouseMachines, maxSourceDestinationTrainLength);
        }

        public static bool IsInitialized => _station2OutgoingCargoGroups != null;

        public static IReadOnlyList<OutgoingCargoGroup> GetOutgoingCargoGroups(StationController station) {
            if (_station2OutgoingCargoGroups == null) {
                throw new InvalidOperationException("Not initialized");
            }

            if (!_station2OutgoingCargoGroups.TryGetValue(station, out var result)) {
                Debug.LogWarning("Could not find outgoing cargo groups for station " + station.logicStation.ID);
                return new List<OutgoingCargoGroup>();
            }

            return result;
        }

        public static IReadOnlyList<IncomingCargoGroup> GetIncomingCargoGroups(StationController station) {
            if (_station2IncomingCargoGroups == null) {
                throw new InvalidOperationException("Not initialized");
            }

            if (!_station2IncomingCargoGroups.TryGetValue(station, out var result)) {
                Debug.LogWarning("Could not find incoming cargo groups for station " + station.logicStation.ID);
                return new List<IncomingCargoGroup>();
            }

            return result;
        }

        public static IReadOnlyList<OutgoingCargoGroupDestination> GetCargoTypeDestinations(CargoType cargoType) {
            if (_cargoType2Destinations == null) {
                throw new InvalidOperationException("Not initialized");
            }

            if (!_cargoType2Destinations.TryGetValue(cargoType, out var result)) {
                Debug.LogWarning("Could not find destinations for cargo type " + cargoType);
                return new List<OutgoingCargoGroupDestination>();
            }

            return result;
        }
    }
}