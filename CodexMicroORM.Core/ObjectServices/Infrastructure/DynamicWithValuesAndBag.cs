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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using Newtonsoft.Json;
using CodexMicroORM.Core.Helper;
using System.Linq;
using System.Text.RegularExpressions;

namespace CodexMicroORM.Core.Services
{
    /// <summary>
    /// An infrastructure wrapper that provides original value and row state tracking.
    /// </summary>
    public class DynamicWithValuesAndBag : DynamicWithBag, IDisposable
    {
        protected Dictionary<string, object?> _originalValues = new(Globals.DefaultDictionaryCapacity, Globals.CurrentStringComparer);
        protected ObjectState _rowState;
#if DEBUG
        private static readonly bool _debugStopEnabled = true;
#endif

        public event EventHandler<DirtyStateChangeEventArgs>? DirtyStateChange;

        public static List<(Type type, ObjectState? state, string ignoreonstack)> DebugStopOnChangeImmediateByType
        {
            get;
        } = [];

        internal DynamicWithValuesAndBag(object o, ObjectState irs, IDictionary<string, object?>? props, IDictionary<string, Type>? types) : base(o, props, types)
        {
            if (o is INotifyPropertyChanged changed)
            {
                changed.PropertyChanged += CEFValueTrackingWrapper_PropertyChanged;
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

        public override ObjectState GetRowState(bool canCheckBag = true)
        {
            if (canCheckBag)
            {
                return ResolvedState;
            }

            using (new ReaderLock(_lock))
            {
                return _rowState;
            }
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

#if DEBUG
                    if (_debugStopEnabled && System.Diagnostics.Debugger.IsAttached)
                    {
                        var (type, state, ignoreonstack) = (from a in DebugStopOnChangeImmediateByType
                                  where a.type == _source?.GetType()
                                  && (!a.state.HasValue || a.state.Value == rs)
                                  select a).FirstOrDefault();

                        if (type != null)
                        {
                            if (ignoreonstack != null)
                            {
                                System.Diagnostics.StackTrace st = new();

                                foreach (var frame in st.GetFrames())
                                {
                                    if (Regex.IsMatch(frame.GetMethod()?.Name ?? "", ignoreonstack, RegexOptions.IgnoreCase))
                                    {
                                        return;
                                    }
                                }
                            }

                            System.Diagnostics.Debugger.Break();
                        }
                    }
#endif
                }
            }
        }

        private ObjectState ResolvedState
        {
            get
            {
                using (new ReaderLock(_lock))
                {
                    return _isBagChanged && _rowState == ObjectState.Unchanged ? ObjectState.Modified : _rowState;
                }
            }
        }

        public static Func<Type, string, bool>? CheckIgnorePropertyModifiedState
        {
            get;
            set;
        }

        public static bool ReconcileModifiedIgnoresValueBag
        {
            get;
            set;
        } = true;

        public bool ReconcileModifiedState(Action<string, object?, object?>? onChanged = null, bool force = false)
        {
            if (_disposedValue)
            {
                return false;
            }

            // For cases where live binding was not possible, tries to identify possible changes in object state (typically at the time of saving)
            if ((!force && _rowState == ObjectState.Unchanged) || (force && (_rowState == ObjectState.Modified || _rowState == ObjectState.ModifiedPriority || _rowState == ObjectState.Unchanged)))
            {
                foreach (var oval in _originalValues)
                {
                    var nval = GetValue(oval.Key);

                    if (CheckIgnorePropertyModifiedState != null && (_source == null || CheckIgnorePropertyModifiedState.Invoke(_source.GetBaseType(), oval.Key)))
                    {
                        continue;
                    }

                    if (ReconcileModifiedIgnoresValueBag && _valueBag.ContainsKey(oval.Key))
                    {
                        continue;
                    }

                    if (!oval.Value.IsSame(nval))
                    {
                        if (_rowState == ObjectState.Unchanged)
                        {
                            if (!Globals.GlobalPropertiesExcludedFromDirtyCheck.Contains(oval.Key))
                            {
                                SetRowState(ObjectState.Modified);
                                onChanged?.Invoke(oval.Key, oval, nval);
                                return true;
                            }
                        }
                        else
                        {
                            return false;
                        }
                    }
                }

                // We can optionally *un*modify an object if in force mode, all original vals = current vals (this is not the default)
                if (force && (_rowState == ObjectState.Modified || _rowState == ObjectState.ModifiedPriority))
                {
                    SetRowState(ObjectState.Unchanged);
                    return true;
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

        protected override void OnPropertyChanged(string propName, object? oldVal, object? newVal, bool isBag)
        {
            if (propName[0] != KeyService.SHADOW_PROP_PREFIX)
            {
                base.OnPropertyChanged(propName, oldVal, newVal, isBag);

                Globals.GlobalPropertyChangePreview?.Invoke(this, propName, oldVal, newVal);

                if (SafeGetState() == ObjectState.Unchanged)
                {
                    if (!Globals.GlobalPropertiesExcludedFromDirtyCheck.Contains(propName))
                    {
#if DEBUG
                        if (CEFDebug.DebugEnabled)
                        {
                            CEFDebug.WriteInfo($"PropChange: {propName}, {oldVal}, {newVal}");
                        }
#endif
                        SetRowState(ObjectState.Modified);
                        DirtyStateChange?.Invoke(this, new DirtyStateChangeEventArgs(ObjectState.Modified));
                    }
                }
            }
        }

        private void CEFValueTrackingWrapper_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_source != null && !_disposedValue)
            {
                var (readable, value) = _source.FastPropertyReadableWithValue(e.PropertyName ?? throw new InvalidOperationException("Missing PropertyName."));

                if (readable)
                {
                    if (_originalValues.TryGetValue(e.PropertyName, out var oldval))
                    {
                        if (!oldval.IsSame(value))
                        {
                            OnPropertyChanged(e.PropertyName, oldval, value, _valueBag.ContainsKey(e.PropertyName));
                        }
                    }
                    else
                    {
                        OnPropertyChanged(e.PropertyName, oldval, value, _valueBag.ContainsKey(e.PropertyName));
                    }
                }
            }
        }

        public override void AcceptChanges()
        {
            if (_disposedValue)
            {
                return;
            }

            bool changed = false;

            using (new WriterLock(_lock))
            {
                if (_rowState == ObjectState.Deleted)
                {
                    SetRowState(ObjectState.Unlinked);
                    return;
                }

                if (_source != null)
                {
                    // Handle CLR properties that are R/W
                    foreach (var (name, type, readable, writeable) in _source.FastGetAllProperties(true, true))
                    {
                        if (!CEF.RegisteredPropertyNameTreatReadOnly.Contains(name))
                        {
                            _originalValues[name] = _source.FastGetValue(name);
                        }
                    }
                }

                foreach (var kvp in _valueBag)
                {
                    _originalValues[kvp.Key] = kvp.Value;
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
                        tw.WriteValue((int)ResolvedState);
                    }
                    else
                    {
                        tw.WriteValue(ResolvedState.ToString());
                    }
                }
            }
        }

        public override void SetOriginalValue(string propName, object? value)
        {
            if (_disposedValue)
            {
                return;
            }

            using (new WriterLock(_lock))
            {
                _originalValues[propName] = value;
            }
        }

        public override object? GetOriginalValue(string propName, bool throwIfNotSet)
        {
            if (_disposedValue)
            {
                throw new CEFInvalidStateException(InvalidStateType.BadAction, "Cannot use disposed object.");
            }

            using (new ReaderLock(_lock))
            {
                if (!_originalValues.TryGetValue(propName, out var ov))
                {
                    if (throwIfNotSet)
                    {
                        throw new CEFInvalidStateException(InvalidStateType.BadAction, $"Property {propName} does not exist on this wrapper.");
                    }

                    return null;
                }

                return ov;
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
                    if (_source is INotifyPropertyChanged changed)
                    {
                        changed.PropertyChanged -= CEFValueTrackingWrapper_PropertyChanged;
                    }
                }

                //_originalValues = null!;
                //_source = null!;
                _disposedValue = true;
            }
        }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
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
