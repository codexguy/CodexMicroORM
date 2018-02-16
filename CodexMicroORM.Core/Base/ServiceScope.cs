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
using CodexMicroORM.Core.Collections;
using System.ComponentModel;
using System.Data;
using System.Reflection;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using System.Text;
using Newtonsoft.Json;
using System.IO;
using System.Threading.Tasks;

namespace CodexMicroORM.Core
{
    /// <summary>
    /// Service scopes are managed collections of objects that are tracked for changes such that saving these changes to a database (among other operations) is possible.
    /// Mapping of object to relational and relational to object properties is controlled by configuration that's (ideally) established (once) on appdomain startup.
    /// Managed objects can use services, and the scope is responsible for tracking the use of these services.
    /// Scopes are typically thread-bound, where inter-thread/process use of scope data would be typically carried out via a presisting service.
    /// </summary>
    public sealed class ServiceScope : IDisposable
    {
        #region "Tracked Object"

        public class TrackedObject : ICEFIndexedListItem
        {
            public string BaseName { get; set; }
            public Type BaseType { get; set; }
            public CEFWeakReference<object> Target { get; set; }
            public CEFWeakReference<ICEFWrapper> Wrapper { get; set; }
            public ICEFInfraWrapper Infra { get; set; }
            public List<ICEFService> Services { get; set; }
            
            public bool ValidTarget
            {
                get
                {
                    return (Target?.IsAlive).GetValueOrDefault();
                }
            }

            public object GetTarget()
            {
                if ((Target?.IsAlive).GetValueOrDefault() && Target.Target != null)
                    return Target.Target;

                return null;
            }

            public ICEFInfraWrapper GetInfra()
            {
                if ((Target?.IsAlive).GetValueOrDefault())
                {
                    return Infra;
                }

                return null;
            }

            public INotifyPropertyChanged GetNotifyFriendly()
            {
                var test1 = GetTarget() as INotifyPropertyChanged;
                if (test1 != null)
                    return test1;

                var test2 = GetWrapper() as INotifyPropertyChanged;
                if (test2 != null)
                    return test2;

                return GetInfra() as INotifyPropertyChanged;
            }

            public ICEFInfraWrapper GetCreateInfra()
            {
                var infra = GetInfra();

                if (infra != null)
                    return infra;

                // Must succeed so create an infra wrapper!
                var wt = GetWrapperTarget();

                if (wt == null)
                    throw new CEFInvalidOperationException("Failed to identify target object to create infrastructure wrapper for.");

                Infra = WrappingHelper.CreateInfraWrapper(WrappingSupport.All, WrappingAction.Dynamic, false, wt, null, null, null);
                return Infra;
            }

            public ICEFWrapper GetWrapper()
            {
                if ((Wrapper?.IsAlive).GetValueOrDefault() && Wrapper.Target != null)
                    return Wrapper.Target as ICEFWrapper;

                return null;
            }

            public object GetInfraWrapperTarget()
            {
                return GetInfra() ?? GetWrapper() ?? GetTarget();
            }

            public object GetWrapperTarget()
            {
                return GetWrapper() ?? GetTarget();
            }

            public object GetValue(string propName, bool unwrap)
            {
                switch (propName)
                {
                    case nameof(BaseName):
                        return BaseName;

                    case nameof(BaseType):
                        return BaseType;

                    case nameof(Target):
                        if (unwrap)
                            return Target.IsAlive ? Target.Target : null;
                        else
                            return Target;

                    case nameof(Wrapper):
                        if (unwrap)
                            return Wrapper.IsAlive ? Wrapper.Target : null;
                        else
                            return Wrapper;

                    case nameof(Infra):
                        return Infra;

                    case nameof(Services):
                        return Services;
                }
                throw new NotSupportedException("Unsupported property name.");
            }

            public bool IsAlive
            {
                get
                {
                    return !((!(Target?.IsAlive).GetValueOrDefault() || Target.Target == null)
                        && (!(Wrapper?.IsAlive).GetValueOrDefault() || Wrapper.Target == null));
                }
            }
        }

        #endregion

        #region "Private state"

        // Global config...
        private static ConcurrentDictionary<Type, CacheBehavior> _cacheBehaviorByType = new ConcurrentDictionary<Type, CacheBehavior>();
        private static ConcurrentDictionary<Type, int> _cacheDurByType = new ConcurrentDictionary<Type, int>();

        // Tracks all objects in this scope - and their services.
        private ConcurrentIndexedList<TrackedObject> _scopeObjects = new ConcurrentIndexedList<TrackedObject>(nameof(TrackedObject.Target), nameof(TrackedObject.Wrapper));

        // Optional state maintained at scope level, per service type
        private ConcurrentDictionary<Type, ICEFServiceObjState> _serviceState = new ConcurrentDictionary<Type, ICEFServiceObjState>();

        // Known/resolved services available in this scope - when asking for non-object-specific services, this offers a fast(er) way to determine
        private ConcurrentBag<ICEFService> _scopeServices = new ConcurrentBag<ICEFService>();

        private ServiceScopeSettings _settings = new ServiceScopeSettings();

        private ConcurrentBag<ICEFService> _localServices = new ConcurrentBag<ICEFService>();

        #endregion

        #region "Constructors"

        internal ServiceScope(ServiceScopeSettings settings)
        {
            _settings = settings;
        }

        internal ServiceScope(ServiceScope template)
        {
            _scopeObjects = template._scopeObjects;
            _serviceState = template._serviceState;
            _scopeServices = template._scopeServices;
            _localServices = template._localServices;

            // Settings need to be deep copied
            _settings = new ServiceScopeSettings()
            {
                InitializeNullCollections = template.Settings.InitializeNullCollections,
                CacheBehavior = template.Settings.CacheBehavior,
                GlobalCacheDuration = template.Settings.GlobalCacheDuration,
                MergeBehavior = template.Settings.MergeBehavior,
                SerializationMode = template.Settings.SerializationMode,
                GetLastUpdatedBy = template.Settings.GetLastUpdatedBy,
                UseAsyncSave = template.Settings.UseAsyncSave,
                AsyncCacheUpdates = template.Settings.AsyncCacheUpdates
            };

            // Special case - this as a shallow copy cannot dispose state!
            _settings.CanDispose = false;
        }

        #endregion

        #region "Public methods"

        public static void SetCacheBehavior<T>(CacheBehavior cb)
        {
            _cacheBehaviorByType[typeof(T)] = cb;
        }

        public static void SetCacheSeconds<T>(int seconds)
        {
            _cacheDurByType[typeof(T)] = seconds;
        }

        public ICEFWrapper GetWrapperFor(object o)
        {
            if (o == null)
                return null;

            if (o is ICEFWrapper)
                return (ICEFWrapper)o;

            return _scopeObjects.GetFirstByName(nameof(TrackedObject.Target), o.AsUnwrapped())?.GetWrapper();
        }

        public T IncludeObjectWithType<T>(T toAdd, ObjectState? drs = null, Dictionary<string, (object value, Type type)> props = null) where T : class, new()
        {
            Dictionary<string, object> propVals = null;
            Dictionary<string, Type> propTypes = null;

            if (props?.Count > 0)
            {
                propVals = new Dictionary<string, object>();
                propTypes = new Dictionary<string, Type>();

                foreach (var kvp in props)
                {
                    propVals[kvp.Key] = kvp.Value.value;
                    propTypes[kvp.Key] = kvp.Value.type;
                }
            }

            return InternalCreateAdd(toAdd, drs.GetValueOrDefault(ObjectState.Unchanged) == ObjectState.Added ? true : false, drs, propVals, propTypes);
        }

        public T IncludeObject<T>(T toAdd, ObjectState? drs = null, Dictionary<string, object> props = null) where T : class, new()
        {
            return InternalCreateAdd(toAdd, drs.GetValueOrDefault(ObjectState.Unchanged) == ObjectState.Added ? true : false, drs, props, null);
        }

        public T GetService<T>(object forObject = null) where T :ICEFService
        {
            ICEFService service = null;

            if (forObject != null)
            {
                forObject = forObject.AsUnwrapped();

                var to = _scopeObjects.GetFirstByName(nameof(TrackedObject.Target), forObject);

                if (to != null && to.Services != null)
                {
                    service = (from a in to.Services where a is T select a).FirstOrDefault();

                    if (service != null)
                    {
                        return (T)service;
                    }
                }
            }

            service = (from a in _scopeServices where a is T select a).FirstOrDefault();

            if (service != null)
            {
                return (T)service;
            }

            // As a last resort, check local and global services
            service = (from a in _localServices where a is T select a).FirstOrDefault();

            if (service == null)
            {
                service = (from a in CEF.GlobalServices where a is T select a).FirstOrDefault();
            }

            if (service != null)
            {
                _scopeServices.Add(service);
            }

            return (T)service;
        }

        public CacheBehavior ResolvedCacheBehaviorForType(Type t)
        {
            if (_cacheBehaviorByType.TryGetValue(t, out CacheBehavior cb))
            {
                return cb;
            }

            return Settings.CacheBehavior.GetValueOrDefault(Globals.DefaultCacheBehavior);
        }

        public int ResolvedCacheDurationForType(Type t)
        {
            if (_cacheDurByType.TryGetValue(t, out int dur))
            {
                return dur;
            }

            return Settings.GlobalCacheDuration.GetValueOrDefault(Globals.DefaultGlobalCacheIntervalSeconds);
        }

        public void AddLocalService(ICEFService service)
        {
            _localServices.Add(service);
        }

        public IList<(object item, string message, int status)> DBSave(DBSaveSettings settings = null)
        {
            var ss = CEF.CurrentServiceScope;

            if (settings == null)
            {
                settings = new DBSaveSettings();
            }

            List<(object item, string message, int status)> retVal = new List<(object item, string message, int status)>();

            // Identify a matching dbservice in scope - that will do the heavy lifting, but we know about the objects in question here in the scope, so DBService expects us to present both a top-down and bottom-up presentation of objects to account for insert/update and deletes
            // We leverage infra if present (should be!)
            var db = GetService<ICEFDataHost>(settings.RootObject) ?? throw new CEFInvalidOperationException("Could not find an available DBService to save with.");

            var useAsync = settings.UseAsyncSave.GetValueOrDefault(this.Settings.UseAsyncSave.GetValueOrDefault(Globals.UseAsyncSave));

            Action<object> act = (state) =>
            {
                var parm = ((DBSaveSettings settings, ServiceScope ss))state;

                using (CEF.UseServiceScope(parm.ss))
                {
                    try
                    {
                        var filterRows = GetFilterRows(parm.settings);

                        // Go through scope, looking for tracked obj which do not implement INotifyPropertyChanged but do have an infra wrapper, to the infra wrapper - can change row states due to this, go through and update row states, if needed
                        ReconcileModifiedState(filterRows);

                        var saveList = GetSaveables(filterRows, parm.settings);

                        if (saveList.Any())
                        {
                            var valsvc = CEF.CurrentValidationService();
                            var vcheck = settings.ValidationChecksOnSave.GetValueOrDefault(Globals.ValidationChecksOnSave);

                            // Perform validations - we use whatever list is requested (can be "none")
                            if (vcheck != ValidationErrorCode.None && valsvc != null)
                            {
                                List<(int error, string message, ICEFInfraWrapper row)> fails = new List<(int error, string message, ICEFInfraWrapper row)>();

                                foreach (var row in saveList)
                                {
                                    var valstate = valsvc.GetObjectMessage(row.AsUnwrapped()).AsString(vcheck);

                                    if (valstate.code != 0)
                                    {
                                        fails.Add((valstate.code, valstate.message, row));
                                    }
                                }

                                if (fails.Any())
                                {
                                    if (settings.ValidationFailureIsException.GetValueOrDefault(Globals.ValidationFailureIsException))
                                    {
                                        throw new CEFValidationException(fails.Count() > 1 ? "Multiple validation failures." : "Validation failure.", (from a in fails select ((ValidationErrorCode)(-a.error), a.message)));
                                    }
                                    else
                                    {
                                        foreach (var f in fails)
                                        {
                                            retVal.Add(((object)f.row, f.message, f.error));
                                            saveList.Remove(f.row);
                                        }
                                    }
                                }
                            }

                            if (saveList.Any())
                            {
                                retVal.AddRange(db.Save(saveList, this, parm.settings));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        if (useAsync)
                        {
                            db.AddCompletionException(ex);
                        }
                        else
                        {
                            throw;
                        }
                    }
                }
            };

            if (useAsync)
            {
                db.AddCompletionTask(Task.Factory.StartNew(act, (settings, ss)));
            }
            else
            {
                act.Invoke((settings, ss));
            }

            return retVal;
        }

        public DynamicWithBag GetDynamicWrapperFor(object o, bool canCreate = true)
        {
            if (o == null)
            {
                return null;
            }

            if (o is DynamicWithBag)
            {
                return (DynamicWithBag)o;
            }

            var uw = o.AsUnwrapped();

            var to = _scopeObjects.GetFirstByName(nameof(TrackedObject.Target), uw);

            DynamicWithBag dwb = null;

            if (to != null)
            {
                if (canCreate)
                {
                    dwb = to.GetCreateInfra() as DynamicWithBag;
                }
                else
                {
                    dwb = to.GetInfra() as DynamicWithBag;
                }
            }

            if (dwb == null && canCreate)
            {
                return WrappingHelper.CreateInfraWrapper(WrappingSupport.All, WrappingAction.Dynamic, false, o, null, null, null) as DynamicWithBag;
            }

            return dwb;
        }

        public void Delete(object root, DeleteCascadeAction action)
        {
            HashSet<object> visits = new HashSet<object>();
            InternalDelete(root, visits, action);
        }

        public void AcceptAllChanges()
        {
            foreach (var to in _scopeObjects)
            {
                if (to.IsAlive && to.Infra != null)
                {
                    to.Infra.AcceptChanges();
                }
            }
        }

        public IEnumerable<ICEFInfraWrapper> GetAllTracked()
        {
            foreach (var to in _scopeObjects)
            {
                if (to.IsAlive)
                {
                    yield return to.GetCreateInfra();
                }
            }
        }

        public T NewObject<T>(T initial = null, Dictionary<string, object> props = null) where T : class, new()
        {
            return InternalCreateAdd(initial, true, null, props, null);
        }

        public int DeserializeScope(string json)
        {
            // Must be an array...
            if (!Regex.IsMatch(json, @"^\s*\[") || !Regex.IsMatch(json, @"\]\s*$"))
            {
                throw new CEFInvalidOperationException("JSON provided is not an array (must be to deserialize a service scope).");
            }

            var setArray = JArray.Parse(json);
            ConcurrentDictionary<object, object> visits = new ConcurrentDictionary<object, object>();

            foreach (var i in setArray.Children())
            {
                if (i.First.Type == JTokenType.Property)
                {
                    var tpn = ((JProperty)i.First).Name;

                    if (tpn == Globals.SerializationTypePropertyName)
                    {
                        InternalDeserialize(Type.GetType(((JProperty)i.First).Value.ToString()), i.ToString(), visits);
                    }
                }
            }

            return visits.Count;
        }

        public T Deserialize<T>(string json) where T : class, new()
        {
            // Must be an object...
            if (!Regex.IsMatch(json, @"^\s*\{") || !Regex.IsMatch(json, @"\}\s*$"))
            {
                throw new CEFInvalidOperationException("JSON provided is not an object.");
            }

            return InternalDeserialize(typeof(T), json, new ConcurrentDictionary<object, object>()) as T;
        }

        private object InternalDeserialize(Type type, string json, IDictionary<object, object> visits)
        {
            var copyFrom = JObject.Parse(json);

            Dictionary<string, object> props = new Dictionary<string, object>();
            Dictionary<string, (Type type, IEnumerable<string> json)> lists = new Dictionary<string, (Type type, IEnumerable<string> json)>();
            Dictionary<string, (Type type, string json)> objs = new Dictionary<string, (Type type, string json)>();
            ObjectState rs = ObjectState.Unchanged;

            foreach (var c in (from a in copyFrom.Children() where a.Type == JTokenType.Property select a))
            {
                var propName = ((JProperty)c).Name;

                if (string.Compare(propName, Globals.SerializationStatePropertyName) == 0)
                {
                    if (int.TryParse(c.First.ToString(), out int rs2))
                    {
                        rs = (ObjectState)rs2;
                    }
                    else
                    {
                        if (Enum.TryParse<ObjectState>(c.First.ToString(), out ObjectState rs3))
                        {
                            rs = rs3;
                        }
                    }
                }
                else
                {
                    switch (c.First.Type)
                    {
                        case JTokenType.Array:
                            {
                                var lp = type.GetProperty(propName);

                                // Can't deal with enumerations as bag properties (yet)
                                if (lp != null && lp.CanWrite && lp.PropertyType.IsGenericType && WrappingHelper.IsWrappableListType(lp.PropertyType, null))
                                {
                                    List<string> itemData = new List<string>();

                                    foreach (var cc in c.First.Children())
                                    {
                                        itemData.Add(cc.ToString());
                                    }

                                    lists[propName] = (lp.PropertyType, itemData);
                                }
                                else
                                {
                                    throw new NotSupportedException("Cannot deserialize this type of data (TODO).");
                                }
                            }
                            break;

                        case JTokenType.Object:
                            {
                                var lp = type.GetProperty(propName);

                                // Can't deal with objects as bag properties (yet)
                                if (lp != null && lp.CanWrite)
                                {
                                    objs[propName] = (lp.PropertyType, c.ToString());
                                }
                                else
                                {
                                    throw new NotSupportedException("Cannot deserialize this type of data (TODO).");
                                }
                            }
                            break;

                        case JTokenType.Boolean:
                            props[propName] = c.First.Value<bool>();
                            break;

                        case JTokenType.Date:
                            props[propName] = c.First.Value<DateTime>();
                            break;

                        case JTokenType.Float:
                            props[propName] = c.First.Value<decimal>();
                            break;

                        case JTokenType.Guid:
                            props[propName] = c.First.Value<Guid>();
                            break;

                        case JTokenType.Integer:
                            props[propName] = c.First.Value<int>();
                            break;

                        case JTokenType.String:
                            if (string.Compare(propName, Globals.SerializationTypePropertyName) != 0)
                            {
                                props[propName] = c.First.ToString();
                            }
                            break;

                        case JTokenType.Null:
                            props[propName] = null;
                            break;

                        default:
                            throw new NotSupportedException("Cannot deserialize this type of data (TODO).");
                    }
                }
            }

            var constructed = CEF.CurrentServiceScope.InternalCreateAddBase(Activator.CreateInstance(type), rs == ObjectState.Added, rs, props, null, visits);

            var iw = constructed.AsInfraWrapped();

            // Lists and objs should get wired up properly - easiest (fewer changes in core) if the parent already exists which is why we deferred this action, more of a breadth-first traversal
            foreach (var kvp in objs)
            {
                iw.SetValue(kvp.Key, InternalDeserialize(kvp.Value.type, kvp.Value.json, visits));
            }

            foreach (var kvp in lists)
            {
                var lw = WrappingHelper.CreateWrappingList(CEF.CurrentServiceScope, kvp.Value.type, constructed, kvp.Key);
                iw.SetValue(kvp.Key, lw);

                var itemType = kvp.Value.type.GenericTypeArguments[0];

                (lw as ISupportInitializeNotification)?.BeginInit();

                foreach (var itemText in kvp.Value.json)
                {
                    InternalDeserialize(itemType, itemText, visits);
                }

                (lw as ISupportInitializeNotification)?.EndInit();
            }

            iw.AcceptChanges();
            iw.SetRowState(rs);

            return constructed;
        }

        public EntitySet<T> DeserializeSet<T>(string json) where T : class, new()
        {
            // Must be an array...
            if (!Regex.IsMatch(json, @"^\s*\[") || !Regex.IsMatch(json, @"\]\s*$"))
            {
                throw new CEFInvalidOperationException("JSON provided is not an array (must be to deserialize as an EntitySet).");
            }

            // Construct a shadow copy that we'll traverse to build the corresponding wrapped structure
            var setArray = JArray.Parse(json);
            var outSet = new EntitySet<T>();
            outSet.BeginInit();

            foreach (var i in setArray.Children())
            {
                outSet.Add(Deserialize<T>(i.ToString()));
            }

            outSet.EndInit();
            return outSet;
        }

        #endregion

        #region "Internals"

        internal ConcurrentIndexedList<TrackedObject> Objects => _scopeObjects;

        internal ServiceScopeSettings Settings => _settings;

        internal T GetServiceState<T>() where T : class, ICEFServiceObjState
        {
            if (_serviceState.TryGetValue(typeof(T), out ICEFServiceObjState val))
            {
                return val as T;
            }

            return null;
        }

        internal object GetWrapperOrTarget(object o)
        {
            if (o == null)
                return null;

            var w = GetWrapperFor(o);

            if (w != null)
            {
                return w;
            }

            return _scopeObjects.GetFirstByName(nameof(TrackedObject.Target), o.AsUnwrapped())?.GetTarget();
        }

        internal ICEFInfraWrapper GetOrCreateInfra(object o, bool canCreate)
        {
            if (o == null)
                return null;

            var to = _scopeObjects.GetFirstByName(nameof(TrackedObject.Target), o.AsUnwrapped());

            if (to == null)
                return null;

            if (canCreate)
            {
                return to.GetCreateInfra();
            }
            else
            {
                return to.GetInfra();
            }
        }

        internal object GetInfraOrWrapperOrTarget(object o)
        {
            if (o == null)
                return null;

            if (o is ICEFInfraWrapper)
            {
                return o;
            }

            var to = _scopeObjects.GetFirstByName(nameof(TrackedObject.Target), o.AsUnwrapped());

            var infra = to?.GetInfra();

            if (infra != null)
            {
                return infra;
            }

            return GetWrapperOrTarget(o);
        }

        internal (Func<object> getter, Type type) GetGetter(object o, string propName)
        {
            if (o == null)
                throw new ArgumentNullException("o");

            if (o is TrackedObject)
            {
                throw new ArgumentException("o cannot be a TrackedObject.");
            }

            var target = GetInfraOrWrapperOrTarget(o);

            if (target != null)
            {
                var dyn = target as DynamicWithBag;

                if (dyn != null)
                {
                    return (() =>
                    {
                        return dyn.GetValue(propName);
                    }
                    , dyn.GetPropertyType(propName));
                }
                else
                {
                    if (target.FastPropertyReadable(propName))
                    {
                        PropertyInfo pi = target.GetType().GetProperty(propName);

                        return (() =>
                        {
                            return target.FastGetValue(propName);
                        }
                        , pi.PropertyType);
                    }
                }
            }

            return (null, null);
        }

        internal (Action<object> setter, Type type) GetSetter(object o, string propName)
        {
            var target = GetInfraOrWrapperOrTarget(o);

            if (target != null)
            {
                var dyn = target as DynamicWithBag;

                if (dyn != null)
                {
                    return ((v) =>
                    {
                        dyn.SetValue(propName, v);
                    }
                    , dyn.GetPropertyType(propName));
                }
                else
                {
                    if (target.FastPropertyWriteable(propName))
                    {
                        PropertyInfo pi = target.GetType().GetProperty(propName);

                        return ((v) =>
                        {
                            target.FastSetValue(propName, v);
                        }
                        , pi.PropertyType);
                    }
                }
            }

            return (null, null);
        }

        internal TrackedObject GetTrackedByWrapperOrTarget(object wot)
        {
            var to = _scopeObjects.GetAllByName(nameof(TrackedObject.Target), wot).FirstOrDefault();

            if (to == null)
            {
                to = _scopeObjects.GetAllByName(nameof(TrackedObject.Wrapper), wot).FirstOrDefault();
            }

            return to;
        }

        private IEnumerable<ICEFService> ResolveTypeServices(object o)
        {
            if (o == null)
                throw new ArgumentNullException("o");

            var bt = o.GetBaseType();

            (bool resolved, IList<ICEFService> list) svcTypes;
            CEF.DefaultServicesByType.TryGetValue(bt, out svcTypes);

            if (svcTypes.list != null && svcTypes.resolved)
            {
                return svcTypes.list.Concat(_localServices);
            }

            var vl = new List<ICEFService>();

            // Append any "missing" global services
            if (svcTypes.list?.Count > 0)
            {
                vl.AddRange(svcTypes.list.Union(CEF.GlobalServices));
            }
            else
            {
                vl.AddRange(CEF.GlobalServices);
            }

            // Look for dependent services that are not yet included - add them as needed
            foreach (var s in (from a in vl
                               where a.RequiredServices()?.Count > 0
                               from b in a.RequiredServices()
                               select b).Distinct().ToArray())
            {
                if (!(from a in vl where a.GetType().Equals(s) select a).Any())
                {
                    ICEFService sta = null;

                    try
                    {
                        // An interface was likely returned from RequiredServices - go look up based on interface first
                        if (s == typeof(ICEFDataHost))
                        {
                            sta = CEF.CurrentDBService(o);
                        }
                        else
                        {
                            if (s == typeof(ICEFAuditHost))
                            {
                                sta = CEF.CurrentAuditService(o);
                            }
                            else
                            {
                                if (s == typeof(ICEFKeyHost))
                                {
                                    sta = CEF.CurrentKeyService(o);
                                }
                                else
                                {
                                    if (s == typeof(ICEFPersistenceHost))
                                    {
                                        sta = CEF.CurrentPCTService(o);
                                    }
                                    else
                                    {
                                        if (s == typeof(ICEFCachingHost))
                                        {
                                            sta = CEF.CurrentCacheService(o);
                                        }
                                        else
                                        {
                                            // Assumes a parameterless constructor exists - most of these should be internal
                                            sta = Activator.CreateInstance(s) as ICEFService;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch
                    {
                    }

                    if (sta == null)
                    {
                        throw new CEFInvalidOperationException($"Service {s.Name} should be initialized with details, cannot create it automatically.");
                    }

                    vl.Add(sta);
                }
            }

            CEF.DefaultServicesByType[bt] = (true, vl);

            return vl.Concat(_localServices);
        }

        private HashSet<object> GetFilterRows(DBSaveSettings settings)
        {
            HashSet<object> filterRows = new HashSet<object>();

            if (settings.RootObject != null)
            {
                var ruw = settings.RootObject.AsUnwrapped();

                if (ruw != null)
                {
                    filterRows.Add(ruw);
                }

                if (settings.IncludeRootChildren)
                {
                    foreach (var o in CEF.CurrentKeyService()?.GetChildObjects(CEF.CurrentServiceScope, settings.RootObject, settings.IncludeRootParents ? RelationTypes.Both : RelationTypes.Children))
                    {
                        var uw = o.AsUnwrapped();

                        if (uw != null)
                        {
                            filterRows.Add(uw);
                        }
                    }
                }

                if (settings.IncludeRootParents)
                {
                    foreach (var o in CEF.CurrentKeyService()?.GetParentObjects(CEF.CurrentServiceScope, settings.RootObject, settings.IncludeRootChildren ? RelationTypes.Both : RelationTypes.Parents))
                    {
                        var uw = o.AsUnwrapped();

                        if (uw != null)
                        {
                            filterRows.Add(uw);
                        }
                    }
                }
            }

            if (settings.SourceList != null)
            {
                foreach (var r in settings.SourceList)
                {
                    var uw = r.AsUnwrapped();

                    if (uw != null)
                    {
                        filterRows.Add(uw);
                    }
                }
            }

            return filterRows.Count == 0 ? null : filterRows;
        }

        public string GetScopeSerializationText(SerializationMode? mode)
        {
            ReconcileModifiedState(null);

            ConcurrentDictionary<object, bool> visits = new ConcurrentDictionary<object, bool>();
            StringBuilder sb = new StringBuilder(16384);
            var actmode = mode.GetValueOrDefault(CEF.CurrentServiceScope.Settings.SerializationMode) | SerializationMode.SingleLevel;

            using (var jw = new JsonTextWriter(new StringWriter(sb)))
            {
                jw.WriteStartArray();

                foreach (var to in _scopeObjects)
                {
                    if (to.IsAlive && !visits.ContainsKey(to.GetWrapperTarget()))
                    {
                        var iw = to.GetInfra();

                        if (iw != null)
                        {
                            var rs = iw.GetRowState();

                            if ((rs != ObjectState.Unchanged && rs != ObjectState.Unlinked) || ((actmode & SerializationMode.OnlyChanged) == 0))
                            {
                                CEF.CurrentPCTService()?.SaveContents(jw, to.GetWrapperTarget(), actmode | SerializationMode.IncludeType, visits);
                            }
                        }
                    }
                }

                jw.WriteEndArray();
            }

            return sb.ToString();
        }

        internal int ReconcileModifiedState(HashSet<object> filterRows)
        {
            int count = 0;
            var db = CEF.CurrentDBService();

            IEnumerable<TrackedObject> list = null;

            if (filterRows != null && filterRows.Any())
            {
                list = (from a in filterRows let t = GetTrackedByWrapperOrTarget(a) where t != null select t);
            }
            else
            {
                list = _scopeObjects;
            }

            foreach (var to in list)
            {
                // If type includes property groups, copy values here (as well as on notifications - which may or may not have done it already)
                db?.CopyPropertyGroupValues(to.GetWrapperTarget());

                if (!(to.GetTarget() is INotifyPropertyChanged))
                {
                    var dyn = to.GetInfra() as DynamicWithValuesAndBag;

                    if (dyn != null)
                    {
                        if (dyn.ReconcileModifiedState((field, oval, nval) =>
                        {
                            // If the modification is for a tracked field, potentially change DB values as well
                            CEF.CurrentKeyService()?.UpdateBoundKeys(to, this, field, oval, nval);
                        }))
                        {
                            ++count;
                        }
                    }
                }
            }

            return count;
        }

        private IList<ICEFInfraWrapper> GetSaveables(HashSet<object> filterRows, DBSaveSettings settings)
        {
            List<ICEFInfraWrapper> tosave = new List<ICEFInfraWrapper>();

            IEnumerable<TrackedObject> list = null;

            if (filterRows != null && filterRows.Any())
            {
                list = (from a in filterRows let t = GetTrackedByWrapperOrTarget(a) where t != null select t);
            }
            else
            {
                list = _scopeObjects;
            }

            foreach (var v in list)
            {
                if (v.IsAlive)
                {
                    var target = v.GetTarget();

                    if (target != null)
                    {
                        if (settings.IgnoreObjectType == null || target.GetType() != settings.IgnoreObjectType)
                        {
                            var db = v.GetCreateInfra() as DynamicWithValuesAndBag;

                            if (db != null)
                            {
                                if (db.State != ObjectState.Unlinked && db.State != ObjectState.Unchanged)
                                {
                                    tosave.Add(db);
                                }
                            }
                        }
                    }
                }
            }

            return tosave;
        }

        internal object InternalCreateAddBase(object initial, bool isNew, ObjectState? initState, IDictionary<string, object> props, IDictionary<string, Type> types, IDictionary<object, object> visits)
        {
            if (initial == null)
                throw new ArgumentNullException("initial");

            if (initial is ICEFInfraWrapper)
            {
                throw new CEFInvalidOperationException("You cannot add an existing infrastructure wrapper to the current scope.");
            }

            if (visits == null)
            {
                // Would have liked to use plain Dictionary but we do support parallel object graph traversal, so...
                visits = new ConcurrentDictionary<object, object>();
            }

            // See if already tracked - if so, return quickly
            var wot = GetTrackedByWrapperOrTarget(initial);

            if (wot != null)
            {
                return wot.GetWrapperTarget();
            }

            var initBase = initial.GetBaseType();

            // Also need to see if can identify it based on key values
            if (props != null)
            {
                var kss = GetServiceState<KeyService.KeyServiceState>();

                if (kss != null)
                {
                    var pkcol = KeyService.ResolveKeyDefinitionForType(initBase);

                    if (pkcol.Any())
                    {
                        var pkval = (from a in pkcol let scn = KeyService.SHADOW_PROP_PREFIX + a where props.ContainsKey(scn) && (props[scn] ?? "").ToString() != "" select props[scn]);

                        if (!pkval.Any())
                        {
                            pkval = (from a in pkcol where props.ContainsKey(a) && (props[a] ?? "").ToString() != "" select props[a]);
                        }

                        if (pkval.Count() == pkcol.Count)
                        {
                            var pkto = kss.GetTrackedByPKValue(this, initBase, pkval);

                            if (pkto != null)
                            {
                                if (props != null)
                                {
                                    var iw = pkto.GetInfraWrapperTarget();

                                    // Since we have properties in hand, option to update the existing object (default is to do this, other option is to fail if any values differ)
                                    foreach (var prop in props)
                                    {
                                        if ((Settings.MergeBehavior & MergeBehavior.SilentMerge) != 0)
                                        {
                                            var pkset = GetSetter(iw, prop.Key);

                                            if (pkset.setter != null)
                                            {
                                                pkset.setter.Invoke(prop.Value);
                                            }
                                        }
                                        else
                                        {
                                            var pkget = GetGetter(iw, prop.Key);

                                            if (pkget.getter != null)
                                            {
                                                if (!prop.Value.IsSame(pkget.getter.Invoke()))
                                                {
                                                    throw new CEFInvalidOperationException($"Based on your merge settings, you're not allowed to load records that are already in scope with different property values. ({pkto.BaseName})");
                                                }
                                            }
                                        }
                                    }

                                    if (initState.HasValue)
                                    {
                                        var aiw = iw as ICEFInfraWrapper;

                                        if (aiw != null)
                                        {
                                            aiw.SetRowState(initState.Value);
                                        }
                                    }
                                }

                                return pkto.GetWrapperTarget();
                            }
                        }
                    }
                }
            }

            List<ICEFService> objServices = new List<ICEFService>();

            object o = initial;
            object repl = null;

            foreach (var svc in ResolveTypeServices(o))
            {
                var stateType = svc.IdentifyStateType(o, this, isNew);

                if (stateType != null && !_serviceState.ContainsKey(stateType))
                {
                    _serviceState[stateType] = Activator.CreateInstance(stateType) as ICEFServiceObjState;
                }

                objServices.Add(svc);
            }

            if (!objServices.Any())
            {
                // No services, cannot track
                return null;
            }

            // Look for wrapping needs across all services
            WrappingSupport wrapneed = WrappingSupport.None;

            foreach (var svc in objServices)
            {
                wrapneed |= svc.IdentifyInfraNeeds(o, null, this, isNew);

                if (wrapneed == WrappingSupport.All)
                    break;
            }

            ICEFInfraWrapper infra = null;

            if (wrapneed != WrappingSupport.None)
            {
                // See if we have support from replacement wrappers and they provide at least 1 service we're after
                if ((Globals.WrapperSupports & wrapneed) != 0)
                {
                    repl = WrappingHelper.CreateWrapper(wrapneed, Globals.DefaultWrappingAction, isNew, o, this, props, types, visits);

                    if (repl != null)
                    {
                        // The replacement wrapper may change what's left to acquire for services: run over the replacement object
                        wrapneed = WrappingSupport.None;

                        foreach (var svc in objServices)
                        {
                            wrapneed |= svc.IdentifyInfraNeeds(o, repl, this, isNew);

                            if (wrapneed == WrappingSupport.All)
                                break;
                        }
                    }
                }

                if (wrapneed != WrappingSupport.None)
                {
                    infra = WrappingHelper.CreateInfraWrapper(wrapneed, Globals.DefaultWrappingAction, isNew, repl ?? o, initState, props, types);
                }
            }

            if (repl == null)
            {
                // With no wraper, we still need to recursively traverse object graph
                WrappingHelper.CopyParsePropertyValues(null, null, o, isNew, this, visits, true);
            }

            // todo - identity service should allow renaming
            Type basetype = (repl != null) ? ((ICEFWrapper)repl).GetBaseType() : (o is ICEFWrapper) ? ((ICEFWrapper)o).GetBaseType() : o.GetType();

            var to = new TrackedObject()
            {
                BaseName = basetype.Name,
                BaseType = basetype,
                Target = new CEFWeakReference<object>(o),
                Wrapper = new CEFWeakReference<ICEFWrapper>(repl as ICEFWrapper),
                Infra = infra,
                Services = objServices
            };

            // This is the big moment! "to" is our "tracked object" container
            _scopeObjects.Add(to);

            foreach (var svc in objServices)
            {
                ICEFServiceObjState state = null;
                Type svcStateType = svc.IdentifyStateType(o, this, isNew);

                if (svcStateType != null)
                {
                    _serviceState.TryGetValue(svcStateType, out state);
                }

                svc.FinishSetup(to, this, isNew, props, state);
            }

            if (!isNew && infra != null)
            {
                infra.AcceptChanges();
            }

            return repl ?? o;
        }

        private void InternalDelete(object root, HashSet<object> visits, DeleteCascadeAction action)
        {
            if (visits.Contains(root))
            {
                return;
            }

            visits.Add(root);

            var infra = GetOrCreateInfra(root, false);

            if (infra == null)
            {
                throw new CEFInvalidOperationException("You require the persistence and change tracking service in order to mark objects for deletion.");
            }

            infra.SetRowState(ObjectState.Deleted);

            if (action == DeleteCascadeAction.Cascade)
            {
                foreach (var co in CEF.CurrentKeyService()?.GetChildObjects(this, root))
                {
                    InternalDelete(co.AsUnwrapped(), visits, action);
                }
            }
        }

        internal T InternalCreateAdd<T>(T initial, bool isNew, ObjectState? initState, Dictionary<string, object> props, Dictionary<string, Type> types) where T : class, new()
        {
            return InternalCreateAddBase(initial ?? new T(), isNew, initState, props, types, new ConcurrentDictionary<object, object>()) as T;
        }

        #endregion

        // expect overloads where can control which services want/need per instance
        #region IDisposable Support

        public bool IsDisposed
        {
            get;
            private set;
        }

        void Dispose(bool disposing)
        {
            if (_settings.CanDispose)
            {
                // Advertise "early disposal" to services used
                foreach (var svc in _scopeServices)
                {
                    svc.Disposing(this);
                }

                // Dispose any infra wrapper objects held in this scope
                foreach (var to in _scopeObjects)
                {
                    if (to.Infra is IDisposable)
                    {
                        ((IDisposable)to.Infra).Dispose();
                    }
                }

                _scopeObjects.Clear();
                _serviceState.Clear();
            }

            IsDisposed = true;

            // Advertise disposal
            Disposed?.Invoke();
        }

        public void Dispose()
        {
            Dispose(true);
        }
        #endregion

        public Action Disposed
        {
            get;
            set;
        }
    }
}
