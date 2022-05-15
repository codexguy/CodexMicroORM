/***********************************************************************
Copyright 2022 CodeX Enterprises LLC

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
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using CodexMicroORM.Core.Helper;

namespace CodexMicroORM.Core.Services
{
    /// <summary>
    /// DynamicBase provides original value / change tracking services, plus property bag services to support "extra" fields.
    /// Although portions are thread-safe, the expectation is these are ServiceScope bound which implies per thread and not likely to encounter MT issues.
    /// Disposal is important here since we have an event handler observing the source for changes. (Infrastructure cascades disposal.)
    /// Data types are inferred based on source values OR by explicit definition - latter is preferred since if source value is null, we can't infer real type (assumes object).
    /// </summary>
    public class DynamicWithBag : DynamicObject, ICEFInfraWrapper, ICEFSerializable
    {
        protected RWLockInfo _lock = new() { AllowDirtyReads = false };
        protected Dictionary<string, object?> _valueBag = new(Globals.DefaultDictionaryCapacity * 3, Globals.CurrentStringComparer);
        protected Dictionary<string, Type> _preferredType = new(Globals.DefaultDictionaryCapacity * 3, Globals.CurrentStringComparer);
        protected object _source;
        protected bool _isBagChanged = false;
        protected int _shadowPropCount = 0;
        private bool _allowPropChanged = true;

        internal DynamicWithBag(object o, IDictionary<string, object?>? props, IDictionary<string, Type>? types)
        {
            _source = o;
            SetInitialProps(props, types);
        }

        protected virtual void SetInitialProps(IDictionary<string, object?>? props, IDictionary<string, Type>? types)
        {
            if (props != null)
            {
                var allowedPropChange = _allowPropChanged;

                try
                {
                    _allowPropChanged = false;

                    foreach (var prop in props)
                    {
                        SetValue(prop.Key, prop.Value);
                    }
                }
                finally
                {
                    _allowPropChanged = allowedPropChange;
                }
            }

            if (types != null)
            {
                using var wl = new WriterLock(_lock);

                foreach (var prop in types)
                {
                    _preferredType[prop.Key] = prop.Value;
                }
            }
        }

        public override string ToString()
        {
            return $"DynamicWithBag (for {_source?.GetType().Name}, {string.Join("/", (from a in this.GetAllValues() select $"{a.Key}={a.Value}").ToArray())})";
        }

        public override bool Equals(object obj)
        {
            if (_source != null)
            {
                var uw1 = this.AsUnwrapped();
                var uw2 = obj.AsUnwrapped();
                return uw1 == uw2;
            }

            return base.Equals(obj);
        }

        public override int GetHashCode()
        {
            if (_source != null)
            {
                var uw1 = this.AsUnwrapped();

                if (uw1 == null)
                {
                    return 0;
                }

                return uw1.GetHashCode();
            }

            return base.GetHashCode();
        }

        protected virtual void OnPropertyChanged(string propName, object? oldVal, object? newVal, bool isBag)
        {
        }

        /// <summary>
        /// Used by DynamicObject to return values for dynamic properties. Internal use only.
        /// </summary>
        /// <param name="binder"></param>
        /// <param name="result"></param>
        /// <returns></returns>
        public override bool TryGetMember(GetMemberBinder binder, out object? result)
        {
            using var rl = new ReaderLock(_lock);

            if (_shadowPropCount > 0 && _valueBag.ContainsKey(KeyService.SHADOW_PROP_PREFIX + binder.Name))
            {
                result = _valueBag[KeyService.SHADOW_PROP_PREFIX + binder.Name];
                return true;
            }

            if (_source != null)
            {
                if (!_source.FastPropertyWriteable(binder.Name) && _valueBag.ContainsKey(binder.Name))
                {
                    result = _valueBag[binder.Name];
                    return true;
                }

                var (readable, value) = _source.FastPropertyReadableWithValue(binder.Name);

                if (readable)
                {
                    result = value;
                    return true;
                }
            }

            if (_valueBag.ContainsKey(binder.Name))
            {
                result = _valueBag[binder.Name];
                return true;
            }

            result = null;
            return false;
        }

        /// <summary>
        /// Used by DynamicObject to set values for dynamic properties. Internal use only.
        /// </summary>
        /// <param name="binder"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public override bool TrySetMember(SetMemberBinder binder, object value)
        {
            return SetValue(binder.Name, value);
        }

        public Type? GetPropertyType(string propName)
        {
            using var rl = new ReaderLock(_lock);

            if (_source != null)
            {
                var pi = _source.FastGetAllProperties(null, null, propName);

                if (pi.Any())
                {
                    return pi.First().type;
                }
            }

            if (_preferredType.ContainsKey(propName))
            {
                return _preferredType[propName];
            }

            if (_valueBag.ContainsKey(propName))
            {
                if (_valueBag[propName] == null)
                {
                    return typeof(object);
                }

                return _valueBag[propName]?.GetType();
            }

            return null;
        }

        public virtual object? GetValue(string propName)
        {
            using var rl = new ReaderLock(_lock);

            if (_shadowPropCount > 0 && _valueBag.ContainsKey(KeyService.SHADOW_PROP_PREFIX + propName))
            {
                return _valueBag[KeyService.SHADOW_PROP_PREFIX + propName];
            }

            if (_valueBag.ContainsKey(propName))
            {
                return _valueBag[propName];
            }

            if (_source != null)
            {
                var (readable, value) = _source.FastPropertyReadableWithValue(propName);

                if (readable)
                {
                    return value;
                }
            }

            return null;
        }

        private object? InternalChangeType(object? source, Type targetType)
        {
            if (source == null)
            {
                return null;
            }

            var asstr = source.ToString();

            if (targetType == typeof(string))
            {
                return asstr;
            }

            var valtype = source.GetType();

            if (valtype == targetType)
            {
                return source;
            }

            var ntt = Nullable.GetUnderlyingType(targetType);

            if (ntt != null)
            {
                if (ntt == valtype)
                {
                    return Activator.CreateInstance(targetType, source);
                }

                if (!string.IsNullOrEmpty(asstr))
                {
                    if (ntt.IsEnum)
                    {
                        return Activator.CreateInstance(targetType, Enum.Parse(ntt, asstr));
                    }

                    if (ntt == typeof(Guid))
                    {
                        return new Guid(asstr);
                    }

                    if (ntt == typeof(TimeSpan))
                    {
                        if (TimeSpan.TryParse(asstr, out TimeSpan pts))
                        {
                            return pts;
                        }
                    }

                    if (ntt == typeof(OnlyDate))
                    {
                        if (valtype == typeof(DateTime))
                        {
                            return new OnlyDate((DateTime)source);
                        }

                        if (OnlyDate.TryParse(asstr, out OnlyDate dov))
                        {
                            return dov;
                        }
                    }

                    if (ntt == typeof(DateTime))
                    {
                        if (valtype == typeof(int) || valtype == typeof(long))
                        {
                            if (OnlyDate.TryParse(asstr, out OnlyDate dov))
                            {
                                return dov.ToDateTime();
                            }
                        }
                    }
                }

                if (source is IConvertible)
                {
                    return Activator.CreateInstance(targetType, Convert.ChangeType(source, ntt));
                }

                throw new InvalidCastException("Cannot coerce type.");
            }

            if (!string.IsNullOrEmpty(asstr))
            {
                if (targetType.IsEnum)
                {
                    return Enum.Parse(targetType, asstr);
                }

                if (targetType == typeof(Guid))
                {
                    return new Guid(asstr);
                }

                if (targetType == typeof(TimeSpan))
                {
                    if (TimeSpan.TryParse(asstr, out TimeSpan pts))
                    {
                        return pts;
                    }
                }

                if (targetType == typeof(OnlyDate))
                {
                    if (valtype == typeof(DateTime))
                    {
                        return new OnlyDate((DateTime)source);
                    }

                    if (OnlyDate.TryParse(asstr, out OnlyDate dov))
                    {
                        return dov;
                    }
                }

                if (targetType == typeof(DateTime))
                {
                    if (valtype == typeof(int) || valtype == typeof(long))
                    {
                        if (OnlyDate.TryParse(asstr, out OnlyDate dov))
                        {
                            return dov.ToDateTime();
                        }
                    }
                }
            }

            if (source is IConvertible)
            {
                return Convert.ChangeType(source, targetType);
            }

            if (!valtype.IsValueType)
            {
                return source;
            }

            throw new InvalidCastException("Cannot coerce type.");
        }

        public void SetPreferredType(string propName, Type preferredType, bool isRequired = false)
        {
            if (preferredType.IsValueType && !isRequired && Nullable.GetUnderlyingType(preferredType) == null)
            {
                preferredType = typeof(Nullable<>).MakeGenericType(preferredType);
            }

            using var wl = new WriterLock(_lock);
            _preferredType[propName] = preferredType;
        }

        public virtual bool SetValue(string propName, object? value, Type? preferredType = null, bool isRequired = false, bool canUseBag = true)
        {
            Type? pt = null;

            if (preferredType != null)
            {
                if (preferredType.IsValueType && !isRequired && !(preferredType.IsGenericType && preferredType.GetGenericTypeDefinition() == typeof(Nullable<>)))
                {
                    preferredType = typeof(Nullable<>).MakeGenericType(preferredType);
                }

                using var wl2 = new WriterLock(_lock);

                _preferredType[propName] = preferredType;
                pt = preferredType;
            }

            using var rl = new ReaderLock(_lock);

            if (pt != null || _preferredType.TryGetValue(propName, out pt))
            {
                try
                {
                    bool isnull = pt.IsGenericType && pt.GetGenericTypeDefinition() == typeof(Nullable<>);

                    if (value != null)
                    {
                        if (value.GetType() != pt && (!isnull || value.GetType() != Nullable.GetUnderlyingType(pt)))
                        {
                            if (value is IConvertible)
                            {
                                if (isnull)
                                {
                                    value = InternalChangeType(value, Nullable.GetUnderlyingType(pt));
                                }
                                else
                                {
                                    value = InternalChangeType(value, pt);
                                }
                            }
                            else
                            {
                                // Coercing only makes sense of type other than object
                                if (pt != null && pt != typeof(object))
                                {
                                    throw new InvalidCastException("Cannot coerce type.");
                                }
                            }
                        }
                    }
                    else
                    {
                        if (isnull)
                        {
                            value = pt.FastCreateNoParm();
                        }
                    }
                }
                catch
                {
                }
            }

            // Special case - if setting a property where there exists a shadow property already AND the new prop is NOT default - blow away the shadow property!
            if (value != null && _shadowPropCount > 0 && propName[0] != KeyService.SHADOW_PROP_PREFIX && _valueBag.ContainsKey(KeyService.SHADOW_PROP_PREFIX + propName))
            {
                var defVal = WrappingHelper.GetDefaultForType(value.GetType());

                if (!defVal.IsSame(value))
                {
                    var k = KeyService.SHADOW_PROP_PREFIX + propName;

                    if (_valueBag.ContainsKey(k))
                    {
                        _valueBag.Remove(k);
                        _shadowPropCount--;
                    }
                }
            }

            if (_source != null && !CEF.RegisteredPropertyNameTreatReadOnly.Contains(propName))
            {
                var info = _source.FastGetAllProperties(true, true, propName);

                if (info.Any())
                {
                    var oldVal = _source.FastGetValue(propName);

                    if (!oldVal.IsSame(value))
                    {
                        rl.Release();
                        using var wl2 = new WriterLock(_lock);

                        _source.FastSetValue(propName, InternalChangeType(value, info.First().type));

                        wl2.Release();

                        if (_allowPropChanged)
                        {
                            OnPropertyChanged(propName, oldVal, value, false);
                        }

                        return true;
                    }

                    return false;
                }
            }

            if (!canUseBag)
            {
                return false;
            }

            var isSpecial = propName[0] == KeyService.SPECIAL_PROP_PREFIX;

            if (_valueBag.ContainsKey(propName))
            {
                if (propName[0] != KeyService.SHADOW_PROP_PREFIX)
                {
                    var oldVal = _valueBag[propName];

                    if (!oldVal.IsSame(value))
                    {
                        if (!isSpecial)
                        {
                            _isBagChanged = true;
                        }

                        rl.Release();
                        using var wl2 = new WriterLock(_lock);

                        _valueBag[propName] = value;

                        wl2.Release();

                        if (!isSpecial && _allowPropChanged)
                        {
                            OnPropertyChanged(propName, oldVal, value, true);
                        }

                        return true;
                    }
                }

                return false;
            }

            rl.Release();
            using var wl = new WriterLock(_lock);

            _valueBag[propName] = value;

            if (propName[0] == KeyService.SHADOW_PROP_PREFIX)
            {
                _shadowPropCount++;
                return false;
            }

            if (!isSpecial)
            {
                _isBagChanged = true;
            }

            wl.Release();

            if (!isSpecial && _allowPropChanged)
            {
                OnPropertyChanged(propName, null, value, true);
            }

            return true;
        }

        public override IEnumerable<string> GetDynamicMemberNames()
        {
            using var rl = new ReaderLock(_lock);

            if (_source != null)
            {
                return _valueBag.Keys.Concat(from pi in _source.FastGetAllProperties() select pi.name).ToArray();
            }

            return Array.Empty<string>();
        }

        public virtual WrappingSupport SupportsWrapping()
        {
            return WrappingSupport.PropertyBag;
        }

        public virtual void UpdateData()
        {
            if (_source != null)
            {
                HashSet<object> hs = new()
                {
                    _source
                };
                CEF.CurrentServiceScope.ReconcileModifiedState(hs);
            }
        }

        public void RemoveProperty(string propName)
        {
            using var wl = new WriterLock(_lock);

            if (_valueBag.ContainsKey(propName))
            {
                _valueBag.Remove(propName);

                if (propName[0] == KeyService.SHADOW_PROP_PREFIX)
                {
                    _shadowPropCount--;
                }
            }
        }

        public bool HasShadowProperty(string propName)
        {
            if (_shadowPropCount == 0)
            {
                return false;
            }

            using var rl = new ReaderLock(_lock);

            if (_valueBag.ContainsKey(KeyService.SHADOW_PROP_PREFIX + propName))
            {
                return true;
            }

            return false;
        }

        public bool HasProperty(string propName)
        {
            if (_source == null)
            {
                return false;
            }

            if (_source.FastPropertyReadable(propName))
            {
                return true;
            }

            using var rl = new ReaderLock(_lock);

            if (_valueBag.ContainsKey(propName))
            {
                return true;
            }

            return false;
        }

        public object GetWrappedObject()
        {
            return _source;
        }

        public virtual ObjectState GetRowState(bool canCheckBag = true)
        {
            return ObjectState.Unchanged;
        }

        public virtual void SetRowState(ObjectState rs)
        {
            throw new NotSupportedException();
        }

        private static readonly ConcurrentDictionary<Type, bool> _serializableCahce = new(Globals.DefaultCollectionConcurrencyLevel, Globals.DefaultDictionaryCapacity);

        private bool IsTypeSerializable(Type t)
        {
            if (_serializableCahce.TryGetValue(t, out bool val))
            {
                return val;
            }

            var can = t.IsSerializable;

            if (can)
            {
                // If generic, generic parm must be too!
                if (t.IsGenericType && (from a in t.GenericTypeArguments where !IsTypeSerializable(a) select a).Any())
                {
                    can = false;
                }
            }

            _serializableCahce[t] = can;
            return can;
        }

        public IDictionary<string, Type> GetAllPreferredTypes(bool onlyWriteable = false, bool onlySerializable = false)
        {
            Dictionary<string, Type> info = new(Globals.DefaultDictionaryCapacity);

            if (_source != null)
            {
                using var rl = new ReaderLock(_lock);

                foreach (var v in (from pi in _source.FastGetAllProperties(true)
                                   where (!onlyWriteable || pi.writeable) && (!onlySerializable || IsTypeSerializable(pi.type))
                                   select new { Key = pi.name, Type = pi.type }).
                    Concat(from a in _preferredType
                           where (!onlySerializable || IsTypeSerializable(a.Value))
                           select new { a.Key, Type = a.Value }))
                {
                    if (!info.ContainsKey(v.Key))
                    {
                        info[v.Key] = v.Type;
                    }
                }
            }

            return info;
        }

        public IDictionary<string, object?> GetAllValues(bool onlyWriteable = false, bool onlySerializable = false)
        {
            Dictionary<string, object?> vals = new(Globals.DefaultDictionaryCapacity);

            if (_source != null)
            {
                using var rl = new ReaderLock(_lock);

                foreach (var v in (from pi in _source.FastGetAllProperties(true)
                                   where (!onlyWriteable || pi.writeable) && (!onlySerializable || IsTypeSerializable(pi.type))
                                   select new { Key = pi.name, Value = _source.FastGetValue(pi.name) }).
                    Concat(from a in _valueBag
                           where (!onlySerializable || a.Value == null || IsTypeSerializable(a.Value.GetType()))
                           select new { a.Key, a.Value }))
                {
                    if (!vals.ContainsKey(v.Key))
                    {
                        vals[v.Key] = v.Value;
                    }
                }
            }

            return vals;
        }

        public IDictionary<string, object?> BagValuesOnly()
        {
            return _valueBag;
        }

        public virtual void AcceptChanges()
        {
            using var wl = new WriterLock(_lock);
            _isBagChanged = false;
        }

        public virtual bool SaveContents(JsonTextWriter tw, SerializationMode mode)
        {
            using var rl = new ReaderLock(_lock);
            return (CEF.CurrentPCTService()?.SaveContents(tw, this, mode, new SerializationVisitTracker())).GetValueOrDefault();
        }

        public virtual void RestoreContents(JsonTextReader tr)
        {
        }

        public virtual void FinalizeObjectContents(JsonTextWriter tw, SerializationMode mode)
        {
        }

        /// <summary>
        /// Returns the JSON representation of the infrastructure wrapper.
        /// </summary>
        /// <param name="mode">Optional control over JSON format (if omitted, uses session / global default).</param>
        /// <returns></returns>
        public string GetSerializationText(SerializationMode? mode = null)
        {
            StringBuilder sb = new(4096);
            var actmode = mode.GetValueOrDefault(CEF.CurrentServiceScope.Settings.SerializationMode);

            using (var jw = new JsonTextWriter(new StringWriter(sb)))
            {
                SaveContents(jw, actmode);
            }

            return sb.ToString();
        }

        public virtual void SetOriginalValue(string propName, object? value)
        {
            throw new NotSupportedException();
        }

        public virtual object? GetOriginalValue(string propName, bool throwIfNotSet)
        {
            throw new NotSupportedException();
        }

        public ValidationWrapper GetValidationState()
        {
            return new ValidationWrapper(this);
        }
    }
}
