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

namespace CodexMicroORM.Core
{
    /// <summary>
    /// CEF (CodeX Entity Framework) offers basic functionality of the framework, generally through static methods. (This is in addition to extension methods found in the Extensions.cs file.)
    /// </summary>
    public static class CEF
    {
        #region "Private state (global)"

        internal static ConcurrentDictionary<Type, (bool resolved, IList<ICEFService> list)> _defaultServicesByType = new ConcurrentDictionary<Type, (bool, IList<ICEFService>)>();
        internal static ConcurrentBag<ICEFService> _globalServices = new ConcurrentBag<ICEFService>();

        [ThreadStatic]
        private static Stack<ServiceScope> _allServiceScopes = new Stack<ServiceScope>();

        [ThreadStatic]
        private static ServiceScope _currentServiceScope = null;

        [ThreadStatic]
        private static Stack<ConnectionScope> _allConnScopes = new Stack<ConnectionScope>();

        [ThreadStatic]
        private static ConnectionScope _currentConnScope = null;

        #endregion

        public static ICollection<ICEFService> GlobalServices => _globalServices.ToArray();

        public static void AddGlobalService(ICEFService srv)
        {
            _globalServices.Add(srv);
        }

        #region "Public methods"

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

        public static ServiceScope UseServiceScope(ServiceScope toUse)
        {
            // This is a special type of service scope - we create a shallow copy and flag it as not allowing destruction of contents when disposed
            ServiceScopeInit(new ServiceScope(toUse ?? throw new ArgumentNullException("toUse")));
            return _currentServiceScope;
        }

        public static ServiceScope NewServiceScope(ServiceScopeSettings settings = null)
        {
            ServiceScopeInit(new ServiceScope(settings ?? new ServiceScopeSettings()));
            return _currentServiceScope;
        }

        public static ServiceScope CurrentServiceScope
        {
            get
            {
                if (_currentServiceScope == null)
                {
                    ServiceScopeInit(new ServiceScope(new ServiceScopeSettings()));
                }

                return _currentServiceScope;
            }
        }

        public static IEnumerable<(object item, string message, int status)> DBSave(DBSaveSettings settings = null)
        {
            return CurrentServiceScope.DBSave(settings);
        }

        public static T DBSave<T>(this T tosave, DBSaveSettings settings = null) where T : ICEFWrapper
        {
            if (settings == null)
            {
                settings = new DBSaveSettings();
            }
            settings.RootObject = tosave;

            CurrentServiceScope.DBSave(settings);
            return tosave;
        }

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

        public static EntitySet<T> DBAppendByQuery<T>(this EntitySet<T> pop, CommandType cmdType, string cmdText, params object[] parms) where T : class, new()
        {
            InternalDBAppendByQuery(pop, cmdType, cmdText, parms);
            return pop;
        }

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

        public static EntitySet<T> DBAppendAll<T>(this EntitySet<T> pop) where T : class, new()
        {
            InternalDBAppendAll(pop);
            return pop;
        }

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

        public static EntitySet<T> DBAppendByKey<T>(this EntitySet<T> pop, params object[] key) where T : class, new()
        {
            InternalDBAppendByKey(pop, key);
            return pop;
        }

        public static T NewObject<T>(T initial = null) where T : class, new()
        {
            return CurrentServiceScope.NewObject<T>(initial);
        }

        public static T IncludeObject<T>(T toAdd, DataRowState? drs = null) where T : class, new()
        {
            return CurrentServiceScope.IncludeObject<T>(toAdd, drs);
        }

        public static void DeleteObject(object obj, DeleteCascadeAction action = DeleteCascadeAction.Cascade)
        {
            CurrentServiceScope.Delete(obj, action);
        }

        public static EntitySet<T> CreateList<T>(params T[] items) where T : class, new()
        {
            return new EntitySet<T>(items);
        }

        public static EntitySet<T> CreateList<T>(object parent, string parentFieldName) where T : class, new()
        {
            var rs = new EntitySet<T>();
            rs.ParentContainer = parent;
            rs.ParentTypeName = parent.GetBaseType().Name;
            rs.ParentFieldName = parentFieldName;
            return rs;
        }

        public static EntitySet<T> CreateList<T>(object parent, string parentFieldName, DataRowState initialState, params T[] items) where T : class, new()
        {
            var rs = new EntitySet<T>(items);
            rs.ParentContainer = parent ?? throw new ArgumentNullException("parent");
            rs.ParentTypeName = parent.GetBaseType().Name;
            rs.ParentFieldName = parentFieldName ?? throw new ArgumentNullException("parentFieldName");

            foreach (var i in rs)
            {
                var iw = i.AsInfraWrapped();

                if (iw != null)
                {
                    iw.SetRowState(initialState);
                }

                // Extra work here to wire up relationship since we know it exists
                KeyService.LinkChildInParentContainer(CEF.CurrentServiceScope, rs.ParentTypeName, parentFieldName, parent, iw.AsUnwrapped());
            }

            return rs;
        }

        public static EntitySet<T> CreateList<T>(DataRowState initialState, params T[] items) where T : class, new()
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

        #endregion

        #region "Internals"

        private static T CurrentService<T>(object forObject) where T : ICEFService
        {
            return CurrentServiceScope.GetService<T>(forObject);
        }

        public static DBService CurrentDBService(object forObject = null)
        {
            return CurrentService<DBService>(forObject);
        }

        internal static void Register<T>(ICEFService service)
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

        private static void ServiceScopeInit(ServiceScope newss)
        {
            if (_currentServiceScope != null)
            {
                if (_allServiceScopes == null)
                {
                    _allServiceScopes = new Stack<ServiceScope>();
                }

                _allServiceScopes.Push(_currentServiceScope);
            }

            _currentServiceScope = newss ?? throw new ArgumentNullException("newss");

            newss.Disposed = () =>
            {
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

            var firstIdx = pop.Count();

            foreach (var row in CurrentDBService().RetrieveAll<T>())
            {
                pop.Add(row);
            }

            pop.EndInit();
        }

        private static void InternalDBAppendByKey<T>(EntitySet<T> pop, object[] key) where T : class, new()
        {
            pop.BeginInit();

            var firstIdx = pop.Count();

            foreach (var row in CurrentDBService().RetrieveByKey<T>(key))
            {
                pop.Add(row);
            }

            pop.EndInit();
        }

        private static void InternalDBAppendByQuery<T>(EntitySet<T> pop, CommandType cmdType, string cmdText, object[] parms) where T : class, new()
        {
            pop.BeginInit();

            var firstIdx = pop.Count();

            foreach (var row in CurrentDBService().RetrieveByQuery<T>(cmdType, cmdText, parms))
            {
                pop.Add(row);
            }

            pop.EndInit();
        }

        #endregion
    }
}
