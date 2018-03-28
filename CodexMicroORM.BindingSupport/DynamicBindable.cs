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
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using CodexMicroORM.Core.Services;
using CodexMicroORM.Core;

namespace CodexMicroORM.BindingSupport
{
    /// <summary>
    /// DynamicBindable wraps individual rows of data (similar to DataRow's). 
    /// By implementing ICustomTypeProvider, it offers a way for data binding engines (such as used by WPF) to interact with the underlying data (both CLR properties and property bag values).
    /// </summary>
    public class DynamicBindable : ICustomTypeProvider, INotifyPropertyChanged, IDisposable, IDataErrorInfo
    {
        private DynamicWithBag _infra;
        private INotifyPropertyChanged _eventSource;
        private IDataErrorInfo _errorSource;
        private int _signalling = 0;

        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler<DirtyStateChangeEventArgs> DirtyStateChange;

        internal DynamicWithBag Wrapped => _infra;

        public DynamicBindable(DynamicWithBag infra)
        {
            _infra = infra;

            var es = infra as INotifyPropertyChanged;

            if (es == null)
            {
                es = infra.GetWrappedObject() as INotifyPropertyChanged;
            }

            _eventSource = es;

            if (_eventSource != null)
            {
                _eventSource.PropertyChanged += eventSource_PropertyChanged;
            }

            var dst = infra as DynamicWithValuesAndBag;

            if (dst != null)
            {
                dst.DirtyStateChange += Dst_DirtyStateChange;
            }

            _errorSource = infra as IDataErrorInfo;

            if (_errorSource == null)
            {
                _errorSource = infra.GetWrappedObject() as IDataErrorInfo;
            }
        }

        public ObjectState State => _infra.GetRowState();

        string IDataErrorInfo.Error => _errorSource?.Error;

        string IDataErrorInfo.this[string columnName] => _errorSource?[columnName];

        private void Dst_DirtyStateChange(object sender, DirtyStateChangeEventArgs e)
        {
            if (_signalling == 0)
            {
                DirtyStateChange?.Invoke(this, e);
            }
        }

        private void eventSource_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_signalling == 0)
            {
                PropertyChanged?.Invoke(this, e);
            }
        }

        public static void AddProperty(string name)
        {
            throw new NotSupportedException("Properties cannot be added using the custom type mechanism.");
        }

        public static void AddProperty(string name, Type propertyType)
        {
            throw new NotSupportedException("Properties cannot be added using the custom type mechanism.");
        }

        public static void AddProperty(string name, Type propertyType, List<Attribute> attributes)
        {
            throw new NotSupportedException("Properties cannot be added using the custom type mechanism.");
        }

        public void SetPropertyValue(string propertyName, object value)
        {
            ++_signalling;

            try
            {
                if (_infra.SetValue(propertyName, value))
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
                }
            }
            finally
            {
                --_signalling;
            }
        }

        public object GetPropertyValue(string propertyName)
        {
            if (_infra.HasProperty(propertyName))
            {
                return _infra.GetValue(propertyName);
            }
            else
            {
                throw new CEFInvalidOperationException($"Property {propertyName} does not exist on wrapper for {_infra.GetBaseType().Name}.");
            }
        }

        public object this[string propName]
        {
            get
            {
                return GetPropertyValue(propName);
            }
            set
            {
                SetPropertyValue(propName, value);
            }
        }

        public PropertyInfo[] GetProperties()
        {
            return this.GetCustomType().GetProperties();
        }

        public Type GetCustomType()
        {
            return new CustomType(this);
        }

        /// <summary>
        /// This private class is used by ICustomTypeProvider to provide property info for both CLR properties and property bag properties supported by CEF.
        /// </summary>
        private class CustomPropertyInfoHelper : PropertyInfo
        {
            public string _name;
            public Type _type;

            public CustomPropertyInfoHelper(string name, object value)
            {
                _name = name;

                if (value == null)
                {
                    _type = typeof(object);
                }
                else
                {
                    _type = value.GetType();
                }
            }

            public override PropertyAttributes Attributes
            {
                get { throw new NotImplementedException(); }
            }

            public override bool CanRead
            {
                get { return true; }
            }

            public override bool CanWrite
            {
                get { return true; }
            }

            public override MethodInfo[] GetAccessors(bool nonPublic)
            {
                throw new NotImplementedException();
            }

            public override MethodInfo GetGetMethod(bool nonPublic)
            {
                return null;
            }

            public override ParameterInfo[] GetIndexParameters()
            {
                return null;
            }

            public override MethodInfo GetSetMethod(bool nonPublic)
            {
                return null;
            }

            // Returns the value from the dictionary stored in the Customer's instance.
            public override object GetValue(object obj, BindingFlags invokeAttr, Binder binder, object[] index, System.Globalization.CultureInfo culture)
            {
                return obj.GetType().GetMethod("GetPropertyValue").Invoke(obj, new[] { _name });
            }

            public override Type PropertyType
            {
                get { return _type; }
            }

            // Sets the value in the dictionary stored in the Customer's instance.
            public override void SetValue(object obj, object value, BindingFlags invokeAttr, Binder binder, object[] index, System.Globalization.CultureInfo culture)
            {
                obj.GetType().GetMethod("SetPropertyValue").Invoke(obj, new[] { _name, value });
            }

            public override Type DeclaringType
            {
                get { throw new NotImplementedException(); }
            }

            public override object[] GetCustomAttributes(Type attributeType, bool inherit)
            {
                return new object[] { };
            }

            public override object[] GetCustomAttributes(bool inherit)
            {
                return new object[] { };
            }

            public override bool IsDefined(Type attributeType, bool inherit)
            {
                throw new NotImplementedException();
            }

            public override string Name
            {
                get { return _name; }
            }

            public override Type ReflectedType
            {
                get { throw new NotImplementedException(); }
            }
        }

        private class CustomType : Type
        {
            Type _baseType;
            DynamicWithBag _infra;

            public CustomType(DynamicBindable source)
            {
                _baseType = source.GetType();
                _infra = source._infra;
            }
            public override Assembly Assembly
            {
                get { return _baseType.Assembly; }
            }

            public override string AssemblyQualifiedName
            {
                get { return _baseType.AssemblyQualifiedName; }
            }

            public override Type BaseType
            {
                get { return _baseType.BaseType; }
            }

            public override string FullName
            {
                get { return _baseType.FullName; }
            }

            public override Guid GUID
            {
                get { return _baseType.GUID; }
            }

            protected override TypeAttributes GetAttributeFlagsImpl()
            {
                throw new NotImplementedException();
            }

            protected override ConstructorInfo GetConstructorImpl(BindingFlags bindingAttr, Binder binder, CallingConventions callConvention, Type[] types, ParameterModifier[] modifiers)
            {
                return _baseType.GetConstructor(bindingAttr, binder, callConvention, types, modifiers);
            }

            public override ConstructorInfo[] GetConstructors(BindingFlags bindingAttr)
            {
                return _baseType.GetConstructors(bindingAttr);
            }

            public override Type GetElementType()
            {
                return _baseType.GetElementType();
            }

            public override EventInfo GetEvent(string name, BindingFlags bindingAttr)
            {
                return _baseType.GetEvent(name, bindingAttr);
            }

            public override EventInfo[] GetEvents(BindingFlags bindingAttr)
            {
                return _baseType.GetEvents(bindingAttr);
            }

            public override FieldInfo GetField(string name, BindingFlags bindingAttr)
            {
                return _baseType.GetField(name, bindingAttr);
            }

            public override FieldInfo[] GetFields(BindingFlags bindingAttr)
            {
                return _baseType.GetFields(bindingAttr);
            }

            public override Type GetInterface(string name, bool ignoreCase)
            {
                return _baseType.GetInterface(name, ignoreCase);
            }

            public override Type[] GetInterfaces()
            {
                return _baseType.GetInterfaces();
            }

            public override RuntimeTypeHandle TypeHandle => _baseType.TypeHandle;

            public override MemberInfo[] GetMembers(BindingFlags bindingAttr)
            {
                return _baseType.GetMembers(bindingAttr);
            }

            protected override MethodInfo GetMethodImpl(string name, BindingFlags bindingAttr, Binder binder, CallingConventions callConvention, Type[] types, ParameterModifier[] modifiers)
            {
                throw new NotImplementedException();
            }

            public override MethodInfo[] GetMethods(BindingFlags bindingAttr)
            {
                return _baseType.GetMethods(bindingAttr);
            }

            public override Type GetNestedType(string name, BindingFlags bindingAttr)
            {
                return _baseType.GetNestedType(name, bindingAttr);
            }

            public override Type[] GetNestedTypes(BindingFlags bindingAttr)
            {
                return _baseType.GetNestedTypes(bindingAttr);
            }

            public override PropertyInfo[] GetProperties(BindingFlags bindingAttr)
            {
                PropertyInfo[] clrProperties = _baseType.GetProperties(bindingAttr);

                var allVals = _infra.GetAllValues();
                var allCustomProps = from a in allVals select new CustomPropertyInfoHelper(a.Key, a.Value);

                if (clrProperties != null)
                {
                    return clrProperties.Concat(allCustomProps).ToArray();
                }
                else
                {
                    return allCustomProps?.ToArray();
                }
            }

            protected override PropertyInfo GetPropertyImpl(string name, BindingFlags bindingAttr, Binder binder, Type returnType, Type[] types, ParameterModifier[] modifiers)
            {
                // Look for the CLR property with this name first.
                PropertyInfo propertyInfo = (from prop in GetProperties(bindingAttr) where prop.Name == name select prop).FirstOrDefault();
                if (propertyInfo == null)
                {
                    // If the CLR property was not found, return a custom property
                    if (_infra.HasProperty(name))
                    {
                        return new CustomPropertyInfoHelper(name, _infra.GetValue(name));
                    }
                }
                return propertyInfo;
            }

            protected override bool HasElementTypeImpl()
            {
                throw new NotImplementedException();
            }

            public override object InvokeMember(string name, BindingFlags invokeAttr, Binder binder, object target, object[] args, ParameterModifier[] modifiers, System.Globalization.CultureInfo culture, string[] namedParameters)
            {
                return _baseType.InvokeMember(name, invokeAttr, binder, target, args, modifiers, culture, namedParameters);
            }

            protected override bool IsArrayImpl()
            {
                throw new NotImplementedException();
            }

            protected override bool IsByRefImpl()
            {
                throw new NotImplementedException();
            }

            protected override bool IsCOMObjectImpl()
            {
                throw new NotImplementedException();
            }

            protected override bool IsPointerImpl()
            {
                throw new NotImplementedException();
            }

            protected override bool IsPrimitiveImpl()
            {
                return _baseType.IsPrimitive;
            }

            public override Module Module
            {
                get { return _baseType.Module; }
            }

            public override string Namespace
            {
                get { return _baseType.Namespace; }
            }

            public override Type UnderlyingSystemType
            {
                get { return _baseType.UnderlyingSystemType; }
            }

            public override object[] GetCustomAttributes(Type attributeType, bool inherit)
            {
                return _baseType.GetCustomAttributes(attributeType, inherit);
            }

            public override object[] GetCustomAttributes(bool inherit)
            {
                return _baseType.GetCustomAttributes(inherit);
            }

            public override bool IsDefined(Type attributeType, bool inherit)
            {
                return _baseType.IsDefined(attributeType, inherit);
            }

            public override string Name
            {
                get { return _baseType.Name; }
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
                    _eventSource.PropertyChanged -= eventSource_PropertyChanged;

                    var dst = _infra as DynamicWithValuesAndBag;

                    if (dst != null)
                    {
                        dst.DirtyStateChange -= Dst_DirtyStateChange;
                    }
                }

                _infra = null;
                _eventSource = null;
                _disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
        #endregion
    }
}
