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
***********************************************************************/
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

        public void AddWrappedItem(object o)
        {
            var cast = o as T;

            if (cast != null)
            {
                if (!_contains.Contains(cast))
                {
                    this.Add(cast);
                }
            }
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
