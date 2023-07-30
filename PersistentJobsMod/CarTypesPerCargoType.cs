using System.Collections.Generic;
using DV.ThingTypes;

namespace PersistentJobsMod {
    class CarTypesPerCargoType {
        public CarTypesPerCargoType(List<TrainCarType> carTypes, CargoType cargoType, float totalCargoAmount) {
            this.carTypes = carTypes;
            this.cargoType = cargoType;
            this.totalCargoAmount = totalCargoAmount;
        }

        public readonly List<TrainCarType> carTypes;

        public readonly CargoType cargoType;

        public readonly float totalCargoAmount;
    }
}