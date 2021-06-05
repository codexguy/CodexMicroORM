/***********************************************************************
Copyright 2021 CodeX Enterprises LLC

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
#nullable enable
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using CodexMicroORM.Core.Helper;
using CodexMicroORM.Providers;

namespace CodexMicroORM.Core.Services
{
    public class DBService : ICEFDataHost
    {
        const int MAX_WAIT_TIME_MS = 40000;

        #region "Static state"

        private const int PARALLEL_THRESHOLD_FOR_PARSE = 100;

        // Can declare what DB schemas any object belongs to (if not expressed in GetSchemaName())
        private static readonly ConcurrentDictionary<Type, string> _schemaTypeMap = new(Globals.DefaultCollectionConcurrencyLevel, Globals.DefaultDictionaryCapacity);

        // Providers can be set, by type
        private static readonly ConcurrentDictionary<Type, IDBProvider> _providerTypeMap = new(Globals.DefaultCollectionConcurrencyLevel, Globals.DefaultDictionaryCapacity);

        // Field names can differ from OM and storage
        private static readonly ConcurrentDictionary<Type, ConcurrentDictionary<string, string>> _typeFieldNameMap = new(Globals.DefaultCollectionConcurrencyLevel, Globals.DefaultDictionaryCapacity);

        // Entity names can differ from OM and storage
        private static readonly ConcurrentDictionary<Type, string> _typeEntityNameMap = new(Globals.DefaultCollectionConcurrencyLevel, Globals.DefaultDictionaryCapacity);

        // Fields can have defaults (can match simple SQL DEFAULTs, for example)
        private static readonly ConcurrentDictionary<Type, List<(string prop, object? value, object? def, Type proptype)>> _typePropDefaults = new(Globals.DefaultCollectionConcurrencyLevel, Globals.DefaultDictionaryCapacity);

        // Property values can be "copied" from contained objects for DB persistence
        private static readonly ConcurrentDictionary<Type, List<(string prop, Type proptype, string prefix)>> _typePropGroups = new(Globals.DefaultCollectionConcurrencyLevel, Globals.DefaultDictionaryCapacity);

        // Properties on an object can be saved to a parent 1:0/1 in the DB
        private static readonly ConcurrentDictionary<Type, (string? schema, string name)> _typeParentSave = new(Globals.DefaultCollectionConcurrencyLevel, Globals.DefaultDictionaryCapacity);

        #endregion

        #region "Private state"

        private readonly ConcurrentDictionary<Type, Func<object[], IEnumerable>> _retrieveByKeyCache = new(Globals.DefaultCollectionConcurrencyLevel, Globals.DefaultDictionaryCapacity);

        #endregion

        #region "Constructors"

        public DBService()
        {
        }

        public static DBService Create(IDBProvider defaultProvider)
        {
            DefaultProvider = defaultProvider;
            return new DBService();
        }

        #endregion

        #region "Static methods"

        public static IDBProvider? DefaultProvider
        {
            get;
            set;
        }

        public static void RegisterSchema<T>(string schema)
        {
            CEF.RegisterForType<T>(new DBService());
            _schemaTypeMap[typeof(T)] = schema;
        }

        public static void RegisterOnSaveParentSave<T>(string name, string? schema = null) where T : class
        {
            CEF.RegisterForType<T>(new DBService());
            _typeParentSave[typeof(T)] = (schema, name);
        }

        public static void RegisterPropertyGroup<T, V>(string propName, string prefix = "") where T : class where V : class
        {
            CEF.RegisterForType<T>(new DBService());

            _typePropGroups.TryGetValue(typeof(T), out List<(string prop, Type proptype, string prefix)> vl);

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

            _typePropDefaults.TryGetValue(typeof(T), out var vl);

            if (vl == null)
            {
                vl = new List<(string prop, object? value, object? def, Type proptype)>();
            }

            vl.Add((propName, defaultValue, default(V), typeof(V)));
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

            _typeFieldNameMap.TryGetValue(typeof(T), out ConcurrentDictionary<string, string> vl);

            if (vl == null)
            {
                vl = new ConcurrentDictionary<string, string>(Globals.DefaultCollectionConcurrencyLevel, Globals.DefaultDictionaryCapacity, Globals.CurrentStringComparer);
            }

            vl[storeName] = propName;
            _typeFieldNameMap[typeof(T)] = vl;
        }

        #endregion

        public static IDBProvider GetProviderForType(Type bt)
        {
            if (_providerTypeMap.TryGetValue(bt, out IDBProvider prov))
            {
                return prov;
            }

            if (DefaultProvider == null)
            {
                throw new CEFInvalidStateException(InvalidStateType.MissingService, $"Could not find a default data provider based on type {bt.Name}.");
            }

            return DefaultProvider;
        }

        public string GetEntityNameByType(Type bt, ICEFWrapper? w)
        {
            if (w != null && _typeEntityNameMap.TryGetValue(w.GetBaseType(), out string name))
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

            return bt.Name;
        }

        public string? GetSchemaNameByType(Type bt)
        {
            if (_schemaTypeMap.TryGetValue(bt, out string sn))
            {
                return sn;
            }

            return null;
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

                if (iw != null)
                {
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
        }

        public IEnumerable<string> GetPropertyGroupFields(Type t)
        {
            if (_typePropGroups.TryGetValue(t, out List<(string prop, Type proptype, string prefix)> vl))
            {
                foreach (var (prop, proptype, prefix) in vl)
                {
                    foreach (var pi in proptype.GetProperties())
                    {
                        var fn = string.Concat(prefix, pi.Name);
                        yield return fn;
                    }
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

                foreach (var (prop, proptype, prefix) in vl)
                {
                    var pv = o.FastGetValue(prop);

                    if (pv == null)
                    {
                        pv = proptype.FastCreateNoParm();
                        o.FastSetValue(prop, pv);
                        set = true;
                    }

                    foreach (var pi in proptype.GetProperties())
                    {
                        var fn = string.Concat(prefix, pi.Name);

                        if (pv != null && iw != null && iw.HasProperty(fn))
                        {
                            pv.FastSetValue(pi.Name, iw.GetValue(fn));
                            set = true;
                        }
                    }
                }

                if (set)
                {
                    iw?.AcceptChanges();
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

                if (iw != null)
                {
                    var set = false;

                    foreach (var k in ks.GetRelationsForChild(bt))
                    {
                        // If we are missing the ID values (on CLR) but we do have a parent property...
                        if (!string.IsNullOrEmpty(k.ParentPropertyName) && (from a in k.ChildRoleName ?? k.ParentKey where !o.FastPropertyReadable(a) && iw.HasProperty(a) select a).Any())
                        {
                            if (iw.GetValue(k.ParentPropertyName) == null && k.ParentType != null)
                            {
                                // Do a retrieve by key on the parent type
                                var parentSet = RetrieveByKeyNonGeneric(k.ParentType, (from a in k.ChildRoleName ?? k.ParentKey select iw.GetValue(a)).ToList()).GetEnumerator();

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
        }

        public void CopyPropertyGroupValues(object? o)
        {
            if (o == null)
            {
                return;
            }

            if (_typePropGroups.TryGetValue(o.GetBaseType(), out List<(string prop, Type proptype, string prefix)> vl))
            {
                var iw = o.AsInfraWrapped();

                if (iw != null)
                {
                    foreach (var (prop, proptype, prefix) in vl)
                    {
                        var val = iw.GetValue(prop);

                        foreach (var pi in (from a in proptype.GetProperties() where a.CanRead select a))
                        {
                            var targName = string.Concat(prefix, pi.Name);

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

        IList<Type> ICEFService.RequiredServices() => new Type[] { typeof(ICEFPersistenceHost), typeof(ICEFKeyHost) };

        public void WaitOnCompletions()
        {
            var sst = CEF.CurrentServiceScope.GetServiceState<DBServiceState>();

            if (sst != null)
            {
                while (sst.Completions.TryDequeue(out Task t))
                {
                    if (!t.Wait(MAX_WAIT_TIME_MS))
                    {
                        throw new TimeoutException($"Waited {MAX_WAIT_TIME_MS} ms for data operation - failed to complete.");
                    }
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

        public static void ExecuteRaw(ConnectionScope cs, string cmdText, bool doThrow = true, bool stopOnError = true)
        {
            if (DefaultProvider == null)
            {
                throw new CEFInvalidStateException(InvalidStateType.MissingInit, "Data provider not set.");
            }

            DefaultProvider.ExecuteRaw(cs, cmdText, doThrow, stopOnError);
        }

        /// <summary>
        /// Primary entry point for saving one or more "rows" of data, sourced from entities.
        /// </summary>
        /// <param name="rows"></param>
        /// <param name="ss"></param>
        /// <param name="settings"></param>
        /// <returns></returns>
        public IList<(object item, string? message, int status)> Save(IList<ICEFInfraWrapper> rows, ServiceScope ss, DBSaveSettings settings)
        {
            List<(object item, string? message, int status)> results = new();

            string? schemaOverride = null;
            string? nameOverride = null;

            if (!string.IsNullOrEmpty(settings.EntityPersistName))
            {
                var (schema, name) = MSSQLCommand.SplitIntoSchemaAndName(settings.EntityPersistName);
                schemaOverride = schema;
                nameOverride = name;
            }

            var cs = CEF.CurrentConnectionScope;
            var aud = CEF.CurrentAuditService();
            var ks = CEF.CurrentKeyService();

            // Ordering of rows supports foreign key dependencies: insert/update top-down, delete bottom-up.
            // Different order #'s require sequential processing, so we group by order # - within an order group/table, we should be able to issue parallel requests
            // We also offer a way to "preview" what will be saved and adjust if needed
            // By grouping by provider type, we support hybrid data sources, by type
            Dictionary<IDBProvider, Dictionary<int, Dictionary<ObjectState, List<(string? schema, string name, Type basetype, ICEFInfraWrapper row)>>>> grouped = new();

            var loader = new Action<ICEFInfraWrapper>((a) =>
            {
                using (CEF.UseServiceScope(ss))
                {
                    var uw = a.AsUnwrapped();
                    var bt = uw?.GetBaseType();

                    if (bt != null && uw != null && (settings.LimitToSingleType == null || settings.LimitToSingleType.Equals(bt)))
                    {
                        var (cansave, treatas) = (settings.RowSavePreview == null ? (true, null) : settings.RowSavePreview.Invoke(a));
                        var rs = treatas.GetValueOrDefault(a.GetRowState());

                        if ((rs != ObjectState.Unchanged && rs != ObjectState.Unlinked) && cansave && ((cs.ToAcceptList?.Count).GetValueOrDefault() == 0 || !cs.ToAcceptList!.Contains(a)))
                        {
                            var w = a.GetWrappedObject() as ICEFWrapper;
                            var prov = GetProviderForType(bt);
                            var level = settings.LimitToSingleType != null ? 1 : ks.GetObjectNestLevel(uw);
                            var schema = (settings.EntityPersistType == bt ? schemaOverride : null) ?? w?.GetSchemaName() ?? GetSchemaNameByType(bt);
                            var name = (settings.EntityPersistType == bt ? nameOverride : null) ?? GetEntityNameByType(bt, w);
                            var row = (aud == null ? a : aud.SavePreview(ss, a, rs, settings));

                            lock (grouped)
                            {
                                if (!grouped.TryGetValue(prov, out var cprov))
                                {
                                    cprov = new Dictionary<int, Dictionary<ObjectState, List<(string? schema, string name, Type basetype, ICEFInfraWrapper row)>>>();
                                    grouped[prov] = cprov;
                                }

                                if (!cprov.TryGetValue(level, out var clevel))
                                {
                                    clevel = new Dictionary<ObjectState, List<(string? schema, string name, Type basetype, ICEFInfraWrapper row)>>();
                                    cprov[level] = clevel;
                                }

                                if (!clevel.TryGetValue(rs, out var crs))
                                {
                                    crs = new List<(string? schema, string name, Type basetype, ICEFInfraWrapper row)>();
                                    clevel[rs] = crs;
                                }

                                crs.Add((schema, name, bt, row));
                            }
                        }
                    }
                }
            });

            if (rows.Count > PARALLEL_THRESHOLD_FOR_PARSE)
            {
                Parallel.ForEach(rows, new ParallelOptions() { MaxDegreeOfParallelism = settings.MaxDegreeOfParallelism }, loader);
            }
            else
            {
                foreach (var a in rows)
                {
                    loader(a);
                }
            }

            if ((settings.AllowedOperations & DBSaveSettings.Operations.Update) != 0)
            {
                foreach (var provkvp in grouped)
                {
                    foreach (var levelkvp in (from a in provkvp.Value orderby a.Key select a))
                    {
                        if (levelkvp.Value.TryGetValue(ObjectState.ModifiedPriority, out var filteredrows))
                        {
                            var parentRows = (from a in filteredrows
                                              where _typeParentSave.ContainsKey(a.basetype)
                                              let pn = _typeParentSave[a.basetype]
                                              select (pn.schema ?? a.schema, pn.name, a.basetype, a.row));

                            if (parentRows.Any())
                            {
                                try
                                {
                                    settings.NoAcceptChanges = true;
                                    provkvp.Key.UpdateRows(cs, parentRows, settings);
                                }
                                finally
                                {
                                    settings.NoAcceptChanges = false;
                                }
                            }

                            results.AddRange(from a in provkvp.Key.UpdateRows(cs, filteredrows, settings) select (a.row.GetWrappedObject(), a.msg, a.status));
                        }
                    }
                }
            }

            if ((settings.AllowedOperations & DBSaveSettings.Operations.Delete) != 0)
            {
                foreach (var provkvp in grouped)
                {
                    foreach (var levelkvp in (from a in provkvp.Value orderby a.Key descending select a))
                    {
                        if (levelkvp.Value.TryGetValue(ObjectState.Deleted, out var filteredrows))
                        {
                            results.AddRange(from a in provkvp.Key.DeleteRows(cs, filteredrows, settings) select (a.row.GetWrappedObject(), a.msg, a.status));

                            var parentRows = (from a in filteredrows
                                              where _typeParentSave.ContainsKey(a.basetype)
                                              let pn = _typeParentSave[a.basetype]
                                              select (pn.schema ?? a.schema, pn.name, a.basetype, a.row));

                            if (parentRows.Any())
                            {
                                try
                                {
                                    settings.NoAcceptChanges = true;
                                    provkvp.Key.DeleteRows(cs, parentRows, settings);
                                }
                                finally
                                {
                                    settings.NoAcceptChanges = false;
                                }
                            }
                        }
                    }
                }
            }

            // Walk down, level by level; perform updates then inserts at the same level - versus separate passes where updates might be needed to link properly as go deeper
            foreach (var provkvp in grouped)
            {
                foreach (var levelkvp in (from a in provkvp.Value orderby a.Key select a))
                {
                    if ((settings.AllowedOperations & DBSaveSettings.Operations.Update) != 0)
                    {
                        if (levelkvp.Value.TryGetValue(ObjectState.Modified, out var filteredrows))
                        {
                            var parentRows = (from a in filteredrows
                                              where _typeParentSave.ContainsKey(a.basetype)
                                              let pn = _typeParentSave[a.basetype]
                                              select (pn.schema ?? a.schema, pn.name, a.basetype, a.row));

                            if (parentRows.Any())
                            {
                                try
                                {
                                    settings.NoAcceptChanges = true;
                                    provkvp.Key.UpdateRows(cs, parentRows, settings);
                                }
                                finally
                                {
                                    settings.NoAcceptChanges = false;
                                }
                            }

                            results.AddRange(from a in provkvp.Key.UpdateRows(cs, filteredrows, settings) select (a.row.GetWrappedObject(), a.msg, a.status));
                        }
                    }

                    if ((settings.AllowedOperations & DBSaveSettings.Operations.Insert) != 0)
                    {
                        if (levelkvp.Value.TryGetValue(ObjectState.Added, out var filteredrows))
                        {
                            var isLeaf = (levelkvp.Key == provkvp.Value.Keys.Max());

                            var parentRows = (from a in filteredrows
                                              where _typeParentSave.ContainsKey(a.basetype)
                                              let pn = _typeParentSave[a.basetype]
                                              select (pn.schema ?? a.schema, pn.name, a.basetype, a.row));

                            if (parentRows.Any())
                            {
                                try
                                {
                                    settings.NoAcceptChanges = true;
                                    provkvp.Key.InsertRows(cs, parentRows, isLeaf, settings);
                                }
                                finally
                                {
                                    settings.NoAcceptChanges = false;
                                }
                            }

                            results.AddRange(from a in provkvp.Key.InsertRows(cs, filteredrows, isLeaf, settings) select (a.row.GetWrappedObject(), a.msg, a.status));
                        }
                    }
                }
            }

            cs.DoneWork();

            // We need to do the cache update at the end since we could have assigned values, etc. during the above save step - cache the final values!
            ICEFCachingHost? cache = CEF.CurrentCacheService();

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

                void act(object state)
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
                }

                if (settings.AsyncCacheUpdates.GetValueOrDefault(ss.Settings.AsyncCacheUpdates.GetValueOrDefault(Globals.AsyncCacheUpdates)))
                {
                    AddCompletionTask(Task.Factory.StartNew(act, forCaching));
                }
                else
                {
                    act(forCaching);
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
                            if (!t2.Wait(MAX_WAIT_TIME_MS))
                            {
                                throw new TimeoutException($"Waited {MAX_WAIT_TIME_MS} ms for data operation - failed to complete.");
                            }
                        }
                    }
                }

                sst.Completions.Enqueue(t);
            }
        }

        public T ExecuteScalar<T>(string cmdText)
        {
            if (DefaultProvider == null)
            {
                throw new CEFInvalidStateException(InvalidStateType.MissingInit, "Data provider not set.");
            }

            var cs = CEF.CurrentConnectionScope;
            var res = DefaultProvider.ExecuteScalar<T>(cs, cmdText);
            cs.DoneWork();
            return res;
        }

        public void ExecuteNoResultSet(CommandType cmdType, string cmdText, params object?[] args)
        {
            if (DefaultProvider == null)
            {
                throw new CEFInvalidStateException(InvalidStateType.MissingInit, "Data provider not set.");
            }

            var cs = CEF.CurrentConnectionScope;
            DefaultProvider.ExecuteNoResultSet(cs, cmdType, cmdText, args);
            cs.DoneWork();
        }

        public void ExecuteRaw(string command, bool doThrow = true, bool stopOnError = true)
        {
            if (DefaultProvider == null)
            {
                throw new CEFInvalidStateException(InvalidStateType.MissingInit, "Data provider not set.");
            }

            var cs = CEF.CurrentConnectionScope;
            DefaultProvider.ExecuteRaw(cs, command, doThrow, stopOnError);
            cs.DoneWork();
        }

        internal IEnumerable RetrieveByKeyNonGeneric(Type bt, params object[] key)
        {
            if (_retrieveByKeyCache.TryGetValue(bt, out Func<object[], IEnumerable> vl))
            {
                return vl.Invoke(key);
            }

            var mi = this.GetType().GetMethod("InternalRetrieveByKey", System.Reflection.BindingFlags.InvokeMethod | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            mi = mi.MakeGenericMethod(bt);
            var funcWrap = (Func<object[], IEnumerable>) mi.Invoke(this, Array.Empty<object>());
            _retrieveByKeyCache[bt] = funcWrap;
            return funcWrap(key);
        }

        // Do not remove, used internally by reflection
#pragma warning disable IDE0051 // Remove unused private members
        private Func<object[], IEnumerable> InternalRetrieveByKey<T>() where T : class, new() => new((k) => { return RetrieveByKey<T>(k); });
#pragma warning restore IDE0051 // Remove unused private members

        public IEnumerable<T> RetrieveByKey<T>(params object[] key) where T : class, new()
        {
            var ss = CEF.CurrentServiceScope;
            var sst = ss.GetServiceState<DBServiceState>();
            var cb = CEF.CurrentServiceScope.ResolvedCacheBehaviorForType(typeof(T));

            // Special situation we look for: async saving can result in unsaved objects, where we're better off to pull from that which might still be in memory
            if (sst?.Completions.Count > 0 && ((ss.Settings.MergeBehavior & MergeBehavior.CheckScopeIfPending) != 0))
            {
                var kss = ss.GetServiceState<KeyService.KeyServiceState>();

                if (kss != null)
                {
                    var eto = kss.GetTrackedByPKValue(typeof(T), key);

                    if (eto != null && eto.IsAlive)
                    {
                        if (eto.GetWrapperTarget() is T ett)
                        {
                            return new T[] { ett };
                        }
                    }
                }
            }

            ICEFCachingHost? cache = null;

            if ((cb & CacheBehavior.IdentityBased) != 0)
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

            if (cs.IsStandalone)
            {
                // If it's a List, we know it's got to be a snapshot already, no need to do this again!
                if (!(res is List<T>))
                {
                    res = res.ToArray();
                }

                cs.DoneWork();
            }

            // If caching is present, call out to it to potentially cache results (by identity)
            if (cache != null && cb != CacheBehavior.Off)
            {
                if (!cs.IsStandalone)
                {
                    // If it's a List, we know it's got to be a snapshot already, no need to do this again!
                    if (!(res is List<T>))
                    {
                        res = res.ToArray();
                    }
                }

                var fr = res.FirstOrDefault();

                if (fr != null)
                {
                    cache.AddByIdentity(fr, key);
                }
            }

            return res;
        }

        public IEnumerable<T> RetrieveByQuery<T>(CommandType cmdType, string cmdText, CEF.ColumnDefinitionCallback? cc, params object?[] parms) where T : class, new()
        {
            ICEFCachingHost? cache = null;
            var cb = CEF.CurrentServiceScope.ResolvedCacheBehaviorForType(typeof(T));

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
            var res = GetProviderForType(typeof(T)).RetrieveByQuery<T>(this, cs, true, cmdType, cmdText, cc, parms);

            if (cs.IsStandalone)
            {
                // If it's a List, we know it's got to be a snapshot already, no need to do this again!
                if (!(res is List<T>))
                {
                    res = res.ToArray();
                }

                cs.DoneWork();
            }

            if (cache == null && (cb & CacheBehavior.ConvertQueryToIdentity) != 0)
            {
                cache = CEF.CurrentCacheService();
            }

            if (cache != null && cb != CacheBehavior.Off)
            {
                if (!cs.IsStandalone)
                {
                    // If it's a List, we know it's got to be a snapshot already, no need to do this again!
                    if (!(res is List<T>))
                    {
                        res = res.ToArray();
                    }
                }

                cache.AddByQuery(res, cmdText, parms, null, cb);
            }

            return res;
        }

        public IEnumerable<T> RetrieveAll<T>() where T : class, new()
        {
            ICEFCachingHost? cache = null;
            var cb = CEF.CurrentServiceScope.ResolvedCacheBehaviorForType(typeof(T));

            if ((cb & CacheBehavior.QueryBased) != 0)
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

            if (cs.IsStandalone)
            {
                // If it's a List, we know it's got to be a snapshot already, no need to do this again!
                if (!(res is List<T>))
                {
                    res = res.ToArray();
                }

                cs.DoneWork();
            }

            if (cache != null && cb != CacheBehavior.Off)
            {
                if (!cs.IsStandalone)
                {
                    // If it's a List, we know it's got to be a snapshot already, no need to do this again!
                    if (!(res is List<T>))
                    {
                        res = res.ToArray();
                    }
                }

                cache.AddByQuery(res, "ALL", null, null, cb);
            }

            return res;
        }

        Type ICEFService.IdentifyStateType(object o, ServiceScope ss, bool isNew)
        {
            return typeof(DBServiceState);
        }

        WrappingSupport ICEFService.IdentifyInfraNeeds(object o, object? replaced, ServiceScope ss, bool isNew)
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

        void ICEFService.FinishSetup(ServiceScope.TrackedObject to, ServiceScope ss, bool isNew, IDictionary<string, object?>? props, ICEFServiceObjState? state, bool initFromTemplate)
        {
            if (isNew && to.BaseType != null)
            {
                if (_typePropDefaults.TryGetValue(to.BaseType, out var defbytype))
                {
                    var t = to.GetInfraWrapperTarget();

                    foreach (var (prop, value, def, proptype) in defbytype)
                    {
                        // If the underlying prop is not nullable, we only overwrite if NOT init from a template object, otherwise can lose purposeful sets on that template object
                        if (Nullable.GetUnderlyingType(proptype) != null || !initFromTemplate)
                        {
                            var curval = ss.GetGetter(t, prop).getter?.Invoke();

                            if (curval.IsSame(def))
                            {
                                ss.GetSetter(t, prop).setter?.Invoke(value);
                            }
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
            public ConcurrentQueue<Task> Completions { get; } = new ConcurrentQueue<Task>();

            public List<Exception> Exceptions { get; } = new List<Exception>();

            public object Sync { get; } = new object();

            public void Cleanup(ServiceScope ss)
            {
            }
        }
    }
}
