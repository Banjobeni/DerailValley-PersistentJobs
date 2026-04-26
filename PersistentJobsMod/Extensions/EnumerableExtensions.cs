using System.Collections.Generic;
using System;
using System.Linq;

namespace PersistentJobsMod.Extensions {
    public static class EnumerableExtensions {
        public static IEnumerable<T> WhereNotNull<T>(this IEnumerable<T> enumerable) where T : class {
            return enumerable.Where(item => item != null);
        }

        public static IEnumerable<(TKey Key, IReadOnlyList<TItem> Items)> GroupConsecutiveBy<TKey, TItem>(this IEnumerable<TItem> items, Func<TItem, TKey> getKey, IEqualityComparer<TKey> keyEqualityComparer = null) {
            keyEqualityComparer ??= EqualityComparer<TKey>.Default;

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

        public static (List<T> First, List<T> Second) SplitInHalf<T>(this IEnumerable<T> source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var list = source as IList<T> ?? source.ToList();

            if (list.Count < 2) return (null, null);

            int mid = (list.Count + 1) / 2;
            var first = list.Take(mid).ToList();
            var second = list.Skip(mid).ToList();

            return (first, second);
        }

        public static bool MultisetEquals<T>(this IEnumerable<T> first, IEnumerable<T> second, IEqualityComparer<T> comparer = null)
        {
            if (ReferenceEquals(first, second)) return true;
            if (first == null || second == null) return false;

            comparer ??= EqualityComparer<T>.Default;
            var lookup = new Dictionary<T, int>(comparer);

            foreach (var item in first)
            {
                if (lookup.TryGetValue(item, out int count))
                    lookup[item] = count + 1;
                else
                    lookup[item] = 1;
            }

            foreach (var item in second)
            {
                if (!lookup.TryGetValue(item, out int count)) return false;

                count--;
                if (count == 0)
                    lookup.Remove(item);
                else
                    lookup[item] = count;
            }

            return lookup.Count == 0;
        }
    }
}