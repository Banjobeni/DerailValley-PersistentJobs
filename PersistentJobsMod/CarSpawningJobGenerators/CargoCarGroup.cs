using System.Collections.Generic;
using DV.ThingTypes;

namespace PersistentJobsMod.CarSpawningJobGenerators {
    public class CargoCarGroup {
        public CargoType CargoType { get; }
        public List<TrainCarLivery> CarLiveries { get; }

        public CargoCarGroup(CargoType cargoType, List<TrainCarLivery> carLiveries) {
            CargoType = cargoType;
            CarLiveries = carLiveries;
        }
    }
}