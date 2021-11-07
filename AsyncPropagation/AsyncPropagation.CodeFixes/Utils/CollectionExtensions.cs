using System;
using System.Collections.Generic;

namespace AsyncPropagation.Utils
{
    public static class CollectionExtensions
    {
        public static void ReplaceRange<T>(this HashSet<T> hashSet, IEnumerable<T> items)
        {
            foreach (var item in items)
            {
                hashSet.Remove(item);
                hashSet.Add(item);
            }
        }

        public static void AddOrUpdate<TKey, TVal>(this Dictionary<TKey, TVal> dictionary, TKey key, Func<TVal, TVal> updateFunc, TVal defaultValue = default!)
        {
            dictionary[key] = dictionary.ContainsKey(key) ? updateFunc(dictionary[key]) : defaultValue;
        }
    }
}