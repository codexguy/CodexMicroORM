/***********************************************************************
Copyright 2024 CodeX Enterprises LLC

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
***********************************************************************/
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using CodexMicroORM.Core.Helper;
#nullable enable

namespace CodexMicroORM.Core.Services
{
    [Flags]
    public enum MergeToType
    {
        Insert = 1,
        Update = 2,
        Delete = 4,
        All = 7
    }

    public interface ICEFIndexed
    {
        IEnumerable<object?> FindByEquality(IEnumerable<string> fieldNames, IEnumerable<object?> values);
    }

    public interface ICEFUpdateableIndex
    {
        void AddNonGeneric(object o);

        void AddNonGenericQueued(object o);

        void WaitForQueuedAdds();

        void RemoveNonGeneric(object o);

        RWLockInfo LockInfo { get; }

        IEnumerable<object?> FindByEquality(IEnumerable<string> fieldNames, IEnumerable<object?> values);

        void Reset();
    }

    /// <summary>
    /// An IndexedSet is similar to an IndexedView, but it remains "live" with respect to changes in its underlying source. This makes it "heavier" than a snapshot, but very useful for cases where you KNOW indexing is beneficial.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class IndexedSet<T> : EntitySet<T>,
        ICEFUpdateableIndex, ICEFIndexed, IDisposable where T : class, new()
    {
        private readonly ConcurrentDictionary<object, IDictionary<string, object?>> _prevAllValues = new();

        // Using containment - don't want to dup all the indexing logic which is held in views, the set's job is to maintain a live copy of data where ins/upd/del causes indexes to natually update
        private IndexedSnapshot<T>? _view;
        private bool _isLive = Globals.LiveIndexesForIndexedSets;

        internal IndexedSnapshot<T> View => _view ?? throw new InvalidOperationException("View not initialized.");

        public IndexedSet() : base()
        {
            if (_isLive)
            {
                _view = new IndexedSnapshot<T>(this, null);
            }
        }

        public IndexedSet(bool live, ServiceScope ss) : base(ss)
        {
            _isLive = live;

            if (_isLive)
            {
                _view = new IndexedSnapshot<T>(this, ss);
            }
        }

        public IndexedSet(ServiceScope ss) : base(ss)
        {
            if (_isLive)
            {
                _view = new IndexedSnapshot<T>(this, ss);
            }
        }

        public IndexedSet(IEnumerable<T> source) : base(source)
        {
            if (_isLive)
            {
                _view = new IndexedSnapshot<T>(this, null);
            }
        }

        public IndexedSet<T> Clone()
        {
            // Takes "deep shallow" copy
            return new IndexedSet<T>(All().ToArray());
        }

        public IndexedSet<T> AddEqualityIndex(string propName)
        {
            if (_view != null)
            {
                _view.AutoInferIndexes = false;
                _view.AddEqualityIndex(propName, null);
            }
            return this;
        }

        public IndexedSet<T> AddRangeIndex(string propName)
        {
            if (_view != null)
            {
                _view.AutoInferIndexes = false;
                _view.AddRangeIndex(propName, null);
            }
            return this;
        }

        public void Reset()
        {
            _prevAllValues.Clear();
            ClearItems();
        }

        public bool IsLiveTracked
        {
            get
            {
                return _isLive;
            }
            set
            {
                if (_isLive != value)
                {
                    _isLive = value;

                    if (_view != null)
                    {
                        UnlinkAll();
                        _view.Dispose();
                    }

                    if (_isLive)
                    {
                        // Rebuild entire index needed
                        _view = new IndexedSnapshot<T>(this, BoundScope);
                    }
                    else
                    {
                        // Blow away existing index data
                        _view = null;
                    }
                }
            }
        }

        void ICEFUpdateableIndex.AddNonGeneric(object o)
        {
            if (o is not T)
            {
                throw new ArgumentException($"o is not type {typeof(T).Name}");
            }

            this.Add(o as T ?? throw new InvalidOperationException("Cannot add object of incompatible type."));
        }

        void ICEFUpdateableIndex.AddNonGenericQueued(object o)
        {
            if (o is not T)
            {
                throw new ArgumentException($"o is not type {typeof(T).Name}");
            }

            this.QueuedAdd(o as T ?? throw new InvalidOperationException("Cannot add object of incompatible type."));
        }

        void ICEFUpdateableIndex.WaitForQueuedAdds()
        {
            this.WaitForQueuedAdds();
        }

        void ICEFUpdateableIndex.RemoveNonGeneric(object o)
        {
            if (o is not T)
            {
                throw new ArgumentException($"o is not type {typeof(T).Name}");
            }

            this.Remove(o as T ?? throw new InvalidOperationException("Cannot remove object of incompatible type."));
        }

        public bool? SupportsJoinRules
        {
            get;
            set;
        } = null;

        public IEnumerable<object?> FindByEquality(IEnumerable<string> fieldNames, IEnumerable<object?> values)
        {
            if (_view == null)
            {
                throw new InvalidOperationException("View is not initialized.");
            }

            using (new ReaderLock(this.LockInfo))
            {
                return ((ICEFIndexed)View).FindByEquality(fieldNames, values).ToArray();
            }
        }

        public IEnumerable<T> FindByEquality(string fieldName, object? value)
        {
            if (_view == null)
            {
                throw new InvalidOperationException("View is not initialized.");
            }

            using (new ReaderLock(this.LockInfo))
            {
                return View.FindByEqualityTyped([ fieldName ], [ value ]).ToArray();
            }
        }

        public IEnumerable<T> FindByEqualityNoLock(string fieldName, object value)
        {
            if (_view == null)
            {
                throw new InvalidOperationException("View is not initialized.");
            }

            return View.FindByEqualityTyped([ fieldName ], [ value ]);
        }

        public IEnumerable<T> FindByEqualityTyped(IEnumerable<string> fieldNames, IEnumerable<object> values)
        {
            if (_view == null)
            {
                throw new InvalidOperationException("View is not initialized.");
            }

            return View.FindByEqualityTyped(fieldNames, values);
        }

        private void IndexChangeTracker_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // This only matters if we actually have indexes present!
            if (sender == null || e.PropertyName == null || (_view?._propIndexes?.Count).GetValueOrDefault() == 0)
            {
                return;
            }

            var iw = sender.AsInfraWrapped(false);

            if (iw != null)
            {
                var cv = iw.GetValue(e.PropertyName);
                object? pv = null;

                using var rl = new ReaderLock(LockInfo);
                if (_prevAllValues.TryGetValue(sender, out var dic))
                {
                    dic.TryGetValue(e.PropertyName, out pv);
                }

                if (!cv.IsSame(pv))
                {
                    rl.Release();

                    using (new WriterLock(LockInfo))
                    {
                        // Update indexes that refer to the changed column, if any
                        if (View._propIndexes.TryGetValue(e.PropertyName, out var piv1))
                        {
                            piv1.UpdateKey(pv, cv, sender);
                        }

                        if (dic == null)
                        {
                            dic = CEF.GetFilteredProperties(typeof(T), iw.GetAllValues(), false);
                            _prevAllValues[sender] = dic;
                        }
                        else
                        {
                            dic[e.PropertyName] = cv;
                        }
                    }
                }
            }
        }

        private void UnlinkAll()
        {
            var ss = BoundScope ?? CEF.CurrentServiceScope;

            foreach (var i in this)
            {
                var nf = ss.GetNotifyFriendlyFor(i);

                if (nf != null)
                {
                    nf.PropertyChanged -= IndexChangeTracker_PropertyChanged;
                }
            }
        }

        protected override void ClearItems()
        {
            UnlinkAll();

            if (_view != null)
            {
                using (new WriterLock(LockInfo))
                {
                    foreach (var kvp in _view._propIndexes)
                    {
                        kvp.Value.ClearAll();
                    }
                }
            }

            base.ClearItems();
        }

        protected override void ProcessAdd(IList newItems, int newStartingIndex)
        {
            base.ProcessAdd(newItems, newStartingIndex);

            if (_isLive)
            {
                var ss = BoundScope ?? CEF.CurrentServiceScope;

                using (CEF.UseServiceScope(ss))
                {
                    foreach (var i in newItems)
                    {
                        var nf = ss.GetNotifyFriendlyFor(i);

                        if (nf != null)
                        {
                            nf.PropertyChanged += IndexChangeTracker_PropertyChanged;

                            _prevAllValues[i] = CEF.GetFilteredProperties(typeof(T), i.MustInfraWrap().GetAllValues(), false);
                        }
                    }
                }

                foreach (var kvp in View._propIndexes)
                {
                    foreach (var i in newItems)
                    {
                        if (i is T t)
                        {
                            using (new WriterLock(LockInfo))
                            {
                                ((ICEFDataIndex<T>)kvp.Value).Add(t);
                            }
                        }
                    }
                }
            }
        }

        protected override void ProcessRemove(IList oldItems)
        {
            if (_isLive)
            {
                var ss = BoundScope ?? CEF.CurrentServiceScope;

                foreach (var i in oldItems)
                {
                    var nf = ss.GetNotifyFriendlyFor(i);

                    if (nf != null)
                    {
                        nf.PropertyChanged -= IndexChangeTracker_PropertyChanged;
                    }
                }

                foreach (var kvp in View._propIndexes)
                {
                    foreach (var i in oldItems)
                    {
                        if (i is T t)
                        {
                            using (new WriterLock(LockInfo))
                            {
                                ((ICEFDataIndex<T>)kvp.Value).Remove(t);
                            }
                        }
                    }
                }

            }

            base.ProcessRemove(oldItems);
        }

        public void MergeTo(IndexedSet<T> toset, string keyfield, MergeToType filter = MergeToType.All, Func<T, MergeToType, MergeToType>? preview = null)
        {
            if ((filter & (MergeToType.Insert | MergeToType.Update)) != 0)
            {
                foreach (T row in this)
                {
                    var val = row.FastGetValue(keyfield);
                    var found = toset.FindByEquality(keyfield, val);
                    var t = MergeToType.Update;

                    if (found == null || !found.Any())
                    {
                        t = MergeToType.Insert;

                        if (preview != null)
                        {
                            t = preview.Invoke(row, t);
                        }

                        if (t == MergeToType.Insert)
                        {
                            // Add
                            var rowcopy = row.DeepCopyObject();
                            toset.Add(rowcopy);
                        }
                    }
                    else
                    {
                        if (preview != null)
                        {
                            t = preview.Invoke(row, t);
                        }

                        switch (t)
                        {
                            case MergeToType.Update:
                                row.CopySharedTo(found.First(), true, keyfield);
                                break;

                            case MergeToType.Delete:
                                toset.Remove(found.First());
                                break;
                        }
                    }
                }
            }

            if ((filter & MergeToType.Delete) != 0)
            {
                List<T> todelete = [];

                foreach (T row in toset)
                {
                    var val = row.FastGetValue(keyfield);
                    var found = FindByEquality(keyfield, val);

                    if (found == null || !found.Any())
                    {
                        var t = MergeToType.Delete;

                        if (preview != null)
                        {
                            t = preview.Invoke(row, t);
                        }

                        if (t == MergeToType.Delete)
                        {
                            todelete.Add(row);
                        }
                    }
                }

                foreach (T tdr in todelete)
                {
                    toset.Remove(tdr);
                }
            }
        }

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    ClearItems();
                }

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }

    /// <summary>
    /// An IndexedView is a static snapshot of data from a source enumerable, indexed based on desired criteria. Indexes can be automatically inferred or explicitly defined.
    /// 
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class IndexedSnapshot<T> : IEnumerable<T>, ICEFIndexed, IDisposable where T : class, new()
    {
        internal ConcurrentDictionary<string, ICEFDataIndex> _propIndexes = new();
        private IEnumerable<T>? _source;
        private readonly ServiceScope? _ss;

        public IndexedSnapshot(IEnumerable<T> source, ServiceScope? ss)
        {
            _source = source;
            _ss = ss;
        }

        internal void Reset()
        {
            foreach (var e in _propIndexes)
            {
                e.Value.ClearAll();
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            return (_source ?? throw new InvalidOperationException("Source not set.")).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return (_source ?? throw new InvalidOperationException("Source not set.")).GetEnumerator();
        }

        public int IndexCount
        {
            get
            {
                return _propIndexes.Count;
            }
        }

        public bool? AutoInferIndexes
        {
            get;
            set;
        } = null;

        public void AddEqualityIndex(string propName, Type? propType = null)
        {
            EnsureIndexed(true, true, propName, () =>
            {
                if (propType == null)
                    propType = typeof(T).GetProperty(propName)?.PropertyType;

                if (propType == null)
                    throw new ArgumentNullException(nameof(propType));

                return propType;
            });
        }

        public void AddRangeIndex(string propName, Type? propType = null)
        {
            EnsureIndexed(true, false, propName, () =>
            {
                if (propType == null)
                {
                    propType = typeof(T).GetProperty(propName)?.PropertyType;
                }

                if (propType == null)
                    throw new ArgumentNullException(nameof(propType));

                return propType;
            });
        }

        private bool EnsureIndexed(bool canCreate, bool isEquality, string propName, Func<Type> propTypeGet)
        {
            _propIndexes.TryGetValue(propName, out ICEFDataIndex? ci);

            if (ci != null)
            {
                // If we have an existing equality index that should be promoted to comparison, do so now
                if (!isEquality && ((ICEFDataIndex)ci).OnlyEqualitySupport())
                {
                    var propType2 = propTypeGet();

                    // A problem if property is not IComparable - todo (may want to address this p2)
                    if (propType2.GetInterface("IComparable") == null)
                    {
                        throw new NotSupportedException("Property must support IComparable.");
                    }

                    if (propType2.IsValueType)
                    {
                        propType2 = typeof(Nullable<>).MakeGenericType(propType2);
                    }

                    _propIndexes[propName] = (ICEFDataIndex)(Activator.CreateInstance(typeof(ComparisonSortedIndex<,>).MakeGenericType(typeof(T), propType2), this, propName) ?? throw new InvalidOperationException($"Could not instantiate ComparisonSortedIndex."));
                }

                return true;
            }

            if (!canCreate)
            {
                return false;
            }

            var propType = propTypeGet();
            var bpt = propType;

            if (propType.IsEnum)
            {
                propType = Enum.GetUnderlyingType(propType);
            }

            if (propType.IsValueType && Nullable.GetUnderlyingType(propType) == null)
            {
                propType = typeof(Nullable<>).MakeGenericType(propType);
            }

            if (isEquality)
            {
                try
                {
                    _propIndexes[propName] = (ICEFDataIndex)(Activator.CreateInstance(typeof(EqualityHashIndex<,>).MakeGenericType(typeof(T), propType), this, propName) ?? throw new InvalidOperationException("Could not instantiate EqualityHashIndex."));
                }
#pragma warning disable CS0168 // Variable is declared but never used
                catch (Exception ex)
#pragma warning restore CS0168 // Variable is declared but never used
                {
#if DEBUG
                    if (System.Diagnostics.Debugger.IsAttached)
                    {
                        System.Diagnostics.Debugger.Break();
                    }
#endif
                    throw;
                }
            }
            else
            {
                // A problem if property is not IComparable - todo (may want to address this p2)
                if (bpt.GetInterface("IComparable") == null)
                {
                    throw new NotSupportedException("Property must support IComparable.");
                }

                _propIndexes[propName] = (ICEFDataIndex)(Activator.CreateInstance(typeof(ComparisonSortedIndex<,>).MakeGenericType(typeof(T), propType), this, propName) ?? throw new InvalidOperationException("Could not instantiate ComparisonSortedIndex."));
            }

            return true;
        }

        public enum RangeType
        {
            GreaterThan = 1,
            GreaterThanOrEqual = 2,
            LessThan = 3,
            LessThanOrEqual = 4
        }

        public IEnumerable<T> FindByRangeTyped(RangeType rt, IEnumerable<string?> fieldNames, IEnumerable<object?> values)
        {
            var fnl = fieldNames.ToArray();
            var vl = values.ToArray();

            if (fnl.Length != vl.Length || fnl.Length == 0)
            {
                throw new ArgumentException("Invalid query parameters.");
            }

            // Build intersection - first pass gives us first list to work from
            IEnumerable<T>? list = null;

            for (int i = 0; i < fnl.Length; ++i)
            {
                var v = vl[i];
                var fn = fnl[i] ?? "";

                if (!EnsureIndexed(AutoInferIndexes.GetValueOrDefault(Globals.AutoInferIndexes), false, fn, () =>
                {
                    Type? pt = null;

                    // Try to determine type from object - if unable, need to try and find from value
                    var pi = typeof(T).GetProperty(fn);

                    if (pi != null)
                    {
                        pt = pi.PropertyType;
                    }
                    else
                    {
                        // If the value is null, we need to be smarter about how to determine type (we should be able to index columns that do contain nulls!)
                        if (v == null)
                        {
                            // Look at data in set, if any
                            foreach (var r in this)
                            {
                                var rv = r.MustInfraWrap().GetValue(fn);

                                if (rv != null)
                                {
                                    pt = rv.GetType();
                                    break;
                                }
                            }
                        }
                        else
                        {
                            pt = v.GetType();
                        }
                    }

                    if (pt == null)
                    {
                        // If is still null, we have a problem - throw error to advise
                        throw new CEFInvalidDataException($"Could not determine data type for property '{fn}' on '{typeof(T).Name}'.");
                    }

                    return pt;
                }))
                {
                    return [];
                }

                using (CEF.UseServiceScope(_ss ?? CEF.NewOrCurrentServiceScope()))
                {
                    ICEFComparisonDataIndex ridx = (ICEFComparisonDataIndex)_propIndexes[fn];
                    IEnumerable<T>? l2 = null;

                    switch (rt)
                    {
                        case RangeType.GreaterThan:
                            l2 = ridx.GetGreaterThanItems(v).Cast<T>();
                            break;

                        case RangeType.GreaterThanOrEqual:
                            l2 = ridx.GetGreaterThanEqualItems(v).Cast<T>();
                            break;

                        case RangeType.LessThan:
                            l2 = ridx.GetLessThanItems(v).Cast<T>();
                            break;

                        case RangeType.LessThanOrEqual:
                            l2 = ridx.GetLessThanEqualItems(v).Cast<T>();
                            break;
                    }

                    // Materializing these due to read lock semantics
                    if (list == null)
                    {
                        list = l2;
                    }
                    else
                    {
                        if (l2 != null)
                        {
                            list = list.Intersect(l2);
                        }
                    }
                }
            }

            return list ?? [];
        }

        public IEnumerable<T> FindByEqualityTyped(IEnumerable<string> fieldNames, IEnumerable<object?> values)
        {
            var fnl = fieldNames.ToArray();
            var vl = values.ToArray();

            if (fnl.Length != vl.Length || fnl.Length == 0)
            {
                throw new ArgumentException("Invalid query parameters.");
            }

            // Build intersection - first pass gives us first list to work from
            IEnumerable<T>? list = null;

            for (int i = 0; i < fnl.Length; ++i)
            {
                var v = vl[i];
                var fn = fnl[i];

                if (!EnsureIndexed(AutoInferIndexes.GetValueOrDefault(Globals.AutoInferIndexes), true, fn, () =>
                {
                    Type? pt = null;

                    // Try to determine type from object - if unable, need to try and find from value
                    var pi = typeof(T).GetProperty(fn);

                    if (pi != null)
                    {
                        pt = pi.PropertyType;
                    }
                    else
                    {
                        // If the value is null, we need to be smarter about how to determine type (we should be able to index columns that do contain nulls!)
                        if (v == null)
                        {
                            // Look at data in set, if any
                            foreach (var r in this)
                            {
                                var rv = r.MustInfraWrap().GetValue(fn);

                                if (rv != null)
                                {
                                    pt = rv.GetType();
                                    break;
                                }
                            }
                        }
                        else
                        {
                            pt = v.GetType();
                        }
                    }

                    if (pt == null)
                    {
                        // If is still null, we have a problem - throw error to advise
                        throw new CEFInvalidDataException($"Could not determine data type for property '{fn}' on '{typeof(T).Name}'.");
                    }

                    return pt;
                }))
                {
                    return [];
                }

                using (CEF.UseServiceScope(_ss ?? CEF.NewOrCurrentServiceScope()))
                {
                    ICEFDataIndex idx = _propIndexes[fn];
                    var l2 = idx.GetEqualItems(v).Cast<T>();

                    // Materializing these due to read lock semantics
                    if (list == null)
                    {
                        list = l2;
                    }
                    else
                    {
                        list = list.Intersect(l2);
                    }
                }
            }

            return list ?? [];
        }

        IEnumerable<object> ICEFIndexed.FindByEquality(IEnumerable<string> fieldNames, IEnumerable<object?> values)
        {
            return FindByEqualityTyped(fieldNames, values);
        }

        internal IEnumerable<TResult> InternalJoin<TInner, TKey, TResult>(IndexedSnapshot<TInner> inner, Expression<Func<T, TKey>> outerKeySelector, Expression<Func<TInner, TKey>> innerKeySelector, Expression<Func<T, TInner, TResult>> resultSelector) where TInner : class, new()
        {
            // Can we perform better than the existing join operator? Likely depends on set sizes - at least one needs to be small, one large (can loop the small, seeking the large) AND the large key column should have an equality index present already
            // Theory is the existing join performance is I believe O(n+m) - if we optimize, can be as low as O(n') where n' = smaller of n and m (if one is large, then could be significant benefit)

            if (_source == null || inner == null)
            {
                yield break;
            }

            var tc = _source.Count();
            var ic = inner.Count();

            if ((tc <= (ic * Globals.IndexedSetJoinSmallerListFactor) && ic >= Globals.IndexedSetJoinMinimumLargeListSize))
            {
                var imi = inner.FindMemberInfo(innerKeySelector);

                if (!string.IsNullOrEmpty(imi))
                {
                    if (inner.EnsureIndexed(AutoInferIndexes.GetValueOrDefault(Globals.AutoInferIndexes), true, imi!, () => { return typeof(TKey); }))
                    {
                        var oks = outerKeySelector.Compile();
                        var rs = resultSelector.Compile();

                        foreach (var i in this)
                        {
                            var v = oks(i);

                            foreach (var im in inner.FindByEqualityTyped([ imi! ], [ v ]))
                            {
                                yield return rs(i, im);
                            }
                        }

                        yield break;
                    }
                }
            }
            else
            {
                if (((tc * Globals.IndexedSetJoinSmallerListFactor) >= ic && tc >= Globals.IndexedSetJoinMinimumLargeListSize))
                {
                    var omi = FindMemberInfo(outerKeySelector);

                    if (!string.IsNullOrEmpty(omi))
                    {
                        if (EnsureIndexed(AutoInferIndexes.GetValueOrDefault(Globals.AutoInferIndexes), true, omi!, () => { return typeof(TKey); }))
                        {
                            var iks = innerKeySelector.Compile();
                            var rs = resultSelector.Compile();

                            foreach (var i in inner)
                            {
                                var v = iks(i);

                                foreach (var om in FindByEqualityTyped([ omi! ], [ v ]))
                                {
                                    yield return rs(om, i);
                                }
                            }

                            yield break;
                        }
                    }
                }
            }

            foreach (var i in Enumerable.Join(this, inner, outerKeySelector.Compile(), innerKeySelector.Compile(), resultSelector.Compile()))
            {
                yield return i;
            }

            yield break;
        }

        /// <summary>
        /// We do it this way since filtering should be done within the context of our expression tree - we can't easily build a new tree a la IQueryable since that still implies per-row evaluation which is not efficient.
        /// We do however do a lot more than i4o, such as dynamic index creation, more flexible syntax support, greater/less/not equal support using indexes, choosing best perf paths, Linq operations such as join, eval subtrees that are not member-specific (avoid scanning), read lock semantics, etc.
        /// We also pay attention to the fact a complex expression could devolve into poorer performance than a simple scan - so use a threshold!
        /// </summary>
        /// <param name="predicate"></param>
        /// <returns></returns>
        internal IEnumerable<T> InternalWhere(Expression<Func<T, bool>> predicate)
        {
            if (predicate.Body is BinaryExpression bb)
            {
                switch (bb.NodeType)
                {
                    case ExpressionType.And:
                    case ExpressionType.AndAlso:
                        {
                            var leftResults = InternalWhere(Expression.Lambda<Func<T, bool>>(bb.Left, predicate.Parameters));

                            if (leftResults == null)
                            {
                                return [];
                            }

                            if (!leftResults.Any())
                            {
                                return leftResults;
                            }

                            var rightResults = InternalWhere(Expression.Lambda<Func<T, bool>>(bb.Right, predicate.Parameters));

                            if (rightResults == null)
                            {
                                return [];
                            }

                            if (!rightResults.Any())
                            {
                                return rightResults;
                            }

                            return leftResults.Intersect(rightResults);
                        }

                    case ExpressionType.Equal:
                        {
                            var list = ExpressionFindByEquality(predicate);

                            if (list != null)
                            {
                                return list;
                            }

                            break;
                        }

                    case ExpressionType.NotEqual:
                        {
                            var eqResults = ExpressionFindByEquality(predicate);

                            if (eqResults == null)
                            {
                                break;
                            }

                            return this.Except(eqResults);
                        }

                    case ExpressionType.GreaterThan:
                    case ExpressionType.GreaterThanOrEqual:
                    case ExpressionType.LessThan:
                    case ExpressionType.LessThanOrEqual:
                        {
                            var list = ExpressionFindByRange(predicate);

                            if (list != null)
                            {
                                return list;
                            }

                            break;
                        }

                    case ExpressionType.Or:
                    case ExpressionType.OrElse:
                        {
                            var leftResults = InternalWhere(Expression.Lambda<Func<T, bool>>(bb.Left, predicate.Parameters));
                            var rightResults = InternalWhere(Expression.Lambda<Func<T, bool>>(bb.Right, predicate.Parameters));

                            if (!leftResults.Any())
                            {
                                return rightResults;
                            }

                            if (!rightResults.Any())
                            {
                                return leftResults;
                            }

                            return leftResults.Union(rightResults);
                        }
                }
            }

            return Enumerable.Where(this, predicate.Compile());
        }

        private (object? val, bool ok) GetDynamic(Delegate d)
        {
            var mi = d.GetMethodInfo();

            if (mi != null && mi.ReturnType != null)
            {
                if (_dyncache.TryGetValue(mi.ReturnType, out var fn))
                {
                    return (fn(d), true);
                }

                MethodInfo internalHelper = typeof(IndexedSnapshot<T>).GetMethod("GetDynamicValue", BindingFlags.Static | BindingFlags.NonPublic) ?? throw new InvalidOperationException("Failed to find GetDynamicValue method.");
                MethodInfo constructedHelper = internalHelper.MakeGenericMethod(mi.ReturnType);
                var asCast = (Func<Delegate, object?>)(constructedHelper.Invoke(null, []) ?? throw new InvalidOperationException("Failed calling MakeGenericMethod."));

                _dyncache[mi.ReturnType] = asCast;
                return (asCast(d), true);
            }

            return (null, false);
        }

        private readonly static ConcurrentDictionary<Type, Func<Delegate, object?>> _dyncache = new(Globals.DefaultCollectionConcurrencyLevel, Globals.DefaultDictionaryCapacity);

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Code Quality", "IDE0051:Remove unused private members", Justification = "Dynamic call")]
        private static Func<Delegate, object?> GetDynamicValue<TRet>()
        {
            return (Delegate target) =>
                    ((Func<TRet>)target ?? throw new ArgumentException($"Target is null for type {typeof(TRet).Name}")).Invoke();
        }

        internal IEnumerable<T> ExpressionFindByEquality(Expression<Func<T, bool>> predicate)
        {
            if (predicate.Body is BinaryExpression be)
            {
                var foundMemberLeft = FindMemberInfo(be.Left);
                var foundMemberRight = FindMemberInfo(be.Right);

                // If found on both, we can't do anything, need to use standard (for now)
                if (foundMemberLeft != null ^ foundMemberRight != null)
                {
                    object? compTo;

                    // Evaluate the "other side" to get comparison value
                    if (foundMemberLeft != null)
                    {
                        var (val, ok) = GetDynamic(Expression.Lambda(be.Right).Compile());

                        if (!ok)
                        {
                            return Enumerable.Where(this, predicate.Compile());
                        }

                        compTo = val;
                    }
                    else
                    {
                        var (val, ok) = GetDynamic(Expression.Lambda(be.Left).Compile());

                        if (!ok)
                        {
                            return Enumerable.Where(this, predicate.Compile());
                        }

                        compTo = val;
                    }

                    return FindByEqualityTyped([ foundMemberLeft ?? foundMemberRight ?? throw new InvalidOperationException("Failed to find member name.") ], [ compTo ]);
                }

                // Special case: linq correlated subqueries - made more efficienct by using indexes for equality
                if (foundMemberLeft != null && foundMemberRight != null)
                {
                    if (FindExpressionByType(be.Left, ExpressionType.Parameter) != null && FindExpressionByType(be.Right, ExpressionType.Constant) != null)
                    {
                        var (val, ok) = GetDynamic(Expression.Lambda(be.Right).Compile());

                        if (ok)
                        {
                            return FindByEqualityTyped([ foundMemberLeft ], [ val ]);
                        }
                    }
                    else
                    {
                        if (FindExpressionByType(be.Right, ExpressionType.Parameter) != null && FindExpressionByType(be.Left, ExpressionType.Constant) != null)
                        {
                            var (val, ok) = GetDynamic(Expression.Lambda(be.Left).Compile());

                            if (ok)
                            {
                                return FindByEqualityTyped([ foundMemberRight ], [ val ]);
                            }
                        }
                    }
                }
            }

            return Enumerable.Where(this, predicate.Compile());
        }

        internal IEnumerable<T> ExpressionFindByRange(Expression<Func<T, bool>> predicate)
        {
            if (predicate.Body is BinaryExpression be)
            {
                var foundMemberLeft = FindMemberInfo(be.Left);
                var foundMemberRight = FindMemberInfo(be.Right);

                // If found on both, we can't do anything, need to use standard (for now)
                if (foundMemberLeft != null ^ foundMemberRight != null)
                {
                    object? compTo;

                    try
                    {
                        // Evaluate the "other side" to get comparison value
                        if (foundMemberLeft != null)
                        {
                            compTo = Expression.Lambda(be.Right).Compile().DynamicInvoke();
                        }
                        else
                        {
                            compTo = Expression.Lambda(be.Left).Compile().DynamicInvoke();
                        }
                    }
                    catch (Exception ex)
                    {
                        CEFDebug.WriteInfo("Internal error: " + ex.Message);
                        throw;
                    }

                    return FindByRangeTyped(be.NodeType == ExpressionType.GreaterThan
                        ? RangeType.GreaterThan : be.NodeType == ExpressionType.GreaterThanOrEqual
                        ? RangeType.GreaterThanOrEqual : be.NodeType == ExpressionType.LessThan
                        ? RangeType.LessThan : be.NodeType == ExpressionType.LessThanOrEqual ? RangeType.LessThanOrEqual : throw new ArgumentException("Invalid range type.")
                        , [ foundMemberLeft ?? foundMemberRight ], [ compTo ]);
                }
            }

            return Enumerable.Where(this, predicate.Compile());
        }

        internal static Expression? FindExpressionByType(Expression src, ExpressionType tofind, bool foundtype = false)
        {
            if (src.NodeType == tofind && foundtype)
            {
                return src;
            }

            if (src is MemberExpression me)
            {
                // Must find "our" type at least somewhere in chain
                if (me.Member.DeclaringType == typeof(T))
                {
                    foundtype = true;
                }

                return FindExpressionByType(me.Expression ?? throw new InvalidOperationException("Failed to get Expression."), tofind, foundtype);
            }

            return null;
        }

        internal string? FindMemberInfo(Expression t)
        {
            if (t is MemberExpression me)
            {
                if (me.Member.DeclaringType == typeof(T))
                {
                    return me.Member.Name;
                }
            }

            if (t is MethodCallExpression ce)
            {
                if (ce.Method.DeclaringType == typeof(PublicExtensions) && ce.Arguments.Count == 2 && ((ce.Method.Name == "PropertyValue") || (ce.Method.Name == "PropertyNullValue")))
                {
                    return (Expression.Lambda(ce.Arguments[1]).Compile().DynamicInvoke() ?? throw new InvalidOperationException("Failed to get a value from DynamicInvoke.")).ToString();
                }
            }

            var ue = t as UnaryExpression;

            if (ue == null)
            {
                if (t is LambdaExpression le)
                {
                    ue = le.Body as UnaryExpression;

                    if (ue == null)
                    {
                        me = (le.Body as MemberExpression)!;

                        if (me != null)
                        {
                            if (me.Member.DeclaringType == typeof(T))
                            {
                                return me.Member.Name;
                            }
                        }
                    }
                }
            }

            if (ue != null)
            {
                if (ue.NodeType == ExpressionType.Convert)
                {
                    if (ue.Operand is MemberExpression me2)
                    {
                        if (me2.Member.DeclaringType == typeof(T))
                        {
                            return me2.Member.Name;
                        }
                    }
                }
            }

            return null;
        }

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    _source = null;
                }

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
