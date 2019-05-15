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
8/2018     0.8     Support for SetNull on deletes
***********************************************************************/
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using CodexMicroORM.Core.Services;
using CodexMicroORM.Core.Collections;
using System.ComponentModel;
using System.Reflection;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using System.Text;
using Newtonsoft.Json;
using System.IO;
using System.Threading.Tasks;
using CodexMicroORM.Core.Helper;
using System.Threading;
using System.Collections;

namespace CodexMicroORM.Core
{
    /// <summary>
    /// Service scopes are managed collections of objects that are tracked for changes such that saving these changes to a database (among other operations) is possible.
    /// Mapping of object to relational and relational to object properties is controlled by configuration that's (ideally) established (once) on appdomain startup.
    /// Managed objects can use services, and the scope is responsible for tracking the use of these services.
    /// Scopes are typically thread-bound, where inter-thread/process use of scope data would be typically carried out via a presisting service.
    /// </summary>
    [Serializable]
    public sealed partial class ServiceScope : IDisposable
    {
        #region "Private state"

        // Global config...
        private static readonly ConcurrentDictionary<Type, CacheBehavior> _cacheBehaviorByType = new ConcurrentDictionary<Type, CacheBehavior>(Globals.DefaultCollectionConcurrencyLevel, Globals.DefaultDictionaryCapacity);
        private static readonly ConcurrentDictionary<Type, int> _cacheDurByType = new ConcurrentDictionary<Type, int>(Globals.DefaultCollectionConcurrencyLevel, Globals.DefaultDictionaryCapacity);
        private static readonly ConcurrentDictionary<Type, bool> _cacheOnlyMemByType = new ConcurrentDictionary<Type, bool>(Globals.DefaultCollectionConcurrencyLevel, Globals.DefaultDictionaryCapacity);
        private static readonly ConcurrentDictionary<(Type, string), PropertyDateStorage> _dateConversionByTypeAndProp = new ConcurrentDictionary<(Type, string), PropertyDateStorage>(Globals.DefaultCollectionConcurrencyLevel, Globals.DefaultDictionaryCapacity);
        private static long _currentSaveNestLevel = 0;

        // Tracks all objects in this scope - and their services.
        private readonly ConcurrentIndexedList<TrackedObject> _scopeObjects = null;

        // Optional state maintained at scope level, per service type
        private readonly ConcurrentDictionary<Type, ICEFServiceObjState> _serviceState = null;

        // Known/resolved services available in this scope - when asking for non-object-specific services, this offers a fast(er) way to determine
        private readonly HashSet<ICEFService> _scopeServices = null;

        private readonly ServiceScopeSettings _settings = new ServiceScopeSettings();

        private readonly HashSet<ICEFService> _localServices = null;

        // For cases we have connection scopes by service scope...
        internal AsyncLocal<Stack<ConnectionScope>> _allConnScopes = new AsyncLocal<Stack<ConnectionScope>>();
        internal AsyncLocal<ConnectionScope> _currentConnScope = new AsyncLocal<ConnectionScope>();

        #endregion

        #region "Constructors"

        internal ServiceScope(ServiceScopeSettings settings)
        {
            _settings = settings;

            // Normally internal locking should be very quick, for very large scopes, dictionary resizing can be slow on rare instances, so increase max timeout (TODO - investigate "flipping" storage to a new type if pass a threshold)
            _scopeObjects = new ConcurrentIndexedList<TrackedObject>(nameof(TrackedObject.Target), nameof(TrackedObject.Wrapper));
            _scopeObjects.LockTimeout = 25000;
            _scopeObjects.AddNeverTrackNull(nameof(TrackedObject.Wrapper)).AddUniqueConstraint(nameof(TrackedObject.Target));

            if (settings != null &&  _scopeObjects.InitialCapacity != settings.EstimatedScopeSize)
            {
                _scopeObjects.InitialCapacity = settings.EstimatedScopeSize;
            }

            _serviceState = new ConcurrentDictionary<Type, ICEFServiceObjState>(Globals.DefaultCollectionConcurrencyLevel, Globals.DefaultDictionaryCapacity);
            _scopeServices = new HashSet<ICEFService>();
            _localServices = new HashSet<ICEFService>();
        }

        internal ServiceScope(ServiceScope template)
        {
            _scopeObjects = template._scopeObjects;
            _serviceState = template._serviceState;
            _scopeServices = template._scopeServices;
            _localServices = template._localServices;
            _allConnScopes = template._allConnScopes;
            _currentConnScope = template._currentConnScope;
            FriendlyName = template.FriendlyName;

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
                AsyncCacheUpdates = template.Settings.AsyncCacheUpdates,
                EntitySetUsesUnwrapped = template.Settings.EntitySetUsesUnwrapped,
                RetrievalPostProcessing = template.Settings.RetrievalPostProcessing,
                EstimatedScopeSize = template.Settings.EstimatedScopeSize,
                ConnectionScopePerThread = template.Settings.ConnectionScopePerThread
            };

            // Special case - this as a shallow copy cannot dispose state!
            _settings.CanDispose = false;
        }

        #endregion

        #region "Static methods"

        public static void SetCacheBehavior<T>(CacheBehavior cb) => _cacheBehaviorByType[typeof(T)] = cb;

        public static void SetCacheSeconds<T>(int seconds) => _cacheDurByType[typeof(T)] = seconds;

        public static void SetCacheOnlyMemory<T>(bool onlyMem = true)
        {
            _cacheOnlyMemByType[typeof(T)] = onlyMem;
        }

        public static void SetDateStorageMode<T>(string prop, PropertyDateStorage mode)
        {
            _dateConversionByTypeAndProp[(typeof(T), prop)] = mode;
        }

        public static long CurrentSaveNestLevel
        {
            get
            {
                return Interlocked.Read(ref _currentSaveNestLevel);
            }
        }

        #endregion

        #region "Public methods"

        public string FriendlyName
        {
            get;
            set;
        }

        /// <summary>
        /// Returns a wrapper object if one is available for the input object.
        /// </summary>
        /// <param name="o"></param>
        /// <returns></returns>
        public ICEFWrapper GetWrapperFor(object o)
        {
            if (o == null)
                return null;

            if (o is ICEFWrapper)
                return (ICEFWrapper)o;

            return _scopeObjects.GetFirstByName(nameof(TrackedObject.Target), o.AsUnwrapped())?.GetWrapper();
        }

        /// <summary>
        /// The passed object is included in the service scope, added with an optional explicit state and additional (potentially extended) properties to assign.
        /// The passed list of properties includes an explicit preferred property type.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="toAdd"></param>
        /// <param name="drs"></param>
        /// <param name="props"></param>
        /// <returns></returns>
        public T IncludeObjectWithType<T>(T toAdd, ObjectState? drs = null, Dictionary<string, (object value, Type type)> props = null) where T : class, new()
        {
            Dictionary<string, object> propVals = null;
            Dictionary<string, Type> propTypes = null;

            if (props?.Count > 0)
            {
                propVals = new Dictionary<string, object>(props.Count);
                propTypes = new Dictionary<string, Type>(props.Count);

                foreach (var kvp in props)
                {
                    propVals[kvp.Key] = kvp.Value.value;
                    propTypes[kvp.Key] = kvp.Value.type;
                }
            }

            return InternalCreateAdd(toAdd, drs.GetValueOrDefault(ObjectState.Unchanged) == ObjectState.Added ? true : false, drs, propVals, propTypes);
        }

        /// <summary>
        /// The passed object is included in the service scope, added with an optional explicit state and additional (potentially extended) properties to assign.
        /// No preferred property type is assumed based on the passed property dictionary.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="toAdd"></param>
        /// <param name="drs"></param>
        /// <param name="props"></param>
        /// <returns></returns>
        public T IncludeObject<T>(T toAdd, ObjectState? drs = null, IDictionary<string, object> props = null) where T : class, new()
        {
            return InternalCreateAdd(toAdd, drs.GetValueOrDefault(ObjectState.Unchanged) == ObjectState.Added ? true : false, drs, props, null);
        }

        /// <summary>
        /// Returns a specific service based on type T, either identified globally from the scope, or an optional passed object.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="forObject"></param>
        /// <returns></returns>
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

            lock (_scopeServices)
            {
                service = (from a in _scopeServices where a is T select a).FirstOrDefault();
            }

            if (service != null)
            {
                return (T)service;
            }

            // As a last resort, check local and global services
            lock (_localServices)
            {
                service = (from a in _localServices where a is T select a).FirstOrDefault();
            }

            if (service == null)
            {
                service = (from a in CEF.GlobalServices where a is T select a).FirstOrDefault();
            }

            if (service != null)
            {
                lock (_scopeServices)
                {
                    _scopeServices.Add(service);
                }
            }

            return (T)service;
        }

        /// <summary>
        /// Returns cache behavior for a given type, if one has been set.
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        public CacheBehavior ResolvedCacheBehaviorForType(Type t)
        {
            if (_cacheBehaviorByType.TryGetValue(t, out CacheBehavior cb))
            {
                return cb;
            }

            return Settings.CacheBehavior.GetValueOrDefault(Globals.DefaultCacheBehavior);
        }

        /// <summary>
        /// Returns cache duration for a given type, if one has been set.
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        public int ResolvedCacheDurationForType(Type t)
        {
            if (_cacheDurByType.TryGetValue(t, out int dur))
            {
                return dur;
            }

            return Settings.GlobalCacheDuration.GetValueOrDefault(Globals.DefaultGlobalCacheIntervalSeconds);
        }

        /// <summary>
        /// Returns whether a given type can only be cached in memory or not.
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        public bool ResolvedCacheOnlyMemoryForType(Type t)
        {
            if (_cacheOnlyMemByType.TryGetValue(t, out bool om))
            {
                return om;
            }

            return false;
        }

        /// <summary>
        /// Returns the date storage mode for a given property name on a given type.
        /// </summary>
        /// <param name="t"></param>
        /// <param name="prop"></param>
        /// <returns></returns>
        public PropertyDateStorage ResolvedDateStorageForTypeAndProperty(Type t, string prop)
        {
            if (_dateConversionByTypeAndProp.TryGetValue((t, prop), out PropertyDateStorage mode))
            {
                return mode;
            }

            return Globals.DefaultPropertyDateStorage;
        }

        /// <summary>
        /// Registers a service for local use by just this service scope (local use).
        /// </summary>
        /// <param name="service"></param>
        public void AddLocalService(ICEFService service)
        {
            lock (_localServices)
            {
                _localServices.Add(service);
            }
        }

        /// <summary>
        /// Performs a database save operation for all objects with a changed state in the service scope. The object graph is traversed in a way to ensure saving can be done successfully.
        /// </summary>
        /// <param name="settings"></param>
        /// <returns></returns>
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
                        Interlocked.Increment(ref _currentSaveNestLevel);

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
                                    var (code, message) = valsvc.GetObjectMessage(row.AsUnwrapped()).AsString(vcheck);

                                    if (code != 0)
                                    {
                                        fails.Add((code, message, row));
                                    }
                                }

                                if (fails.Any())
                                {
                                    if (settings.ValidationFailureIsException.GetValueOrDefault(Globals.ValidationFailureIsException))
                                    {
                                        throw new CEFValidationException(fails.Count() > 1 ? $"Multiple validation failures. (First: {fails.First().message})" : $"Validation failure. ({fails.First().message})", (from a in fails select ((ValidationErrorCode)(-a.error), a.message)));
                                    }
                                    else
                                    {
                                        foreach (var (error, message, row) in fails)
                                        {
                                            retVal.Add(((object)row, message, error));
                                            saveList.Remove(row);
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
                    finally
                    {
                        Interlocked.Decrement(ref _currentSaveNestLevel);
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

        /// <summary>
        /// Returns an infrastructure wrapper for an input object, registering it in the this service scope. The wrapper returned inherits from System.DynamicObject, allowing for dynamic property access.
        /// </summary>
        /// <param name="o"></param>
        /// <param name="canCreate"></param>
        /// <returns></returns>
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

        /// <summary>
        /// Marks an object for deletion. Cascading behavior to related objects is specified explicitly.
        /// </summary>
        /// <param name="root"></param>
        /// <param name="action"></param>
        public void Delete(object root, DeleteCascadeAction action)
        {
            HashSet<object> visits = new HashSet<object>();
            InternalDelete(root, visits, action);
        }

        /// <summary>
        /// Marks all objects contained in service scope as unmodified.
        /// </summary>
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

        /// <summary>
        /// Returns all tracked objects in the service scope which match the input type.
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        public IEnumerable<TrackedObject> GetAllTrackedByType(Type t)
        {
            foreach (var to in _scopeObjects)
            {
                if (to.IsAlive && to.BaseType == t)
                {
                    yield return to;
                }
            }
        }

        /// <summary>
        /// Retuns all tracked objects in the service scope.
        /// </summary>
        /// <returns></returns>
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

        /// <summary>
        /// Adds an object to the service scope in an "added" state, optionally specifying a "template" object or list of explicit properties.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="initial"></param>
        /// <param name="props"></param>
        /// <returns></returns>
        public T NewObject<T>(T initial = null, Dictionary<string, object> props = null) where T : class, new()
        {
            return InternalCreateAdd(initial, true, null, props, null);
        }

        /// <summary>
        /// Takes a service scope level format JSON string and rehydrates corresponding objects in the service scope.
        /// </summary>
        /// <param name="json"></param>
        /// <returns>The number of objects that were rehydrated.</returns>
        public int DeserializeScope(string json)
        {
            // Must be an array...
            if (!Regex.IsMatch(json, @"^\s*\[") || !Regex.IsMatch(json, @"\]\s*$"))
            {
                throw new CEFInvalidOperationException("JSON provided is not an array (must be to deserialize a service scope).");
            }

            var setArray = JArray.Parse(json);
            Dictionary<object, object> visits = new Dictionary<object, object>(Globals.DefaultDictionaryCapacity);

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

        /// <summary>
        /// Takes a single object format JSON string and rehydrates that instance in the service scope, also returning the rehydrated object.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="json"></param>
        /// <returns></returns>
        public T Deserialize<T>(string json) where T : class, new()
        {
            // Must be an object...
            if (!Regex.IsMatch(json, @"^\s*\{") || !Regex.IsMatch(json, @"\}\s*$"))
            {
                throw new CEFInvalidOperationException("JSON provided is not an object.");
            }

            return InternalDeserialize(typeof(T), json, new Dictionary<object, object>(Globals.DefaultDictionaryCapacity)) as T;
        }

        /// <summary>
        /// Takes an EntitySet format JSON string and rehydrates that set in the service scope, also returning the rehydrated set.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="json"></param>
        /// <returns></returns>
        public EntitySet<T> DeserializeSet<T>(string json) where T : class, new()
        {
            // Must be an array...
            if (!Regex.IsMatch(json, @"^\s*\[") || !Regex.IsMatch(json, @"\]\s*$"))
            {
                throw new CEFInvalidOperationException("JSON provided is not an array (must be to deserialize as an EntitySet).");
            }

            // Construct a shadow copy that we'll traverse to build the corresponding wrapped structure
            var setArray = JArray.Parse(json);
            var outSet = Globals.NewEntitySet<T>();
            outSet.BeginInit();

            foreach (var i in setArray.Children())
            {
                outSet.Add(Deserialize<T>(i.ToString()));
            }

            outSet.EndInit();
            return outSet;
        }

        public ServiceScopeSettings Settings => _settings;

        public void CleanupServiceStates()
        {
            foreach (var val in _serviceState.Values)
            {
                val.Cleanup(this);
            }
        }

        public T GetServiceState<T>() where T : class, ICEFServiceObjState
        {
            if (_serviceState.TryGetValue(typeof(T), out ICEFServiceObjState val))
            {
                return val as T;
            }

            return null;
        }

        public object IncludeObjectNonGeneric(object initial, IDictionary<string, object> props)
        {
            return InternalCreateAddBase(initial, false, null, props, null, null, initial != null, false);
        }

        public object IncludeObjectNonGeneric(object initial, IDictionary<string, object> props, ObjectState state)
        {
            return InternalCreateAddBase(initial, false, state, props, null, null, initial != null, false);
        }

        public INotifyPropertyChanged GetNotifyFriendlyFor(object o)
        {
            if (o == null)
                return null;

            var to = _scopeObjects.GetFirstByName(nameof(TrackedObject.Target), o.AsUnwrapped());

            if (to == null)
                return null;

            return to.GetNotifyFriendly();
        }

        public TrackedObject GetTrackedByWrapperOrTarget(object wot)
        {
            if (wot == null)
            {
                return null;
            }

            var to = _scopeObjects.GetFirstByName(nameof(TrackedObject.Target), wot);

            if (to == null)
            {
                to = _scopeObjects.GetFirstByName(nameof(TrackedObject.Wrapper), wot);
            }

            return to;
        }

        public string GetScopeSerializationText(SerializationMode? mode)
        {
            ReconcileModifiedState(null);

            SerializationVisitTracker visits = new SerializationVisitTracker();
            StringBuilder sb = new StringBuilder(16384);
            var actmode = mode.GetValueOrDefault(CEF.CurrentServiceScope.Settings.SerializationMode) | SerializationMode.SingleLevel;

            using (var jw = new JsonTextWriter(new StringWriter(sb)))
            {
                jw.WriteStartArray();

                foreach (var to in _scopeObjects)
                {
                    if (to.IsAlive && !visits.Objects.Contains(to.GetWrapperTarget()))
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

        public void CopyPropertyValues(IDictionary<string, object> props, ICEFInfraWrapper iw)
        {
            foreach (var prop in props)
            {
                var (setter, type) = GetSetter(iw, prop.Key);

                if (setter != null)
                {
                    setter.Invoke(prop.Value);
                }
            }
        }

        #endregion

        #region "Internals"

        private object InternalDeserialize(Type type, string json, IDictionary<object, object> visits)
        {
            var copyFrom = JObject.Parse(json);

            Dictionary<string, object> props = new Dictionary<string, object>(Globals.DefaultDictionaryCapacity);
            Dictionary<string, object> origProps = new Dictionary<string, object>(Globals.DefaultDictionaryCapacity);
            Dictionary<string, (Type type, IEnumerable<string> json)> lists = new Dictionary<string, (Type type, IEnumerable<string> json)>(Globals.DefaultDictionaryCapacity);
            Dictionary<string, (Type type, string json)> objs = new Dictionary<string, (Type type, string json)>(Globals.DefaultDictionaryCapacity);
            ObjectState rs = ObjectState.Unchanged;
            bool anyOrig = false;

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
                    var propNameRaw = propName;

                    var isOrig = propName.StartsWith("\\\\");

                    if (isOrig)
                    {
                        propName = propName.Substring(2);
                        anyOrig = true;
                    }

                    switch (c.First.Type)
                    {
                        case JTokenType.Array:
                            {
                                if (!isOrig)
                                {
                                    var lp = type.GetProperty(propNameRaw);

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
                                        CEFDebug.WriteInfo("Cannot deserialize this type of data (TODO).");
                                    }
                                }
                            }
                            break;

                        case JTokenType.Object:
                            {
                                if (!isOrig)
                                {
                                    var lp = type.GetProperty(propNameRaw);

                                    // Can't deal with objects as bag properties (yet)
                                    if (lp != null && lp.CanWrite)
                                    {
                                        objs[propName] = (lp.PropertyType, c.ToString());
                                    }
                                    else
                                    {
                                        CEFDebug.WriteInfo("Cannot deserialize this type of data (TODO).");
                                    }
                                }
                            }
                            break;

                        case JTokenType.Boolean:
                            if (isOrig)
                            {
                                origProps[propName] = c.First.Value<bool>();
                            }
                            else
                            {
                                props[propName] = c.First.Value<bool>();
                            }
                            break;

                        case JTokenType.Date:
                            if (isOrig)
                            {
                                origProps[propName] = c.First.Value<DateTime>();
                            }
                            else
                            {
                                props[propName] = c.First.Value<DateTime>();
                            }
                            break;

                        case JTokenType.Float:
                            if (isOrig)
                            {
                                origProps[propName] = c.First.Value<decimal>();
                            }
                            else
                            {
                                props[propName] = c.First.Value<decimal>();
                            }
                            break;

                        case JTokenType.Guid:
                            if (isOrig)
                            {
                                origProps[propName] = c.First.Value<Guid>();
                            }
                            else
                            {
                                props[propName] = c.First.Value<Guid>();
                            }
                            break;

                        case JTokenType.Integer:
                            if (isOrig)
                            {
                                origProps[propName] = c.First.Value<int>();
                            }
                            else
                            {
                                props[propName] = c.First.Value<int>();
                            }
                            break;

                        case JTokenType.String:
                            if (string.Compare(propNameRaw, Globals.SerializationTypePropertyName) != 0)
                            {
                                if (isOrig)
                                {
                                    origProps[propName] = c.First.ToString();
                                }
                                else
                                {
                                    props[propName] = c.First.ToString();
                                }
                            }
                            break;

                        case JTokenType.Null:
                            if (isOrig)
                            {
                                origProps[propName] = null;
                            }
                            else
                            {
                                props[propName] = null;
                            }
                            break;

                        default:
                            CEFDebug.WriteInfo("Cannot deserialize this type of data (TODO).");
                            break;
                    }
                }
            }

            var constructed = CEF.CurrentServiceScope.InternalCreateAddBase(type.FastCreateNoParm(), rs == ObjectState.Added, rs, props, null, visits, true, false);

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

                foreach (var itemText in kvp.Value.json)
                {
                    InternalDeserialize(itemType, itemText, visits);
                }

                (lw as ISupportInitializeNotification)?.EndInit();
            }

            iw.AcceptChanges();
            iw.SetRowState(rs);

            // Any original values implies a need to set them all now in infrawrapper - will only do with property vals (for now)
            if (anyOrig)
            {
                foreach (var kvp in origProps)
                {
                    iw.SetOriginalValue(kvp.Key, kvp.Value);
                }
            }

            return constructed;
        }

        internal void ConnScopeInit(ConnectionScope newcs, ScopeMode mode)
        {
            if (_allConnScopes.Value == null)
            {
                _allConnScopes.Value = new Stack<ConnectionScope>();
            }

            if (_currentConnScope.Value != null)
            {
                _allConnScopes.Value.Push(_currentConnScope.Value);
            }

            _currentConnScope.Value = newcs ?? throw new ArgumentNullException("newcs");

            var db = GetService<DBService>();

            newcs.Disposing = () =>
            {
                // Not just service scopes but connection scopes should wait for all pending operations!
                db?.WaitOnCompletions();
            };

            newcs.Disposed = () =>
            {
                if (_allConnScopes.Value?.Count > 0)
                {
                    _currentConnScope.Value = _allConnScopes.Value.Pop();
                    return;
                }

                _currentConnScope.Value = null;
            };
        }

        internal ConcurrentIndexedList<TrackedObject> Objects => _scopeObjects;

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

            if (o is DynamicWithBag dyn)
            {
                return (() =>
                {
                    return dyn.GetValue(propName);
                }
                , dyn.GetPropertyType(propName));
            }

            var target = GetInfraOrWrapperOrTarget(o);

            if (target != null)
            {
                dyn = target as DynamicWithBag;

                if (dyn != null)
                {
                    return (() =>
                    {
                        return dyn.GetValue(propName);
                    }
                    , dyn.GetPropertyType(propName));
                }
            }

            var pi = (target ?? o).FastPropertyReadableWithValue(propName);

            if (pi.readable)
            {
                var (name, type, readable, writeable) = (target ?? o).FastGetAllProperties(null, null, propName).First();

                return (() =>
                {
                    return pi.value;
                }
                , type);
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

        private IEnumerable<ICEFService> ResolveTypeServices(object o)
        {
            if (o == null)
                throw new ArgumentNullException("o");

            var bt = o.GetBaseType();

            var list = CEF.ResolvedServicesByType.AddWithFactory(bt, (key) =>
            {
                var vl = new List<ICEFService>();

                // Append any "missing" global services
                vl.AddRange(CEF.GlobalServices);

                // Append services registered by type
                CEF.RegisteredServicesByType.TryGetValue(bt, out var rl);

                if (rl != null)
                {
                    vl.AddRange(from a in rl where !(from b in vl where b.GetType() == a.GetType() select b).Any() select a);
                }

                // Look for dependent services that are not yet included - add them as needed
                foreach (var s in (from a in vl
                                   where a.RequiredServices()?.Count > 0
                                   from b in a.RequiredServices()
                                   select b).Distinct().ToArray())
                {
                    if (!(from a in vl where s.IsAssignableFrom(a.GetType()) select a).Any())
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
                                                sta = s.FastCreateNoParm() as ICEFService;
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

                return vl;
            });

            if (list != null)
            {
                return list.Concat(from a in _localServices where !(from b in list where a.GetType() == b.GetType() select b).Any() select a);
            }
            else
            {
                return _localServices;
            }
        }

        private HashSet<object> GetFilterRows(DBSaveSettings settings)
        {
            HashSet<object> filterRows = new HashSet<object>();

            // If the root object is actually a collection, treat as a source list instead
            if (settings.RootObject != null && settings.RootObject is IEnumerable)
            {
                settings.SourceList = ((IEnumerable)settings.RootObject).Cast<object>();
                settings.RootObject = null;
            }

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

        internal int ReconcileModifiedState(HashSet<object> filterRows)
        {
            const int USE_PARALLEL_THRESHOLD = 20000;

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

            void work(TrackedObject to)
            {
                // If object is unlinked, ignore and evict!
                if (to.GetInfra()?.GetRowState() == ObjectState.Unlinked)
                {
                    _scopeObjects.Remove(to);
                }
                else
                {
                    // If type includes property groups, copy values here (as well as on notifications - which may or may not have done it already)
                    db?.CopyPropertyGroupValues(to.GetWrapperTarget());

                    if (!(to.GetTarget() is INotifyPropertyChanged))
                    {
                        if (to.GetInfra() is DynamicWithValuesAndBag dyn)
                        {
                            if (dyn.ReconcileModifiedState((field, oval, nval) =>
                            {
                                // If the modification is for a tracked field, potentially change DB values as well
                                CEF.CurrentKeyService()?.UpdateBoundKeys(to, this, field, oval, nval);
                            }))
                            {
                                Interlocked.Increment(ref count);
                            }
                        }
                    }
                }
            }

            if (list.Count() < USE_PARALLEL_THRESHOLD)
            {
                foreach (var to in list)
                {
                    work(to);
                }

            }
            else
            {
                Parallel.ForEach(list, (i) =>
                {
                    using (CEF.UseServiceScope(this))
                    {
                        work(i);
                    }
                });
            }

            return count;
        }

        private IList<ICEFInfraWrapper> GetSaveables(HashSet<object> filterRows, DBSaveSettings settings)
        {
            const int USE_PARALLEL_THRESHOLD = 20000;

            IEnumerable<TrackedObject> list = null;

            if (filterRows != null && filterRows.Any())
            {
                list = (from a in filterRows let t = GetTrackedByWrapperOrTarget(a) where t != null select t);
            }
            else
            {
                list = _scopeObjects;
            }

            var cnt = list.Count();

            List<ICEFInfraWrapper> tosave = new List<ICEFInfraWrapper>(cnt);

            void work(TrackedObject v)
            {
                if (v.IsAlive)
                {
                    var target = v.GetTarget();

                    if (target != null)
                    {
                        if (settings.IgnoreObjectType == null || target.GetType() != settings.IgnoreObjectType)
                        {
                            if (v.GetCreateInfra() is DynamicWithValuesAndBag db)
                            {
                                if (db.State != ObjectState.Unlinked && db.State != ObjectState.Unchanged)
                                {
                                    lock (tosave)
                                    {
                                        tosave.Add(db);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            if (cnt < USE_PARALLEL_THRESHOLD)
            {
                foreach (var v in list)
                {
                    work(v);
                }
            }
            else
            {
                Parallel.ForEach(list, (v) =>
                {
                    using (CEF.UseServiceScope(this))
                    {
                        work(v);
                    }
                });
            }

            return tosave;
        }

        internal object InternalCreateAddBase(object initial, bool isNew, ObjectState? initState, IDictionary<string, object> props, IDictionary<string, Type> types, IDictionary<object, object> visits, bool initFromTemplate, bool mustValueMatch)
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
                visits = new Dictionary<object, object>(Globals.DefaultDictionaryCapacity);
            }

            // See if already tracked - if so, return quickly
            var wot = GetTrackedByWrapperOrTarget(initial);

            if (wot != null)
            {
                return wot.GetWrapperTarget();
            }

            var initBase = initial.GetBaseType();

            // Also need to see if can identify it based on key values
            if (mustValueMatch || props != null)
            {
                IDictionary<string, object> pkcheck;

                if (props == null)
                {
                    pkcheck = new Dictionary<string, object>();

                    foreach (var (name, type, readable, writeable) in initial.FastGetAllProperties(true))
                    {
                        pkcheck[name] = GetGetter(initial, name).getter.Invoke();
                    }
                }
                else
                {
                    pkcheck = props;
                }

                var kss = GetServiceState<KeyService.KeyServiceState>();

                if (kss != null)
                {
                    var pkcol = KeyService.ResolveKeyDefinitionForType(initBase);

                    if (pkcol.Any())
                    {
                        var pkval = (from a in pkcol let scn = KeyService.SHADOW_PROP_PREFIX + a where pkcheck.ContainsKey(scn) && (pkcheck[scn] ?? "").ToString() != "" select pkcheck[scn]);

                        if (!pkval.Any())
                        {
                            pkval = (from a in pkcol where pkcheck.ContainsKey(a) && (pkcheck[a] ?? "").ToString() != "" select pkcheck[a]);
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
                                            var (setter, type) = GetSetter(iw, prop.Key);

                                            if (setter != null)
                                            {
                                                setter.Invoke(prop.Value);
                                            }
                                        }
                                        else
                                        {
                                            var (getter, type) = GetGetter(iw, prop.Key);

                                            if (getter != null)
                                            {
                                                if (!prop.Value.IsSame(getter.Invoke()))
                                                {
                                                    throw new CEFInvalidOperationException($"Based on your merge settings, you're not allowed to load records that are already in scope with different property values. ({pkto.BaseName})");
                                                }
                                            }
                                        }
                                    }

                                    if (initState.HasValue && iw is ICEFInfraWrapper aiw)
                                    {
                                        aiw.SetRowState(initState.Value);
                                    }
                                }

                                if (!_scopeObjects.Contains(pkto))
                                {
                                    _scopeObjects.Add(pkto);
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
                    _serviceState[stateType] = stateType.FastCreateNoParm() as ICEFServiceObjState;
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
            _scopeObjects.Add(to, false);

            foreach (var svc in objServices)
            {
                ICEFServiceObjState state = null;
                Type svcStateType = svc.IdentifyStateType(o, this, isNew);

                if (svcStateType != null)
                {
                    _serviceState.TryGetValue(svcStateType, out state);
                }

                svc.FinishSetup(to, this, isNew, props, state, initFromTemplate);
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

            if (action == DeleteCascadeAction.Fail)
            {
                foreach (var co in CEF.CurrentKeyService()?.GetChildObjects(this, root))
                {
                    var rs = (co.AsInfraWrapped(false)?.GetRowState()).GetValueOrDefault(ObjectState.Unlinked);

                    if (rs != ObjectState.Deleted && rs != ObjectState.Unlinked)
                    {
                        throw new CEFConstraintException($"Failed to delete object - child objects exist and must be deleted first.");
                    }
                }
            }

            infra.SetRowState(ObjectState.Deleted);

            if (action == DeleteCascadeAction.Cascade)
            {
                foreach (var co in CEF.CurrentKeyService()?.GetChildObjects(this, root))
                {
                    InternalDelete(co.AsUnwrapped(), visits, action);
                }
            }
            else
            {
                if (action == DeleteCascadeAction.SetNull)
                {
                    var ks = CEF.CurrentKeyService(root);

                    if (ks != null)
                    {
                        var top = GetTrackedByWrapperOrTarget(root.AsUnwrapped());
                        var rbt = top.BaseType;

                        foreach (var co in ks.GetChildObjects(this, root).ToArray())
                        {
                            var toc = GetTrackedByWrapperOrTarget(co);
                            var bt = toc.BaseType;

                            foreach (var k in ks.GetRelationsForChild(bt))
                            {
                                if (k.ParentType == rbt)
                                {
                                    if (toc != null)
                                    {
                                        if (ks.RemoveFK(this, k, top, toc, top.GetNotifyFriendly(), true) == null)
                                        {
                                            continue;
                                        }
                                    }

                                    // We use modified priority to ensure on save, unlinks happen before deletions of parents
                                    var iw = co.AsInfraWrapped();
                                    var rs = iw.GetRowState();
                                    
                                    if (rs == ObjectState.Modified)
                                    {
                                        iw.SetRowState(ObjectState.ModifiedPriority);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        internal T InternalCreateAdd<T>(T initial, bool isNew, ObjectState? initState, IDictionary<string, object> props, IDictionary<string, Type> types) where T : class, new()
        {
            var v = InternalCreateAddBase(initial ?? new T(), isNew, initState, props, types, new Dictionary<object, object>(Globals.DefaultDictionaryCapacity), initial != null, false);
            return v as T;
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
