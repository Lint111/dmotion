using System.Collections.Generic;

namespace DMotion.Editor
{
    /// <summary>
    /// Generic object pool for Lists to avoid per-frame allocations.
    /// </summary>
    internal static class ListPool<T>
    {
        private static readonly Stack<List<T>> _pool = new Stack<List<T>>(4);
        private const int MaxPoolSize = 8;
        private const int DefaultCapacity = 16;

        /// <summary>
        /// Gets a list from the pool, or creates a new one if pool is empty.
        /// </summary>
        public static List<T> Get()
        {
            if (_pool.Count > 0)
            {
                return _pool.Pop();
            }
            return new List<T>(DefaultCapacity);
        }

        /// <summary>
        /// Gets a list from the pool with at least the specified capacity.
        /// </summary>
        public static List<T> Get(int minCapacity)
        {
            var list = Get();
            if (list.Capacity < minCapacity)
            {
                list.Capacity = minCapacity;
            }
            return list;
        }

        /// <summary>
        /// Returns a list to the pool. The list is cleared before pooling.
        /// </summary>
        public static void Return(List<T> list)
        {
            if (list == null)
                return;

            list.Clear();

            if (_pool.Count < MaxPoolSize)
            {
                _pool.Push(list);
            }
        }

        /// <summary>
        /// Clears the pool. Call this if you need to release memory.
        /// </summary>
        public static void Clear()
        {
            _pool.Clear();
        }
    }

    /// <summary>
    /// Generic object pool for HashSets to avoid per-frame allocations.
    /// </summary>
    internal static class HashSetPool<T>
    {
        private static readonly Stack<HashSet<T>> _pool = new Stack<HashSet<T>>(4);
        private const int MaxPoolSize = 8;

        /// <summary>
        /// Gets a HashSet from the pool, or creates a new one if pool is empty.
        /// </summary>
        public static HashSet<T> Get()
        {
            if (_pool.Count > 0)
            {
                return _pool.Pop();
            }
            return new HashSet<T>();
        }

        /// <summary>
        /// Returns a HashSet to the pool. The set is cleared before pooling.
        /// </summary>
        public static void Return(HashSet<T> set)
        {
            if (set == null)
                return;

            set.Clear();

            if (_pool.Count < MaxPoolSize)
            {
                _pool.Push(set);
            }
        }

        /// <summary>
        /// Clears the pool.
        /// </summary>
        public static void Clear()
        {
            _pool.Clear();
        }
    }

    /// <summary>
    /// Generic object pool for Dictionaries to avoid per-frame allocations.
    /// </summary>
    internal static class DictionaryPool<TKey, TValue>
    {
        private static readonly Stack<Dictionary<TKey, TValue>> _pool = new Stack<Dictionary<TKey, TValue>>(4);
        private const int MaxPoolSize = 8;

        /// <summary>
        /// Gets a Dictionary from the pool, or creates a new one if pool is empty.
        /// </summary>
        public static Dictionary<TKey, TValue> Get()
        {
            if (_pool.Count > 0)
            {
                return _pool.Pop();
            }
            return new Dictionary<TKey, TValue>();
        }

        /// <summary>
        /// Returns a Dictionary to the pool. The dictionary is cleared before pooling.
        /// </summary>
        public static void Return(Dictionary<TKey, TValue> dict)
        {
            if (dict == null)
                return;

            dict.Clear();

            if (_pool.Count < MaxPoolSize)
            {
                _pool.Push(dict);
            }
        }

        /// <summary>
        /// Clears the pool.
        /// </summary>
        public static void Clear()
        {
            _pool.Clear();
        }
    }

    /// <summary>
    /// Pool for string arrays to avoid allocations from ToArray() calls.
    /// </summary>
    internal static class StringArrayPool
    {
        private static readonly Stack<string[]> _smallPool = new Stack<string[]>(4); // <= 16 elements
        private static readonly Stack<string[]> _mediumPool = new Stack<string[]>(4); // <= 64 elements
        private const int SmallSize = 16;
        private const int MediumSize = 64;
        private const int MaxPoolSize = 4;

        /// <summary>
        /// Gets a string array with at least the specified capacity.
        /// </summary>
        public static string[] Get(int minCapacity)
        {
            if (minCapacity <= SmallSize && _smallPool.Count > 0)
            {
                return _smallPool.Pop();
            }
            if (minCapacity <= MediumSize && _mediumPool.Count > 0)
            {
                return _mediumPool.Pop();
            }

            // Create new with appropriate size
            int size = minCapacity <= SmallSize ? SmallSize :
                       minCapacity <= MediumSize ? MediumSize :
                       minCapacity;
            return new string[size];
        }

        /// <summary>
        /// Returns a string array to the pool.
        /// </summary>
        public static void Return(string[] array)
        {
            if (array == null)
                return;

            // Clear references to allow GC
            System.Array.Clear(array, 0, array.Length);

            if (array.Length <= SmallSize && _smallPool.Count < MaxPoolSize)
            {
                _smallPool.Push(array);
            }
            else if (array.Length <= MediumSize && _mediumPool.Count < MaxPoolSize)
            {
                _mediumPool.Push(array);
            }
            // Larger arrays are not pooled
        }
    }
}
