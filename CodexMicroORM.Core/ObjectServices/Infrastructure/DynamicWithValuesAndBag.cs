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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using Newtonsoft.Json;
using CodexMicroORM.Core.Helper;
using System.Linq;

namespace CodexMicroORM.Core.Services
{
    /// <summary>
    /// An infrastructure wrapper that provides original value and row state tracking.
    /// </summary>
    public class DynamicWithValuesAndBag : DynamicWithBag, IDisposable
    {
        protected Dictionary<string, object> _originalValues = new Dictionary<string, object>(Globals.DefaultDictionaryCapacity, Globals.CurrentStringComparer);
        protected ObjectState _rowState;

        public event EventHandler<DirtyStateChangeEventArgs> DirtyStateChange;

        internal DynamicWithValuesAndBag(object o, ObjectState irs, IDictionary<string, object> props, IDictionary<string, Type> types) : base(o, props, types)
        {
            if (o is INotifyPropertyChanged)
            {
                ((INotifyPropertyChanged)o).PropertyChanged += CEFValueTrackingWrapper_PropertyChanged;
            }

            using (new WriterLock(_lock))
            {
                AcceptChanges();
                SetRowState(irs);
            }
        }

        public override string ToString()
        {
            return $"DynamicWithValuesAndBag (for {_source?.GetType().Name}, {_rowState}, {string.Join("/", (from a in this.GetAllValues() select $"{a.Key}={a.Value}").ToArray())})";
        }

        public override ObjectState GetRowState()
        {
            return State;
        }

        public override void SetRowState(ObjectState rs)
        {
            using (new WriterLock(_lock))
            {
                if (_rowState != rs)
                {
                    CEFDebug.WriteInfo($"RowState={rs}, {_source?.GetBaseType().Name}", _source);
                    _rowState = rs;

                    if (rs == ObjectState.Unchanged)
                    {
                        _isBagChanged = false;
                    }
                }
            }
        }

        public ObjectState State
        {
            get
            {
                using (new ReaderLock(_lock))
                {
                    if (_isBagChanged && _rowState == ObjectState.Unchanged)
                    {
                        return ObjectState.Modified;
                    }

                    return _rowState;
                }
            }
        }

        public bool ReconcileModifiedState(Action<string, object, object> onChanged = null, bool force = false)
        {
            // For cases where live binding was not possible, tries to identify possible changes in object state (typically at the time of saving)
            if (force || _rowState == ObjectState.Unchanged)
            {
                foreach (var oval in _originalValues)
                {
                    var nval = GetValue(oval.Key);

                    if (!oval.Value.IsSame(nval))
                    {
                        SetRowState(ObjectState.Modified);
                        onChanged?.Invoke(oval.Key, oval, nval);
                        return true;
                    }
                }
            }

            return false;
        }

        public override WrappingSupport SupportsWrapping()
        {
            return WrappingSupport.OriginalValues | WrappingSupport.PropertyBag;
        }

        private ObjectState SafeGetState()
        {
            using (new ReaderLock(_lock))
            {
                return _rowState;
            }
        }

        public void Delete()
        {
            if (SafeGetState() == ObjectState.Added)
            {
                SetRowState(ObjectState.Unlinked);
            }
            else
            {
                SetRowState(ObjectState.Deleted);
            }
        }

        protected override void OnPropertyChanged(string propName, object oldVal, object newVal, bool isBag)
        {
            if (propName[0] != KeyService.SHADOW_PROP_PREFIX)
            {
                base.OnPropertyChanged(propName, oldVal, newVal, isBag);

                if (SafeGetState() == ObjectState.Unchanged)
                {
                    SetRowState(ObjectState.Modified);
                    DirtyStateChange?.Invoke(this, new DirtyStateChangeEventArgs(ObjectState.Modified));
                }
            }
        }

        private void CEFValueTrackingWrapper_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            var (readable, value) = _source.FastPropertyReadableWithValue(e.PropertyName);

            if (readable)
            {
                var oldval = _originalValues[e.PropertyName];

                if (!oldval.IsSame(value))
                {
                    OnPropertyChanged(e.PropertyName, oldval, value, _valueBag.ContainsKey(e.PropertyName));
                }
            }
        }

        public override void AcceptChanges()
        {
            bool changed = false;

            using (new WriterLock(_lock))
            {
                if (_rowState == ObjectState.Deleted)
                {
                    SetRowState(ObjectState.Unlinked);
                    return;
                }

                if (_originalValues != null)
                {
                    if (_source != null)
                    {
                        // Handle CLR properties that are R/W
                        foreach (var pi in _source.FastGetAllProperties(true, true))
                        {
                            _originalValues[pi.name] = _source.FastGetValue(pi.name);
                        }
                    }

                    foreach (var kvp in _valueBag)
                    {
                        _originalValues[kvp.Key] = kvp.Value;
                    }
                }

                changed = (_rowState != ObjectState.Unchanged);

                SetRowState(ObjectState.Unchanged);
                _isBagChanged = false;
            }

            if (changed)
            {
                DirtyStateChange?.Invoke(this, new DirtyStateChangeEventArgs(ObjectState.Unchanged));
            }
        }

        public override void FinalizeObjectContents(JsonTextWriter tw, SerializationMode mode)
        {
            using (new ReaderLock(_lock))
            {
                base.FinalizeObjectContents(tw, mode);

                if ((mode & SerializationMode.ObjectState) != 0)
                {
                    tw.WritePropertyName(Globals.SerializationStatePropertyName);

                    if (Globals.SerializationStateAsInteger)
                    {
                        tw.WriteValue((int)State);
                    }
                    else
                    {
                        tw.WriteValue(State.ToString());
                    }
                }
            }
        }

        public override object GetOriginalValue(string propName, bool throwIfNotSet)
        {
            using (new ReaderLock(_lock))
            {
                if (!_originalValues.ContainsKey(propName))
                {
                    if (throwIfNotSet)
                    {
                        throw new CEFInvalidOperationException($"Property {propName} does not exist on this wrapper.");
                    }

                    return null;
                }

                return _originalValues[propName];
            }
        }

        #region IDisposable Support
        private bool _disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    if (_source is INotifyPropertyChanged)
                    {
                        ((INotifyPropertyChanged)_source).PropertyChanged -= CEFValueTrackingWrapper_PropertyChanged;
                    }
                }

                _originalValues = null;
                _source = null;
                _disposedValue = true;
            }
        }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            Dispose(true);
        }
        #endregion

    }

    public class DirtyStateChangeEventArgs : EventArgs
    {
        public ObjectState NewState
        {
            get;
            private set;
        }

        public DirtyStateChangeEventArgs(ObjectState newState)
        {
            NewState = newState;
        }
    }

}
