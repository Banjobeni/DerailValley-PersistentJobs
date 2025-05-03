using UnityModManagerNet;

namespace PersistentJobsMod {
    public sealed class Settings : UnityModManager.ModSettings, IDrawable {
        [Draw("Prevent accepting shunting (un)load jobs if cars are already on loading track (L)")]
        public bool PreventStartingShuntingJobForCarsOnWarehouseTrack = true;

        public bool PreventStartingShuntingJobForCarsOnWarehouseTrackMessageWasShown = false;

        public override void Save(UnityModManager.ModEntry modEntry) {
            Save(this, modEntry);
        }

        void IDrawable.OnChange() { }
    }
}