using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

namespace PersistentJobsMod.Extensions {
    public static class RandomExtensions {
        public static T GetRandomElement<T>(this Random rng, IReadOnlyList<T> list) {
            var index = rng.Next(0, list.Count);
            return list[index];
        }

        // taken from StationProcedurationJobGenerator.GetMultipleRandomsFromList
        public static List<T> GetMultipleRandomsFromList<T>(this Random rng, IReadOnlyList<T> list, int countToGet) {
            var list2 = new List<T>(list);
            if (countToGet > list2.Count) {
                Debug.LogError("Trying to get more random items from list than it contains. Returning all items from list.");
                return list2;
            }
            var list3 = new List<T>();
            for (var i = 0; i < countToGet; i++) {
                var index = rng.Next(0, list2.Count);
                list3.Add(list2[index]);
                list2.RemoveAt(index);
            }
            return list3;
        }

        public static List<T> GetRandomPermutation<T>(this Random rng, IReadOnlyList<T> list) {
            return GetMultipleRandomsFromList<T>(rng, list, list.Count);
        }
    }
}