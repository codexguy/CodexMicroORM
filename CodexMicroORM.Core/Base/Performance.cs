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
12/2017    0.2.1   Initial release (Joel Champagne)
***********************************************************************/
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;

namespace CodexMicroORM.Core
{
    /// <summary>
    /// Internal class offers extension functions that can improve performance of what would noramlly be Reflection operations.
    /// In testing, it looks like probably a 50% improvement!
    /// </summary>
    internal static class PropertyHelper
    {
        private static ConcurrentDictionary<string, Delegate> _getterCache = new ConcurrentDictionary<string, Delegate>(Globals.CurrentStringComparer);
        private static ConcurrentDictionary<string, Delegate> _setterCache = new ConcurrentDictionary<string, Delegate>(Globals.CurrentStringComparer);

        public static void FlushCaches()
        {
            _getterCache.Clear();
            _setterCache.Clear();
        }

        public static bool FastPropertyReadable(this object o, string propName)
        {
            string key = o.GetType().Name + propName;

            if (_getterCache.TryGetValue(key, out Delegate call))
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

        public static object FastGetValue(this object o, string propName)
        {
            string key = o.GetType().Name + propName;

            if (_getterCache.TryGetValue(key, out Delegate call))
            {
                return ((Func<object, object>)call).Invoke(o);
            }

            var pi = o.GetType().GetProperty(propName);
            MethodInfo internalHelper = typeof(PropertyHelper).GetMethod("InternalGet", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo constructedHelper = internalHelper.MakeGenericMethod(o.GetType(), pi.PropertyType);
            var asCast = (Func<object, object>)constructedHelper.Invoke(null, new object[] { pi.GetGetMethod() });

            _getterCache[key] = asCast;
            return asCast.Invoke(o);
        }

        private static Func<object, object> InternalGet<TTarget, TReturn>(MethodInfo method)
        {
            var func = (Func<TTarget, TReturn>)Delegate.CreateDelegate(typeof(Func<TTarget, TReturn>), method);
            return (object target) => func((TTarget)target);
        }

        public static bool FastPropertyWriteable(this object o, string propName)
        {
            string key = o.GetType().Name + propName;

            if (_setterCache.TryGetValue(key, out Delegate call))
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
            catch (Exception ex)
            {
                _setterCache[key] = null;
                return false;
            }
        }

        public static void FastSetValue(this object o, string propName, object value)
        {
            string key = o.GetType().Name + propName;

            if (_setterCache.TryGetValue(key, out Delegate call))
            {
                ((Action<object, object>)call).Invoke(o, value);
                return;
            }

            var pi = o.GetType().GetProperty(propName);
            MethodInfo internalHelper = typeof(PropertyHelper).GetMethod("InternalSet", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo constructedHelper = internalHelper.MakeGenericMethod(o.GetType(), pi.PropertyType);
            var asCast = (Action<object, object>)constructedHelper.Invoke(null, new object[] { pi.GetSetMethod() });

            _setterCache[key] = asCast;
            asCast.Invoke(o, value);
        }

        private static Action<object, object> InternalSet<TTarget, TProp>(MethodInfo method)
        {
            var func = (Action<TTarget, TProp>)Delegate.CreateDelegate(typeof(Action<TTarget, TProp>), method);
            return (object target, object value) => func((TTarget)target, (TProp)value);
        }
    }
}
