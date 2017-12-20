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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Dynamic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace CodexMicroORM.Core.Services
{
    /// <summary>
    /// DynamicBase provides original value / change tracking services, plus property bag services to support "extra" fields.
    /// Although portions are thread-safe, the expectation is these are ServiceScope bound which implies per thread and not likely to encounter MT issues.
    /// Disposal is important here since we have an event handler observing the source for changes. (Infrastructure cascades disposal.)
    /// Data types are inferred based on source values OR by explicit definition - latter is preferred since if source value is null, we can't infer real type (assumes object).
    /// </summary>
    public class DynamicWithBag : DynamicObject, ICEFInfraWrapper
    {
        protected ConcurrentDictionary<string, object> _valueBag = new ConcurrentDictionary<string, object>();
        protected ConcurrentDictionary<string, Type> _preferredType = new ConcurrentDictionary<string, Type>();
        protected object _source;
        protected bool _isBagChanged = false;

        internal DynamicWithBag(object o, IDictionary<string, object> props)
        {
            _source = o;
            SetInitialProps(props);
        }

        protected virtual void SetInitialProps(IDictionary<string, object> props)
        {
            if (props != null)
            {
                foreach (var prop in props)
                {
                    SetValue(prop.Key, prop.Value);
                }
            }
        }

        public override bool Equals(object obj)
        {
            if (_source != null)
            {
                return _source.IsSame(obj);
            }
            return base.Equals(obj);
        }

        public override int GetHashCode()
        {
            return (_source?.GetHashCode()).GetValueOrDefault(base.GetHashCode());
        }

        protected virtual void OnPropertyChanged(string propName, object oldVal, object newVal, bool isBag)
        {
        }

        public override bool TryGetMember(GetMemberBinder binder, out object result)
        {
            var pi = _source.GetType().GetProperty(binder.Name);

            if (pi != null)
            {
                result = pi.GetValue(_source);
                return true;
            }

            if (_valueBag.ContainsKey(binder.Name))
            {
                result = _valueBag[binder.Name];
                return true;
            }
            result = null;
            return false;
        }

        public override bool TrySetMember(SetMemberBinder binder, object value)
        {
            return SetValue(binder.Name, value);
        }

        internal Type GetPropertyType(string propName)
        {
            var pi = _source.GetType().GetProperty(propName);

            if (pi != null)
            {
                return pi.PropertyType;
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

                return _valueBag[propName].GetType();
            }

            return null;
        }

        public virtual object GetValue(string propName)
        {
            if (_valueBag.ContainsKey(propName))
            {
                return _valueBag[propName];
            }

            var pi = _source.GetType().GetProperty(propName);

            if (pi != null && pi.CanRead)
            {
                return pi.GetValue(_source);
            }

            return null;
        }

        private object InternalChangeType(object source, Type targetType)
        {
            if (source == null)
                return null;

            if (source.GetType() == targetType)
                return source;

            if (targetType.Name.StartsWith("Nullable`"))
            {
                return Activator.CreateInstance(targetType, source);
            }

            if (targetType.IsEnum)
            {
                return Enum.Parse(targetType, source.ToString());
            }

            if (source is IConvertible)
            {
                return Convert.ChangeType(source, targetType);
            }

            if (!source.GetType().IsValueType)
            {
                return source;
            }

            return Convert.ChangeType(source.ToString(), targetType);
        }

        public void SetPreferredType(string propName, Type preferredType, bool isRequired = false)
        {
            if (preferredType.IsValueType && !isRequired)
            {
                // todo
            }

            _preferredType[propName] = preferredType;
        }

        public virtual bool SetValue(string propName, object value, Type preferredType = null, bool isRequired = false)
        {
            if (preferredType != null)
            {
                if (preferredType.IsValueType && !isRequired)
                {
                    // todo
                }

                _preferredType[propName] = preferredType;
            }

            var pi = _source.GetType().GetProperty(propName);

            if (pi != null && pi.CanWrite)
            {
                var oldVal = pi.GetValue(_source);

                if (!oldVal.IsSame(value))
                {
                    pi.SetValue(_source, InternalChangeType(value, pi.PropertyType));
                    OnPropertyChanged(propName, oldVal, value, false);
                    return true;
                }
                return false;
            }

            if (_valueBag.ContainsKey(propName))
            {
                var oldVal = _valueBag[propName];

                if (!oldVal.IsSame(value))
                {
                    _isBagChanged = true;
                    _valueBag[propName] = value;
                    OnPropertyChanged(propName, oldVal, value, true);
                    return true;
                }
                return false;
            }

            _valueBag[propName] = value;
            _isBagChanged = true;
            OnPropertyChanged(propName, null, value, true);
            return true;
        }

        public override IEnumerable<string> GetDynamicMemberNames()
        {
            return _valueBag.Keys.Concat(from pi in _source.GetType().GetProperties() select pi.Name);
        }

        public virtual WrappingSupport SupportsWrapping()
        {
            return WrappingSupport.PropertyBag;
        }

        public bool HasProperty(string propName)
        {
            var pi = _source.GetType().GetProperty(propName);

            if (pi != null)
            {
                return true;
            }

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

        public virtual DataRowState GetRowState()
        {
            return DataRowState.Unchanged;
        }

        public virtual void SetRowState(DataRowState rs)
        {
            throw new NotSupportedException();
        }

        public IDictionary<string, object> GetAllValues()
        {
            Dictionary<string, object> vals = new Dictionary<string, object>();

            foreach (var v in (from a in _valueBag select new { a.Key, a.Value }).Concat(from pi in _source.GetType().GetProperties() select new { Key = pi.Name, Value = pi.GetValue(_source) }))
            {
                vals[v.Key] = v.Value;
            }

            return vals;
        }

        public virtual void AcceptChanges()
        {
            _isBagChanged = false;
        }
    }

}
