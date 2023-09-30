using System.Collections.Generic;
using System;
using System.Linq;

namespace PersistentJobsMod.Extensions {
    public static class EnumerableExtensions {
        public static IEnumerable<T> WhereNotNull<T>(this IEnumerable<T> enumerable) where T : class {
            return enumerable.Where(item => item != null);
        }

        public static IEnumerable<(TKey Key, IReadOnlyList<TItem> Items)> GroupConsecutiveBy<TKey, TItem>(this IEnumerable<TItem> items, Func<TItem, TKey> getKey, IEqualityComparer<TKey> keyEqualityComparer = null) {
            keyEqualityComparer = keyEqualityComparer ?? EqualityComparer<TKey>.Default;

            (TKey key, List<TItem> items)? current = null;

            foreach (var item in items) {
                var key = getKey(item);
                if (current == null) {
                    current = (key, new List<TItem> { item });
                } else {
                    if (keyEqualityComparer.Equals(key, current.Value.key)) {
                        current.Value.items.Add(item);
                    } else {
                        yield return current.Value;

                        current = (key, new List<TItem> { item });
                    }
                }
            }

            if (current != null) {
                yield return current.Value;
            }
        }

        public static IReadOnlyList<T> ToReadOnlyList<T>(this IEnumerable<T> enumerable) {
            return enumerable.ToList();
        }
    }
}