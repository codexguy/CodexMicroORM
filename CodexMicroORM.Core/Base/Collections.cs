/***********************************************************************
Copyright 2017 CodeX Enterprises LLC

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
    public class ConcurrentIndexedList<T> : IEnumerable<T>, IEnumerable, ICollection<T>, ICollection  where T : class, ICEFIndexedListItem
    {
        #region "Private state"

        private static object _asNull = new object();

        private long _dataID = long.MinValue;

        ConcurrentDictionary<long, T> _data = new ConcurrentDictionary<long, T>();
        HashSet<string> _isUnique = new HashSet<string>();
        ConcurrentDictionary<T, long> _contains = new ConcurrentDictionary<T, long>();
        ConcurrentDictionary<string, ConcurrentDictionary<object, ImmutableList<long>>> _masterIndex = new ConcurrentDictionary<string, ConcurrentDictionary<object, ImmutableList<long>>>();

        #endregion

        public static object NullValue => _asNull;

        public ConcurrentIndexedList(params string[] propsToIndex)
        {
            foreach (var props in propsToIndex)
            {
                _masterIndex[props] = new ConcurrentDictionary<object, ImmutableList<long>>();
            }
        }

        public T Find(T source)
        {
            if (_contains.TryGetValue(source, out long id))
            {
                return _data[id];
            }

            return null;
        }

        public int Count => _data.Count;

        public bool IsReadOnly => false;

        public bool IsSynchronized => true;

        public object SyncRoot => this;

        public ConcurrentIndexedList<T> AddUniqueConstraint(string propName)
        {
            _isUnique.Add(propName);
            return this;
        }

        public void Add(T item)
        {
            if (_contains.ContainsKey(item ?? throw new ArgumentNullException("item")))
            {
                return;
            }

            var id = Interlocked.Increment(ref _dataID);

            try
            {
            }
            finally
            {
                lock (this)
                {
                    foreach (var dic in _masterIndex)
                    {
                        var propVal = item.GetValue(dic.Key, false);

                        if (dic.Value.TryGetValue(propVal ?? _asNull, out ImmutableList<long> bag))
                        {
                            if (bag.Count > 0 && _isUnique.Contains(dic.Key))
                            {
                                throw new CEFInvalidOperationException($"Collection already contains an entry for '{propVal}'.");
                            }

                            dic.Value[propVal ?? _asNull] = bag.Add(id);
                        }
                        else
                        {
                            var newBag = ImmutableList.Create(id);
                            dic.Value[propVal ?? _asNull] = newBag;
                        }
                    }

                    _data[id] = item;
                    _contains[item] = id;
                }
            }
        }

        public void Clear()
        {
            try
            {
            }
            finally
            {
                lock (this)
                {
                    foreach (var dic in _masterIndex)
                    {
                        dic.Value.Clear();
                    }

                    _data.Clear();
                    _contains.Clear();
                }
            }
        }

        /// <summary>
        /// Wrapper for GetAllByName(..).FirstOrDefault().
        /// </summary>
        /// <param name="propName">Property name of T, as returned by T's ICEFIndexListItem's GetValue().</param>
        /// <param name="propValue">Value to match for in the virtual column.</param>
        /// <returns></returns>
        public T GetFirstByName(string propName, object propValue, Func<T, bool> preview = null)
        {
            return GetAllByName(propName, propValue).FirstOrDefault();
        }

        /// <summary>
        /// Retrieves zero, one or many elements that match the input propValue for column propName.
        /// Always returns a sequence, never null (may be empty).
        /// </summary>
        /// <param name="propName">Property name of T, as returned by T's ICEFIndexListItem's GetValue().</param>
        /// <param name="propValue">Value to match for in the virtual column.</param>
        /// <returns>Sequence of T for matches on value in the propName virtual column.</returns>
        public IEnumerable<T> GetAllByName(string propName, object propValue, Func<T, bool> preview = null)
        {
            if (!_masterIndex.ContainsKey(propName ?? throw new ArgumentNullException("propName")))
            {
                throw new CEFInvalidOperationException($"Collection does not contain property {propName}.");
            }

            if (_masterIndex[propName].TryGetValue(propValue ?? _asNull, out ImmutableList<long> bag))
            {
                T val = null;
                return (from a in bag where _data.TryGetValue(a, out val) where preview == null || preview.Invoke(val) select val);
            }
            else
            {
                return new T[] { };
            }
        }

        /// <summary>
        /// Should be called when the collection user is aware of a case where a key value has changed and requires updates therefore of indexes.
        /// </summary>
        /// <param name="propName"></param>
        /// <param name="oldval"></param>
        /// <param name="newval"></param>
        public void UpdateFieldIndex(string propName, object oldval, object newval)
        {
            if (!_masterIndex.ContainsKey(propName ?? throw new ArgumentNullException("propName")))
            {
                throw new CEFInvalidOperationException($"Collection does not contain property {propName}.");
            }

            var index = _masterIndex[propName];

            if (index.TryGetValue(oldval ?? _asNull, out ImmutableList<long> oldBag))
            {
                if (index.TryGetValue(newval ?? _asNull, out ImmutableList<long> newBag))
                {
                    if (_isUnique.Contains(propName) && newBag.Count > 0)
                    {
                        throw new CEFInvalidOperationException($"Collection already contains an entry for '{newval}'.");
                    }

                    oldBag = oldBag.AddRange(from a in newBag where !oldBag.Contains(a) select a);
                }

                try
                {
                }
                finally
                {
                    lock (this)
                    {
                        index[newval ?? _asNull] = oldBag;

                        index.TryRemove(oldval ?? _asNull, out ImmutableList<long> removed);
                    }
                }
            }
        }

        public bool Contains(T item)
        {
            return _contains.ContainsKey(item ?? throw new ArgumentNullException("item"));
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
            return _data.Values.GetEnumerator();
        }

        public bool Remove(T item)
        {
            if (!_contains.ContainsKey(item ?? throw new ArgumentNullException("item")))
            {
                return false;
            }

            var id = _contains[item];

            try
            {
            }
            finally
            {
                lock (this)
                {
                    foreach (var dic in _masterIndex)
                    {
                        foreach (var v1 in dic.Value)
                        {
                            dic.Value[v1.Key] = v1.Value.Remove(id);
                        }
                    }

                    _data.TryRemove(id, out T val);
                    _contains.TryRemove(item, out id);
                }
            }

            return true;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return _data.Values.GetEnumerator();
        }
    }
}
