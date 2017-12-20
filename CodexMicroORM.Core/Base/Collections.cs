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
        ConcurrentDictionary<string, bool> _isUnique = new ConcurrentDictionary<string, bool>();
        ConcurrentDictionary<T, long> _contains = new ConcurrentDictionary<T, long>();
        ConcurrentDictionary<string, ConcurrentDictionary<object, ConcurrentBag<long>>> _masterIndex = new ConcurrentDictionary<string, ConcurrentDictionary<object, ConcurrentBag<long>>>();

        #endregion

        public ConcurrentIndexedList(params string[] propsToIndex)
        {
            foreach (var props in propsToIndex)
            {
                _masterIndex[props] = new ConcurrentDictionary<object, ConcurrentBag<long>>();
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
            _isUnique[propName] = true;
            return this;
        }

        public void Add(T item)
        {
            if (_contains.ContainsKey(item ?? throw new ArgumentNullException("item")))
            {
                return;
            }

            var id = Interlocked.Increment(ref _dataID);

            foreach (var dic in _masterIndex)
            {
                var propVal = item.GetValue(dic.Key, false);

                if (dic.Value.TryGetValue(propVal ?? _asNull, out ConcurrentBag<long> bag))
                {
                    if (bag.Count > 0 && _isUnique.ContainsKey(dic.Key))
                    {
                        throw new CEFInvalidOperationException($"Collection already contains an entry for '{propVal}'.");
                    }

                    bag.Add(id);
                }
                else
                {
                    var newBag = new ConcurrentBag<long>();
                    newBag.Add(id);
                    dic.Value[propVal ?? _asNull] = newBag;
                }
            }

            _data[id] = item;
            _contains[item] = id;
        }

        public void Clear()
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

            if (_masterIndex[propName].TryGetValue(propValue ?? _asNull, out ConcurrentBag<long> bag))
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

            if (index.TryGetValue(oldval ?? _asNull, out ConcurrentBag<long> oldBag))
            {
                if (index.TryGetValue(newval ?? _asNull, out ConcurrentBag<long> newBag))
                {
                    if (_isUnique.ContainsKey(propName) && newBag.Count > 0)
                    {
                        throw new CEFInvalidOperationException($"Collection already contains an entry for '{newval}'.");
                    }

                    foreach (var l in (from a in newBag where !oldBag.Contains(a) select a))
                    {
                        oldBag.Add(l);
                    }
                }

                index[newval ?? _asNull] = oldBag;

                index.TryRemove(oldval ?? _asNull, out ConcurrentBag<long> removed);
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

            lock (this)
            {
                foreach (var dic in _masterIndex)
                {
                    foreach (var v1 in dic.Value)
                    {
                        // There's no "remove" so we need to scan - may consider changing to a dictionary if this proves a drag
                        dic.Value[v1.Key] = new ConcurrentBag<long>(from a in v1.Value where a != id select a);
                    }
                }

                _data.TryRemove(id, out T val);
                _contains.TryRemove(item, out id);
            }

            return true;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return _data.Values.GetEnumerator();
        }
    }
}
