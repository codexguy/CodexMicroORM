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

Major Changes:
12/2017    0.2     Initial release (Joel Champagne)
***********************************************************************/
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using CodexMicroORM.Core.Services;
using System.Data;
using System.Collections.Immutable;
using System.Threading;
using CodexMicroORM.Core.Collections;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Collections;
using static CodexMicroORM.Core.Services.KeyService;
using System.Runtime.CompilerServices;

namespace CodexMicroORM.Core
{
    /// <summary>
    /// CEF (CodeX Entity Framework) offers basic functionality of the framework, generally through static methods. (This is in addition to extension methods found in the Extensions.cs file.)
    /// </summary>
    public static class CEF
    {
        #region "Private state (global)"

        private static readonly SlimConcurrentDictionary<Type, IList<ICEFService>> _resolvedServicesByType = [];
        private static readonly ConcurrentDictionary<Type, IList<ICEFService>> _regServicesByType = new(Globals.DefaultCollectionConcurrencyLevel, Globals.DefaultDictionaryCapacity);
        private static ImmutableArray<ICEFService> _globalServices = [];
        private static readonly HashSet<string> _treatPropertyAsReadOnly = [];
        private static readonly HashSet<Action<bool>> _appEventList = [];

        internal static ServiceScope? InternalGlobalServiceScope = null;
        internal static ConcurrentDictionary<Type, Func<DBSaveTriggerFlags, ICEFInfraWrapper, DBSaveSettings, object?, object?>> SaveTriggers = [];

        private static readonly AsyncLocal<ImmutableStack<ServiceScope>> _allServiceScopes = new();

        private static readonly AsyncLocal<ServiceScope?> _currentServiceScope = new();

        private static readonly AsyncLocal<ImmutableStack<ConnectionScope>> _allConnScopes = new();

        private static readonly AsyncLocal<ConnectionScope> _currentConnScope = new();

        public static ICollection<ICEFService> GlobalServices => _globalServices.ToArray();

        internal static SlimConcurrentDictionary<Type, IList<ICEFService>> ResolvedServicesByType => _resolvedServicesByType;
        internal static ConcurrentDictionary<Type, IList<ICEFService>> RegisteredServicesByType => _regServicesByType;
        public static HashSet<string> RegisteredPropertyNameTreatReadOnly => _treatPropertyAsReadOnly;

        private static Action<string, long>? _queryPerfInfo = null;

        private static readonly object _lockDB = new();
        private static readonly object _lockAudit = new();
        private static readonly object _lockKey = new();
        private static readonly object _lockPCT = new();

        private static readonly ConcurrentDictionary<Type, List<string>> _toSaveProps = new(Globals.DefaultCollectionConcurrencyLevel, Globals.DefaultLargerDictionaryCapacity);
        private static readonly ConcurrentDictionary<Type, List<string>> _toSavePropsPersist = new(Globals.DefaultCollectionConcurrencyLevel, Globals.DefaultLargerDictionaryCapacity);
        private static readonly ConcurrentBag<string> _globalPropToSave = [];

        #endregion

        #region "Public methods"

        public delegate void ColumnDefinitionCallback(string name, Type dataType);

        public static ServiceScope? GlobalServiceScope => InternalGlobalServiceScope;

        public static Action<string, string>? DataAccessCallout
        {
            get;
            set;
        }

        /// <summary>
        /// Can be used to advise the framework that a running app is being suspended.
        /// This can in turn reduce database timeouts, where a long gap of time might exist during suspension (in reality, the amount of "runnable time" should only be considered).
        /// </summary>
        public static void SignalAppSleep()
        {
            List<Action<bool>> l;

            lock (_appEventList)
            {
                l = _appEventList.ToList();
            }

            foreach (var sub in _appEventList)
            {
                sub.Invoke(true);
            }
        }

        /// <summary>
        /// If SignalAppSleep() gets called, it would be expected to call SignalAppResume() when the app is resumed.
        /// </summary>
        public static void SignalAppResume()
        {
            List<Action<bool>> l;

            lock (_appEventList)
            {
                l = _appEventList.ToList();
            }

            foreach (var sub in _appEventList)
            {
                sub.Invoke(false);
            }
        }

        public static void RegisterQueryPerformanceTracking(Action<string, long> perfHandler)
        {
            _queryPerfInfo = perfHandler;
        }

        /// <summary>
        /// Registers a global service, applicable to any object.
        /// </summary>
        /// <param name="srv"></param>
        public static void AddGlobalService(ICEFService srv)
        {
            lock (typeof(CEF))
            {
                _globalServices = _globalServices.Add(srv);
            }
        }

        /// <summary>
        /// Allows registration of handler that can preview/postview save ops, by row. E.g. the save settings provided as input can be used with UserPayload to queue operations to perform after the save completes.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="handler"></param>
        public static void RegisterSaveTrigger<T>(Func<DBSaveTriggerFlags, ICEFInfraWrapper, DBSaveSettings, object?, object?> handler)
        {
            SaveTriggers[typeof(T)] = handler;
        }

        /// <summary>
        /// Creates a new connection scope that is transactional.
        /// </summary>
        /// <param name="settings"></param>
        /// <returns></returns>
        public static ConnectionScope NewTransactionScope(ConnectionScopeSettings? settings = null)
        {
            settings ??= new ConnectionScopeSettings();
            settings.IsTransactional = true;
            return NewConnectionScope(settings);
        }

        /// <summary>
        /// Creates a new connection scope that's transactional and tied to a specific service scope.
        /// </summary>
        /// <param name="relateTo"></param>
        /// <returns></returns>
        public static ConnectionScope NewTransactionScope(ServiceScope relateTo)
        {
            if (relateTo?.Settings == null)
            {
                throw new CEFInvalidStateException(InvalidStateType.ArgumentNull, nameof(relateTo));
            }

            var settings = new ConnectionScopeSettings
            {
                IsTransactional = true
            };
            relateTo.Settings.ConnectionScopePerThread = false;

            return NewConnectionScope(settings);
        }

        /// <summary>
        /// Creates a new connection scope (may or may not be transactional depending on settings).
        /// </summary>
        /// <param name="settings"></param>
        /// <returns></returns>
        public static ConnectionScope NewConnectionScope(ConnectionScopeSettings? settings = null)
        {
            settings ??= new ConnectionScopeSettings();
            var mode = settings.ScopeMode.GetValueOrDefault(Globals.DefaultConnectionScopeMode);

            var ss = CurrentServiceScope;
            var useLocal = ss.Settings.ConnectionScopePerThread.GetValueOrDefault(Globals.ConnectionScopePerThread);
            var cs = new ConnectionScope(settings.IsTransactional.GetValueOrDefault(Globals.UseTransactionsForNewScopes), settings.ConnectionStringOverride, settings.CommandTimeoutOverride);

            if (!useLocal)
            {
                if (mode == ScopeMode.CreateNew || ss._currentConnScope == null)
                {
                    ss.ConnScopeInit(cs);
                }
            }
            else
            {
                if (mode == ScopeMode.CreateNew || _currentConnScope == null)
                {
                    ConnScopeInit(cs);
                }
            }

            return CurrentConnectionScope;
        }

        /// <summary>
        /// Gets the ambient connection scope.
        /// </summary>
        public static ConnectionScope CurrentConnectionScope
        {
            get
            {
                var ss = CurrentServiceScope;
                var useLocal = ss.Settings.ConnectionScopePerThread.GetValueOrDefault(Globals.ConnectionScopePerThread);

                if (!useLocal)
                {
                    if (ss._currentConnScope?.Value == null)
                    {
                        var cs = new ConnectionScope(Globals.DefaultTransactionalStandalone)
                        {
                            IsStandalone = true
                        };
                        ss.ConnScopeInit(cs);
                    }

                    return ss._currentConnScope?.Value ?? throw new InvalidOperationException("Failed to get current scope.");
                }

                if (_currentConnScope?.Value == null)
                {
                    var cs = new ConnectionScope(Globals.DefaultTransactionalStandalone)
                    {
                        IsStandalone = true
                    };
                    ConnScopeInit(cs);
                }

                return _currentConnScope?.Value ?? throw new InvalidOperationException("Failed to get current scope.");
            }
        }

        /// <summary>
        /// Makes the ambient service scope the one that is passed in.
        /// </summary>
        /// <param name="toUse"></param>
        /// <returns></returns>
        public static ServiceScope UseServiceScope(ServiceScope toUse)
        {
            // This is a special type of service scope - we create a shallow copy and flag it as not allowing destruction of contents when disposed
            // The tempation to check if the toUse == current should be ignored - if we're using in a using block, we might not want destruction of the input scope, so better to push a new one even if more costly
            ServiceScopeInit(new ServiceScope(toUse ?? throw new CEFInvalidStateException(InvalidStateType.ArgumentNull, nameof(toUse))), null);
            return _currentServiceScope.Value!;
        }

        /// <summary>
        /// If there's an ambient service scope, returns it, otherwise creates a new service scope and returns it.
        /// </summary>
        /// <param name="settings"></param>
        /// <param name="additionalServices"></param>
        /// <returns></returns>
        public static ServiceScope NewOrCurrentServiceScope(ServiceScopeSettings settings, params ICEFService[] additionalServices)
        {
            if (InternalGlobalServiceScope != null)
            {
                ServiceScopeInit(new ServiceScope(InternalGlobalServiceScope), additionalServices);
                return _currentServiceScope.Value!;
            }

            if (_currentServiceScope.Value != null)
            {
                ServiceScopeInit(new ServiceScope(_currentServiceScope.Value), additionalServices);
                return _currentServiceScope.Value;
            }

            ServiceScopeInit(new ServiceScope(settings), additionalServices);
            return _currentServiceScope.Value ?? throw new CEFInvalidStateException(InvalidStateType.LowLevelState, "Could not determine service scope.");
        }

        /// <summary>
        /// Throws an exception if not in a valid service scope
        /// </summary>
        public static void AssertInServiceScope()
        {
            if (_currentServiceScope.Value == null)
            {
#if DEBUG
                if (System.Diagnostics.Debugger.IsAttached)
                {
                    System.Diagnostics.Debugger.Break();
                }
#endif
                throw new CEFInvalidStateException(InvalidStateType.MissingServiceState);
            }
        }

        /// <summary>
        /// If there's an ambient service scope, returns it, otherwise creates a new service scope and returns it.
        /// </summary>
        /// <param name="additionalServices"></param>
        /// <returns></returns>
        public static ServiceScope NewOrCurrentServiceScope(params ICEFService[] additionalServices)
        {
            if (InternalGlobalServiceScope != null)
            {
                ServiceScopeInit(new ServiceScope(InternalGlobalServiceScope), additionalServices);
                return _currentServiceScope.Value!;
            }

            if (_currentServiceScope.Value != null)
            {
                ServiceScopeInit(new ServiceScope(_currentServiceScope.Value), additionalServices);
                return _currentServiceScope.Value;
            }

            ServiceScopeInit(new ServiceScope(new ServiceScopeSettings()), additionalServices);
            return _currentServiceScope.Value!;
        }

        /// <summary>
        /// If there's an ambient service scope, returns it, otherwise creates a new service scope and returns it.
        /// </summary>
        /// <param name="settings"></param>
        /// <returns></returns>
        public static ServiceScope NewOrCurrentServiceScope(ServiceScopeSettings? settings = null)
        {
            if (InternalGlobalServiceScope != null)
            {
                ServiceScopeInit(new ServiceScope(InternalGlobalServiceScope), null);
                return _currentServiceScope.Value!;
            }

            if (_currentServiceScope.Value != null)
            {
                ServiceScopeInit(new ServiceScope(_currentServiceScope.Value), null);
                return _currentServiceScope.Value;
            }

            ServiceScopeInit(new ServiceScope(settings ?? new ServiceScopeSettings()), null);
            return _currentServiceScope.Value!;
        }

        /// <summary>
        /// Resets global state to have no available service scopes - typically used by infrastructure only.
        /// </summary>
        public static void RemoveAllServiceScopes()
        {
            while (_allServiceScopes.Value?.Count() > 0)
            {
                try
                {
                    _allServiceScopes.Value = _allServiceScopes.Value.Pop(out var ss);
                    ss.Dispose();
                }
                catch
                {
                }
            }
            _currentServiceScope.Value?.Dispose();
            _currentServiceScope.Value = null;
        }

        /// <summary>
        /// Creates a new service scope, makes it the ambient scope and returns it.
        /// </summary>
        /// <param name="settings"></param>
        /// <param name="additionalServices"></param>
        /// <returns></returns>
        public static ServiceScope NewServiceScope(ServiceScopeSettings settings, params ICEFService[] additionalServices)
        {
            if (InternalGlobalServiceScope != null)
            {
                ServiceScopeInit(new ServiceScope(InternalGlobalServiceScope), additionalServices);
                return _currentServiceScope.Value!;
            }

            ServiceScopeInit(new ServiceScope(settings), additionalServices);
            return _currentServiceScope.Value!;
        }

        /// <summary>
        /// Creates a new service scope, makes it the ambient scope and returns it.
        /// </summary>
        /// <param name="additionalServices"></param>
        /// <returns></returns>
        public static ServiceScope NewServiceScope(params ICEFService[] additionalServices)
        {
            if (InternalGlobalServiceScope != null)
            {
                ServiceScopeInit(new ServiceScope(InternalGlobalServiceScope), additionalServices);
                return _currentServiceScope.Value!;
            }

            ServiceScopeInit(new ServiceScope(new ServiceScopeSettings()), additionalServices);
            return _currentServiceScope.Value!;
        }

        /// <summary>
        /// Creates a new service scope, makes it the ambient scope and returns it.
        /// </summary>
        /// <param name="settings"></param>
        /// <returns></returns>
        public static ServiceScope NewServiceScope(ServiceScopeSettings? settings = null)
        {
            if (InternalGlobalServiceScope != null)
            {
                ServiceScopeInit(new ServiceScope(InternalGlobalServiceScope), null);
                return _currentServiceScope.Value!;
            }

            ServiceScopeInit(new ServiceScope(settings ?? new ServiceScopeSettings()), null);
            return _currentServiceScope.Value!;
        }

        /// <summary>
        /// Gets the ambient service scope.
        /// </summary>
        public static ServiceScope CurrentServiceScope
        {
            get
            {
                if (_currentServiceScope.Value == null)
                {
                    if (InternalGlobalServiceScope != null)
                    {
                        return InternalGlobalServiceScope;
                    }

                    ServiceScopeInit(new ServiceScope(new ServiceScopeSettings()), null);
                }

                return _currentServiceScope.Value!;
            }
        }

        /// <summary>
        /// Deserializes input JSON into a collection of entities of known type. All entities are added to the ambient service scope.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="json"></param>
        /// <returns></returns>
        public static EntitySet<T> DeserializeSet<T>(string json) where T : class, new()
        {
            return CurrentServiceScope.DeserializeSet<T>(json);
        }

        /// <summary>
        /// Deserializes input JSON into a single entity of known type. The entity is added to the ambient service scope.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="json"></param>
        /// <returns></returns>
        public static T Deserialize<T>(string json) where T : class, new()
        {
            return CurrentServiceScope.Deserialize<T>(json);
        }

        /// <summary>
        /// Deserializes input JSON into objects that are added to the ambient service scope. Typically this JSON format must match that obtained by serializing a scope, since it includes type names with specific attributes, etc.
        /// </summary>
        /// <param name="json"></param>
        /// <returns></returns>
        public static int DeserializeScope(string json)
        {
            return CurrentServiceScope.DeserializeScope(json);
        }

        /// <summary>
        /// Returns all entities that are currently tracked in the ambient service scope.
        /// </summary>
        /// <returns></returns>
        public static IEnumerable<ICEFInfraWrapper> GetAllTracked()
        {
            return CurrentServiceScope.GetAllTracked();
        }

        /// <summary>
        /// The modified state of all entities in the ambient scope is set to "unchanged".
        /// </summary>
        public static void AcceptAllChanges()
        {
            CurrentServiceScope.AcceptAllChanges();
        }

        public static IEnumerable<(object item, string? message, int status)> DBSaveTransactional(DBSaveSettings? settings = null)
        {
            using var tx = NewTransactionScope();
            var rv = CurrentServiceScope.DBSave(settings);
            tx.CanCommit();
            tx.Dispose();
            return rv!;
        }

        public static async Task<IEnumerable<(object item, string? message, int status)>> DBSaveTransactionalAsync(DBSaveSettings? settings = null)
        {
            IEnumerable<(object item, string? message, int status)>? rv = null;
            Exception? ex = null;

            await Task.Run(() =>
            {
                try
                {
                    using var tx = NewTransactionScope();
                    rv = CurrentServiceScope.DBSave(settings);
                    tx.CanCommit();
                    tx.Dispose();
                }
                catch (Exception ex2)
                {
                    ex = ex2;
                }
            });

            if (ex != null)
            {
                throw ex;
            }

            return rv!;
        }

#if DEBUG
        public static string? DebugLastAsyncSaveFrom
        {
            get;
            private set;
        }
#endif

        /// <summary>
        /// Requests database persistence over all entities in the ambient service scope.
        /// </summary>
        /// <param name="settings"></param>
        /// <returns>One element per entity saved, indicating any message and/or status returned by the save process for that entity.</returns>
        public static async Task<IEnumerable<(object item, string? message, int status)>> DBSaveAsync(DBSaveSettings? settings = null, [CallerFilePath] string? filepath = null, [CallerLineNumber] int? linenumber = null)
        {
            IEnumerable<(object item, string? message, int status)>? rv = null;
            Exception? ex = null;

#if DEBUG
            var cf = $"{filepath};{linenumber}";
            System.Diagnostics.Debug.WriteLine($"I: DBSaveAsync called from {cf}");
            DebugLastAsyncSaveFrom = cf;
#endif

            await Task.Run(() =>
            {
                try
                {
                    rv = CurrentServiceScope.DBSave(settings);
                }
                catch (Exception ex2)
                {
                    ex = ex2;

//#if DEBUG
//                    if (System.Diagnostics.Debugger.IsAttached)
//                    {
//                        System.Diagnostics.Debugger.Break();
//                    }
//#endif
                }
            });

            if (ex != null)
            {
                throw ex;
            }

            return rv!;
        }

        /// <summary>
        /// Requests database persistence over all entities in the ambient service scope.
        /// </summary>
        /// <param name="settings"></param>
        /// <returns>One element per entity saved, indicating any message and/or status returned by the save process for that entity.</returns>
        public static IEnumerable<(object item, string? message, int status)> DBSave(DBSaveSettings? settings = null)
        {
            return CurrentServiceScope.DBSave(settings);
        }

        /// <summary>
        /// Requests database persistence for a specific entity in the ambient service scope.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="tosave"></param>
        /// <param name="allRelated">If true, all related entities are also considered as candidates for saving. If false, only the specific entity is considered a candidate for saving.</param>
        /// <returns></returns>
        public static T DBSave<T>(this T tosave, bool allRelated) where T : class, new()
        {
            var settings = new DBSaveSettings
            {
                RootObject = tosave,
                IncludeRootChildren = allRelated,
                IncludeRootParents = allRelated,
                EntityPersistName = GetEntityPersistName<T>(tosave),
                EntityPersistType = typeof(T)
            };

            CurrentServiceScope.DBSave(settings);
            return tosave;
        }

        /// <summary>
        /// Requests database persistence for a specific entity in the ambient service scope.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="tosave"></param>
        /// <param name="settings"></param>
        /// <returns></returns>
        public static T? DBSave<T>(this T tosave, DBSaveSettings? settings = null) where T : class, new()
        {
            settings ??= new DBSaveSettings();

            if (tosave != null && tosave is IEnumerable enumerable)
            {
                settings.SourceList = enumerable.Cast<object>();
            }
            else
            {
                settings.RootObject = tosave;
            }

            settings.EntityPersistName ??= GetEntityPersistName<T>(tosave!);
            settings.EntityPersistType = typeof(T);

            CurrentServiceScope.DBSave(settings);
            return tosave;
        }

        /// <summary>
        /// Requests database persistence for a specific entity in the ambient service scope. This method unlike DBSave returns a validation-specific code/message that comes from the validation service.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="tosave"></param>
        /// <param name="settings"></param>
        /// <returns></returns>
        public static (ValidationErrorCode error, string? message) DBSaveWithMessage<T>(this T tosave, DBSaveSettings? settings = null) where T : class, new()
        {
            settings ??= new DBSaveSettings();

            settings.RootObject = tosave;
            settings.EntityPersistName ??= GetEntityPersistName<T>(tosave);
            settings.EntityPersistType = typeof(T);

            var res = CurrentServiceScope.DBSave(settings);

            if (res.Any())
            {
                return ((ValidationErrorCode)(-res.First().status), res.First().message);
            }

            return (ValidationErrorCode.None, null);
        }

        /// <summary>
        /// Executes a raw command that returns no result set, in the database layer.
        /// </summary>
        /// <param name="cmdText"></param>
        /// <param name="parms"></param>
        public static void DBExecuteNoResult(CommandType cmdType, string cmdText, params object?[] parms)
        {
            CurrentDBService().ExecuteNoResultSet(cmdType, cmdText, parms);
        }

        /// <summary>
        /// Retrieves zero, one or many entities, populated into an existing EntitySet collection, using a custom query. The contents of the collection are replaced.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="pop"></param>
        /// <param name="cmdType"></param>
        /// <param name="cmdText"></param>
        /// <param name="parms"></param>
        /// <returns></returns>
        public static EntitySet<T> DBRetrieveByQuery<T>(this EntitySet<T> pop, CommandType cmdType, string cmdText, ColumnDefinitionCallback cc, params object?[] parms) where T : class, new()
        {
            if (pop.Any())
            {
                pop.Clear();
            }

            InternalDBAppendByQuery(pop, cmdType, cmdText, cc, parms);
            return pop;
        }

        /// <summary>
        /// Retrieves zero, one or many entities, populated into an existing EntitySet collection, using a custom query. The contents of the collection are replaced.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="pop"></param>
        /// <param name="cmdText"></param>
        /// <param name="parms"></param>
        /// <returns></returns>
        public static EntitySet<T> DBRetrieveByQuery<T>(this EntitySet<T> pop, string cmdText, ColumnDefinitionCallback cc, params object?[] parms) where T : class, new()
        {
            if (pop.Any())
            {
                pop.Clear();
            }

            InternalDBAppendByQuery(pop, CommandType.StoredProcedure, cmdText, cc, parms);
            return pop;
        }

        /// <summary>
        /// Retrieves zero, one or many entities, populated into an existing EntitySet collection, using a custom query. The contents of the collection are replaced.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="pop"></param>
        /// <param name="cmdType"></param>
        /// <param name="cmdText"></param>
        /// <param name="parms"></param>
        /// <returns></returns>
        public static EntitySet<T> DBRetrieveByQuery<T>(this EntitySet<T> pop, CommandType cmdType, string cmdText, params object?[] parms) where T : class, new()
        {
            if (pop.Any())
            {
                pop.Clear();
            }

            InternalDBAppendByQuery(pop, cmdType, cmdText, null, parms);
            return pop;
        }

        /// <summary>
        /// Retrieves zero, one or many entities, populated into an existing EntitySet collection, using a custom query. The contents of the collection are replaced.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="pop"></param>
        /// <param name="cmdText"></param>
        /// <param name="parms"></param>
        /// <returns></returns>
        public static EntitySet<T> DBRetrieveByQuery<T>(this EntitySet<T> pop, string cmdText, params object?[] parms) where T : class, new()
        {
            if (pop.Any())
            {
                pop.Clear();
            }

            InternalDBAppendByQuery(pop, CommandType.StoredProcedure, cmdText, null, parms);
            return pop;
        }

        /// <summary>
        /// Retrieves zero, one or many entities, populated into an existing EntitySet collection, using a custom query. The pre-existing contents of the collection are retained.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="pop"></param>
        /// <param name="cmdType"></param>
        /// <param name="cmdText"></param>
        /// <param name="parms"></param>
        /// <returns></returns>
        public static EntitySet<T> DBAppendByQuery<T>(this EntitySet<T> pop, CommandType cmdType, string cmdText, params object?[] parms) where T : class, new()
        {
            InternalDBAppendByQuery(pop, cmdType, cmdText, null, parms);
            return pop;
        }

        /// <summary>
        /// Retrieves zero, one or many entities, populated into an existing EntitySet collection, using a custom query. The pre-existing contents of the collection are retained.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="pop"></param>
        /// <param name="cmdText"></param>
        /// <param name="parms"></param>
        /// <returns></returns>
        public static EntitySet<T> DBAppendByQuery<T>(this EntitySet<T> pop, string cmdText, params object?[] parms) where T : class, new()
        {
            InternalDBAppendByQuery(pop, CommandType.StoredProcedure, cmdText, null, parms);
            return pop;
        }

        /// <summary>
        /// Retrieves all available entities from a specific data store (based on entity type). The contents of the collection are replaced.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="pop"></param>
        /// <returns></returns>
        public static EntitySet<T> DBRetrieveAll<T>(this EntitySet<T> pop) where T : class, new()
        {
            if (pop.Any())
            {
                pop.Clear();
            }

            InternalDBAppendAll(pop);
            return pop;
        }

        /// <summary>
        /// Retrieves all available entities from a specific data store (based on entity type). The pre-existing contents of the collection are retained.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="pop"></param>
        /// <returns></returns>
        public static EntitySet<T> DBAppendAll<T>(this EntitySet<T> pop) where T : class, new()
        {
            InternalDBAppendAll(pop);
            return pop;
        }

        public static EntitySet<T> DBRetrieveByKeyOrInsert<T>(this EntitySet<T> pop, T template) where T : class, new ()
        {
            var kv = CurrentKeyService()?.GetKeyValues(template);

            if (kv?.Count > 0)
            {
                pop.DBRetrieveByKey((from a in kv select a.value).ToArray());

                if (!pop.Any())
                {
                    template = NewObject(template);
                    pop.Add(template);
                    DBSave(template, false);
                }
            }

            return pop;
        }

        /// <summary>
        /// Retrieves zero or one entity from a specific data store (based on entity type). The contents of the collection are replaced.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="pop"></param>
        /// <param name="key"></param>
        /// <returns></returns>
        public static EntitySet<T> DBRetrieveByKey<T>(this EntitySet<T> pop, params object[] key) where T : class, new()
        {
            if (pop.Any())
            {
                pop.Clear();
            }

            InternalDBAppendByKey(pop, key);

            return pop;
        }

        /// <summary>
        /// Retrieves zero or one entity from a specific data store (based on entity type). The pre-existing contents of the collection are retained.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="pop"></param>
        /// <param name="key"></param>
        /// <returns></returns>
        public static EntitySet<T> DBAppendByKey<T>(this EntitySet<T> pop, params object[] key) where T : class, new()
        {
            InternalDBAppendByKey(pop, key);
            return pop;
        }

        /// <summary>
        /// Instantiates a new entity of type T, optionally copies values from a template object, and adds it to the ambient service scope in an "added" state.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="initial"></param>
        /// <returns></returns>
        public static T NewObject<T>(T? initial = null, bool recursive = false) where T : class, new()
        {
            return CurrentServiceScope.NewObject(initial, recursive: recursive);
        }

        /// <summary>
        /// Adds an existing entity to the ambient service scope using an optional initial state.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="toAdd"></param>
        /// <param name="drs"></param>
        /// <returns></returns>
        public static T IncludeObject<T>(T toAdd, ObjectState? drs = null) where T : class, new()
        {
            var ss = CurrentServiceScope;
            return ss.IncludeObject<T>(toAdd, ss.ResolvedRetrievalIdentityMode(toAdd), drs, null);
        }

        /// <summary>
        /// Adds an existing entity to the ambient service scope using explicit initialization properties.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="toAdd"></param>
        /// <param name="props"></param>
        /// <returns></returns>
        public static T IncludeObject<T>(T toAdd, IDictionary<string, object?> props) where T : class, new()
        {
            var ss = CurrentServiceScope;
            return ss.IncludeObject<T>(toAdd, ss.ResolvedRetrievalIdentityMode(toAdd), null, props);
        }

        /// <summary>
        /// Marks a specific tracked entity as being in a deleted state. Option to cascade to children.
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="action"></param>
        public static void DeleteObject(object obj, DeleteCascadeAction action = DeleteCascadeAction.Cascade)
        {
            CurrentServiceScope.Delete(obj, action);
        }

        /// <summary>
        /// Takes an input of one or more objects of a trackable type and returns a collection that "monitors changes".
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="items"></param>
        /// <returns></returns>
        public static EntitySet<T> CreateList<T>(params T[] items) where T : class, new()
        {
            return Globals.NewEntitySet<T>(items);
        }

        /// <summary>
        /// Returns a new collection that tracks changes in entities, where the collection is a representation of "children of" a specific parent entity / property.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="parent"></param>
        /// <param name="parentFieldName"></param>
        /// <returns></returns>
        public static EntitySet<T> CreateList<T>(object parent, string parentFieldName) where T : class, new()
        {
            var rs = Globals.NewEntitySet<T>();
            rs.ParentContainer = parent;
            rs.ParentTypeName = parent.GetBaseType().Name;
            rs.ParentFieldName = parentFieldName;
            return rs;
        }

        /// <summary>
        /// Returns a new collection that tracks changes in entities, where the collection is a representation of "children of" a specific parent entity / property.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="parent"></param>
        /// <param name="parentFieldName"></param>
        /// <param name="initialState"></param>
        /// <param name="items"></param>
        /// <returns></returns>
        public static EntitySet<T> CreateList<T>(object parent, string parentFieldName, ObjectState initialState, params T[] items) where T : class, new()
        {
            var rs = Globals.NewEntitySet<T>();

            rs.ParentContainer = parent ?? throw new CEFInvalidStateException(InvalidStateType.ArgumentNull, nameof(parent));
            rs.ParentTypeName = parent.GetBaseType().Name;
            rs.ParentFieldName = parentFieldName ?? throw new CEFInvalidStateException(InvalidStateType.ArgumentNull, nameof(parentFieldName));

            if (items?.Length > 0)
            {
                foreach (var i in items)
                {
                    rs.Add(IncludeObject(i, initialState));

                    // Extra work here to wire up relationship since we know it exists
                    CurrentKeyService()?.LinkChildInParentContainer(CurrentServiceScope, rs.ParentTypeName, parentFieldName, parent, i);
                }
            }

            return rs;
        }

        /// <summary>
        /// Returns a collection that tracks changes in entities, marking them with a specific initial entity state.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="initialState"></param>
        /// <param name="items"></param>
        /// <returns></returns>
        public static EntitySet<T> CreateList<T>(ObjectState initialState, params T[] items) where T : class, new()
        {
            var list = new EntitySet<T>(items);

            foreach (var i in list)
            {
                var iw = i.AsInfraWrapped();
                iw?.SetRowState(initialState);
            }

            return list;
        }

        /// <summary>
        /// Returns the ambient persistence and change tracking service either for a specific object, or globally.
        /// </summary>
        /// <param name="forObject"></param>
        /// <returns></returns>
        public static ICEFPersistenceHost CurrentPCTService(object? forObject = null)
        {
            lock (_lockPCT)
            {
                ICEFPersistenceHost? s = CurrentService<ICEFPersistenceHost>(forObject);

                if (s == null)
                {
                    s = Activator.CreateInstance(Globals.DefaultPCTServiceType) as ICEFPersistenceHost;
                    AddGlobalService(s!);
                }

                return s!;
            }
        }

        /// <summary>
        /// Returns the ambient key management service either for a specific object, or globally.
        /// </summary>
        /// <param name="forObject"></param>
        /// <returns></returns>
        public static ICEFKeyHost CurrentKeyService(object? forObject = null)
        {
            lock (_lockKey)
            {
                ICEFKeyHost? s = CurrentService<ICEFKeyHost>(forObject);

                if (s == null)
                {
                    s = Activator.CreateInstance(Globals.DefaultKeyServiceType) as ICEFKeyHost;
                    AddGlobalService(s!);
                }

                return s!;
            }
        }

        /// <summary>
        /// Returns the ambient audit service either for a specific object, or globally.
        /// </summary>
        /// <param name="forObject"></param>
        /// <returns></returns>
        public static ICEFAuditHost CurrentAuditService(object? forObject = null)
        {
            lock (_lockAudit)
            {
                ICEFAuditHost? s = CurrentService<ICEFAuditHost>(forObject);

                if (s == null)
                {
                    s = Activator.CreateInstance(Globals.DefaultAuditServiceType) as ICEFAuditHost;
                    AddGlobalService(s!);
                }

                return s!;
            }
        }

        /// <summary>
        /// Returns the ambient database service either for a specific object, or globally.
        /// </summary>
        /// <param name="forObject"></param>
        /// <returns></returns>
        public static ICEFDataHost CurrentDBService(object? forObject = null)
        {
            lock (_lockDB)
            {
                ICEFDataHost? s = CurrentService<ICEFDataHost>(forObject);

                if (s == null)
                {
                    s = Activator.CreateInstance(Globals.DefaultDBServiceType) as ICEFDataHost ?? throw new CEFInvalidStateException(InvalidStateType.BadAction, "DB Service type is incorrect.");
                    AddGlobalService(s);
                }

                return s;
            }
        }

        /// <summary>
        /// Returns the ambient validation service either for a specific object, or globally.
        /// </summary>
        /// <param name="forObject"></param>
        /// <returns></returns>
        public static ICEFValidationHost CurrentValidationService(object? forObject = null)
        {
            ICEFValidationHost? s = CurrentService<ICEFValidationHost>(forObject);

            if (s == null)
            {
                s = Activator.CreateInstance(Globals.DefaultValidationServiceType) as ICEFValidationHost;
                AddGlobalService(s!);
            }

            return s!;
        }

        /// <summary>
        /// Returns the ambient caching service either for a specific object, or globally.
        /// </summary>
        /// <param name="forObject"></param>
        /// <returns></returns>
        public static ICEFCachingHost? CurrentCacheService(object? forObject = null)
        {
            return CurrentService<ICEFCachingHost>(forObject);
        }

        #endregion

        #region "Internals"

        private static bool IsPersistable(Type t)
        {
            if (t.IsValueType)
                return true;

            if (t.Equals(typeof(string)))
                return true;

            if (t.IsPrimitive)
                return true;

            if (t.IsSerializable)
                return true;

            return false;
        }

        internal static List<string> GetToSaveProperties(Type t, bool persisted)
        {
            List<string>? list = null;

            if (persisted)
            {
                _toSavePropsPersist.TryGetValue(t, out list);
            }
            else
            {
                _toSaveProps.TryGetValue(t, out list);
            }

            if (list != null)
            {
                return list;
            }

            // Return all writeable CLR properties, key fields, FK role name fields
            list = (from a in t.GetProperties() where a.CanWrite && IsPersistable(a.PropertyType) select a.Name)
                .Union(_globalPropToSave)
                .Union(from cr in KeyService.ResolveKeyDefinitionForType(t) select cr)
                .Union((from cr in CurrentKeyService().GetRelationsForChild(t) select cr.ChildResolvedKey).SelectMany((p) => p))
                .Union(from cr in CurrentDBService().GetPropertyGroupFields(t) select cr)
                .ToList();

            // For non-persisted, we can look 1 level deeper to identify nested props as well - we iterate properties that belong to the same assembly as the root object
            if (!persisted)
            {
                list.AddRange((from a in t.GetProperties()
                               where a.PropertyType.Assembly == t.Assembly
                               select (from b in a.PropertyType.GetProperties()
                                       where b.CanWrite && IsPersistable(b.PropertyType)
                                       select b.Name)).SelectMany((p) => p));
            }

            var asvc = CurrentAuditService();

            if (asvc != null)
            {
                if (!string.IsNullOrEmpty(asvc.LastUpdatedByField) && !list.Contains(asvc.LastUpdatedByField!))
                {
                    list.Add(asvc.LastUpdatedByField!);
                }

                if (!string.IsNullOrEmpty(asvc.LastUpdatedDateField) && !list.Contains(asvc.LastUpdatedDateField!))
                {
                    list.Add(asvc.LastUpdatedDateField!);
                }
            }

            // Support idea of globally excluded properties (i.e. should never persist since might be things like EMailAddressPlain which is just a surrogate for encrypted EMailAddress)
            var sl = (from a in list where !RegisteredPropertyNameTreatReadOnly.Contains(a) select a).Distinct().OrderBy((p) => p).ToList();

            if (persisted)
            {
                _toSavePropsPersist[t] = sl;
            }
            else
            {
                _toSaveProps[t] = sl;
            }

            return sl;
        }

        internal static IDictionary<string, object?> GetFilteredProperties(Type t, IDictionary<string, object?> props, bool persisted)
        {
            Dictionary<string, object?> ret = new(props.Count);

            foreach (var pn in GetToSaveProperties(t, persisted))
            {
                if (props.TryGetValue(pn, out var pv))
                {
                    ret[pn] = pv;
                }
            }

            return ret;
        }

        private static Action<bool> InternalSubscribeAppEvents(Action<bool> subscriber)
        {
            lock (_appEventList)
            {
                _appEventList.Add(subscriber);
            }

            return subscriber;
        }

        private static void InternalUnsubscribeAppEvents(Action<bool> subscriber)
        {
            lock (_appEventList)
            {
                _appEventList.Remove(subscriber);
            }
        }

        /// <summary>
        /// Returns the service implementation for a specific type of service, either per object or globally, as available in the ambient service scope.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="forObject"></param>
        /// <returns></returns>
        private static T? CurrentService<T>(object? forObject = null) where T : class, ICEFService
        {
            var svc = CurrentServiceScope.GetService<T>(forObject);

            if (svc != null)
            {
                return svc;
            }

            if (_allServiceScopes.Value?.Count() > 0)
            {
                foreach (var ss in _allServiceScopes.Value.ToArray())
                {
                    svc = ss.GetService<T>(forObject);

                    if (svc != null)
                    {
                        return svc;
                    }
                }
            }

            return null;
        }

        internal static string? GetEntityPersistName<T>(object? tosave)
        {
            if (tosave is ICEFStorageNaming sn)
            {
                return sn.EntityPersistedName;
            }

            return null;
        }

        public static void RegisterPropertyNameTreatReadOnly(string propName)
        {
            _treatPropertyAsReadOnly.Add(propName);
        }

        internal static void RegisterForType<T>(ICEFService service)
        {
            _regServicesByType.TryGetValue(typeof(T), out IList<ICEFService>? existing);

            bool doadd = false;

            if (existing == null)
            {
                existing = [];
                doadd = true;
            }

            if (!(from a in existing where a.GetType().Equals(service.GetType()) select a).Any())
            {
                existing.Add(service);
            }

            if (doadd)
            {
                _regServicesByType[typeof(T)] = existing;
            }
        }

        private static void ConnScopeInit(ConnectionScope newcs)
        {
            var ss = CurrentServiceScope;

            var useLocal = ss.Settings.ConnectionScopePerThread.GetValueOrDefault(Globals.ConnectionScopePerThread);

            if (!useLocal)
            {
                ss.ConnScopeInit(newcs);
                return;
            }

            _allConnScopes.Value ??= [];

            if (_currentConnScope.Value != null)
            {
                _allConnScopes.Value = _allConnScopes.Value.Push(_currentConnScope.Value);
            }

            _currentConnScope.Value = newcs ?? throw new CEFInvalidStateException(InvalidStateType.ArgumentNull, nameof(newcs));

            var db = ss.GetService<DBService>();

            newcs.Disposing = () =>
            {
                // Not just service scopes but connection scopes should wait for all pending operations!
                db?.WaitOnCompletions();
            };

            newcs.Disposed = () =>
            {
                if (!_allConnScopes.Value.IsEmpty)
                {
                    try
                    {
                        _allConnScopes.Value = _allConnScopes.Value.Pop(out var cs);
                        _currentConnScope.Value = cs;
                    }
                    catch
                    {
                    }

                    return;
                }

                _currentConnScope.Value = null!;
            };
        }

        private static void ServiceScopeInit(ServiceScope newss, ICEFService[]? additionalServices)
        {
            _allServiceScopes.Value ??= [];

            if (_currentServiceScope.Value != null)
            {
                _allServiceScopes.Value = _allServiceScopes.Value.Push(_currentServiceScope.Value);
            }

            if (additionalServices != null)
            {
                foreach (var s in additionalServices)
                {
                    newss.AddLocalService(s);
                }
            }

            _currentServiceScope.Value = newss ?? throw new CEFInvalidStateException(InvalidStateType.ArgumentNull, nameof(newss));

            newss.Disposed = () =>
            {
                if (additionalServices != null)
                {
                    foreach (var s in additionalServices)
                    {
                        if (s is IDisposable disposable)
                        {
                            disposable.Dispose();
                        }
                    }
                }

                if (!_allServiceScopes.Value.IsEmpty)
                {
                    try
                    {
                        _allServiceScopes.Value = _allServiceScopes.Value.Pop(out var ss);
                        _currentServiceScope.Value = ss;
                    }
                    catch
                    {
                    }

                    return;
                }

                _currentServiceScope.Value = null!;
            };
        }

        private static void InternalDBAppendAll<T>(EntitySet<T> pop) where T : class, new()
        {
            DataAccessCallout?.Invoke("InternalDBAppendAll", pop.GetType().Name + "All");

            Exception? tex = null;
            var ss = CurrentServiceScope;
            var rim = ss.ResolvedRetrievalIdentityMode(pop);

            void a(CancellationToken ct, DateTime? start)
            {
                using var aesw = new AppEventSubscriberWrapper();

                foreach (var row in CurrentDBService().RetrieveAll<T>(rim))
                {
                    if (Globals.GlobalQueryTimeout.HasValue)
                    {
                        ct.ThrowIfCancellationRequested();

                        if (start.HasValue)
                        {
                            if (DateTime.UtcNow.Subtract(start.Value).TotalMilliseconds - aesw.GetSleptMs() >= Globals.GlobalQueryTimeout.Value)
                            {
                                throw new CEFTimeoutException($"The query failed to complete in the allowed time ({((Globals.GlobalQueryTimeout.Value) / 1000)} sec).");
                            }
                        }
                    }

                    pop.Add(row);
                }
            }

            tex = InternalRunQuery(ss, a);
        }

        private static Exception? InternalRunQuery(ServiceScope ss, Action<CancellationToken, DateTime?> a)
        {
            Exception? tex = null;

            if (Globals.GlobalQueryTimeout.HasValue)
            {
                CancellationTokenSource cts = new();
                DateTime start = DateTime.UtcNow;

                void b()
                {
                    try
                    {
                        using (UseServiceScope(ss))
                        {
                            a(cts.Token, start);
                        }
                    }
                    catch (Exception ex)
                    {
                        tex = ex;
                    }
                }

                if (Globals.QueriesUseDedicatedThreads)
                {
                    var th = new Thread(new ThreadStart(b));
                    th.Start();

                    if (!th.Join(Globals.GlobalQueryTimeout.Value))
                    {
                        cts.Cancel();

                        // We'll wait an additional second after a cancel request to see if thread stops naturally, otherwise we'll abort it
                        if (!th.Join(1000))
                        {
                            //th.Abort(); <- not well supported in .NET Core
                            throw new CEFTimeoutException($"The query failed to complete in the allowed time ({((Globals.GlobalQueryTimeout.Value + 1000) / 1000)} sec).");
                        }
                    }
                }
                else
                {
                    // We'll just use cooperative checks to wait for completion
                    CancellationToken ct = new();
                    a(ct, start);
                }

                if (tex != null)
                {
                    throw tex;
                }
            }
            else
            {
                // No timeout means we just wait as long as it takes! If we're using an RDBMS, its timeout may be in force and offer non-cooperative timeouts which is actually a good thing.
                CancellationToken ct = new();
                a(ct, null);
            }

            return tex;
        }

        private static void InternalDBAppendByKey<T>(EntitySet<T> pop, object[] key) where T : class, new()
        {
            var prevAddedIsNew = pop.AddedIsNew;

            try
            {
                DataAccessCallout?.Invoke("InternalDBAppendByKey", pop.GetType().Name + "Key");

                pop.AddedIsNew = false;

                Exception? tex = null;
                var ss = CurrentServiceScope;
                var rim = ss.ResolvedRetrievalIdentityMode(pop);

                void a(CancellationToken ct, DateTime? start)
                {
                    using var aesw = new AppEventSubscriberWrapper();

                    foreach (var row in CurrentDBService().RetrieveByKey<T>(rim, key))
                    {
                        if (Globals.GlobalQueryTimeout.HasValue)
                        {
                            ct.ThrowIfCancellationRequested();

                            if (start.HasValue)
                            {
                                if (DateTime.UtcNow.Subtract(start.Value).TotalMilliseconds - aesw.GetSleptMs() >= Globals.GlobalQueryTimeout.Value)
                                {
                                    throw new CEFTimeoutException($"The query failed to complete in the allowed time ({((Globals.GlobalQueryTimeout.Value) / 1000)} sec).");
                                }
                            }
                        }

                        pop.Add(row);
                    }
                };

                tex = InternalRunQuery(ss, a);
            }
            finally
            {
                pop.AddedIsNew = prevAddedIsNew;
            }
        }

        private static void InternalDBAppendByQuery<T>(EntitySet<T> pop, CommandType cmdType, string cmdText, ColumnDefinitionCallback? cc, object?[] parms) where T : class, new()
        {
            var prevAddedIsNew = pop.AddedIsNew;

            try
            {
                DataAccessCallout?.Invoke("InternalDBAppendByQuery", cmdText);

                pop.AddedIsNew = false;

                Exception? tex = null;
                var ss = CurrentServiceScope;
                var rim = ss.ResolvedRetrievalIdentityMode(pop);

                HashSet<KeyServiceState.CompositeWrapper>? keys = [];
                var keycols = KeyService.ResolveKeyDefinitionForType(typeof(T), rim);

                // Default is to check but can disable on case-by-case if needed
                if (ss.RetrieveAppendChecksExisting)
                {
                    if (keycols.Count > 0)
                    {
                        // Make record of all existing values in collection so can ignore these if already present
                        foreach (var er in pop)
                        {
                            var iw = er.AsInfraWrapped(false, ss);

                            if (iw != null)
                            {
                                var cw = new KeyServiceState.CompositeWrapper(typeof(T));

                                foreach (var kc in keycols)
                                {
                                    cw.Add(new KeyServiceState.CompositeItemWrapper(iw.GetValue(kc)));
                                }

                                keys.Add(cw);
                            }
                        }
                    }
                }

                void a(CancellationToken ct, DateTime? start)
                {
                    long tickstart = DateTime.UtcNow.Ticks;

                    using var aesw = new AppEventSubscriberWrapper();

                    foreach (var row in CurrentDBService().RetrieveByQuery<T>(rim, cmdType, cmdText, cc, parms))
                    {
                        if (Globals.GlobalQueryTimeout.HasValue)
                        {
                            ct.ThrowIfCancellationRequested();

                            if (start.HasValue)
                            {
                                if (DateTime.UtcNow.Subtract(start.Value).TotalMilliseconds - aesw.GetSleptMs() >= Globals.GlobalQueryTimeout.Value)
                                {
                                    throw new CEFTimeoutException($"The query failed to complete in the allowed time ({((Globals.GlobalQueryTimeout.Value) / 1000)} sec).");
                                }
                            }
                        }

                        // If row already exists in collection, do not add!
                        if (keycols.Count == 0 || keys.Count == 0)
                        {
                            pop.Add(row);
                        }
                        else
                        {
                            var cw = new KeyServiceState.CompositeWrapper(typeof(T));
                            var iw = row.AsInfraWrapped(false, ss);

                            if (iw != null)
                            {
                                foreach (var kc in keycols)
                                {
                                    cw.Add(new KeyServiceState.CompositeItemWrapper(iw.GetValue(kc)));
                                }

                                if (!keys.Contains(cw))
                                {
                                    pop.Add(row);
                                }
                            }
                            else
                            {
                                pop.Add(row);
                            }
                        }
                    }

                    _queryPerfInfo?.Invoke(cmdText, DateTime.UtcNow.Ticks - tickstart);
                }

                tex = InternalRunQuery(ss, a);
            }
            finally
            {
                pop.AddedIsNew = prevAddedIsNew;
            }
        }

        #endregion

        #region "Internal classes"

        private class AppEventSubscriberWrapper : IDisposable
        {
            private readonly Action<bool> _handler;
            private DateTime? _lastSleep;
            private double _sleptTime = 0;

            public double GetSleptMs()
            {
                if (_lastSleep.HasValue)
                {
                    return DateTime.UtcNow.Subtract(_lastSleep.Value).TotalMilliseconds;
                }

                return _sleptTime;
            }

            public AppEventSubscriberWrapper()
            {
                _handler = InternalSubscribeAppEvents((s) =>
                {
                    if (s)
                    {
                        if (!_lastSleep.HasValue)
                        {
                            _lastSleep = DateTime.UtcNow;
                        }
                    }
                    else
                    {
                        if (_lastSleep.HasValue)
                        {
                            _sleptTime += DateTime.UtcNow.Subtract(_lastSleep.Value).TotalMilliseconds;
                            _lastSleep = null;
                        }
                    }
                });
            }

            public void Dispose()
            {
                InternalUnsubscribeAppEvents(_handler);
            }
        }

        #endregion
    }
}
