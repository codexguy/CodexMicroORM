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
04/2018    0.6     Fairly major rework: removed use of ObservableCollection as base
07/2018    0.7     Updates for portable json, etc.
***********************************************************************/
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Collections;
using System.Collections.Concurrent;
using Newtonsoft.Json;
using System.Text;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CodexMicroORM.Core.Collections;
using CodexMicroORM.Core.Helper;

namespace CodexMicroORM.Core.Services
{
    /// <summary>
    /// EntitySet's are specialized collections of objects that CEF can leverage.
    /// This does not mean you can't use plain collections with CEF, but CEF will make an attempt to "promote" collections to EntitySet's where it can.
    /// You should not care much about this implementation detail: handle most of your collections as ICollection<T> or IList<T>.
    /// In 0.6 release, I've changed this class quite a bit, to avoid locking (both read and write!). It initializes per-thread buckets that are
    /// thread-safe for the executing thread (cross-thread, we still use r/w locks but some key operations that were slow such as bulk adding rows should be much faster).
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class EntitySet<T> : ConcurrentObservableCollection<T>, ICEFList, ICEFSerializable, ISupportInitializeNotification where T : class, new()
    {
        #region "Private state"

        private RWLockInfo _lock = new RWLockInfo();

        private SlimConcurrentDictionary<T, bool> _contains;
        private long _init = 1;
        private List<T> _toWire = new List<T>();

        #endregion

        #region "Constructors"

        public EntitySet() : base()
        {
            _contains = new SlimConcurrentDictionary<T, bool>(Globals.DefaultLargerDictionaryCapacity);
            BoundScope = CEF.CurrentServiceScope;
            EndInit();
        }

        public EntitySet(ServiceScope ss) : base()
        {
            _contains = new SlimConcurrentDictionary<T, bool>(Globals.DefaultLargerDictionaryCapacity);
            BoundScope = ss;
            EndInit();
        }

        public EntitySet(IEnumerable<T> source) : base()
        {
            _contains = new SlimConcurrentDictionary<T, bool>(Globals.DefaultLargerDictionaryCapacity);
            BoundScope = CEF.CurrentServiceScope;

            foreach (var i in source)
            {
                this.Add(CEF.IncludeObject<T>(i, ObjectState.Unchanged));
            }

            EndInit();
        }

        #endregion

        #region "Public methods"

        public RWLockInfo LockInfo => _lock;

        public bool IsInitialized => Interlocked.Read(ref _init) == 0;

        public event EventHandler Initialized;

        public bool ContainsItemByKey(object o)
        {
            if (o == null)
                return false;

            var key = GetKey(o);

            if (key != null)
            {
                return (from a in this where GetKey(a) == key select a).Any();
            }

            return false;
        }

        public bool ContainsItem(object o)
        {
            if (o == null)
                return false;

            return _contains.Contains(o as T);
        }

        public bool IsDirty()
        {
            return (from b in (from a in this let iw = a.AsInfraWrapped() where iw != null select iw.GetRowState()) where b != ObjectState.Unchanged select b).Any();
        }

        /// <summary>
        /// Marks all collection items as unchanged. Items marked deleted are removed from the collection.
        /// </summary>
        /// <returns></returns>
        public int AcceptChanges()
        {
            int cnt = 0;

            foreach (var i in this)
            {
                var iw = i.AsInfraWrapped();

                if (iw != null)
                {
                    var rs = iw.GetRowState();

                    if (rs == ObjectState.Added || rs == ObjectState.Modified)
                    {
                        iw.AcceptChanges();
                        ++cnt;
                    }
                }
            }

            foreach (var i in (from a in this let iw = a.AsInfraWrapped() let rs = iw?.GetRowState() where rs == ObjectState.Deleted || rs == ObjectState.Unlinked select a).ToArray())
            {
                this.Remove(i);
                ++cnt;
            }

            return cnt;
        }

        public void PopulateFromSerializationText(string json, JsonSerializationSettings jss = null)
        {
            if (jss == null)
            {
                jss = new JsonSerializationSettings();
            }

            // Must be an array...
            if (jss.SerializationType == SerializationType.Array)
            {
                if (!Regex.IsMatch(json, @"^\s*\[") || !Regex.IsMatch(json, @"\]\s*$"))
                {
                    throw new CEFInvalidOperationException("JSON provided is not an array (must be to deserialize a service scope).");
                }
            }

            foreach (var i in CEF.CurrentPCTService()?.GetItemsFromSerializationText<T>(json, jss))
            {
                this.Add(i);
            }
        }

        public Dictionary<string, T> ToDictionary(IEnumerable<string> cols)
        {
            if (cols == null)
            {
                throw new ArgumentNullException("cols");
            }

            Dictionary<string, T> ret = new Dictionary<string, T>();

            foreach (T i in this)
            {
                var iw = i.AsInfraWrapped();
                var key = iw.DictionaryKeyFromColumns(cols);

                if (!Globals.AssumeSafe && ret.ContainsKey(key))
                {
                    throw new InvalidOperationException("Proposed key does not provide unique values in collection.");
                }

                ret[key] = i;
            }

            return ret;
        }

        /// <summary>
        /// Once client-side changes have been made to portable serialization text, the typical process should be on submission to a) re-retrieve the original entity set from the database, b) apply changes to the set using this method, c) save changes to the database.
        /// When the method is finished, you should have entity data in a state that reflects what happened on the client: insertions, updates and deletions depending on the incoming JSON.
        /// This process is only "relatively stateless": our database is really our persistent state - things like additional caching are possible but the responsibility of framework users, not the framework itself.
        /// </summary>
        /// <param name="json"></param>
        /// <returns>A shallow copy of the set being acted upon, where deleted rows are *not* present. (They remain in the acted on set, to support "saving by set".)</returns>
        public EntitySet<T> ApplyChangesFromPortableText(string json)
        {
            EntitySet<T> retVal = new EntitySet<T>();

            if (string.IsNullOrWhiteSpace(json) || json.Length > 100000000)
            {
                throw new ArgumentException("Incoming data is too short or too long.");
            }

            var kdef = KeyService.ResolveKeyDefinitionForType(typeof(T));

            if (kdef?.Count == 0)
            {
                throw new InvalidOperationException("Cannot apply changes without a primary key defined for type.");
            }

            using (var jr = new Newtonsoft.Json.JsonTextReader(new StringReader(json)))
            {
                var jq = Newtonsoft.Json.Linq.JObject.Load(jr);

                var sourceCols = new List<string>();
                var sourceTypes = new List<string>();
                var keyIndexes = new List<int>();
                var idx = 0;

                foreach (Newtonsoft.Json.Linq.JObject scol in jq.Value<Newtonsoft.Json.Linq.JArray>("schema"))
                {
                    var cn = scol.Property("cn").Value.ToString();
                    sourceCols.Add(cn);
                    var dt = scol.Property("dt").Value.ToString();
                    sourceTypes.Add(dt);

                    if (kdef.Contains(cn))
                    {
                        keyIndexes.Add(idx);
                    }

                    idx++;
                }

                // An indexed view in this case can be a dictionary for fast lookup
                var index = ToDictionary(kdef);
                HashSet<T> sourceVisits = new HashSet<T>();

                foreach (Newtonsoft.Json.Linq.JArray row in jq.Value<Newtonsoft.Json.Linq.JArray>("rows"))
                {
                    var keyval = new StringBuilder(128);

                    foreach (var c in kdef)
                    {
                        if (keyval.Length > 0)
                        {
                            keyval.Append("~");
                        }

                        var kloc = sourceCols.IndexOf(c);
                        keyval.Append(row[kloc].ToString());
                    }

                    bool isnew = false;

                    if (!index.TryGetValue(keyval.ToString(), out T target))
                    {
                        target = new T();
                        this.Add(target);
                        isnew = true;
                    }
                    else
                    {
                        sourceVisits.Add(target);
                        retVal.Add(target);
                    }

                    var tiw = target.AsInfraWrapped();
                    var prefTypes = tiw.GetAllPreferredTypes();

                    for (int i = 0; i < sourceCols.Count; ++i)
                    {
                        if (!keyIndexes.Contains(i) || isnew)
                        {
                            var v = row[i].ToString();

                            if (!string.IsNullOrEmpty(v))
                            {
                                var scn = sourceCols[i];
                                var oldval = tiw.GetValue(scn)?.ToString();

                                if (string.Compare(sourceTypes[i], "datetime") == 0)
                                {
                                    if (long.TryParse(v, out long ld))
                                    {
                                        v = (new DateTime((ld * 10000L) + 621355968000000000L, DateTimeKind.Utc)).ToString("O");
                                    }

                                    if (!string.IsNullOrEmpty(oldval))
                                    {
                                        oldval = Convert.ToDateTime(oldval).ToString("O");
                                    }
                                }

                                if (string.Compare(oldval, v, false) != 0)
                                {
                                    if (prefTypes.TryGetValue(scn, out Type pt))
                                    {
                                        tiw.SetValue(scn, v.CoerceType(pt));
                                    }
                                    else
                                    {
                                        tiw.SetValue(scn, v);
                                    }
                                }
                            }
                        }
                    }
                }

                foreach (T i in index.Values)
                {
                    if (!sourceVisits.Contains(i))
                    {
                        CEF.DeleteObject(i);
                        this.Remove(i);
                    }
                }
            }

            return retVal;
        }

        /// <summary>
        /// Portable serialization text is intended to be usable to send complete objects to a web client where we can have client-side JS framework awareness of the format, etc.
        /// We send back extended details including schema details that would not normally be necessary as such, hence the "portability". (Without this, we would need to know the schema ahead of time.)
        /// Options on this method support serializing a subset of properties, among other things that would be relevant when working with client-side logic.
        /// Notably things like row-state are *not* included which differs from some over-wire strategies - we expect to pair use of this method with ApplyChangesFromPortableText() in order to "apply changes" against a re-retrieved set, which has value for security, for example.
        /// The Portable being called out in the name is intended to be more explicit than making an option on the non-Portable function.
        /// </summary>
        /// <param name="mode"></param>
        /// <returns></returns>
        public string GetPortableText(PortableSerializationOptions options = null)
        {
            if (options == null)
            {
                options = new PortableSerializationOptions();
            }

            StringBuilder sb = new StringBuilder(4096);
            var actmode = options.Mode.GetValueOrDefault(Globals.PortableJSONMode.GetValueOrDefault(CEF.CurrentServiceScope.Settings.SerializationMode));

            CEF.CurrentServiceScope.ReconcileModifiedState(null);

            using (var jw = new JsonTextWriter(new StringWriter(sb)))
            {
                jw.FloatFormatHandling = FloatFormatHandling.DefaultValue;

                // All contained within an object
                jw.WriteStartObject();

                jw.WritePropertyName("schema");
                jw.WriteStartArray();

                var c = typeof(T).FastGetAllProperties(true, (actmode & SerializationMode.IncludeReadOnlyProps) == 0 ? true : new bool?()).ToList();

                // Use a top x sample of entries in collection to determine if there are any extended properties to serialize
                if (options.IncludeExtended.GetValueOrDefault(Globals.PortableJSONIncludeExtended) && options.ExtendedPropertySampleSize.GetValueOrDefault(Globals.PortableJSONExtendedPropertySampleSize) > 0)
                {
                    foreach (T i in this.Take(options.ExtendedPropertySampleSize.GetValueOrDefault(Globals.PortableJSONExtendedPropertySampleSize)))
                    {
                        c = c.Union(from a in i.AsInfraWrapped().GetAllPreferredTypes() select (a.Key, a.Value, true, true)).ToList();
                    }
                }

                // Explicitly remove audit if needed
                if (options.ExcludeAudit.GetValueOrDefault(Globals.PortableJSONExcludeAudit))
                {
                    if (!string.IsNullOrEmpty(CEF.CurrentAuditService()?.LastUpdatedByField))
                    {
                        var trem = (from a in c where string.Compare(a.name, CEF.CurrentAuditService()?.LastUpdatedByField, true) == 0 select a);

                        if (trem.Any())
                        {
                            c.Remove(trem.First());
                        }
                    }

                    if (!string.IsNullOrEmpty(CEF.CurrentAuditService()?.LastUpdatedDateField))
                    {
                        var trem = (from a in c where string.Compare(a.name, CEF.CurrentAuditService()?.LastUpdatedDateField, true) == 0 select a);

                        if (trem.Any())
                        {
                            c.Remove(trem.First());
                        }
                    }

                    if (!string.IsNullOrEmpty(CEF.CurrentAuditService()?.IsDeletedField))
                    {
                        var trem = (from a in c where string.Compare(a.name, CEF.CurrentAuditService()?.IsDeletedField, true) == 0 select a);

                        if (trem.Any())
                        {
                            c.Remove(trem.First());
                        }
                    }
                }

                // Apply column name filters if needed
                if (options.IncludeColumns != null)
                {
                    c = (from a in c where (from b in options.IncludeColumns where string.Compare(a.name, b, true) == 0 select b).Any() select a).ToList();
                }

                if (options.ExcludeColumns != null)
                {
                    c = (from a in c where !(from b in options.ExcludeColumns where string.Compare(a.name, b, true) == 0 select b).Any() select a).ToList();
                }

                // Get any available key for this type
                var keydef = KeyService.ResolveKeyDefinitionForType(typeof(T));

                List<string> finalName = new List<string>();
                List<Type> finalType = new List<Type>();

                // Actual schema write based on distinct list of columns and types
                foreach (var prop in (from n in (from a in c select a.name).Distinct() select new { Name = n, Type = (from t in c where string.Compare(t.name, n, true) == 0 orderby (t.type == null ? 1 : 0) select t.type).First() }))
                {
                    var restype = prop.Type;
                    var req = !(prop.Type.IsGenericType && prop.Type.GetGenericTypeDefinition() == typeof(Nullable<>));

                    if (!req)
                    {
                        restype = Nullable.GetUnderlyingType(prop.Type);
                    }

                    var rp = ValidationService.GetRequiredFor(typeof(T), prop.Name);

                    if (rp.HasValue)
                    {
                        req = rp.Value;
                    }

                    // Ignore non-primative ref types - things like property classes in generated code should interop with the base instance
                    if (restype.IsPrimitive || restype.IsValueType || restype.IsSerializable)
                    {
                        jw.WriteStartObject();
                        jw.WritePropertyName("cn");
                        jw.WriteValue(prop.Name);

                        if ((actmode & SerializationMode.IncludeType) != 0)
                        {
                            jw.WritePropertyName("dt");
                            jw.WriteValue(restype.Name.ToLower().Replace("system.", ""));
                            jw.WritePropertyName("key");
                            jw.WriteValue((from a in keydef where string.Compare(a, prop.Name, true) == 0 select a).Any());
                            jw.WritePropertyName("req");
                            jw.WriteValue(req);

                            // If there's a maxlength setting available, write it
                            var ml = ValidationService.GetMaxLengthFor(typeof(T), prop.Name);

                            if (ml.HasValue)
                            {
                                jw.WritePropertyName("maxlen");
                                jw.WriteValue(ml);
                            }
                        }

                        jw.WriteEndObject();

                        finalName.Add(prop.Name);
                        finalType.Add(restype);
                    }
                }

                // end schema
                jw.WriteEndArray();

                // Start data
                jw.WritePropertyName("rows");
                jw.WriteStartArray();

                var cdates = options.ConvertDates.GetValueOrDefault(Globals.PortableJSONConvertDates);

                IEnumerable<ICEFInfraWrapper> list = this.AllAsInfraWrapped();

                if (options.SortSpec != null)
                {
                    list = list.OrderBy(options.SortSpec);
                }

                if (options.FilterSpec != null)
                {
                    list = list.Where(options.FilterSpec);
                }

                foreach (var iw in list)
                {
                    if ((actmode & SerializationMode.OnlyChanged) == 0 || iw.GetRowState() != ObjectState.Unchanged)
                    {
                        jw.WriteStartArray();

                        for (int i = 0; i < finalName.Count; ++i)
                        {
                            var cv = iw.GetValue(finalName[i]);

                            if (cv == null)
                            {
                                string s = null;
                                jw.WriteValue(s);
                            }
                            else
                            {
                                if (finalType[i] == typeof(DateTime))
                                {
                                    var asdate = Convert.ToDateTime(cv);

                                    if (cdates == DateConversionMode.ToGMTAlways || (cdates == DateConversionMode.ToGMTWhenHasTime && asdate.TimeOfDay.Seconds > 0))
                                    {
                                        asdate = asdate.ToUniversalTime();
                                    }

                                    var d = Convert.ToInt64(asdate.Ticks - 621355968000000000L) / 10000L;
                                    jw.WriteValue(d);
                                }
                                else
                                {
                                    var cvt = cv.GetType();

                                    // For non-primative ref types, need to "flatten" properties
                                    if (cvt.IsValueType || cvt.IsPrimitive || cvt.IsSerializable)
                                    {
                                        jw.WriteValue(cv);
                                    }
                                }
                            }
                        }

                        jw.WriteEndArray();
                    }
                }

                // end data
                jw.WriteEndArray();
                jw.WriteEndObject();
            }

            return sb.ToString();
        }

        /// <summary>
        /// Serialization text on the EntitySet level turns into a JSON array, composed of individual object-per-collection member.
        /// The output format is lighter-weight than the portable format (no schema).
        /// </summary>
        /// <param name="mode"></param>
        /// <returns></returns>
        public string GetSerializationText(SerializationMode? mode = null)
        {
            StringBuilder sb = new StringBuilder(4096);
            var actmode = mode.GetValueOrDefault(CEF.CurrentServiceScope.Settings.SerializationMode);

            CEF.CurrentServiceScope.ReconcileModifiedState(null);

            using (var jw = new JsonTextWriter(new StringWriter(sb)))
            {
                jw.WriteStartArray();

                foreach (var i in this)
                {
                    var iw = i.AsInfraWrapped();
                    var rs = iw.GetRowState();

                    if ((rs != ObjectState.Unchanged && rs != ObjectState.Unlinked) || ((actmode & SerializationMode.OnlyChanged) == 0))
                    {
                        CEF.CurrentPCTService()?.SaveContents(jw, i, actmode, new Dictionary<object, bool>(Globals.DefaultDictionaryCapacity));
                    }
                }

                jw.WriteEndArray();
            }

            return sb.ToString();
        }

        private string GetKey(object o)
        {
            var kv = CEF.CurrentKeyService().GetKeyValues(o);

            if (kv?.Count > 0)
            {
                StringBuilder sb = new StringBuilder();

                foreach (var k in kv)
                {
                    sb.Append(k.value?.ToString());
                    sb.Append("~");
                }

                return sb.ToString();
            }

            return null;
        }

        public void RemoveItem(object o)
        {
            var cast = o as T;

            if (cast != null)
            {
                if (_contains.Contains(cast))
                {
                    this.Remove(cast);
                }
            }
        }

        /// <summary>
        /// Adds a new item to the collection, returning it. Adds in "inserted" state.
        /// </summary>
        /// <returns></returns>
        public T Add()
        {
            return CEF.NewObject<T>();
        }

        private void StartToAddWorkers()
        {
            if (Interlocked.Read(ref _toAddWorkers) == 0 && _toAdd.Count > 0)
            {
                Interlocked.Increment(ref _toAddWorkers);

                Task.Factory.StartNew(() =>
                {
                    try
                    {
                        using (new WriterLock(_lock))
                        {
                            while (_toAdd.Count > 0)
                            {
                                if (_toAdd.TryDequeue(out var t))
                                {
                                    base.Add(t);
                                }
                            }
                        }
                    }
                    finally
                    {
                        Interlocked.Decrement(ref _toAddWorkers);
                    }
                });
            }
        }

        public void WaitForQueuedAdds()
        {
            while (_toAdd.Count > 0)
            {
                StartToAddWorkers();
                Thread.Sleep(0);
            }
        }

        private ConcurrentQueue<T> _toAdd = new ConcurrentQueue<T>();
        private long _toAddWorkers = 0;

        public void QueuedAdd(T o)
        {
            using (var wl = new QuietWriterLock(_lock))
            {
                if (wl.IsActive)
                {
                    base.Add(o);
                }
                else
                {
                    _toAdd.Enqueue(o);
                    StartToAddWorkers();
                }
            }
        }

        public bool AddWrappedItem(object o)
        {
            if (o is T cast)
            {
                if (!_contains.Contains(cast))
                {
                    this.Add(cast);
                    return true;
                }
            }

            return false;
        }

        public void Initialize(ServiceScope ss, object parentContainer, string parentTypeName, string parentFieldName)
        {
            BoundScope = ss;
            ParentContainer = parentContainer;
            ParentTypeName = parentTypeName;
            ParentFieldName = parentFieldName;
        }

        public bool EnableIntegration
        {
            get;
            set;
        } = true;

        public void SuspendNotifications(bool stop)
        {
            if (stop)
            {
                Interlocked.Increment(ref _init);
            }
            else
            {
                Interlocked.Decrement(ref _init);
            }
        }

        public void BeginInit()
        {
            Interlocked.Increment(ref _init);
        }

        public void EndInit()
        {
            var was = _init;

            Interlocked.Decrement(ref _init);

            if (_init < 0)
                Interlocked.Exchange(ref _init, 0);

            if (was == 1)
            {
                WireDependencies();
                Initialized?.Invoke(this, EventArgs.Empty);
            }
        }

        public void DeleteAll(bool clear = false, bool cascade = false)
        {
            foreach (var i in this)
            {
                CEF.DeleteObject(i, cascade ? DeleteCascadeAction.Cascade : DeleteCascadeAction.SetNull);
            }

            if (clear)
            {
                this.Clear();
            }
        }

        #endregion

        #region "Internals"

        internal ServiceScope BoundScope
        {
            get;
            set;
        }

        internal object ParentContainer
        {
            get;
            set;
        }

        internal string ParentTypeName
        {
            get;
            set;
        }

        internal string ParentFieldName
        {
            get;
            set;
        }

        protected override void ClearItems()
        {
            _contains = new SlimConcurrentDictionary<T, bool>(Globals.DefaultLargerDictionaryCapacity);
            base.ClearItems();
        }

        private void WireDependencies()
        {
            foreach (var item in _toWire.ToList())
            {
                CEF.CurrentKeyService()?.WireDependents(item.AsUnwrapped(), item, BoundScope, this, null);
            }

            _toWire.Clear();
        }

        protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            if (Interlocked.Read(ref _init) == 0)
            {
                switch (e.Action)
                {
                    case NotifyCollectionChangedAction.Replace:
                        {
                            ProcessRemove(e.OldItems);
                            ProcessAdd(e.NewItems, e.NewStartingIndex);
                            break;
                        }

                    case NotifyCollectionChangedAction.Remove:
                        {
                            ProcessRemove(e.OldItems);
                            break;
                        }

                    case NotifyCollectionChangedAction.Add:
                        {
                            ProcessAdd(e.NewItems, e.NewStartingIndex);
                            break;
                        }
                }

                base.OnCollectionChanged(e);
            }
            else
            {
                // Need to defer wiring dependencies
                if (e.NewItems?.Count > 0)
                {
                    foreach (T ni in e.NewItems)
                    {
                        _toWire.Add(ni);
                    }
                }
            }
        }

        protected virtual void ProcessRemove(IList oldItems)
        {
            foreach (var oi in oldItems.Cast<T>())
            {
                _contains.Remove(oi);
            }

            if (!EnableIntegration)
            {
                return;
            }

            if (ParentContainer != null)
            {
                var oiCopy = (from a in oldItems.Cast<Object>() select a).ToList();

                foreach (var oi in oiCopy)
                {
                    if (oi != null)
                    {
                        // Attempt to establish a FK relationship, carry parent key down
                        CEF.CurrentKeyService()?.UnlinkChildFromParentContainer(BoundScope, ParentTypeName, ParentFieldName, ParentContainer, oi);
                    }
                }
            }
        }        

        protected virtual void ProcessAdd(IList newItems, int newStartingIndex)
        {
            var niCopy = (from a in newItems.Cast<Object>() select a).ToList();

            foreach (T ni in niCopy)
            {
                _contains[ni] = true;
            }

            if (!EnableIntegration)
            {
                return;
            }

            if (BoundScope != null)
            {
                // First, we inspect to see if we have a wrapped object or not - if not, we try to do so and REPLACE the current item
                if (!BoundScope.Settings.EntitySetUsesUnwrapped)
                {
                    var idx2 = 0;
                    foreach (var ni in niCopy.ToArray())
                    {
                        if (ni != null)
                        {
                            if (BoundScope.InternalCreateAddBase(ni, true, null, null, null, new Dictionary<object, object>(Globals.DefaultDictionaryCapacity)) is ICEFWrapper w)
                            {
                                var cast = w as T;

                                if (ni != cast)
                                {
                                    try
                                    {
                                        this.SuspendNotifications(true);

                                        using (new WriterLock(_lock))
                                        {
                                            this.Replace(ni as T, cast);

                                            _contains.Add(cast, true);
                                            _contains.Remove(newItems[idx2] as T);
                                        }
                                    }
                                    finally
                                    {
                                        this.SuspendNotifications(false);
                                    }
                                }

                                niCopy[idx2] = cast;
                            }
                        }

                        idx2++;
                    }
                }
            }

            if (ParentContainer != null)
            {
                foreach (var ni in niCopy)
                {
                    if (ni != null)
                    {
                        // Attempt to establish a FK relationship, carry parent key down
                        CEF.CurrentKeyService()?.LinkChildInParentContainer(BoundScope, ParentTypeName, ParentFieldName, ParentContainer, ni);
                    }
                }
            }
        }

        #endregion
    }
}
