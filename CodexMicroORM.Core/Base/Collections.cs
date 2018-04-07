/***********************************************************************
Copyright 2018 CodeX Enterprises LLC

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.

Major Changes:
12/2017    0.2     Initial release (Joel Champagne)
***********************************************************************/
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Collections;
using System.Threading;
using System.Linq;
using System.Collections.Immutable;

namespace CodexMicroORM.Core.Collections
{
    public interface ICEFIndexedListItem
    {
        object GetValue(string propName, bool unwrap);
    }
   
    /// <summary>
    /// This collection type is very similar to "table" whereby we maintain indexes for as many "columns" as we'd like.
    /// The cost is increased memory usage, but lookups can be performed very quickly on any indexed column.
    /// It is also thread-safe and can get used in plenty of situations outside of the "ORM" of CodexMicroORM.
    /// </summary>
    /// <typeparam name="T">Must implement ICEFIndexedListItem which affords a way to customize values returned by each item instance, such as unwrapping of WeakReference's.</typeparam>
    public sealed class ConcurrentIndexedList<T> : IEnumerable<T>, IEnumerable, ICollection<T>, ICollection  where T : class, ICEFIndexedListItem
    {
        #region "Private state"

        private static int _baseCapacity = Environment.ProcessorCount * 7;
        private static object _asNull = new object();

        private long _dataID = long.MinValue;
        private RWLockInfo _lock = new RWLockInfo();
        private int _initCapacity = _baseCapacity;

        private Dictionary<long, T> _data;
        private HashSet<string> _isUnique = new HashSet<string>();
        private HashSet<string> _neverNull = new HashSet<string>();
        private Dictionary<T, long> _contains;
        private Dictionary<string, Dictionary<object, List<long>>> _masterIndex = new Dictionary<string, Dictionary<object, List<long>>>();

        #endregion

        public static object NullValue => _asNull;

        public RWLockInfo LockInfo => _lock;

        public ConcurrentIndexedList(int lockTimeout, int initCapacity, params string[] propsToIndex)
        {
            _initCapacity = initCapacity;
            _lock.Timeout = lockTimeout;
            Init(propsToIndex);
        }

        public ConcurrentIndexedList(int initCapacity, params string[] propsToIndex)
        {
            _initCapacity = initCapacity;
            Init(propsToIndex);
        }

        public ConcurrentIndexedList(params string[] propsToIndex)
        {
            Init(propsToIndex);
        }

        /// <summary>
        /// Allows control over lock timeout specific to this collection.
        /// </summary>
        public int LockTimeout
        {
            get
            {
                return _lock.Timeout;
            }
            set
            {
                _lock.Timeout = value;
            }
        }

        /// <summary>
        /// If you know a general maximum capacity for the collection, there are benefits to setting it early.
        /// </summary>
        public int InitialCapacity
        {
            get
            {
                return _initCapacity;
            }
            set
            {
                if (_initCapacity != value)
                {
                    _initCapacity = value;
                    Init((from a in _masterIndex select a.Key).ToArray());
                }
            }
        }

        private void Init(string[] propsToIndex)
        {
            foreach (var props in propsToIndex)
            {
                int cap = _baseCapacity;

                if (_isUnique.Contains(props) && !_neverNull.Contains(props))
                {
                    cap = _initCapacity;
                }

                _masterIndex[props] = new Dictionary<object, List<long>>(cap);
            }

            _data = new Dictionary<long, T>(_initCapacity);
            _contains = new Dictionary<T, long>(_initCapacity);
        }

        /// <summary>
        /// Locate a specific record, if it exists in the collection.
        /// </summary>
        /// <param name="source"></param>
        /// <returns></returns>
        public T Find(T source)
        {
            using (new ReaderLock(_lock))
            {
                if (_contains.TryGetValue(source, out long id))
                {
                    return _data[id];
                }
            }

            return null;
        }

        public static bool AssumeSafe
        {
            get;
            set;
        } = true;

        public int Count
        {
            get
            {
                using (new ReaderLock(_lock))
                {
                    return _data.Count;
                }
            }
        }

        public bool IsReadOnly => false;

        public bool IsSynchronized => true;

        public object SyncRoot => _lock;

        /// <summary>
        /// Causes collection to throw an error if try to add a duplicate value for the given property. (Affords some optimizations as well.)
        /// </summary>
        /// <param name="propName"></param>
        /// <returns></returns>
        public ConcurrentIndexedList<T> AddUniqueConstraint(string propName)
        {
            _isUnique.Add(propName);
            return this;
        }

        /// <summary>
        /// If you know you will never access the collection for this property using a null value, affords a way for the collection to consume less memory.
        /// </summary>
        /// <param name="propName"></param>
        /// <returns></returns>
        public ConcurrentIndexedList<T> AddNeverTrackNull(string propName)
        {
            _neverNull.Add(propName);
            return this;
        }

        /// <summary>
        /// Add an item to the collection. (If it already exists, no operation.)
        /// </summary>
        /// <param name="item"></param>
        public void Add(T item)
        {
            using (new ReaderLock(_lock))
            {
                if (_contains.ContainsKey(item ?? throw new ArgumentNullException("item")))
                {
                    return;
                }
            }

            var id = Interlocked.Increment(ref _dataID);

            using (new WriterLock(_lock))
            {
                foreach (var dic in _masterIndex)
                {
                    var record = true;

                    if (_neverNull.Contains(dic.Key))
                    {
                        if (item.GetValue(dic.Key, true) == null)
                        {
                            record = false;
                        }                        
                    }

                    if (record)
                    {
                        var propVal = item.GetValue(dic.Key, false);

                        if (dic.Value.TryGetValue(propVal ?? _asNull, out List<long> bag))
                        {
                            if (bag.Count > 0 && _isUnique.Contains(dic.Key))
                            {
                                throw new CEFInvalidOperationException($"Collection already contains an entry for '{propVal}'.");
                            }

                            bag.Add(id);
                        }
                        else
                        {
                            List<long> newBag = new List<long>();
                            newBag.Add(id);
                            dic.Value[propVal ?? _asNull] = newBag;
                        }
                    }
                }

                _data[id] = item;
                _contains[item] = id;
            }
        }

        /// <summary>
        /// Remove all items from the collection.
        /// </summary>
        public void Clear()
        {
            using (new WriterLock(_lock))
            {
                foreach (var dic in _masterIndex)
                {
                    dic.Value.Clear();
                }

                _data.Clear();
                _contains.Clear();
            }
        }

        /// <summary>
        /// Wrapper for GetAllByName(..).FirstOrDefault().
        /// </summary>
        /// <param name="propName">Property name of T, as returned by T's ICEFIndexListItem's GetValue().</param>
        /// <param name="propValue">Value to match for in the virtual column.</param>
        /// <returns></returns>
        public T GetFirstByName(string propName, object propValue, Func<T, bool> preview)
        {
            if (!AssumeSafe && !_masterIndex.ContainsKey(propName ?? throw new ArgumentNullException("propName")))
            {
                throw new CEFInvalidOperationException($"Collection does not contain property {propName}.");
            }

            using (new ReaderLock(_lock))
            {
                if (_masterIndex[propName].TryGetValue(propValue ?? _asNull, out List<long> bag))
                {
                    T val = null;
                    return (from a in bag where _data.TryGetValue(a, out val) where preview(val) select val).FirstOrDefault();
                }
            }

            return default;
        }

        /// <summary>
        /// Wrapper for GetAllByName(..).FirstOrDefault().
        /// </summary>
        /// <param name="propName"></param>
        /// <param name="propValue"></param>
        /// <returns></returns>
        public T GetFirstByName(string propName, object propValue)
        {
            if (!AssumeSafe && !_masterIndex.ContainsKey(propName ?? throw new ArgumentNullException("propName")))
            {
                throw new CEFInvalidOperationException($"Collection does not contain property {propName}.");
            }

            using (new ReaderLock(_lock))
            {
                if (_masterIndex[propName].TryGetValue(propValue ?? _asNull, out List<long> bag))
                {
                    return (from a in bag select _data[a]).FirstOrDefault();
                }
            }

            return default;
        }

        /// <summary>
        /// Wrapper for GetAllByName(..).FirstOrDefault().
        /// </summary>
        /// <param name="propName"></param>
        /// <param name="propValue"></param>
        /// <param name="preview"></param>
        /// <returns></returns>
        public T GetFirstByNameNoLock(string propName, object propValue, Func<T, bool> preview)
        {
            if (!AssumeSafe && !_masterIndex.ContainsKey(propName ?? throw new ArgumentNullException("propName")))
            {
                throw new CEFInvalidOperationException($"Collection does not contain property {propName}.");
            }

            if (_masterIndex[propName].TryGetValue(propValue ?? _asNull, out List<long> bag))
            {
                T val = null;
                return (from a in bag where _data.TryGetValue(a, out val) where preview(val) select val).FirstOrDefault();
            }

            return default;
        }

        /// <summary>
        /// Wrapper for GetAllByName(..).FirstOrDefault().
        /// </summary>
        /// <param name="propName"></param>
        /// <param name="propValue"></param>
        /// <returns></returns>
        public T GetFirstByNameNoLock(string propName, object propValue)
        {
            if (!AssumeSafe && !_masterIndex.ContainsKey(propName ?? throw new ArgumentNullException("propName")))
            {
                throw new CEFInvalidOperationException($"Collection does not contain property {propName}.");
            }

            if (_masterIndex[propName].TryGetValue(propValue ?? _asNull, out List<long> bag))
            {
                return (from a in bag select _data[a]).FirstOrDefault();
            }

            return default;
        }

        /// <summary>
        /// Retrieves zero, one or many elements that match the input propValue for column propName. Always returns a sequence, never null (may be empty).
        /// </summary>
        /// <param name="propName">Property name of T, as returned by T's ICEFIndexListItem's GetValue().</param>
        /// <param name="propValue">Value to match for in the virtual column.</param>
        /// <returns>Sequence of T for matches on value in the propName virtual column.</returns>
        public IEnumerable<T> GetAllByName(string propName, object propValue, Func<T, bool> preview)
        {
            if (!AssumeSafe && !_masterIndex.ContainsKey(propName ?? throw new ArgumentNullException("propName")))
            {
                throw new CEFInvalidOperationException($"Collection does not contain property {propName}.");
            }

            using (new ReaderLock(_lock))
            {
                if (_masterIndex[propName].TryGetValue(propValue ?? _asNull, out List<long> bag))
                {
                    T val = null;
                    return (from a in bag where _data.TryGetValue(a, out val) where preview(val) select val).ToArray();
                }
            }

            return new T[] { };
        }

        /// <summary>
        /// Retrieves zero, one or many elements that match the input propValue for column propName. Always returns a sequence, never null (may be empty).
        /// </summary>
        /// <param name="propName"></param>
        /// <param name="propValue"></param>
        /// <returns></returns>
        public IEnumerable<T> GetAllByName(string propName, object propValue)
        {
            if (!AssumeSafe && !_masterIndex.ContainsKey(propName ?? throw new ArgumentNullException("propName")))
            {
                throw new CEFInvalidOperationException($"Collection does not contain property {propName}.");
            }

            using (new ReaderLock(_lock))
            {
                if (_masterIndex[propName].TryGetValue(propValue ?? _asNull, out List<long> bag))
                {
                    return (from a in bag select _data[a]).ToArray();
                }
            }

            return new T[] { };
        }

        /// <summary>
        /// Retrieves zero, one or many elements that match the input propValue for column propName. Always returns a sequence, never null (may be empty).
        /// </summary>
        /// <param name="propName"></param>
        /// <param name="propValue"></param>
        /// <param name="preview"></param>
        /// <returns></returns>
        public IEnumerable<T> GetAllByNameNoLock(string propName, object propValue, Func<T, bool> preview)
        {
            if (!AssumeSafe && !_masterIndex.ContainsKey(propName ?? throw new ArgumentNullException("propName")))
            {
                throw new CEFInvalidOperationException($"Collection does not contain property {propName}.");
            }

            if (_masterIndex[propName].TryGetValue(propValue ?? _asNull, out List<long> bag))
            {
                T val = null;
                return (from a in bag where _data.TryGetValue(a, out val) where preview(val) select val);
            }

            return new T[] { };
        }

        /// <summary>
        /// Retrieves zero, one or many elements that match the input propValue for column propName. Always returns a sequence, never null (may be empty).
        /// </summary>
        /// <param name="propName"></param>
        /// <param name="propValue"></param>
        /// <returns></returns>
        public IEnumerable<T> GetAllByNameNoLock(string propName, object propValue)
        {
            if (!AssumeSafe && !_masterIndex.ContainsKey(propName ?? throw new ArgumentNullException("propName")))
            {
                throw new CEFInvalidOperationException($"Collection does not contain property {propName}.");
            }

            if (_masterIndex[propName].TryGetValue(propValue ?? _asNull, out List<long> bag))
            {
                return (from a in bag select _data[a]);
            }

            return new T[] { };
        }

        /// <summary>
        /// Should be called when the collection user is aware of a case where a key value has changed and requires updates therefore of indexes.
        /// </summary>
        /// <param name="propName"></param>
        /// <param name="oldval"></param>
        /// <param name="newval"></param>
        public void UpdateFieldIndex(string propName, object oldval, object newval)
        {
            if (!AssumeSafe && !_masterIndex.ContainsKey(propName ?? throw new ArgumentNullException("propName")))
            {
                throw new CEFInvalidOperationException($"Collection does not contain property {propName}.");
            }

            using (new WriterLock(_lock))
            {
                var index = _masterIndex[propName];

                if (index.TryGetValue(oldval ?? _asNull, out List<long> oldBag))
                {
                    if (index.TryGetValue(newval ?? _asNull, out List<long> newBag))
                    {
                        if (_isUnique.Contains(propName) && newBag.Count > 0)
                        {
                            throw new CEFInvalidOperationException($"Collection already contains an entry for '{newval}'.");
                        }

                        oldBag.AddRange(from a in newBag where !oldBag.Contains(a) select a);
                    }

                    index[newval ?? _asNull] = oldBag;

                    if (index.ContainsKey(oldval ?? _asNull))
                    {
                        index.Remove(oldval ?? _asNull);
                    }
                }
            }
        }

        /// <summary>
        /// Returns true if the given item already exists in the collection.
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public bool Contains(T item)
        {
            using (new ReaderLock(_lock))
            {
                return _contains.ContainsKey(item ?? throw new ArgumentNullException("item"));
            }
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            throw new NotImplementedException();
        }

        public void CopyTo(Array array, int index)
        {
            throw new NotImplementedException();
        }

        public IEnumerator<T> GetEnumerator()
        {
            using (new ReaderLock(_lock))
            {
                // We need to take an effective snapshot during a read-lock to be certain about what's being handed back!
                return _data.Values.ToList().GetEnumerator();
            }
        }

        /// <summary>
        /// Removes an existing item from the collection (returns false if does not exist in the collection).
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public bool Remove(T item)
        {
            using (new WriterLock(_lock))
            {
                if (!_contains.ContainsKey(item ?? throw new ArgumentNullException("item")))
                {
                    return false;
                }

                var id = _contains[item];

                foreach (var dic in _masterIndex)
                {
                    var propVal = item.GetValue(dic.Key, false);

                    if (dic.Value.TryGetValue(propVal ?? _asNull, out List<long> bag))
                    {
                        bag.Remove(id);
                    }
                }

                if (_data.ContainsKey(id))
                {
                    _data.Remove(id);
                }

                if (_contains.ContainsKey(item))
                {
                    _contains.Remove(item);
                }
            }

            return true;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            using (new ReaderLock(_lock))
            {
                // To honor read lock, ToList takes an effective snapshot - if locking is a non-issue, consider using the NoLock derivative.
                return _data.Values.ToList().GetEnumerator();
            }
        }

        public IEnumerator GetEnumeratorNoLock()
        {
            return _data.Values.GetEnumerator();
        }
    }
}
