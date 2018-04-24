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
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using CodexMicroORM.Core.Helper;

namespace CodexMicroORM.Core.Services
{
    public class DBService : ICEFDataHost
    {
        private static IDBProvider _defaultProvider;

        // Can declare what DB schemas any object belongs to (if not expressed in GetSchemaName())
        private static ConcurrentDictionary<Type, string> _schemaTypeMap = new ConcurrentDictionary<Type, string>(Globals.DefaultCollectionConcurrencyLevel, Globals.DefaultDictionaryCapacity);

        // Providers can be set, by type
        private static ConcurrentDictionary<Type, IDBProvider> _providerTypeMap = new ConcurrentDictionary<Type, IDBProvider>(Globals.DefaultCollectionConcurrencyLevel, Globals.DefaultDictionaryCapacity);

        // Field names can differ from OM and storage
        private static ConcurrentDictionary<Type, ConcurrentDictionary<string, string>> _typeFieldNameMap = new ConcurrentDictionary<Type, ConcurrentDictionary<string, string>>(Globals.DefaultCollectionConcurrencyLevel, Globals.DefaultDictionaryCapacity);

        // Entity names can differ from OM and storage
        private static ConcurrentDictionary<Type, string> _typeEntityNameMap = new ConcurrentDictionary<Type, string>(Globals.DefaultCollectionConcurrencyLevel, Globals.DefaultDictionaryCapacity);

        // Fields can have defaults (can match simple SQL DEFAULTs, for example)
        private static ConcurrentDictionary<Type, List<(string prop, object value, object def)>> _typePropDefaults = new ConcurrentDictionary<Type, List<(string prop, object value, object def)>>(Globals.DefaultCollectionConcurrencyLevel, Globals.DefaultDictionaryCapacity);

        // Property values can be "copied" from contained objects for DB persistence
        private static ConcurrentDictionary<Type, List<(string prop, Type proptype, string prefix)>> _typePropGroups = new ConcurrentDictionary<Type, List<(string prop, Type proptype, string prefix)>>(Globals.DefaultCollectionConcurrencyLevel, Globals.DefaultDictionaryCapacity);

        // Properties on an object can be saved to a parent 1:0/1 in the DB
        private static ConcurrentDictionary<Type, (string schema, string name)> _typeParentSave = new ConcurrentDictionary<Type, (string schema, string name)>(Globals.DefaultCollectionConcurrencyLevel, Globals.DefaultDictionaryCapacity);

        public DBService()
        {
        }

        public DBService(IDBProvider defaultProvider)
        {
            _defaultProvider = defaultProvider;
        }

        public IDBProvider GetProviderForType(Type bt)
        {
            if (_providerTypeMap.TryGetValue(bt, out IDBProvider prov))
            {
                return prov;
            }

            return _defaultProvider;
        }

        public static IDBProvider DefaultProvider => _defaultProvider;

        public static void RegisterSchema<T>(string schema)
        {
            CEF.RegisterForType<T>(new DBService());
            _schemaTypeMap[typeof(T)] = schema;
        }

        public string GetEntityNameByType(Type bt, ICEFWrapper w)
        {
            string name;

            if (w != null && _typeEntityNameMap.TryGetValue(w.GetBaseType(), out name))
            {
                return name;
            }

            if (w != null)
            {
                return w.GetBaseType().Name;
            }

            if (_typeEntityNameMap.TryGetValue(bt, out name))
            {
                return name;
            }

            return bt?.Name;
        }

        public string GetSchemaNameByType(Type bt)
        {
            if (_schemaTypeMap.TryGetValue(bt, out string sn))
            {
                return sn;
            }

            return null;
        }

        public static void RegisterOnSaveParentSave<T>(string name, string schema = null) where T : class
        {
            CEF.RegisterForType<T>(new DBService());
            _typeParentSave[typeof(T)] = (schema, name);
        }

        public static void RegisterPropertyGroup<T, V>(string propName, string prefix = "") where T : class where V : class
        {
            CEF.RegisterForType<T>(new DBService());

            List<(string prop, Type proptype, string prefix)> vl = null;

            _typePropGroups.TryGetValue(typeof(T), out vl);

            if (vl == null)
            {
                vl = new List<(string prop, Type proptype, string prefix)>();
            }

            vl.Add((propName, typeof(V), prefix ?? propName));
            _typePropGroups[typeof(T)] = vl;
        }

        public static void RegisterDefault<T, V>(string propName, V defaultValue) where T : class
        {
            CEF.RegisterForType<T>(new DBService());

            List<(string prop, object value, object def)> vl = null;

            _typePropDefaults.TryGetValue(typeof(T), out vl);

            if (vl == null)
            {
                vl = new List<(string prop, object value, object def)>();
            }

            vl.Add((propName, defaultValue, default(V)));
            _typePropDefaults[typeof(T)] = vl;
        }

        public static void RegisterStorageEntityName<T>(string entityName) where T : class
        {
            CEF.RegisterForType<T>(new DBService());
            _typeEntityNameMap[typeof(T)] = entityName;
        }

        public static void RegisterStorageFieldName<T>(string propName, string storeName) where T : class
        {
            CEF.RegisterForType<T>(new DBService());

            ConcurrentDictionary<string, string> vl = null;

            _typeFieldNameMap.TryGetValue(typeof(T), out vl);

            if (vl == null)
            {
                vl = new ConcurrentDictionary<string, string>(Globals.DefaultCollectionConcurrencyLevel, Globals.DefaultDictionaryCapacity, Globals.CurrentStringComparer);
            }

            vl[storeName] = propName;
            _typeFieldNameMap[typeof(T)] = vl;
        }

        public string GetPropertyNameFromStorageName(Type baseType, string storeName)
        {
            if (baseType != null)
            {
                if (_typeFieldNameMap.TryGetValue(baseType, out ConcurrentDictionary<string, string> vl))
                {
                    if (vl.TryGetValue(storeName, out string propName))
                    {
                        return propName;
                    }
                }
            }

            return storeName;
        }

        public void FixupPropertyStorageNames(object o)
        {
            var bt = o.GetBaseType();

            if (_typeFieldNameMap.TryGetValue(bt, out ConcurrentDictionary<string, string> vl))
            {
                var iw = o.AsInfraWrapped();
                var set = false;

                foreach (var v in (from a in vl where iw.HasProperty(a.Key) select a))
                {
                    iw.SetValue(v.Value, iw.GetValue(v.Key));
                    set = true;
                }

                if (set)
                {
                    iw.AcceptChanges();
                }
            }
        }

        public void ExpandPropertyGroupValues(object o)
        {
            var bt = o.GetBaseType();

            if (_typePropGroups.TryGetValue(bt, out List<(string prop, Type proptype, string prefix)> vl))
            {
                var iw = o.AsInfraWrapped();
                var set = false;

                foreach (var v in vl)
                {
                    var pv = o.FastGetValue(v.prop);

                    if (pv == null)
                    {
                        pv = v.proptype.FastCreateNoParm();
                        o.FastSetValue(v.prop, pv);
                        set = true;
                    }

                    foreach (var pi in v.proptype.GetProperties())
                    {
                        var fn = string.Concat(v.prefix, pi.Name);

                        if (iw.HasProperty(fn))
                        {
                            pv.FastSetValue(pi.Name, iw.GetValue(fn));
                            set = true;
                        }
                    }
                }

                if (set)
                {
                    iw.AcceptChanges();
                }
            }
        }

        public void InitializeObjectsWithoutCorrespondingIDs(object o)
        {
            var bt = o.GetBaseType();
            var ks = CEF.CurrentKeyService(o);

            if (ks != null)
            {
                var iw = o.AsInfraWrapped();
                var set = false;

                foreach (var k in ks.GetRelationsForChild(bt))
                {
                    // If we are missing the ID values (on CLR) but we do have a parent property...
                    if (!string.IsNullOrEmpty(k.ParentPropertyName) && (from a in k.ChildRoleName ?? k.ParentKey where !o.FastPropertyReadable(a) && iw.HasProperty(a) select a).Any())
                    {
                        if (iw.GetValue(k.ParentPropertyName) == null)
                        {
                            // Do a retrieve by key on the parent type
                            var parentSet = RetrieveByKeyNonGeneric(k.ParentType, (from a in k.ChildRoleName ?? k.ParentKey select iw.GetValue(a)).ToArray()).GetEnumerator();

                            if (parentSet.MoveNext())
                            {
                                o.FastSetValue(k.ParentPropertyName, parentSet.Current);
                                set = true;
                            }
                        }
                    }
                }

                if (set)
                {
                    iw.AcceptChanges();
                }
            }
        }

        public void CopyPropertyGroupValues(object o)
        {
            if (_typePropGroups.TryGetValue(o.GetBaseType(), out List<(string prop, Type proptype, string prefix)> vl))
            {
                var iw = o.AsInfraWrapped();

                if (iw != null)
                {
                    foreach (var v in vl)
                    {
                        var val = iw.GetValue(v.prop);

                        foreach (var pi in (from a in v.proptype.GetProperties() where a.CanRead select a))
                        {
                            var targName = string.Concat(v.prefix, pi.Name);

                            if (val != null)
                            {
                                iw.SetValue(targName, val.FastGetValue(pi.Name));
                            }
                            else
                            {
                                iw.SetValue(targName, null);
                            }
                        }
                    }
                }
            }
        }

        IList<Type> ICEFService.RequiredServices()
        {
            return new Type[] { typeof(ICEFPersistenceHost), typeof(ICEFKeyHost) };
        }

        public void WaitOnCompletions()
        {
            var sst = CEF.CurrentServiceScope.GetServiceState<DBServiceState>();

            if (sst != null)
            {
                while (sst.Completions.TryDequeue(out Task t))
                {
                    t.Wait();
                }

                lock (sst.Sync)
                {
                    if (sst.Exceptions.Count > 0)
                    {
                        var toThrow = sst.Exceptions.ToArray();
                        sst.Exceptions.Clear();
                        throw new AggregateException(toThrow);
                    }
                }
            }
        }

        public void AddCompletionException(Exception ex)
        {
            var sst = CEF.CurrentServiceScope.GetServiceState<DBServiceState>();

            if (sst != null)
            {
                lock (sst.Sync)
                {
                    sst.Exceptions.Add(ex);
                }
            }
        }

        public void ExecuteRaw(ConnectionScope cs, string cmdText, bool doThrow = true, bool stopOnError = true)
        {
            _defaultProvider.ExecuteRaw(cs, cmdText, doThrow, stopOnError);
        }

        public IList<(object item, string message, int status)> Save(IList<ICEFInfraWrapper> rows, ServiceScope ss, DBSaveSettings settings)
        {
            List<(object item, string message, int status)> results = new List<(object item, string message, int status)>();

            var cs = CEF.CurrentConnectionScope;
            var aud = CEF.CurrentAuditService();
            var ks = CEF.CurrentKeyService();

            // Ordering of rows supports foreign key dependencies: insert/update top-down, delete bottom-up.
            // Different order #'s require sequential processing, so we group by order # - within an order group/table, we should be able to issue parallel requests
            // We also offer a way to "preview" what will be saved and adjust if needed
            // By grouping by provider type, we support hybrid data sources, by type
            List<(ICEFInfraWrapper row, Type basetype, string schema, string name, int level, ObjectState rs, IDBProvider prov)> tempList = new List<(ICEFInfraWrapper row, Type basetype, string schema, string name, int level, ObjectState rs, IDBProvider prov)>();

            Parallel.ForEach(rows, (a) =>
            {
                using (CEF.UseServiceScope(ss))
                {
                    var uw = a.AsUnwrapped();
                    var bt = uw.GetBaseType();

                    if (uw != null && (settings.LimitToSingleType == null || settings.LimitToSingleType.Equals(bt)))
                    {
                        var sp = (settings.RowSavePreview == null ? (true, null) : settings.RowSavePreview.Invoke(a));
                        var rs = sp.treatas.GetValueOrDefault(a.GetRowState());

                        if ((rs != ObjectState.Unchanged && rs != ObjectState.Unlinked) && sp.cansave && (cs.ToAcceptList?.Count == 0 || !cs.ToAcceptList.Contains(a)))
                        {
                            var w = a.GetWrappedObject() as ICEFWrapper;
                            var prov = GetProviderForType(bt);
                            var level = settings.LimitToSingleType != null ? 1 : ks.GetObjectNestLevel(uw);
                            var schema = w?.GetSchemaName() ?? GetSchemaNameByType(bt);
                            var name = GetEntityNameByType(bt, w);
                            var row = (aud == null ? a : aud.SavePreview(ss, a, rs));

                            lock (tempList)
                            {
                                tempList.Add((row, bt, schema, name, level, rs, prov));
                            }
                        }
                    }
                }
            });

            var ordRows = from a in tempList
                          group a by new { Level = a.level, RowState = a.rs, Provider = a.prov }
                          into g
                          select new { g.Key.Provider, g.Key.Level, g.Key.RowState, Rows = (from asp in g select (asp.schema, asp.name, asp.basetype, asp.row)) };

            /*
            var ordRows = (from a in rows
                           let uw = a.AsUnwrapped()
                           let bt = uw?.GetBaseType()
                           where uw != null && (settings.LimitToSingleType == null || settings.LimitToSingleType.Equals(bt))
                           let sp = (settings.RowSavePreview == null ? (true, null) : settings.RowSavePreview.Invoke(a))
                           let rs = sp.treatas.GetValueOrDefault(a.GetRowState())
                           where (rs != ObjectState.Unchanged && rs != ObjectState.Unlinked) && sp.cansave && (cs.ToAcceptList?.Count == 0 || !cs.ToAcceptList.Contains(a))
                           let w = a.GetWrappedObject() as ICEFWrapper
                           let rd = new { Row = a, BaseType = bt, Schema = w?.GetSchemaName() ?? GetSchemaNameByType(bt), Name = GetEntityNameByType(bt, w) }
                           let prov = GetProviderForType(bt)
                           let level = settings.LimitToSingleType != null ? 1 : ks.GetObjectNestLevel(uw)
                           group rd by new
                           {
                               Level = level,
                               RowState = rs,
                               Provider = prov
                           }
                           into g
                           select new { g.Key.Provider, g.Key.Level, g.Key.RowState, Rows = (from asp in g select (asp.Schema, asp.Name, asp.BaseType, Row: (aud == null ? asp.Row : aud.SavePreview(ss, asp.Row, g.Key.RowState)))) });
            */

            if ((settings.AllowedOperations & DBSaveSettings.Operations.Delete) != 0)
            {
                foreach (var prov in (from a in ordRows select a.Provider).Distinct())
                {
                    results.AddRange(from a in prov.DeleteRows(cs, (from a in ordRows where a.Provider == prov && a.RowState == ObjectState.Deleted select (a.Level, a.Rows)), settings) select (a.row.GetWrappedObject(), a.msg, a.status));

                    // Perform for any parent rows, if applicable
                    var parentRows = (from a in ordRows
                                      where a.Provider == prov && a.RowState == ObjectState.Deleted
                                      from r in a.Rows
                                      where _typeParentSave.ContainsKey(r.basetype)
                                      group r by a.Level into g
                                      select (g.Key, (from b in g let pn = _typeParentSave[b.basetype] select (pn.schema ?? b.schema, pn.name, b.basetype, b.row))));

                    if (parentRows.Any())
                    {
                        try
                        {
                            settings.NoAcceptChanges = true;
                            prov.DeleteRows(cs, parentRows, settings);
                        }
                        finally
                        {
                            settings.NoAcceptChanges = false;
                        }
                    }
                }
            }

            if ((settings.AllowedOperations & DBSaveSettings.Operations.Insert) != 0)
            {
                foreach (var prov in (from a in ordRows select a.Provider).Distinct())
                {
                    // Perform for any parent rows, if applicable
                    var parentRows = (from a in ordRows
                                      where a.Provider == prov && a.RowState == ObjectState.Added
                                      from r in a.Rows
                                      where _typeParentSave.ContainsKey(r.basetype)
                                      group r by a.Level into g
                                      select (g.Key, (from b in g let pn = _typeParentSave[b.basetype] select (pn.schema ?? b.schema, pn.name, b.basetype, b.row))));

                    if (parentRows.Any())
                    {
                        try
                        {
                            settings.NoAcceptChanges = true;
                            prov.InsertRows(cs, parentRows, settings);
                        }
                        finally
                        {
                            settings.NoAcceptChanges = false;
                        }
                    }

                    results.AddRange(from a in prov.InsertRows(cs, (from a in ordRows where a.RowState == ObjectState.Added select (a.Level, a.Rows)), settings) select (a.row.GetWrappedObject(), a.msg, a.status));
                }
            }

            if ((settings.AllowedOperations & DBSaveSettings.Operations.Update) != 0)
            {
                foreach (var prov in (from a in ordRows select a.Provider).Distinct())
                {
                    // Perform for any parent rows, if applicable
                    var parentRows = (from a in ordRows
                                      where a.Provider == prov && a.RowState == ObjectState.Modified
                                      from r in a.Rows
                                      where _typeParentSave.ContainsKey(r.basetype)
                                      group r by a.Level into g
                                      select (g.Key, (from b in g let pn = _typeParentSave[b.basetype] select (pn.schema ?? b.schema, pn.name, b.basetype, b.row))));

                    if (parentRows.Any())
                    {
                        try
                        {
                            settings.NoAcceptChanges = true;
                            prov.UpdateRows(cs, parentRows, settings);
                        }
                        finally
                        {
                            settings.NoAcceptChanges = false;
                        }
                    }

                    results.AddRange(from a in prov.UpdateRows(cs, (from a in ordRows where a.RowState == ObjectState.Modified select (a.Level, a.Rows)), settings) select (a.row.GetWrappedObject(), a.msg, a.status));
                }
            }

            cs.DoneWork();

            // We need to do the cache update at the end since we could have assigned values, etc. during the above save step - cache the final values!
            ICEFCachingHost cache = CEF.CurrentCacheService();

            if (cache != null && results.Any())
            {
                var forCaching = (from a in results
                                  let bt = a.item.GetBaseType()
                                  let cb = ss.ResolvedCacheBehaviorForType(bt)
                                  where cb != 0
                                  let iw = a.item.AsInfraWrapped()
                                  where iw != null
                                  let rs = iw.GetRowState()
                                  group new { a, iw } by new { Type = bt, CacheMode = cb, RowState = rs } into g
                                  select (g.Key.Type, g.Key.CacheMode, g.Key.RowState, Rows: (from c in g select c.iw.GetAllValues(true, true)).ToList())).ToList();

                Action<object> act = (object state) =>
                {
                    try
                    {
                        cache.DoingWork();

                        using (CEF.UseServiceScope(ss))
                        {
                            var list = ((IEnumerable<(Type Type, CacheBehavior CacheMode, ObjectState RowState, List<IDictionary<string, object>> Rows)>)state);

                            // Update by identity entries with new values (or invalidate for deletions)
                            foreach (var ci in (from a in list where (a.CacheMode & CacheBehavior.IdentityBased) != 0 select a))
                            {
                                if (ci.RowState == ObjectState.Deleted || ci.RowState == ObjectState.Unlinked)
                                {
                                    foreach (var r in ci.Rows)
                                    {
                                        cache.InvalidateIdentityEntry(ci.Type, r);
                                    }
                                }
                                else
                                {
                                    foreach (var r in ci.Rows)
                                    {
                                        cache.UpdateByIdentity(ci.Type, r);
                                    }
                                }
                            }

                            // Invalidate all by query entries for any types contained in the save
                            foreach (var invType in (from a in list where (a.CacheMode & (CacheBehavior.QueryBased | CacheBehavior.OnlyForAllQuery)) != 0 select a.Type).Distinct())
                            {
                                cache.InvalidateForByQuery(invType, Globals.TypeSpecificCacheEvictionsOnUpdates);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        AddCompletionException(ex);
                        CEFDebug.WriteInfo($"Cache error on DBSave: {ex.Message}");
                    }
                    finally
                    {
                        cache.DoneWork();
                    }
                };

                if (settings.AsyncCacheUpdates.GetValueOrDefault(ss.Settings.AsyncCacheUpdates.GetValueOrDefault(Globals.AsyncCacheUpdates)))
                {
                    AddCompletionTask(Task.Factory.StartNew(act, forCaching));
                }
                else
                {
                    act.Invoke(forCaching);
                }
            }

            return results;
        }

        public void AddCompletionTask(Task t)
        {
            var sst = CEF.CurrentServiceScope.GetServiceState<DBServiceState>();

            if (sst != null)
            {
                if (Globals.MaximumCompletionItemsQueued > 0)
                {
                    while (sst.Completions.Count >= Globals.MaximumCompletionItemsQueued)
                    {
                        if (sst.Completions.TryDequeue(out Task t2))
                        {
                            t2.Wait();
                        }
                    }
                }

                sst.Completions.Enqueue(t);
            }
        }

        public T ExecuteScalar<T>(string cmdText)
        {
            var cs = CEF.CurrentConnectionScope;
            var res = _defaultProvider.ExecuteScalar<T>(cs, cmdText);
            cs.DoneWork();
            return res;
        }

        public void ExecuteRaw(string command, bool doThrow = true, bool stopOnError = true)
        {
            var cs = CEF.CurrentConnectionScope;
            _defaultProvider.ExecuteRaw(cs, command, doThrow, stopOnError);
            cs.DoneWork();
        }

        private ConcurrentDictionary<Type, Func<object[], IEnumerable>> _retrieveByKeyCache = new ConcurrentDictionary<Type, Func<object[], IEnumerable>>(Globals.DefaultCollectionConcurrencyLevel, Globals.DefaultDictionaryCapacity);

        internal IEnumerable RetrieveByKeyNonGeneric(Type bt, params object[] key)
        {
            if (_retrieveByKeyCache.TryGetValue(bt, out Func<object[], IEnumerable> vl))
            {
                return vl.Invoke(key);
            }

            var mi = this.GetType().GetMethod("InternalRetrieveByKey", System.Reflection.BindingFlags.InvokeMethod | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            mi = mi.MakeGenericMethod(bt);
            var funcWrap = (Func<object[], IEnumerable>) mi.Invoke(this, new object[] { });
            _retrieveByKeyCache[bt] = funcWrap;
            return funcWrap(key);
        }

        private Func<object[], IEnumerable> InternalRetrieveByKey<T>() where T : class, new()
        {
            return new Func<object[], IEnumerable>((k) => { return RetrieveByKey<T>(k); });
        }

        public IEnumerable<T> RetrieveByKey<T>(params object[] key) where T : class, new()
        {
            var ss = CEF.CurrentServiceScope;
            var sst = ss.GetServiceState<DBServiceState>();

            // Special situation we look for: async saving can result in unsaved objects, where we're better off to pull from that which might still be in memory
            if (sst?.Completions.Count > 0 && ((ss.Settings.MergeBehavior & MergeBehavior.CheckScopeIfPending) != 0))
            {
                var kss = ss.GetServiceState<KeyService.KeyServiceState>();

                if (kss != null)
                {
                    var eto = kss.GetTrackedByPKValue(ss, typeof(T), key);

                    if (eto != null && eto.IsAlive)
                    {
                        return new T[] { eto.GetWrapperTarget() as T };
                    }
                }
            }

            ICEFCachingHost cache = null;

            if ((ss.Settings.CacheBehavior & CacheBehavior.IdentityBased) != 0)
            {
                // If caching is present, see if this identity is already in cache and return it if so
                cache = CEF.CurrentCacheService();
                var cached = cache?.GetByIdentity<T>(key);

                if (cached != null)
                {
                    return new T[] { cached };
                }
            }

            var cs = CEF.CurrentConnectionScope;
            var res = GetProviderForType(typeof(T)).RetrieveByKey<T>(this, cs, true, key);
            cs.DoneWork();

            // If caching is present, call out to it to potentially cache results (by identity)
            if (cache != null)
            {
                res = res.ToArray();

                var fr = res.FirstOrDefault();

                if (fr != null)
                {
                    cache.AddByIdentity(fr, key);
                }
            }

            return res;
        }

        public IEnumerable<T> RetrieveByQuery<T>(CommandType cmdType, string cmdText, params object[] parms) where T : class, new()
        {
            ICEFCachingHost cache = null;
            var cb = CEF.CurrentServiceScope.Settings.CacheBehavior;

            if ((cb & CacheBehavior.QueryBased) != 0 && (cb & CacheBehavior.OnlyForAllQuery) == 0)
            {
                // If caching is present, see if this identity is already in cache and return it if so
                cache = CEF.CurrentCacheService();
                var cached = cache?.GetByQuery<T>(cmdText, parms);

                if (cached != null)
                {
                    return cached;
                }
            }

            var cs = CEF.CurrentConnectionScope;
            var res = GetProviderForType(typeof(T)).RetrieveByQuery<T>(this, cs, true, cmdType, cmdText, parms);
            cs.DoneWork();

            if (cache == null && (cb & CacheBehavior.ConvertQueryToIdentity) != 0)
            {
                cache = CEF.CurrentCacheService();
            }

            if (cache != null)
            {
                res = res.ToArray();

                if (res.Any())
                {
                    cache.AddByQuery(res, cmdText, parms);
                }
            }

            return res;
        }

        public IEnumerable<T> RetrieveAll<T>() where T : class, new()
        {
            ICEFCachingHost cache = null;

            if ((CEF.CurrentServiceScope.Settings.CacheBehavior & CacheBehavior.QueryBased) != 0)
            {
                // If caching is present, see if this identity is already in cache and return it if so
                cache = CEF.CurrentCacheService();
                var cached = cache?.GetByQuery<T>("ALL", null);

                if (cached != null)
                {
                    return cached;
                }
            }

            var cs = CEF.CurrentConnectionScope;
            var res = GetProviderForType(typeof(T)).RetrieveAll<T>(this, cs, true);
            cs.DoneWork();

            if (cache != null)
            {
                res = res.ToArray();

                if (res.Any())
                {
                    cache.AddByQuery(res, "ALL", null);
                }
            }

            return res;
        }

        Type ICEFService.IdentifyStateType(object o, ServiceScope ss, bool isNew)
        {
            return typeof(DBServiceState);
        }

        WrappingSupport ICEFService.IdentifyInfraNeeds(object o, object replaced, ServiceScope ss, bool isNew)
        {
            if ((replaced ?? o) is INotifyPropertyChanged)
            {
                return WrappingSupport.OriginalValues | WrappingSupport.PropertyBag;
            }
            else
            {
                return WrappingSupport.Notifications | WrappingSupport.OriginalValues | WrappingSupport.PropertyBag;
            }
        }

        void ICEFService.FinishSetup(ServiceScope.TrackedObject to, ServiceScope ss, bool isNew, IDictionary<string, object> props, ICEFServiceObjState state)
        {
            if (isNew)
            {
                if (_typePropDefaults.TryGetValue(to.BaseType, out List<(string prop, object value, object def)> defbytype))
                {
                    var t = to.GetInfraWrapperTarget();

                    foreach (var d in defbytype)
                    {
                        var curval = ss.GetGetter(t, d.prop).getter?.Invoke();

                        if (curval.IsSame(d.def))
                        {
                            ss.GetSetter(t, d.prop).setter?.Invoke(d.value);
                        }
                    }
                }
            }
        }

        public virtual void Disposing(ServiceScope ss)
        {
            WaitOnCompletions();
        }

        #region IDisposable Support
        private bool _disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    WaitOnCompletions();
                }

                _disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
        #endregion

        public class DBServiceState : ICEFServiceObjState
        {
            private ConcurrentQueue<Task> _completions = new ConcurrentQueue<Task>();
            private List<Exception> _completionEx = new List<Exception>();
            private object _sync = new object();

            public ConcurrentQueue<Task> Completions => _completions;
            public List<Exception> Exceptions => _completionEx;
            public object Sync => _sync;
        }
    }
}
