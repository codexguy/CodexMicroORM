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
using System.Linq;
using CodexMicroORM.Core.Services;
using System.Data;
using System.Collections.Immutable;

namespace CodexMicroORM.Core
{
    /// <summary>
    /// CEF (CodeX Entity Framework) offers basic functionality of the framework, generally through static methods. (This is in addition to extension methods found in the Extensions.cs file.)
    /// </summary>
    public static class CEF
    {
        #region "Private state (global)"

        private static ConcurrentDictionary<Type, (bool resolved, IList<ICEFService> list)> _defaultServicesByType = new ConcurrentDictionary<Type, (bool, IList<ICEFService>)>();
        private static ImmutableArray<ICEFService> _globalServices = ImmutableArray<ICEFService>.Empty;

        [ThreadStatic]
        private static Stack<ServiceScope> _allServiceScopes = new Stack<ServiceScope>();

        [ThreadStatic]
        private static ServiceScope _currentServiceScope = null;

        [ThreadStatic]
        private static Stack<ConnectionScope> _allConnScopes = new Stack<ConnectionScope>();

        [ThreadStatic]
        private static ConnectionScope _currentConnScope = null;

        public static ICollection<ICEFService> GlobalServices => _globalServices.ToArray();

        internal static ConcurrentDictionary<Type, (bool resolved, IList<ICEFService> list)> DefaultServicesByType => _defaultServicesByType;

        #endregion

        #region "Public methods"

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
        /// Creates a new connection scope that is transactional.
        /// </summary>
        /// <param name="settings"></param>
        /// <returns></returns>
        public static ConnectionScope NewTransactionScope(ConnectionScopeSettings settings = null)
        {
            if (settings == null)
            {
                settings = new ConnectionScopeSettings();
            }

            settings.IsTransactional = true;

            return NewConnectionScope(settings);
        }

        /// <summary>
        /// Creates a new connection scope (may or may not be transactional depending on settings).
        /// </summary>
        /// <param name="settings"></param>
        /// <returns></returns>
        public static ConnectionScope NewConnectionScope(ConnectionScopeSettings settings = null)
        {
            if (settings == null)
            {
                settings = new ConnectionScopeSettings();
            }

            var mode = settings.ScopeMode.GetValueOrDefault(Globals.DefaultConnectionScopeMode);

            if (mode == ScopeMode.CreateNew || _currentConnScope == null)
            {
                var cs = new ConnectionScope(settings.IsTransactional.GetValueOrDefault(Globals.UseTransactionsForNewScopes), settings.ConnectionStringOverride);
                ConnScopeInit(cs, mode);
            }

            return _currentConnScope;
        }

        /// <summary>
        /// Gets the ambient connection scope.
        /// </summary>
        public static ConnectionScope CurrentConnectionScope
        {
            get
            {
                if (_currentConnScope == null)
                {
                    var cs = new ConnectionScope(Globals.DefaultTransactionalStandalone);
                    cs.IsStandalone = true;
                    ConnScopeInit(cs, Globals.DefaultConnectionScopeMode);
                }

                return _currentConnScope;
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
            ServiceScopeInit(new ServiceScope(toUse ?? throw new ArgumentNullException("toUse")), null);
            return _currentServiceScope;
        }

        /// <summary>
        /// If there's an ambient service scope, returns it, otherwise creates a new service scope and returns it.
        /// </summary>
        /// <param name="settings"></param>
        /// <param name="additionalServices"></param>
        /// <returns></returns>
        public static ServiceScope NewOrCurrentServiceScope(ServiceScopeSettings settings, params ICEFService[] additionalServices)
        {
            if (_currentServiceScope != null)
            {
                ServiceScopeInit(new ServiceScope(_currentServiceScope), null);
                return _currentServiceScope;
            }

            ServiceScopeInit(new ServiceScope(settings), additionalServices);
            return _currentServiceScope;
        }

        /// <summary>
        /// If there's an ambient service scope, returns it, otherwise creates a new service scope and returns it.
        /// </summary>
        /// <param name="additionalServices"></param>
        /// <returns></returns>
        public static ServiceScope NewOrCurrentServiceScope(params ICEFService[] additionalServices)
        {
            if (_currentServiceScope != null)
            {
                ServiceScopeInit(new ServiceScope(_currentServiceScope), null);
                return _currentServiceScope;
            }

            ServiceScopeInit(new ServiceScope(new ServiceScopeSettings()), additionalServices);
            return _currentServiceScope;
        }

        /// <summary>
        /// If there's an ambient service scope, returns it, otherwise creates a new service scope and returns it.
        /// </summary>
        /// <param name="settings"></param>
        /// <returns></returns>
        public static ServiceScope NewOrCurrentServiceScope(ServiceScopeSettings settings = null)
        {
            if (_currentServiceScope != null)
            {
                ServiceScopeInit(new ServiceScope(_currentServiceScope), null);
                return _currentServiceScope;
            }

            ServiceScopeInit(new ServiceScope(settings ?? new ServiceScopeSettings()), null);
            return _currentServiceScope;
        }

        /// <summary>
        /// Creates a new service scope, makes it the ambient scope and returns it.
        /// </summary>
        /// <param name="settings"></param>
        /// <param name="additionalServices"></param>
        /// <returns></returns>
        public static ServiceScope NewServiceScope(ServiceScopeSettings settings, params ICEFService[] additionalServices)
        {
            ServiceScopeInit(new ServiceScope(settings), additionalServices);
            return _currentServiceScope;
        }

        /// <summary>
        /// Creates a new service scope, makes it the ambient scope and returns it.
        /// </summary>
        /// <param name="additionalServices"></param>
        /// <returns></returns>
        public static ServiceScope NewServiceScope(params ICEFService[] additionalServices)
        {
            ServiceScopeInit(new ServiceScope(new ServiceScopeSettings()), additionalServices);
            return _currentServiceScope;
        }

        /// <summary>
        /// Creates a new service scope, makes it the ambient scope and returns it.
        /// </summary>
        /// <param name="settings"></param>
        /// <returns></returns>
        public static ServiceScope NewServiceScope(ServiceScopeSettings settings = null)
        {
            ServiceScopeInit(new ServiceScope(settings ?? new ServiceScopeSettings()), null);
            return _currentServiceScope;
        }

        /// <summary>
        /// Gets the ambient service scope.
        /// </summary>
        public static ServiceScope CurrentServiceScope
        {
            get
            {
                if (_currentServiceScope == null)
                {
                    ServiceScopeInit(new ServiceScope(new ServiceScopeSettings()), null);
                }

                return _currentServiceScope;
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

        /// <summary>
        /// Requests database persistence over all entities in the ambient service scope.
        /// </summary>
        /// <param name="settings"></param>
        /// <returns>One element per entity saved, indicating any message and/or status returned by the save process for that entity.</returns>
        public static IEnumerable<(object item, string message, int status)> DBSave(DBSaveSettings settings = null)
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
            var settings = new DBSaveSettings();
            settings.RootObject = tosave;
            settings.IncludeRootChildren = allRelated;
            settings.IncludeRootParents = allRelated;

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
        public static T DBSave<T>(this T tosave, DBSaveSettings settings = null) where T : class, new()
        {
            if (settings == null)
            {
                settings = new DBSaveSettings();
            }
            settings.RootObject = tosave;

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
        public static (ValidationErrorCode error, string message) DBSaveWithMessage<T>(this T tosave, DBSaveSettings settings = null) where T : class, new()
        {
            if (settings == null)
            {
                settings = new DBSaveSettings();
            }
            settings.RootObject = tosave;

            var res = CurrentServiceScope.DBSave(settings);

            if (res.Any())
            {
                return ((ValidationErrorCode)(-res.First().status), res.First().message);
            }

            return (ValidationErrorCode.None, null);
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
        public static EntitySet<T> DBRetrieveByQuery<T>(this EntitySet<T> pop, CommandType cmdType, string cmdText, params object[] parms) where T : class, new()
        {
            if (pop.Any())
            {
                pop.BeginInit();
                pop.Clear();
                pop.EndInit();
            }

            InternalDBAppendByQuery(pop, cmdType, cmdText, parms);
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
        public static EntitySet<T> DBAppendByQuery<T>(this EntitySet<T> pop, CommandType cmdType, string cmdText, params object[] parms) where T : class, new()
        {
            InternalDBAppendByQuery(pop, cmdType, cmdText, parms);
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
                pop.BeginInit();
                pop.Clear();
                pop.EndInit();
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
                pop.BeginInit();
                pop.Clear();
                pop.EndInit();
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
        public static T NewObject<T>(T initial = null) where T : class, new()
        {
            return CurrentServiceScope.NewObject<T>(initial);
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
            return CurrentServiceScope.IncludeObject<T>(toAdd, drs, null);
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
            return new EntitySet<T>(items);
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
            var rs = new EntitySet<T>();
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
            var rs = new EntitySet<T>();

            rs.ParentContainer = parent ?? throw new ArgumentNullException("parent");
            rs.ParentTypeName = parent.GetBaseType().Name;
            rs.ParentFieldName = parentFieldName ?? throw new ArgumentNullException("parentFieldName");

            if (items?.Length > 0)
            {
                foreach (var i in items)
                {
                    rs.Add(CEF.IncludeObject(i, initialState));

                    // Extra work here to wire up relationship since we know it exists
                    CurrentKeyService()?.LinkChildInParentContainer(CEF.CurrentServiceScope, rs.ParentTypeName, parentFieldName, parent, i);
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

                if (iw != null)
                {
                    iw.SetRowState(initialState);
                }
            }

            return list;
        }

        /// <summary>
        /// Returns the ambient persistence and change tracking service either for a specific object, or globally.
        /// </summary>
        /// <param name="forObject"></param>
        /// <returns></returns>
        public static ICEFPersistenceHost CurrentPCTService(object forObject = null)
        {
            var s = CurrentService<ICEFPersistenceHost>(forObject);

            if (s == null)
            {
                s = Activator.CreateInstance(Globals.DefaultPCTServiceType) as ICEFPersistenceHost;
                AddGlobalService(s);
            }

            return s;
        }

        /// <summary>
        /// Returns the ambient key management service either for a specific object, or globally.
        /// </summary>
        /// <param name="forObject"></param>
        /// <returns></returns>
        public static ICEFKeyHost CurrentKeyService(object forObject = null)
        {
            var s = CurrentService<ICEFKeyHost>(forObject);

            if (s == null)
            {
                s = Activator.CreateInstance(Globals.DefaultKeyServiceType) as ICEFKeyHost;
                AddGlobalService(s);
            }

            return s;
        }

        /// <summary>
        /// Returns the ambient audit service either for a specific object, or globally.
        /// </summary>
        /// <param name="forObject"></param>
        /// <returns></returns>
        public static ICEFAuditHost CurrentAuditService(object forObject = null)
        {
            var s = CurrentService<ICEFAuditHost>(forObject);

            if (s == null)
            {
                s = Activator.CreateInstance(Globals.DefaultAuditServiceType) as ICEFAuditHost;
                AddGlobalService(s);
            }

            return s;
        }

        /// <summary>
        /// Returns the ambient database service either for a specific object, or globally.
        /// </summary>
        /// <param name="forObject"></param>
        /// <returns></returns>
        public static ICEFDataHost CurrentDBService(object forObject = null)
        {
            var s = CurrentService<ICEFDataHost>(forObject);

            if (s == null)
            {
                s = Activator.CreateInstance(Globals.DefaultDBServiceType) as ICEFDataHost;
                AddGlobalService(s);
            }

            return s;
        }

        /// <summary>
        /// Returns the ambient validation service either for a specific object, or globally.
        /// </summary>
        /// <param name="forObject"></param>
        /// <returns></returns>
        public static ICEFValidationHost CurrentValidationService(object forObject = null)
        {
            var s = CurrentService<ICEFValidationHost>(forObject);

            if (s == null)
            {
                s = Activator.CreateInstance(Globals.DefaultValidationServiceType) as ICEFValidationHost;
                AddGlobalService(s);
            }

            return s;
        }

        /// <summary>
        /// Returns the ambient caching service either for a specific object, or globally.
        /// </summary>
        /// <param name="forObject"></param>
        /// <returns></returns>
        public static ICEFCachingHost CurrentCacheService(object forObject = null)
        {
            return CurrentService<ICEFCachingHost>(forObject);
        }

        #endregion

        #region "Internals"

        /// <summary>
        /// Returns the service implementation for a specific type of service, either per object or globally, as available in the ambient service scope.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="forObject"></param>
        /// <returns></returns>
        private static T CurrentService<T>(object forObject = null) where T : class, ICEFService
        {
            var svc = CurrentServiceScope.GetService<T>(forObject);

            if (svc != null)
            {
                return svc;
            }

            if (_allServiceScopes != null)
            {
                foreach (var ss in _allServiceScopes)
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

        internal static void RegisterForType<T>(ICEFService service)
        {
            (bool resolved, IList<ICEFService> list) existing;

            _defaultServicesByType.TryGetValue(typeof(T), out existing);

            bool doadd = false;

            if (existing.list == null)
            {
                existing.list = new List<ICEFService>();
                doadd = true;
            }

            if (!(from a in existing.list where a.GetType().Equals(service.GetType()) select a).Any())
            {
                existing.list.Add(service);
            }

            if (doadd)
            {
                _defaultServicesByType[typeof(T)] = existing;
            }
        }

        private static void ConnScopeInit(ConnectionScope newcs, ScopeMode mode)
        {
            if (_currentConnScope != null)
            {
                if (_allConnScopes == null)
                {
                    _allConnScopes = new Stack<ConnectionScope>();
                }

                _allConnScopes.Push(_currentConnScope);
            }

            _currentConnScope = newcs ?? throw new ArgumentNullException("newcs");

            newcs.Disposing = () =>
            {
                // Not just service scopes but connection scopes should wait for all pending operations!
                CEF.CurrentDBService()?.WaitOnCompletions();
            };

            newcs.Disposed = () =>
            {
                if (_allConnScopes?.Count > 0)
                {
                    do
                    {
                        var cs = _allConnScopes.Pop();

                        if (cs != _currentConnScope)
                        {
                            _currentConnScope = cs;
                            return;
                        }
                    } while (_allConnScopes.Count > 0);
                }

                _currentConnScope = null;
            };
        }

        private static void ServiceScopeInit(ServiceScope newss, ICEFService[] additionalServices)
        {
            if (_currentServiceScope != null)
            {
                if (_allServiceScopes == null)
                {
                    _allServiceScopes = new Stack<ServiceScope>();
                }

                _allServiceScopes.Push(_currentServiceScope);
            }

            if (additionalServices != null)
            {
                foreach (var s in additionalServices)
                {
                    newss.AddLocalService(s);
                }
            }

            _currentServiceScope = newss ?? throw new ArgumentNullException("newss");

            newss.Disposed = () =>
            {
                if (additionalServices != null)
                {
                    foreach (var s in additionalServices)
                    {
                        if (s is IDisposable)
                        {
                            ((IDisposable)s).Dispose();
                        }
                    }
                }

                if (_allServiceScopes?.Count > 0)
                {
                    do
                    {
                        var ts = _allServiceScopes.Pop();

                        if (ts != _currentServiceScope)
                        {
                            _currentServiceScope = ts;
                            return;
                        }
                    } while (_allServiceScopes.Count > 0);
                }

                _currentServiceScope = null;
            };
        }

        private static void InternalDBAppendAll<T>(EntitySet<T> pop) where T : class, new()
        {
            pop.BeginInit();

            foreach (var row in CurrentDBService().RetrieveAll<T>())
            {
                pop.Add(row);
            }

            pop.EndInit();
        }

        private static void InternalDBAppendByKey<T>(EntitySet<T> pop, object[] key) where T : class, new()
        {
            pop.BeginInit();

            foreach (var row in CurrentDBService().RetrieveByKey<T>(key))
            {
                pop.Add(row);
            }

            pop.EndInit();
        }

        private static void InternalDBAppendByQuery<T>(EntitySet<T> pop, CommandType cmdType, string cmdText, object[] parms) where T : class, new()
        {
            pop.BeginInit();

            foreach (var row in CurrentDBService().RetrieveByQuery<T>(cmdType, cmdText, parms))
            {
                pop.Add(row);
            }

            pop.EndInit();
        }

        #endregion
    }
}
