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
04/2018    0.6     ConcurrentObservableCollection, LightweightLongList, SlimConcurrentDictionary
***********************************************************************/
using System;
using System.Collections.Generic;
using System.Collections;
using System.Threading;
using System.Linq;
using System.Collections.Specialized;
using System.ComponentModel;

namespace CodexMicroORM.Core.Collections
{
    public interface ICEFIndexedListItem
    {
        object GetValue(string propName, bool unwrap);
    }

    /// <summary>
    /// In a single-threaded scenario, this is not much different than a vanilla dictionary. In a multi-threaded scenario, it uses a sub-dictionary per thread (bucket).
    /// Doing so, most update (add) operations can be very quick: you don't have to worry about waiting for anyone because you're partying on your own instance for your own thread (well, we hope usually).
    /// Readers might be slower, however, since it's not as simple as looking at one dictionary, potentially - one per thread (bucket) that was ever used to populate.
    /// The main difference compared to ConcurrentDictionary: writes are about 2x faster, reads can be slower - but still comparatively fast.
    /// Of note, types used by this if kept to "atomic" types, we avoid read locks but allow for possibly dirty reads (noting this as by design).
    /// Future optimizations could include consolidation, such as after heavy parallel loading is performed, at "quiet times", etc. (This would give best of all worlds - reduce to 1 dictionary when time permits but would be a potentially long blocking action.)
    /// </summary>
    /// <typeparam name="TKey"></typeparam>
    /// <typeparam name="TValue"></typeparam>
    public class SlimConcurrentDictionary<TKey, TValue> : ICollection<KeyValuePair<TKey, TValue>>, IEnumerable<KeyValuePair<TKey, TValue>>, IEnumerable, IDictionary<TKey, TValue>, IReadOnlyCollection<KeyValuePair<TKey, TValue>>, IReadOnlyDictionary<TKey, TValue>, ICollection, IDictionary
    {
        private class BucketInfo
        {
            public Dictionary<TKey, TValue> Map;
            public RWLockInfo Lock = new RWLockInfo() { AllowDirtyReads = false };
        }

        private BucketInfo[] _perThreadMap;
        private object _lock = new object();
        private int _count = 0;
        private int _initCapacity = Globals.DefaultDictionaryCapacity;
        private HashSet<TKey> _building = new HashSet<TKey>();

        public SlimConcurrentDictionary(int? initCapacity = null, int? buckets = null)
        {
            if (!buckets.HasValue)
            {
                buckets = Globals.DefaultCollectionConcurrencyLevel;
            }

            if (initCapacity.HasValue)
            {
                _initCapacity = (initCapacity.Value / buckets.Value).MaxOf(Globals.DefaultDictionaryCapacity);
            }

            _perThreadMap = new BucketInfo[buckets.Value];
        }

        public TValue this[TKey key] { get => ValueByKey(key); set => SafeAdd(key, value); }

        public ICollection<TKey> Keys => (from a in All() select a.Key).ToList();

        public ICollection<TValue> Values => (from a in All() select a.Value).ToList();

        public int Count => Interlocked.Add(ref _count, 0);

        public bool IsReadOnly => false;

        IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => (from a in All() select a.Key);

        IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => (from a in All() select a.Value);

        public bool IsSynchronized => true;

        public object SyncRoot => _lock;

        public bool IsFixedSize => false;

        ICollection IDictionary.Keys => (from a in All() select a.Key).ToList();

        ICollection IDictionary.Values => (from a in All() select a.Value).ToList();

        public object this[object key] { get => ValueByKey((TKey)key); set => SafeAdd((TKey)key, (TValue)value); }

        public void Compact()
        {
            while (true)
            {
                var orig = _perThreadMap;
                var newpt = new BucketInfo[_perThreadMap.Length];
                var dw = new BucketInfo() { Map = new Dictionary<TKey, TValue>(_count.MaxOf(_initCapacity)) };
                newpt[0] = dw;

                var snap = All().ToList();

                foreach (var kvp in snap)
                {
                    dw.Map[kvp.Key] = kvp.Value;
                }

                if (Interlocked.CompareExchange(ref _perThreadMap, newpt, orig) == orig)
                {
                    Interlocked.Exchange(ref _count, snap.Count());
                    return;
                }

                Thread.Yield();
            }
        }

        public IEnumerable<KeyValuePair<TKey, TValue>> All()
        {
            foreach (var bi in _perThreadMap)
            {
                if (bi != null)
                {
                    foreach (var i in bi.Map)
                    {
                        yield return i;
                    }
                }
            }
        }

        private void SafeAdd(TKey key, TValue value)
        {
            var found = FindByKey(key);

            if (found.map == null)
            {
                Add(key, value);
                return;
            }

            using (new WriterLock(found.map.Lock))
            {
                found.map.Map[key] = value;
            }
        }

        public void Add(TKey key, TValue value)
        {
            AddInternal(key, value);
        }

        public TValue AddWithFactory(TKey key, Func<TKey, TValue> creator)
        {
            var (map, value) = FindByKey(key);

            if (map != null)
            {
                return value;
            }

            while (true)
            {
                lock (_building)
                {
                    if (!_building.Contains(key))
                    {
                        _building.Add(key);
                        break;
                    }
                }

                Thread.Yield();
            }

            try
            {
                (map, value) = FindByKey(key);

                if (map == null)
                {
                    value = creator.Invoke(key);
                    Add(key, value);
                }

                return value;
            }
            finally
            {
                lock (_building)
                {
                    _building.Remove(key);
                }
            }
        }

        private void AddInternal(TKey key, TValue value)
        {
            var tkey = (Environment.CurrentManagedThreadId + _perThreadMap.Length - 1) % _perThreadMap.Length;
            var dw = _perThreadMap[tkey];

            if (dw == null)
            {
                dw = new BucketInfo() { Map = new Dictionary<TKey, TValue>(_initCapacity) };
                dw = Interlocked.CompareExchange(ref _perThreadMap[tkey], dw, null) ?? dw;
            }

            using (new WriterLock(dw.Lock))
            {
                dw.Map[key] = value;
            }

            Interlocked.Increment(ref _count);
        }

        private (BucketInfo map, TValue value) FindByKey(TKey key)
        {
            foreach (var bi in _perThreadMap)
            {
                if (bi != null)
                {
                    try
                    {
                        if (bi.Map.TryGetValue(key, out var val))
                        {
                            return (bi, val);
                        }
                    }
                    catch
                    {
                        using (new ReaderLock(bi.Lock))
                        {
                            if (bi.Map.TryGetValue(key, out var val))
                            {
                                return (bi, val);
                            }
                        }
                    }
                }
            }

            return (null, default);
        }

        private TValue ValueByKey(TKey key)
        {
            var (map, value) = FindByKey(key);

            if (map == null)
            {
                throw new InvalidOperationException("Could not find value in dictionary.");
            }

            return value;
        }

        public void Add(KeyValuePair<TKey, TValue> item)
        {
            AddInternal(item.Key, item.Value);
        }

        public void Clear()
        {
            foreach (var i in _perThreadMap)
            {
                if (i != null)
                {
                    i.Map.Clear();
                }
            }

            Interlocked.Exchange(ref _count, 0);
        }

        public bool Contains(KeyValuePair<TKey, TValue> item)
        {
            var (map, value) = FindByKey(item.Key);

            if (map != null)
            {
                if (value == null && item.Value == null)
                {
                    return true;
                }

                if (value == null || item.Value == null)
                {
                    return false;
                }

                return value.Equals(item.Value);
            }

            return false;
        }

        public bool ContainsKey(TKey key)
        {
            return FindByKey(key).map != null;
        }

        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
            throw new NotSupportedException();
        }

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        {
            return All().GetEnumerator();
        }

        public bool Remove(TKey key)
        {
            return RemoveInternal(key);
        }

        private bool RemoveInternal(TKey key)
        {
            var (map, value) = FindByKey(key);

            if (map != null)
            {
                using (new WriterLock(map.Lock))
                {
                    return map.Map.Remove(key);
                }
            }

            return false;
        }

        public bool Remove(KeyValuePair<TKey, TValue> item)
        {
            var (map, value) = FindByKey(item.Key);

            if (map != null)
            {
                if ((item.Value == null && value == null) || (!(item.Value == null || value == null) && (item.Value.Equals(value))))
                {
                    using (new WriterLock(map.Lock))
                    {
                        return map.Map.Remove(item.Key);
                    }
                }
            }

            return false;
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            var found = FindByKey(key);

            if (found.map != null)
            {
                value = found.value;
                return true;
            }

            value = default;
            return false;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return All().GetEnumerator();
        }

        public void CopyTo(Array array, int index)
        {
            throw new NotSupportedException();
        }

        public void Add(object key, object value)
        {
            AddInternal((TKey)key, (TValue)value);
        }

        public bool Contains(object key)
        {
            return FindByKey((TKey)key).map != null;
        }

        IDictionaryEnumerator IDictionary.GetEnumerator()
        {
            throw new NotSupportedException();
        }

        public void Remove(object key)
        {
            RemoveInternal((TKey)key);
        }
    }

    /// <summary>
    /// This class will serve as a drop-in replacement for ObservableCollection, as used by EntitySet and its subclasses.
    /// The main benefit is thread-safety offered by use of bucket-per-thread. (R/W locks are still valid, across buckets.)
    /// Some of the concepts from LightweightLongList are borrowed here.
    /// In a single-threaded scenario, this operates like a simple array up to certain size threshold.
    /// Fragmentation is possible for large lists - will offer some compaction services later.
    /// Some subtle differences exist compared to a traditional list: ordering is not predictable, some operations not supported (efficiency bad or deemed seldom used).
    /// Read performance is emphasized here, beats ConcurrentBag as a comparable.
    /// After looking at BCL options having this available as a custom solution is "good until further notice" - preliminary testing looks pretty darn good.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class ConcurrentObservableCollection<T> : ICollection<T>, IEnumerable<T>, IList<T>, IEnumerable, IList, INotifyCollectionChanged, INotifyPropertyChanged where T : class, new()
    {
        private class Block
        {
            public T[] Data;
            public Block Next;

            public ref T GetItem(int idx)
            {
                return ref Data[idx];
            }
        }

        private class BucketInfo
        {
            public Block Head;
            public Block Tail;
            public int TailNextPos;
            public int TotalSize;
            public RWLockInfo Lock = new RWLockInfo() { AllowDirtyReads = false };
        }

        private BucketInfo[] _perThreadMap;
        private int _count = 0;
        private RWLockInfo _lock = new RWLockInfo() { AllowDirtyReads = false };
        private int _initCapacity = Globals.DefaultListCapacity;

        public ConcurrentObservableCollection(int? initCapacity = null, int? buckets = null)
        {
            if (!buckets.HasValue)
            {
                buckets = Globals.DefaultCollectionConcurrencyLevel;
            }

            if (initCapacity.HasValue)
            {
                _initCapacity = initCapacity.Value;
            }

            _perThreadMap = new BucketInfo[buckets.Value];
        }

        public int Count => Interlocked.Add(ref _count, 0);

        public bool IsReadOnly => false;

        public bool IsFixedSize => false;

        public bool IsSynchronized => true;

        public object SyncRoot => _lock;

        object IList.this[int index] { get => All().Skip(index).FirstOrDefault(); set => throw new NotSupportedException(); }

        public T this[int index] { get => All().Skip(index).FirstOrDefault(); set => throw new NotSupportedException(); }

        public event PropertyChangedEventHandler PropertyChanged;
        public event NotifyCollectionChangedEventHandler CollectionChanged;

        public void Add(T item)
        {
            var tkey = (Environment.CurrentManagedThreadId + _perThreadMap.Length - 1) % _perThreadMap.Length;
            var info = _perThreadMap[tkey];

            if (info == null)
            {
                var nb = new Block();
                info = new BucketInfo() { Head = nb, Tail = nb, TailNextPos = 0 };
                info = Interlocked.CompareExchange(ref _perThreadMap[tkey], info, null) ?? info;
            }

            using (new WriterLock(info.Lock))
            {
                if (info.Tail.Data == null)
                {
                    info.Tail.Data = new T[_initCapacity];
                    info.TotalSize = _initCapacity;
                }

                if (info.TailNextPos >= info.Tail.Data.Length)
                {
                    // Small lists grow aggressively, large ones less so
                    var total = info.TotalSize;
                    var newsize = Convert.ToInt32(total < 30000 ? total * 4 : total < 1000000 ? total * 1.6 : total * 1.2);
                    ResizeBucket(info, newsize);
                }

                info.Tail.GetItem(info.TailNextPos) = item;

                Interlocked.Increment(ref info.TailNextPos);
            }

            Interlocked.Increment(ref _count);
            OnPropertyChanged("Count");
            OnPropertyChanged("Item[]");
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item));
        }

        public void Replace(T olditem, T newitem)
        {
            var (info, block, index) = Find(olditem);

            if (block != null)
            {
                using (new WriterLock(info.Lock))
                {
                    block.GetItem(index) = newitem;
                }

                OnPropertyChanged("Item[]");
                OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, newitem, olditem));
            }
        }

        private void ResizeBucket(BucketInfo info, int newtotalsize)
        {
            // Below a "small" list threshold, we try to create/copy a new array - above the threshold, just create a new block and link it
            if (newtotalsize <= 32768)
            {
                var newdata = new T[newtotalsize];
                int pos = 0;

                for (int i = 0; i < info.TailNextPos; ++i)
                {
                    T v = info.Tail.Data[i];

                    if (v != null)
                    {
                        newdata[pos] = v;
                        ++pos;
                    }
                }

                info.TotalSize = newtotalsize;
                info.Tail.Data = newdata;
                info.TailNextPos = pos;
                return;
            }

            var blksize = newtotalsize - info.TotalSize;

            // This means we're adding a new block in linked list of blocks
            var nb = new Block
            {
                Data = new T[blksize]
            };

            info.TotalSize = newtotalsize;
            info.Tail.Next = nb;
            info.Tail = nb;
            info.TailNextPos = 0;
        }

        protected virtual void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            CollectionChanged?.Invoke(this, e);
        }

        protected virtual void OnPropertyChanged(string prop)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }

        public void Clear()
        {
            ClearItems();
        }

        protected virtual void ClearItems()
        {
            using (new WriterLock(_lock))
            {
                _perThreadMap = new BucketInfo[_perThreadMap.Length];
                Interlocked.Exchange(ref _count, 0);
            }

            OnPropertyChanged("Count");
            OnPropertyChanged("Item[]");
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        private (BucketInfo info, Block block, int index) Find(T item)
        {
            foreach (var bi in _perThreadMap)
            {
                if (bi != null)
                {
                    using (new ReaderLock(bi.Lock))
                    {
                        var b = bi.Head;

                        while (b != null)
                        {
                            var len = (b == bi.Tail ? bi.TailNextPos : b.Data.Length);

                            for (int i = 0; i < len; ++i)
                            {
                                var v = b.Data[i];

                                if (v == item)
                                {
                                    return (bi, b, i);
                                }
                            }

                            b = b.Next;
                        }
                    }
                }
            }

            return (null, null, -1);
        }

        public IEnumerable<T> All()
        {
            foreach (var bi in _perThreadMap)
            {
                if (bi != null)
                {
                    using (new ReaderLock(bi.Lock))
                    {
                        var b = bi.Head;

                        while (b != null)
                        {
                            var len = (b == bi.Tail ? bi.TailNextPos : b.Data.Length);

                            for (int i = 0; i < len; ++i)
                            {
                                var v = b.Data[i];

                                if (v != null)
                                {
                                    yield return v;
                                }
                            }

                            b = b.Next;
                        }
                    }
                }
            }
        }

        public bool Contains(T item)
        {
            return Find(item).block != null;
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            throw new NotSupportedException();
        }

        public IEnumerator<T> GetEnumerator()
        {
            return All().GetEnumerator();
        }

        public bool Remove(T item)
        {
            var (info, block, index) = Find(item);

            if (block != null)
            {
                ref var dp = ref block.GetItem(index);

                using (var wl = new WriterLock(info.Lock))
                {
                    if (Interlocked.CompareExchange(ref dp, null, item) == item)
                    {
                        Interlocked.Decrement(ref _count);

                        wl.Release();
                        OnPropertyChanged("Count");
                        OnPropertyChanged("Item[]");
                        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, item));
                        return true;
                    }

                    // Small chance was compacted or something else to move it so check one more time with lock in place
                    var found = Find(item);

                    if (found.block == block)
                    {
                        ref var dp2 = ref found.block.GetItem(found.index);

                        if (Interlocked.CompareExchange(ref dp2, null, item) == item)
                        {
                            Interlocked.Decrement(ref _count);

                            wl.Release();
                            OnPropertyChanged("Count");
                            OnPropertyChanged("Item[]");
                            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, item));
                            return true;
                        }
                    }

                    throw new InvalidOperationException("Failed to remove item from collection.");
                }
            }

            return false;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return All().GetEnumerator();
        }

        public int IndexOf(T item)
        {
            int pos = 0;

            foreach (var i in All())
            {
                if (i == item)
                {
                    return pos;
                }

                ++pos;
            }

            return -1;
        }

        public void Insert(int index, T item)
        {
            throw new NotSupportedException();
        }

        public void RemoveAt(int index)
        {
            Remove(this[index]);
        }

        public int Add(object value)
        {
            var t = (value as T) ?? throw new ArgumentException("value must be type T.");
            Add(t);
            return IndexOf(t);
        }

        public bool Contains(object value)
        {
            if (value is T t)
            {
                return Contains(t);
            }

            return false;
        }

        public int IndexOf(object value)
        {
            if (value is T t)
            {
                return IndexOf(t);
            }

            return -1;
        }

        public void Insert(int index, object value)
        {
            throw new NotSupportedException();
        }

        public void Remove(object value)
        {
            var t = (value as T) ?? throw new ArgumentException("value must be type T.");
            Remove(t);
        }

        public void CopyTo(Array array, int index)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>
    /// This specialized list type uses arrays, no reader locks, and automatic growth that does not involve copy operations past a certain threshold.
    /// This sacrifices a bit of read performance (more cpu cache misses compared to a single array) which isn't a great thing, but it tries to find a balance with expensive operations like resizes and adds.
    /// It also assumes removals are infrequent and/or lots of memory available and/or object is trasient since items are marked as removed, not actually removed.
    /// We may offer explicit compaction services in the future.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public sealed class LightweightLongList
    {
        private class Block
        {
            public long[] Data;
            public Block Next;

            public ref long GetItem(int idx)
            {
                return ref Data[idx];
            }
        }

        private int _nextPos = 0;
        private Block _head;
        private Block _tail;
        private int _count = 0;
        private int _curCapacity;
        private object _sizingLock = new object();

        public IEnumerable<long> All()
        {
            var b = _head;

            while (b != null)
            {
                var len = (b == _tail ? _nextPos : b.Data.Length);

                for (int i = 0; i < len; ++i)
                {
                    var v = b.Data[i];

                    if (v != long.MaxValue)
                    {
                        yield return v;
                    }
                }

                b = b.Next;
            }
        }

        public LightweightLongList(int? initCapacity = null)
        {
            if (!initCapacity.HasValue)
            {
                initCapacity = Globals.DefaultListCapacity;
            }

            _curCapacity = initCapacity.Value;
            int np = 0;
            Resize(_curCapacity, ref np);
        }

        public int Count => Interlocked.Add(ref _count, 0);

        public bool IsReadOnly => false;

        private void Resize(int newsize, ref int np)
        {
            // Below a "small" list threshold, we try to create/copy a new array - above the threshold, it's more efficient to just create a new block
            if (newsize <= 250000)
            {
                var newdata = new long[newsize];
                int pos = 0;

                for (int i = 0; i < np; ++i)
                {
                    long v = _head.Data[i];

                    if (v != long.MaxValue)
                    {
                        newdata[pos] = v;
                        ++pos;
                    }
                }

                for (int i = pos; i < newsize; ++i)
                {
                    newdata[i] = long.MaxValue;
                }

                if (_head == null)
                {
                    var nb2 = new Block
                    {
                        Data = newdata,
                    };

                    _head = nb2;
                    _tail = nb2;
                }
                else
                {
                    _head.Data = newdata;
                }

                np = _nextPos = pos;
                _curCapacity = newsize;
                return;
            }

            var blksize = newsize - _curCapacity;

            // This means we're adding a new block in linked list of blocks
            var nb = new Block
            {
                Data = new long[blksize],
            };

            for (int i = 0; i < blksize; ++i)
            {
                nb.Data[i] = long.MaxValue;
            }

            if (_head == null)
            {
                _head = nb;
                _tail = nb;
            }
            else
            {
                _tail.Next = nb;
                _tail = nb;
            }

            np = _nextPos = 0;
            _curCapacity = newsize;
        }

        public void AddRange(IEnumerable<long> items)
        {
            foreach (var i in items)
            {
                Add(i);
            }
        }

        public void Add(long item)
        {
            if (item == long.MaxValue)
            {
                throw new ArgumentException("item cannot be max value.");
            }

            // Keep trying to add it to tail
            while (true)
            {
                int np;

                lock (_sizingLock)
                {
                    np = Interlocked.Add(ref _nextPos, 0);

                    if (np >= _tail.Data.Length)
                    {
                        // Growth is aggressive for small lists, conservative for larger lists
                        var newsize = Convert.ToInt32(_curCapacity < 500000 ? _curCapacity * 4 : _curCapacity < 2500000 ? _curCapacity * 1.6 : _curCapacity * 1.2);
                        Resize(newsize, ref np);
                    }
                }

                if (Interlocked.CompareExchange(ref _tail.GetItem(np), item, long.MaxValue) == long.MaxValue)
                {
                    // Success! Since we own this slot now, remainder of housekeeping should be ok
                    Interlocked.Increment(ref _nextPos);
                    Interlocked.Increment(ref _count);
                    return;
                }

                // Failed, so just keep trying!
                Thread.Yield();
            }
        }

        public void Clear()
        {
            lock (_sizingLock)
            {
                _head = null;
                _tail = null;
                int np = 0;
                Resize(_curCapacity, ref np);
            }
        }

        public bool Contains(long item)
        {
            return (from a in All() where a == item select a).Any();
        }

        public bool Remove(long item)
        {
            // Remove simply marks found item as unused - could theoretically offer an explicit compaction method but for now we assume have lots of memory and can grow
            var b = _head;

            while (b != null)
            {
                var len = (b == _tail ? _nextPos : b.Data.Length);

                for (int i = 0; i < len; ++i)
                {
                    if (b.Data[i] == item)
                    {
                        b.Data[i] = long.MaxValue;
                        Interlocked.Decrement(ref _count);
                        return true;
                    }
                }

                b = b.Next;
            }

            return false;
        }
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

        private static int _baseCapacity = Environment.ProcessorCount * 14;
        private static object _asNull = new object();

        private long _dataID = long.MinValue;
        private RWLockInfo _lock = new RWLockInfo();
        private int _initCapacity = _baseCapacity;

        // Config related
        private HashSet<string> _isUnique = new HashSet<string>();
        private HashSet<string> _neverNull = new HashSet<string>();

        // Instance state
        private SlimConcurrentDictionary<long, T> _data;
        private SlimConcurrentDictionary<T, long> _contains;
        private Dictionary<string, SlimConcurrentDictionary<object, LightweightLongList>> _masterIndex = new Dictionary<string, SlimConcurrentDictionary<object, LightweightLongList>>();

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

                _masterIndex[props] = new SlimConcurrentDictionary<object, LightweightLongList>(cap);
            }

            _data = new SlimConcurrentDictionary<long, T>(_initCapacity);
            _contains = new SlimConcurrentDictionary<T, long>(_initCapacity);
        }

        /// <summary>
        /// Locate a specific record, if it exists in the collection.
        /// </summary>
        /// <param name="source"></param>
        /// <returns></returns>
        public T Find(T source)
        {
            if (_contains.TryGetValue(source, out long id))
            {
                return _data[id];
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
        public void AddSafe(T item)
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

                        if (dic.Value.TryGetValue(propVal ?? _asNull, out LightweightLongList bag))
                        {
                            if (bag.Count > 0 && _isUnique.Contains(dic.Key))
                            {
                                throw new CEFInvalidOperationException($"Collection already contains an entry for '{propVal}'.");
                            }

                            bag.Add(id);
                        }
                        else
                        {
                            LightweightLongList newBag = new LightweightLongList();
                            newBag.Add(id);
                            dic.Value[propVal ?? _asNull] = newBag;
                        }
                    }
                }

                _data[id] = item;
                _contains[item] = id;
            }
        }

        public void Add(T item)
        {
            Add(item, true);
        }

        /// <summary>
        /// Add an item to the collection. (If it already exists, no operation.) Slightly less safe than AddSafe given can have dirty reads - record may appear "missing" when searching by some fields until all are committed, but maintains consistency of underlying structures.
        /// </summary>
        /// <param name="item"></param>
        public void Add(T item, bool checkExists)
        {
            if (checkExists && _contains.ContainsKey(item ?? throw new ArgumentNullException("item")))
            {
                return;
            }

            var id = Interlocked.Increment(ref _dataID);

            bool addeddata = false;
            bool addedcontains = false;
            List<(LightweightLongList bag, long id)> toremove = new List<(LightweightLongList bag, long id)>(8);

            try
            {
                _data[id] = item;
                addeddata = true;
                _contains[item] = id;
                addedcontains = true;

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

                        if (dic.Value.TryGetValue(propVal ?? _asNull, out LightweightLongList bag))
                        {
                            if (bag.Count > 0 && _isUnique.Contains(dic.Key))
                            {
                                throw new CEFInvalidOperationException($"Collection already contains an entry for '{propVal}'.");
                            }

                            bag.Add(id);
                            toremove.Add((bag, id));
                        }
                        else
                        {
                            LightweightLongList newBag = new LightweightLongList();
                            newBag.Add(id);
                            dic.Value[propVal ?? _asNull] = newBag;
                            toremove.Add((newBag, id));
                        }
                    }
                }
            }
            catch
            {
                // We are effectively doing a compensating tx here, rolling back possible state updates. At the end we rethrow.
                foreach (var remitem in toremove)
                {
                    remitem.bag.Remove(remitem.id);
                }

                if (addedcontains)
                {
                    _contains.Remove(item);
                }

                if (addeddata)
                {
                    _data.Remove(id);
                }

                throw;
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
                if (_masterIndex[propName].TryGetValue(propValue ?? _asNull, out LightweightLongList bag))
                {
                    T val = null;
                    return (from a in bag.All() where _data.TryGetValue(a, out val) && preview(val) select val).FirstOrDefault();
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
                if (_masterIndex[propName].TryGetValue(propValue ?? _asNull, out LightweightLongList bag))
                {
                    return (from a in bag.All() select _data[a]).FirstOrDefault();
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

            if (_masterIndex[propName].TryGetValue(propValue ?? _asNull, out LightweightLongList bag))
            {
                T val = null;
                return (from a in bag.All() where _data.TryGetValue(a, out val) && preview(val) select val).FirstOrDefault();
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

            if (_masterIndex[propName].TryGetValue(propValue ?? _asNull, out LightweightLongList bag))
            {
                return (from a in bag.All() select _data[a]).FirstOrDefault();
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
                if (_masterIndex[propName].TryGetValue(propValue ?? _asNull, out LightweightLongList bag))
                {
                    T val = null;
                    return (from a in bag.All() where _data.TryGetValue(a, out val) && preview(val) select val).ToArray();
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
                if (_masterIndex[propName].TryGetValue(propValue ?? _asNull, out LightweightLongList bag))
                {
                    return (from a in bag.All() select _data[a]).ToArray();
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

            if (_masterIndex[propName].TryGetValue(propValue ?? _asNull, out LightweightLongList bag))
            {
                T val = null;
                return (from a in bag.All() where _data.TryGetValue(a, out val) && preview(val) select val);
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

            if (_masterIndex[propName].TryGetValue(propValue ?? _asNull, out LightweightLongList bag))
            {
                return (from a in bag.All() select _data[a]);
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

            var index = _masterIndex[propName];

            // todo - compensating tx?

            if (index.TryGetValue(oldval ?? _asNull, out LightweightLongList oldBag))
            {
                if (index.TryGetValue(newval ?? _asNull, out LightweightLongList newBag))
                {
                    if (_isUnique.Contains(propName) && newBag.Count > 0)
                    {
                        throw new CEFInvalidOperationException($"Collection already contains an entry for '{newval}'.");
                    }

                    oldBag.AddRange(from a in newBag.All() where !oldBag.Contains(a) select a);
                }

                index[newval ?? _asNull] = oldBag;

                if (index.ContainsKey(oldval ?? _asNull))
                {
                    index.Remove(oldval ?? _asNull);
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
            return _contains.ContainsKey(item ?? throw new ArgumentNullException("item"));
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            throw new NotSupportedException();
        }

        public void CopyTo(Array array, int index)
        {
            throw new NotSupportedException();
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

                    if (dic.Value.TryGetValue(propVal ?? _asNull, out LightweightLongList bag))
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
