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

Note: some concepts here were covered in the following article: https://codeblog.jonskeet.uk/2008/08/09/making-reflection-fly-and-exploring-delegates/

Major Changes:
12/2017    0.2.1   Initial release (Joel Champagne)
4/2018     0.5.3   Performance mods
***********************************************************************/
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Linq;

namespace CodexMicroORM.Core.Helper
{
    internal class DelegateCacheKey
    {
        private Type _type;
        private string _prop;
        private int _prophash;

        public DelegateCacheKey(Type type, string prop)
        {
            _type = type;
            _prop = prop;

            if (Globals.CaseSensitiveDictionaries)
            {
                _prophash = prop.GetHashCode();
            }
            else
            {
                _prophash = prop.ToLower().GetHashCode();
            }
        }

        public override bool Equals(object obj)
        {
            var other = (DelegateCacheKey)obj;

            if (Globals.CaseSensitiveDictionaries)
            {
                return _type.Equals(other._type) && _prophash == other._prophash && _prop.Equals(other._prop);
            }
            else
            {
                return _type.Equals(other._type) && string.Compare(_prop, other._prop, true) == 0;
            }
        }

        public override int GetHashCode()
        {
            return _type.GetHashCode() ^ _prophash;
        }
    }

    /// <summary>
    /// Internal class offers extension functions that can improve performance of what would noramlly be Reflection operations.
    /// In testing, it looks like probably a 35% improvement!
    /// </summary>
    public static class PropertyHelper
    {
        private static RWLockInfo _lock = new RWLockInfo();
        private static Dictionary<DelegateCacheKey, Func<object, object>> _getterCache = new Dictionary<DelegateCacheKey, Func<object, object>>();
        private static Dictionary<DelegateCacheKey, Action<object, object>> _setterCache = new Dictionary<DelegateCacheKey, Action<object, object>>();
        private static Dictionary<Type, IEnumerable<(string name, Type type, bool readable, bool writeable)>> _allProps = new Dictionary<Type, IEnumerable<(string name, Type type, bool readable, bool writeable)>>();

        public static void FlushCaches()
        {
            using (new WriterLock(_lock))
            {
                _getterCache.Clear();
                _setterCache.Clear();
                _allProps.Clear();
            }
        }

        public static IEnumerable<(string name, Type type, bool readable, bool writeable)> FastGetAllProperties(this object o, bool? canRead = null, bool? canWrite = null, string name = null)
        {
            if (!_allProps.TryGetValue(o.GetType(), out var list))
            {
                list = (from a in o.GetType().GetProperties() select (a.Name, a.PropertyType, a.CanRead, a.CanWrite));

                using (new WriterLock(_lock))
                {
                    _allProps[o.GetType()] = list.ToArray();
                }
            }

            return (from a in list
                    where (!canRead.HasValue || canRead.Value == a.readable) 
                        && (!canWrite.HasValue || canWrite.Value == a.writeable) 
                        && (name == null || name == a.name)
                    select (a.name, a.type, a.readable, a.writeable));
        }

        public static IEnumerable<(string name, Type type, bool readable, bool writeable)> FastGetAllPropertiesNoLock(this object o, bool? canRead = null, bool? canWrite = null, string name = null)
        {
            if (!_allProps.TryGetValue(o.GetType(), out var list))
            {
                list = (from a in o.GetType().GetProperties() select (a.Name, a.PropertyType, a.CanRead, a.CanWrite));
                _allProps[o.GetType()] = list.ToArray();
            }

            return (from a in list
                    where (!canRead.HasValue || canRead.Value == a.readable)
                        && (!canWrite.HasValue || canWrite.Value == a.writeable)
                        && (name == null || name == a.name)
                    select (a.name, a.type, a.readable, a.writeable));
        }

        public static (bool readable, object value) FastPropertyReadableWithValue(this object o, string propName)
        {
            var key = new DelegateCacheKey(o.GetType(), propName);

            using (new ReaderLock(_lock))
            {
                if (_getterCache.TryGetValue(key, out Func<object, object> call))
                {
                    return (call != null, call == null ? null : call(o));
                }
            }

            try
            {
                var pi = o.GetType().GetProperty(propName);

                if (pi == null || !pi.CanRead)
                {
                    using (new WriterLock(_lock))
                    {
                        _getterCache[key] = null;
                    }

                    return (false, null);
                }

                MethodInfo internalHelper = typeof(PropertyHelper).GetMethod("InternalGet", BindingFlags.Static | BindingFlags.NonPublic);
                MethodInfo constructedHelper = internalHelper.MakeGenericMethod(o.GetType(), pi.PropertyType);
                var asCast = (Func<object, object>)constructedHelper.Invoke(null, new object[] { pi.GetGetMethod() });

                using (new WriterLock(_lock))
                {
                    _getterCache[key] = asCast;
                }

                return (true, asCast(o));
            }
            catch (Exception)
            {
                using (new WriterLock(_lock))
                {
                    _getterCache[key] = null;
                }

                return (false, null);
            }
        }

        public static (bool readable, object value) FastPropertyReadableWithValueNoLock(this object o, string propName)
        {
            var key = new DelegateCacheKey(o.GetType(), propName);

            if (_getterCache.TryGetValue(key, out Func<object, object> call))
            {
                return (call != null, call == null ? null : call(o));
            }

            try
            {
                var pi = o.GetType().GetProperty(propName);

                if (pi == null || !pi.CanRead)
                {
                    _getterCache[key] = null;
                    return (false, null);
                }

                MethodInfo internalHelper = typeof(PropertyHelper).GetMethod("InternalGet", BindingFlags.Static | BindingFlags.NonPublic);
                MethodInfo constructedHelper = internalHelper.MakeGenericMethod(o.GetType(), pi.PropertyType);
                var asCast = (Func<object, object>)constructedHelper.Invoke(null, new object[] { pi.GetGetMethod() });

                _getterCache[key] = asCast;
                return (true, asCast(o));
            }
            catch (Exception)
            {
                _getterCache[key] = null;
                return (false, null);
            }
        }

        public static bool FastPropertyReadable(this object o, string propName)
        {
            var key = new DelegateCacheKey(o.GetType(), propName);

            using (new ReaderLock(_lock))
            {
                if (_getterCache.TryGetValue(key, out Func<object, object> call))
                {
                    return call != null;
                }
            }

            try
            {
                var pi = o.GetType().GetProperty(propName);

                if (pi == null || !pi.CanRead)
                {
                    using (new WriterLock(_lock))
                    {
                        _getterCache[key] = null;
                    }

                    return false;
                }

                MethodInfo internalHelper = typeof(PropertyHelper).GetMethod("InternalGet", BindingFlags.Static | BindingFlags.NonPublic);
                MethodInfo constructedHelper = internalHelper.MakeGenericMethod(o.GetType(), pi.PropertyType);
                var asCast = (Func<object, object>)constructedHelper.Invoke(null, new object[] { pi.GetGetMethod() });

                using (new WriterLock(_lock))
                {
                    _getterCache[key] = asCast;
                }

                return true;
            }
            catch (Exception)
            {
                using (new WriterLock(_lock))
                {
                    _getterCache[key] = null;
                }

                return false;
            }
        }

        public static bool FastPropertyReadableNoLock(this object o, string propName)
        {
            var key = new DelegateCacheKey(o.GetType(), propName);

            if (_getterCache.TryGetValue(key, out Func<object, object> call))
            {
                return call != null;
            }

            try
            {
                var pi = o.GetType().GetProperty(propName);

                if (pi == null || !pi.CanRead)
                {
                    _getterCache[key] = null;
                    return false;
                }

                MethodInfo internalHelper = typeof(PropertyHelper).GetMethod("InternalGet", BindingFlags.Static | BindingFlags.NonPublic);
                MethodInfo constructedHelper = internalHelper.MakeGenericMethod(o.GetType(), pi.PropertyType);
                var asCast = (Func<object, object>)constructedHelper.Invoke(null, new object[] { pi.GetGetMethod() });

                _getterCache[key] = asCast;
                return true;
            }
            catch (Exception)
            {
                _getterCache[key] = null;
                return false;
            }
        }

        /* This was only intended for demo purposes...
         * 
        private static Dictionary<DelegateCacheKey, MethodInfo> _reflGetMethodInfos = new Dictionary<DelegateCacheKey, MethodInfo>();

        public static object GetValueReflection(this object o, string propName)
        {
            var key = new DelegateCacheKey(o.GetType(), propName);

            using (new ReaderLock(_lock))
            {
                if (_reflGetMethodInfos.TryGetValue(key, out MethodInfo mi2))
                {
                    return mi2.Invoke(o, new object[] { });
                }
            }

            var mi = o.GetType().GetProperty(propName).GetGetMethod();

            using (new WriterLock(_lock))
            {
                _reflGetMethodInfos[key] = mi;
            }

            return mi.Invoke(o, new object[] { });
        }
        */

        public static object FastGetValue(this object o, string propName)
        {
            var key = new DelegateCacheKey(o.GetType(), propName);

            using (new ReaderLock(_lock))
            {
                if (_getterCache.TryGetValue(key, out Func<object, object> call))
                {
                    return call(o);
                }
            }

            var pi = o.GetType().GetProperty(propName);
            MethodInfo internalHelper = typeof(PropertyHelper).GetMethod("InternalGet", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo constructedHelper = internalHelper.MakeGenericMethod(o.GetType(), pi.PropertyType);
            var asCast = (Func<object, object>)constructedHelper.Invoke(null, new object[] { pi.GetGetMethod() });

            using (new WriterLock(_lock))
            {
                _getterCache[key] = asCast;
            }

            return asCast(o);
        }

        public static object FastGetValueNoLock(this object o, string propName)
        {
            var key = new DelegateCacheKey(o.GetType(), propName);

            if (_getterCache.TryGetValue(key, out Func<object, object> call))
            {
                return call(o);
            }

            var pi = o.GetType().GetProperty(propName);
            MethodInfo internalHelper = typeof(PropertyHelper).GetMethod("InternalGet", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo constructedHelper = internalHelper.MakeGenericMethod(o.GetType(), pi.PropertyType);
            var asCast = (Func<object, object>)constructedHelper.Invoke(null, new object[] { pi.GetGetMethod() });

            _getterCache[key] = asCast;

            return asCast(o);
        }

        private static Func<object, object> InternalGet<TTarget, TReturn>(MethodInfo method)
        {
            var func = (Func<TTarget, TReturn>)Delegate.CreateDelegate(typeof(Func<TTarget, TReturn>), method);
            return (object target) => func((TTarget)target);
        }

        public static bool FastPropertyWriteable(this object o, string propName)
        {
            var key = new DelegateCacheKey(o.GetType(), propName);

            using (new ReaderLock(_lock))
            {
                if (_setterCache.TryGetValue(key, out Action<object, object> call))
                {
                    return call != null;
                }
            }

            try
            {
                var pi = o.GetType().GetProperty(propName);

                if (pi == null || !pi.CanWrite)
                {
                    using (new WriterLock(_lock))
                    {
                        _setterCache[key] = null;
                    }

                    return false;
                }

                MethodInfo internalHelper = typeof(PropertyHelper).GetMethod("InternalSet", BindingFlags.Static | BindingFlags.NonPublic);
                MethodInfo constructedHelper = internalHelper.MakeGenericMethod(o.GetType(), pi.PropertyType);
                var asCast = (Action<object, object>)constructedHelper.Invoke(null, new object[] { pi.GetSetMethod() });

                using (new WriterLock(_lock))
                {
                    _setterCache[key] = asCast;
                }

                return true;
            }
            catch (Exception)
            {
                using (new WriterLock(_lock))
                {
                    _setterCache[key] = null;
                }

                return false;
            }
        }

        public static bool FastPropertyWriteableNoLock(this object o, string propName)
        {
            var key = new DelegateCacheKey(o.GetType(), propName);

            if (_setterCache.TryGetValue(key, out Action<object, object> call))
            {
                return call != null;
            }

            try
            {
                var pi = o.GetType().GetProperty(propName);

                if (pi == null || !pi.CanWrite)
                {
                    _setterCache[key] = null;
                    return false;
                }

                MethodInfo internalHelper = typeof(PropertyHelper).GetMethod("InternalSet", BindingFlags.Static | BindingFlags.NonPublic);
                MethodInfo constructedHelper = internalHelper.MakeGenericMethod(o.GetType(), pi.PropertyType);
                var asCast = (Action<object, object>)constructedHelper.Invoke(null, new object[] { pi.GetSetMethod() });

                _setterCache[key] = asCast;
                return true;
            }
            catch (Exception)
            {
                _setterCache[key] = null;
                return false;
            }
        }

        public static void FastSetValue(this object o, string propName, object value)
        {
            var key = new DelegateCacheKey(o.GetType(), propName);

            using (new ReaderLock(_lock))
            {
                if (_setterCache.TryGetValue(key, out Action<object, object> call))
                {
                    call(o, value);
                    return;
                }
            }

            var pi = o.GetType().GetProperty(propName);
            MethodInfo internalHelper = typeof(PropertyHelper).GetMethod("InternalSet", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo constructedHelper = internalHelper.MakeGenericMethod(o.GetType(), pi.PropertyType);
            var asCast = (Action<object, object>)constructedHelper.Invoke(null, new object[] { pi.GetSetMethod() });

            using (new WriterLock(_lock))
            {
                _setterCache[key] = asCast;
            }

            asCast(o, value);
        }

        public static void FastSetValueNoLock(this object o, string propName, object value)
        {
            var key = new DelegateCacheKey(o.GetType(), propName);

            if (_setterCache.TryGetValue(key, out Action<object, object> call))
            {
                call(o, value);
                return;
            }

            var pi = o.GetType().GetProperty(propName);
            MethodInfo internalHelper = typeof(PropertyHelper).GetMethod("InternalSet", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo constructedHelper = internalHelper.MakeGenericMethod(o.GetType(), pi.PropertyType);
            var asCast = (Action<object, object>)constructedHelper.Invoke(null, new object[] { pi.GetSetMethod() });

            _setterCache[key] = asCast;

            asCast(o, value);
        }

        private static Action<object, object> InternalSet<TTarget, TProp>(MethodInfo method)
        {
            var func = (Action<TTarget, TProp>)Delegate.CreateDelegate(typeof(Action<TTarget, TProp>), method);
            return (object target, object value) => func((TTarget)target, (TProp)value);
        }
    }
}
