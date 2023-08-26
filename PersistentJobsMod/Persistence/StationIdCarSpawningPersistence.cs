using System.Collections.Generic;
using System.Linq;

namespace PersistentJobsMod.Persistence {
    public sealed class StationIdCarSpawningPersistence {
        public static readonly StationIdCarSpawningPersistence Instance = new StationIdCarSpawningPersistence();

        private List<string> _stationIdsThatSpawnedCars = new List<string>();

        public void HandleSavegameLoadedSpawnedStationIds(IReadOnlyList<string> stationIds) {
            _stationIdsThatSpawnedCars = stationIds.ToList();
        }

        public bool GetHasStationSpawnedCarsFlag(StationController station) {
            return GetHasStationSpawnedCarsFlag(station.logicStation.ID);
        }

        public bool GetHasStationSpawnedCarsFlag(string stationId) {
            return _stationIdsThatSpawnedCars.Contains(stationId);
        }

        public void SetHasStationSpawnedCarsFlag(StationController station, bool value) {
            SetHasStationSpawnedCarsFlag(station.logicStation.ID, value);
        }

        public void SetHasStationSpawnedCarsFlag(string stationId, bool value) {
            var currentValue = _stationIdsThatSpawnedCars.Contains(stationId);

            if (!currentValue && value) {
                _stationIdsThatSpawnedCars.Add(stationId);
            } else if (currentValue && !value) {
                _stationIdsThatSpawnedCars.Remove(stationId);
            }
        }

        public void ClearStationsSpawnedCarsFlagForAllStations() {
            _stationIdsThatSpawnedCars.Clear();
        }

        public List<string> GetAllSetStationSpawnedCarFlags() {
            return _stationIdsThatSpawnedCars.ToList();
        }
    }
}