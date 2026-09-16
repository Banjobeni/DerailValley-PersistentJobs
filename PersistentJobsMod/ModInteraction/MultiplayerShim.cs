using System;
using System.Linq;
using System.Reflection;
using UnityModManagerNet;

#nullable enable
namespace PersistentJobsMod.ModInteraction
{
    public static class MultiplayerShim
    {
        const string MULTIPLAYER_MOD_ID = "Multiplayer";
        const string MPAPI_ASSEMBLY_NAME = "MultiplayerAPI";
        const string MPAPI_TYPE_NAME = "MPAPI.MultiplayerAPI";
        const string MPAPI_INSTANCE_PROPERTY = "Instance";

        const string MP_VERSION_PROPERTY = "MultiplayerVersion";
        const string IS_HOST_PROPERTY = "IsHost";

        private static object? _mpApiInstance;
        public static PropertyInfo? _isHost;

        internal static bool IsInitialized { get; private set; } = false;

        internal static bool IsHost
        {
            get
            {
                if (_isHost == null)
                    return true;

                return (bool)_isHost.GetValue(_mpApiInstance)!;
            }
        }

        // Add more wrapped methods/properties as needed...

        internal static void Initialize(UnityModManager.ModEntry modEntry)
        {
            UnityModManager.ModEntry? multiplayer = UnityModManager.FindMod(MULTIPLAYER_MOD_ID);
            var mpapiAssembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == MPAPI_ASSEMBLY_NAME);
            modEntry.Logger.Log("Trying to initialize compatibility with MP mod");

            try
            {
                if (multiplayer?.Enabled == true && mpapiAssembly != null)
                {
                    var mpApiType = mpapiAssembly.GetType(MPAPI_TYPE_NAME);
                    var instanceProp = mpApiType?.GetProperty(MPAPI_INSTANCE_PROPERTY, BindingFlags.Public | BindingFlags.Static);

                    _mpApiInstance = instanceProp!.GetValue(null);

                    // Find properties and methods by reflection
                    _isHost = _mpApiInstance.GetType().GetProperty(IS_HOST_PROPERTY, BindingFlags.Public | BindingFlags.Instance);

                    IsInitialized = _mpApiInstance != null && _isHost != null;

                    _mpApiInstance?.GetType().GetMethod("SetModCompatibility").Invoke(_mpApiInstance, [modEntry.Info.Id, (byte)3]);
                }

                modEntry.Logger.Log("Multiplayer API Loaded.");
            }
            catch (Exception ex)
            {
                modEntry.Logger.Warning($"Failed to load multiplayer API.\r\n{ex.Message}\r\n{ex.StackTrace}");
            }
        }
    }
}