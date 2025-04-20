using System.Linq;
using CommandTerminal;
using DV.Utils;
using HarmonyLib;
using UnityEngine;

namespace PersistentJobsMod.HarmonyPatches.Console {
    [HarmonyPatch]
    public sealed class Console_Patches {
        [HarmonyPatch(typeof(DV.Console), "Dev_TeleportTrainToTrack")]
        [HarmonyPrefix]
        public static bool Dev_TeleportTrainToTrack_Prefix(CommandArg[] args) {
            if (Terminal.IssuedError)
                return false;

            var trackId = args[0].String.ToLower();
            var destinationRailTrack = RailTrackRegistry.Instance.AllTracks.FirstOrDefault(rt => rt.LogicTrack().ID.FullDisplayID.ToLower() == trackId);

            if (destinationRailTrack == null) {
                Debug.LogError("Couldn't find railtrack with id " + trackId);
                return false;
            }

            if (PlayerManager.Car == null) {
                Debug.LogError("Player is currently not on any train");
                return false;
            }

            var trainCarsToMove = PlayerManager.Car.trainset.cars.ToList();

            SingletonBehaviour<CoroutineManager>.Instance.Run(DV.Console.MoveCarsCoro(trainCarsToMove, destinationRailTrack));

            return false;
        }
    }
}