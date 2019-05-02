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
    internal struct DelegateCacheKey
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
                return _prophash == other._prophash && _type.Equals(other._type) && _prop.Equals(other._prop);
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
    public static class CEFHelper
    {
        private static RWLockInfo _lock = new RWLockInfo();
        private static RWLockInfo _lockAllProp = new RWLockInfo();
        private static Dictionary<DelegateCacheKey, Func<object, object>> _getterCache = new Dictionary<DelegateCacheKey, Func<object, object>>(Globals.DefaultDictionaryCapacity);
        private static Dictionary<DelegateCacheKey, Action<object, object>> _setterCache = new Dictionary<DelegateCacheKey, Action<object, object>>(Globals.DefaultDictionaryCapacity);
        private static ConcurrentDictionary<Type, ConcurrentDictionary<string, (Type type, bool readable, bool writeable)>> _allProps = new ConcurrentDictionary<Type, ConcurrentDictionary<string, (Type type, bool readable, bool writeable)>>(Globals.DefaultCollectionConcurrencyLevel, Globals.DefaultDictionaryCapacity);
        private static Dictionary<Type, Func<object>> _constructorCache = new Dictionary<Type, Func<object>>(Globals.DefaultDictionaryCapacity);

        public static void FlushCaches()
        {
            using (new WriterLock(_lock))
            {
                _getterCache.Clear();
                _setterCache.Clear();
                _allProps.Clear();
                _constructorCache.Clear();
            }

            using (new WriterLock(_lockAllProp))
            {
                _allProps.Clear();
            }
        }

        public static object FastCreateNoParm(this Type t)
        {
            if (_constructorCache.TryGetValue(t, out var del))
            {
                return del();
            }

            var exp = (Func<object>)Expression.Lambda(Expression.New(t.GetConstructor(Type.EmptyTypes))).Compile();

            using (new WriterLock(_lock))
            {
                _constructorCache[t] = exp;
            }

            return exp();
        }

        public static IEnumerable<(string name, Type type, bool readable, bool writeable)> FastGetAllProperties(this Type t, bool? canRead = null, bool? canWrite = null, string name = null)
        {
            if (!_allProps.TryGetValue(t, out var pnmap))
            {
                pnmap = new ConcurrentDictionary<string, (Type type, bool readable, bool writeable)>(
                    from a in t.GetProperties()
                    select new KeyValuePair<string, (Type type, bool readable, bool writeable)>(a.Name, (a.PropertyType, a.CanRead, a.CanWrite)));

                _allProps[t] = pnmap;
            }

            if (!string.IsNullOrEmpty(name))
            {
                // If name provided, a direct lookup can be used
                if (pnmap.TryGetValue(name, out var info)
                    && (!canRead.HasValue || canRead.Value == info.readable)
                    && (!canWrite.HasValue || canWrite.Value == info.writeable))
                {
                    return new(string name, Type type, bool readable, bool writeable)[] { (name, info.type, info.readable, info.writeable) };
                }

                return new(string name, Type type, bool readable, bool writeable)[] { };
            }
            else
            {
                return (from a in pnmap
                        where (!canRead.HasValue || canRead.Value == a.Value.readable)
                            && (!canWrite.HasValue || canWrite.Value == a.Value.writeable)
                        select (a.Key, a.Value.type, a.Value.readable, a.Value.writeable));
            }
        }

        public static IEnumerable<(string name, Type type, bool readable, bool writeable)> FastGetAllProperties(this object o, bool? canRead = null, bool? canWrite = null, string name = null)
        {
            return FastGetAllProperties(o?.GetType() ?? throw new ArgumentNullException("o"), canRead, canWrite, name);
        }

        public static (bool readable, object value) FastPropertyReadableWithValue(this object o, string propName)
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
                    using (new WriterLock(_lock))
                    {
                        _getterCache[key] = null;
                    }

                    return (false, null);
                }

                MethodInfo internalHelper = typeof(CEFHelper).GetMethod("InternalGet", BindingFlags.Static | BindingFlags.NonPublic);
                MethodInfo constructedHelper = internalHelper.MakeGenericMethod(o.GetType(), pi.PropertyType);

                try
                {
                    var asCast = (Func<object, object>)constructedHelper.Invoke(null, new object[] { pi.GetGetMethod() });

                    using (new WriterLock(_lock))
                    {
                        _getterCache[key] = asCast;
                    }

                    return (true, asCast(o));
                }
                catch (TargetInvocationException)
                {
                    return (true, o.GetType().GetProperty(propName).GetGetMethod().Invoke(o, new object[] { }));
                }
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

                MethodInfo internalHelper = typeof(CEFHelper).GetMethod("InternalGet", BindingFlags.Static | BindingFlags.NonPublic);
                MethodInfo constructedHelper = internalHelper.MakeGenericMethod(o.GetType(), pi.PropertyType);

                try
                {
                    var asCast = (Func<object, object>)constructedHelper.Invoke(null, new object[] { pi.GetGetMethod() });

                    _getterCache[key] = asCast;
                    return (true, asCast(o));
                }
                catch (TargetInvocationException)
                {
                    return (true, o.GetType().GetProperty(propName).GetGetMethod().Invoke(o, new object[] { }));
                }
            }
            catch (Exception)
            {
                _getterCache[key] = null;
                return (false, null);
            }
        }

        public static bool FastPropertyReadable(this object o, string propName)
        {
            if (o == null)
            {
                return false;
            }

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
                    using (new WriterLock(_lock))
                    {
                        _getterCache[key] = null;
                    }

                    return false;
                }

                MethodInfo internalHelper = typeof(CEFHelper).GetMethod("InternalGet", BindingFlags.Static | BindingFlags.NonPublic);
                MethodInfo constructedHelper = internalHelper.MakeGenericMethod(o.GetType(), pi.PropertyType);

                try
                {
                    var asCast = (Func<object, object>)constructedHelper.Invoke(null, new object[] { pi.GetGetMethod() });

                    using (new WriterLock(_lock))
                    {
                        _getterCache[key] = asCast;
                    }
                }
                catch (TargetInvocationException)
                {
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

                MethodInfo internalHelper = typeof(CEFHelper).GetMethod("InternalGet", BindingFlags.Static | BindingFlags.NonPublic);
                MethodInfo constructedHelper = internalHelper.MakeGenericMethod(o.GetType(), pi.PropertyType);

                try
                {
                    var asCast = (Func<object, object>)constructedHelper.Invoke(null, new object[] { pi.GetGetMethod() });

                    _getterCache[key] = asCast;
                }
                catch (TargetInvocationException)
                {
                }

                return true;
            }
            catch (Exception)
            {
                _getterCache[key] = null;
                return false;
            }
        }

        public static object FastGetValue(this object o, string propName)
        {
            var key = new DelegateCacheKey(o.GetType(), propName);

            if (_getterCache.TryGetValue(key, out Func<object, object> call))
            {
                return call(o);
            }

            var pi = o.GetType().GetProperty(propName);
            MethodInfo internalHelper = typeof(CEFHelper).GetMethod("InternalGet", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo constructedHelper = internalHelper.MakeGenericMethod(o.GetType(), pi.PropertyType);

            try
            {
                var asCast = (Func<object, object>)constructedHelper.Invoke(null, new object[] { pi.GetGetMethod() });

                using (new WriterLock(_lock))
                {
                    _getterCache[key] = asCast;
                }

                return asCast(o);
            }
            catch (TargetInvocationException)
            {
                return o.GetType().GetProperty(propName).GetGetMethod().Invoke(o, new object[] { });
            }
        }

        public static object FastGetValueNoLock(this object o, string propName)
        {
            var key = new DelegateCacheKey(o.GetType(), propName);

            if (_getterCache.TryGetValue(key, out Func<object, object> call))
            {
                return call(o);
            }

            var pi = o.GetType().GetProperty(propName);
            MethodInfo internalHelper = typeof(CEFHelper).GetMethod("InternalGet", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo constructedHelper = internalHelper.MakeGenericMethod(o.GetType(), pi.PropertyType);

            try
            {
                var asCast = (Func<object, object>)constructedHelper.Invoke(null, new object[] { pi.GetGetMethod() });
                _getterCache[key] = asCast;
                return asCast(o);
            }
            catch (TargetInvocationException)
            {
                // Fallback - to investigate some cases where the above can fail (todo)
                return o.GetType().GetProperty(propName).GetGetMethod().Invoke(o, new object[] { });
            }
        }

        private static Func<object, object> InternalGet<TTarget, TReturn>(MethodInfo method)
        {
            var func = (Func<TTarget, TReturn>)Delegate.CreateDelegate(typeof(Func<TTarget, TReturn>), method);
            return (object target) => func((TTarget)target);
        }

        public static bool FastPropertyWriteable(this object o, string propName)
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
                    using (new WriterLock(_lock))
                    {
                        _setterCache[key] = null;
                    }

                    return false;
                }

                MethodInfo internalHelper = typeof(CEFHelper).GetMethod("InternalSet", BindingFlags.Static | BindingFlags.NonPublic);
                MethodInfo constructedHelper = internalHelper.MakeGenericMethod(o.GetType(), pi.PropertyType);

                try
                {
                    var asCast = (Action<object, object>)constructedHelper.Invoke(null, new object[] { pi.GetSetMethod() });

                    using (new WriterLock(_lock))
                    {
                        _setterCache[key] = asCast;
                    }
                }
                catch (TargetInvocationException)
                {
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

                MethodInfo internalHelper = typeof(CEFHelper).GetMethod("InternalSet", BindingFlags.Static | BindingFlags.NonPublic);
                MethodInfo constructedHelper = internalHelper.MakeGenericMethod(o.GetType(), pi.PropertyType);

                try
                {
                    var asCast = (Action<object, object>)constructedHelper.Invoke(null, new object[] { pi.GetSetMethod() });

                    _setterCache[key] = asCast;
                }
                catch (TargetInvocationException)
                {
                }

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

            if (_setterCache.TryGetValue(key, out Action<object, object> call))
            {
                call(o, value);
                return;
            }

            var pi = o.GetType().GetProperty(propName);
            MethodInfo internalHelper = typeof(CEFHelper).GetMethod("InternalSet", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo constructedHelper = internalHelper.MakeGenericMethod(o.GetType(), pi.PropertyType);

            try
            {
                var asCast = (Action<object, object>)constructedHelper.Invoke(null, new object[] { pi.GetSetMethod() });

                using (new WriterLock(_lock))
                {
                    _setterCache[key] = asCast;
                }

                asCast(o, value);
            }
            catch (TargetInvocationException)
            {
                o.GetType().GetProperty(propName).GetSetMethod().Invoke(o, new object[] { value });
            }
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
            MethodInfo internalHelper = typeof(CEFHelper).GetMethod("InternalSet", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo constructedHelper = internalHelper.MakeGenericMethod(o.GetType(), pi.PropertyType);

            try
            {
                var asCast = (Action<object, object>)constructedHelper.Invoke(null, new object[] { pi.GetSetMethod() });

                _setterCache[key] = asCast;

                asCast(o, value);
            }
            catch (TargetInvocationException)
            {
                o.GetType().GetProperty(propName).GetSetMethod().Invoke(o, new object[] { value });
            }
        }

        private static Action<object, object> InternalSet<TTarget, TProp>(MethodInfo method)
        {
            var func = (Action<TTarget, TProp>)Delegate.CreateDelegate(typeof(Action<TTarget, TProp>), method);
            return (object target, object value) => func((TTarget)target, (TProp)value);
        }
    }
}
