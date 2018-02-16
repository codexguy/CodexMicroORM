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
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Threading;
using System.Collections;
using System.Collections.Concurrent;
using Newtonsoft.Json;
using System.Text;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace CodexMicroORM.Core.Services
{
    /// <summary>
    /// EntitySet's are specialized collections of objects that CEF can leverage.
    /// This does not mean you can't use plain collections with CEF, but CEF will make an attempt to "promote" collections to EntitySet's where it can.
    /// You should not care much about this implementation detail: handle most of your collections as ICollection<T> or IList<T>.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class EntitySet<T> : ObservableCollection<T>, ICEFList, ICEFSerializable, ISupportInitializeNotification where T : class, new()
    {
        #region "Private state"

        private ConcurrentDictionary<T, bool> _contains = new ConcurrentDictionary<T, bool>();
        private long _init = 1;
        private int? _firstToWire = null;
        private int _nestedOp = 0;

        #endregion

        #region "Constructors"

        public EntitySet() : base()
        {
            BoundScope = CEF.CurrentServiceScope;
            EndInit();
        }

        public EntitySet(ServiceScope ss) : base()
        {
            BoundScope = ss;
            EndInit();
        }

        public EntitySet(IEnumerable<T> source) : base()
        {
            BoundScope = CEF.CurrentServiceScope;

            foreach (var i in source)
            {
                this.Add(CEF.IncludeObject<T>(i, ObjectState.Unchanged));
            }

            EndInit();
        }

        #endregion

        #region "Public methods"

        public bool IsInitialized => Interlocked.Read(ref _init) == 0;

        public event EventHandler Initialized;

        public bool ContainsItem(object o)
        {
            if (o == null)
                return false;

            return this._contains.ContainsKey(o as T);
        }

        public bool IsDirty()
        {
            return (from b in (from a in this let iw = a.AsInfraWrapped() where iw != null select iw.GetRowState()) where b != ObjectState.Unchanged select b).Any();
        }

        public void PopulateFromSerializationText(string json)
        {
            // Must be an array...
            if (!Regex.IsMatch(json, @"^\s*\[") || !Regex.IsMatch(json, @"\]\s*$"))
            {
                throw new CEFInvalidOperationException("JSON provided is not an array (must be to deserialize a service scope).");
            }

            foreach (var i in CEF.CurrentPCTService()?.GetItemsFromSerializationText<T>(json))
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
                        CEF.CurrentPCTService()?.SaveContents(jw, i, actmode, new ConcurrentDictionary<object, bool>());
                    }
                }

                jw.WriteEndArray();
            }

            return sb.ToString();
        }

        public void RemoveItem(object o)
        {
            var cast = o as T;

            if (cast != null)
            {
                if (this._contains.ContainsKey(cast))
                {
                    this.Remove(cast);
                }
            }
        }

        public void AddWrappedItem(object o)
        {
            var cast = o as T;

            if (cast != null)
            {
                if (!this._contains.ContainsKey(cast))
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
                WireDependencies(_firstToWire.GetValueOrDefault(), null);
                _firstToWire = null;

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
            _contains = new ConcurrentDictionary<T, bool>();
            base.ClearItems();
        }

        private void WireDependencies(int? firstToWire, int? lastToWire)
        {
            for (int i = firstToWire.GetValueOrDefault(); i <= lastToWire.GetValueOrDefault() && i < this.Count; ++i)
            {
                var item = this[i];
                CEF.CurrentKeyService()?.WireDependents(item.AsUnwrapped(), item, BoundScope, this, null);
            }
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
        }

        private void ProcessRemove(IList oldItems)
        {
            foreach (var oi in oldItems.Cast<T>())
            {
                _contains.TryRemove(oi, out bool t);
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

        private void ProcessAdd(IList newItems, int newStartingIndex)
        {
            foreach (var ni in newItems.Cast<T>())
            {
                _contains[ni] = true;
            }

            Interlocked.Increment(ref _nestedOp);

            try
            {
                var niCopy = (from a in newItems.Cast<Object>() select a).ToList();

                if (BoundScope != null)
                {
                    var idx = newStartingIndex;

                    if (!_firstToWire.HasValue || idx < _firstToWire)
                    {
                        _firstToWire = idx;
                    }

                    // First, we inspect to see if we have a wrapped object or not - if not, we try to do so and REPLACE the current item
                    var idx2 = 0;
                    foreach (var ni in niCopy.ToArray())
                    {
                        if (ni != null)
                        {
                            var w = BoundScope.InternalCreateAddBase(ni, true, null, null, null, new ConcurrentDictionary<object, object>()) as ICEFWrapper;

                            if (w != null)
                            {
                                var cast = w as T;

                                if (this[idx] != cast)
                                {
                                    try
                                    {
                                        this.SuspendNotifications(true);

                                        this[idx] = cast;

                                        _contains[cast] = true;
                                        _contains.TryRemove(newItems[idx2] as T, out bool t);
                                    }
                                    finally
                                    {
                                        this.SuspendNotifications(false);
                                    }
                                }

                                niCopy[idx2] = this[idx];
                            }
                        }

                        idx++;
                        idx2++;
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
            finally
            {
                Interlocked.Decrement(ref _nestedOp);
            }
        }

        #endregion
    }
}
