namespace PersistentJobsMod.HarmonyPatches.Save {
    public static class SaveDataConstants {
        // ReSharper disable InconsistentNaming
        // ReSharper disable ConvertToConstant.Global
        public static readonly string SAVE_DATA_PRIMARY_KEY = "PersistentJobsMod";
        public static readonly string SAVE_DATA_VERSION_KEY = "Version";
        public static readonly string SAVE_DATA_SPAWN_BLOCK_KEY = "SpawnBlockList";
        // ReSharper restore ConvertToConstant.Global
        // ReSharper restore InconsistentNaming

        // see private field CarsSaveManager.TRACK_HASH_SAVE_KEY
        public const string TRACK_HASH_SAVE_KEY = "trackHash";
    }
}