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
using CodexMicroORM.Core.Services;
using System.ComponentModel;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Text;
using CodexMicroORM.Core.Helper;
using System.Collections.Concurrent;

namespace CodexMicroORM.Core
{
    /// <summary>
    /// Not intended for general consumption, helper functions for the framework.
    /// </summary>
    internal static class InternalExtensions
    {
        public static bool HasProperty(this object o, string propName)
        {
            if (o == null)
                return false;

            if (o is ICEFInfraWrapper)
            {
                return ((ICEFInfraWrapper)o).HasProperty(propName);
            }

            return o.FastPropertyReadable(propName);
        }

        public static TV AssignReturn<TK, TV>(this Dictionary<TK, TV> dic, TK key, TV val)
        {
            dic[key] = val;
            return val;
        }

        public static TV TestAssignReturn<TK, TV>(this Dictionary<TK, TV> dic, TK key, Func<TV> getval)
        {
            if (dic.ContainsKey(key))
            {
                return dic[key];
            }

            var val = getval();
            dic[key] = val;
            return val;
        }
    }

    /// <summary>
    /// Mostly syntactic sugar for existing methods such as for static methods on the CEF class.
    /// </summary>
    public static class PublicExtensions
    {
        private static ConcurrentDictionary<Type, Type> _typeMap = new ConcurrentDictionary<Type, Type>();

        public static int MinOf(this int i1, int i2)
        {
            if (i1 < i2)
                return i1;

            return i2;
        }

        public static int MaxOf(this int i1, int i2)
        {
            if (i1 > i2)
                return i1;

            return i2;
        }

        public static bool IsSame(this object o1, object o2, bool canCompareNull = true)
        {
            if (canCompareNull)
            {
                if (o1 == null && o2 == null)
                    return true;

                if (o1 == null && o2 != null)
                    return false;

                if (o2 == null && o1 != null)
                    return false;
            }
            else
            {
                if (o1 == null || o2 == null)
                    return false;
            }

            return o1.Equals(o2);
        }

        public static void ForAll<T>(this IEnumerable<T> items, Action<T> work)
        {
            foreach (var i in items)
            {
                work(i);
            }
        }

        /// <summary>
        /// Save a specific entity set. Restricts/fitlers to rows present in the collection.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="toSave">EntitySet to use as a save filter.</param>
        /// <param name="settings">Optional save config settings.</param>
        /// <returns></returns>
        public static EntitySet<T> DBSave<T>(this EntitySet<T> toSave, DBSaveSettings settings = null) where T: class, new()
        {
            if (settings == null)
            {
                settings = new DBSaveSettings();
            }

            settings.SourceList = toSave;

            CEF.DBSave(settings);
            return toSave;
        }

        /// <summary>
        /// Takes a potentially unwrapped object and returns a dynamic (DLR) object that exposes the same properties plus all available "extended properties".
        /// Note: this is not always "bindable" for UI data binding (see GenericBindableSet for WPF, for example).
        /// </summary>
        /// <param name="unwrapped">Any object that's tracked within the current service scope.</param>
        /// <returns></returns>
        public static dynamic AsDynamic(this object unwrapped)
        {
            return CEF.CurrentServiceScope.GetDynamicWrapperFor(unwrapped);
        }

        /// <summary>
        /// Infrastructure wrappers offer extended information about tracked objects, such as their "row state" (added, modified, etc.).
        /// </summary>
        /// <param name="o">Any object that's tracked within the current service scope.</param>
        /// <param name="canCreate">If false, the object must have an existing infrastructure wrapper or null is returned; if true, a new wrapper can be created.</param>
        /// <returns></returns>
        public static ICEFInfraWrapper AsInfraWrapped(this object o, bool canCreate = true)
        {
            ICEFInfraWrapper w = CEF.CurrentServiceScope.GetOrCreateInfra(o, canCreate);

            if (w == null && canCreate)
            {
                throw new CEFInvalidOperationException("Failed to identify wrapper object, current scope may be invalid.");
            }

            return w;
        }

        /// <summary>
        /// Abstracts property value access to work with virtually any type of object, accessing a named property that's a value type (returning a nullable form of it).
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="o"></param>
        /// <param name="propName"></param>
        /// <returns></returns>
        public static T? PropertyNullValue<T>(this object o, string propName) where T : struct
        {
            var iw = o.AsInfraWrapped(true);

            if (iw.HasProperty(propName))
            {
                return (T?)iw.GetValue(propName);
            }

            return null;
        }

        /// <summary>
        /// Abstracts property value access to work with virtually any type of object, accessing a named property that's a reference type.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="o"></param>
        /// <param name="propName"></param>
        /// <returns></returns>
        public static T PropertyValue<T>(this object o, string propName) where T : class
        {
            var iw = o.AsInfraWrapped(true);

            if (iw.HasProperty(propName))
            {
                return (T)iw.GetValue(propName);
            }

            return null;
        }

        public static IEnumerable<ICEFInfraWrapper> AllAsInfraWrapped<T>(this IEnumerable<T> items) where T: class, new()
        {
            foreach (var i in items)
            {
                var iw = i.AsInfraWrapped();

                if (iw != null)
                {
                    yield return i.AsInfraWrapped();
                }
            }
        }

        public static IEnumerable<dynamic> AllAsDynamic<T>(this IEnumerable<T> items) where T : class, new()
        {
            foreach (var i in items)
            {
                yield return i.AsDynamic();
            }
        }

        public static (int code, string message) AsString(this IEnumerable<(ValidationErrorCode error, string message)> msgs, ValidationErrorCode? only = null, string concat = " ")
        {
            int code = 0;
            StringBuilder sb = new StringBuilder();

            foreach (var m in msgs)
            {
                if (!only.HasValue || (only.Value & m.error) != 0)
                {
                    if (sb.Length > 0)
                    {
                        sb.Append(concat);
                    }

                    sb.Append(m.message);
                    code |= (int)m.error;
                }
            }

            return (-code, sb.ToString());
        }

        /// <summary>
        /// Given a potentially wrapped object, returns the base object type that it maps to. (E.g. an instance of a derived class from a base POCO object passed in would return the base POCO Type.)
        /// </summary>
        /// <param name="wrapped">Any object that's tracked within the current service scope.</param>
        /// <returns></returns>
        public static Type GetBaseType(this object wrapped)
        {
            if (wrapped == null)
            {
                throw new ArgumentNullException("wrapped");
            }

            if (wrapped is ICEFInfraWrapper)
            {
                var wo = ((ICEFInfraWrapper)wrapped).GetWrappedObject();

                if (wo != null)
                {
                    wrapped = wo;
                }
            }

            if (wrapped is ICEFWrapper)
            {
                return ((ICEFWrapper)wrapped).GetBaseType();
            }

            var wt = wrapped.GetType();

            if (_typeMap.ContainsKey(wt))
            {
                return _typeMap[wt];
            }

            var uw = CEF.CurrentServiceScope.GetWrapperOrTarget(wrapped);

            if (uw is ICEFWrapper)
            {
                var rt = ((ICEFWrapper)uw).GetBaseType();
                _typeMap[wt] = rt;
                return rt;
            }

            if (uw == null)
            {
                // It's an error if the wrapped object presents itself as an IW object at this point!
                if (wrapped is ICEFInfraWrapper)
                {
                    throw new CEFInvalidOperationException("Cannot determine base type for infrastructure wrapped object, no contained object available.");
                }

                _typeMap[wt] = wt;
                return wt;
            }

            _typeMap[wt] = uw.GetType();
            return uw.GetType();
        }

        /// <summary>
        /// Returns the "least wrapped" version if the input (potentially) wrapped object.
        /// </summary>
        /// <param name="wrapped">An object in the current service scope (can be wrapped or unwrapped).</param>
        /// <returns>The "least wrapped" instance of the input object.</returns>
        public static object AsUnwrapped(this object wrapped)
        {
            if (wrapped != null)
            {
                var w = wrapped as ICEFWrapper;

                if (w != null)
                {
                    var uw = w.GetCopyTo();

                    if (uw != null)
                    {
                        return uw;
                    }
                }

                var iw = wrapped as ICEFInfraWrapper;

                if (iw != null)
                {
                    var wo = iw.GetWrappedObject();

                    if (wo != null)
                    {
                        w = wo as ICEFWrapper;

                        if (w != null)
                        {
                            wo = w.GetCopyTo();

                            if (wo == null)
                            {
                                wo = w;
                            }
                        }

                        return wo;
                    }
                }

                return wrapped;
            }

            return null;
        }

        /// <summary>
        /// Returns a wrapped version of the input object, if one is available.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="unwrapped">A potentially unwrapped object.</param>
        /// <returns></returns>
        public static T AsWrapped<T>(this object unwrapped) where T : class, ICEFWrapper
        {
            return CEF.CurrentServiceScope.GetWrapperFor(unwrapped) as T;
        }

        /// <summary>
        /// Returns true if the underlying infra wrapper indicates the row state is not unchanged.
        /// </summary>
        /// <param name="iw">An infra wrapper object.</param>
        /// <returns></returns>
        public static bool IsDirty(this ICEFInfraWrapper iw)
        {
            if (iw != null)
            {
                return iw.GetRowState() != ObjectState.Unchanged;
            }

            return false;
        }

        /// <summary>
        /// Returns the JSON representation of the object. If it's an infrastructure wrapped object, used CEF rules, otherwise plain Newtonsoft serialization rules.
        /// </summary>
        /// <param name="o">Object to serialize.</param>
        /// <param name="mode">Serialization mode (applicable if an infrastructure wrapped object).</param>
        /// <returns>String representation of object.</returns>
        public static string AsJSON(this object o, SerializationMode? mode = null)
        {
            if (o == null)
                return null;

            // Special case - if o is a session scope, we're asking to serialize everything in scope, as one big array of objects!
            if (o is ServiceScope)
            {
                return ((ServiceScope)o).GetScopeSerializationText(mode);
            }

            if (o is ICEFList)
            {
                return ((ICEFList)o).GetSerializationText(mode);
            }

            var iw = o.AsInfraWrapped(false) as ICEFSerializable;

            if (iw == null)
            {
                return JsonConvert.SerializeObject(o);
            }

            CEF.CurrentServiceScope.ReconcileModifiedState(null);

            return iw.GetSerializationText(mode);
        }

        public static void AcceptAllChanges(this ICEFInfraWrapper iw)
        {
            WrappingHelper.NodeVisiter(iw, (iw2) =>
            {
                iw2.AcceptChanges();
            });
        }

        public static void AcceptAllChanges(this ICEFInfraWrapper iw, Type onlyForType)
        {
            if (onlyForType == null)
                throw new ArgumentNullException("onlyForType");

            WrappingHelper.NodeVisiter(iw, (iw2) =>
            {
                if (iw2.GetBaseType().Equals(onlyForType))
                {
                    iw2.AcceptChanges();
                }
            });
        }

        public static void AcceptAllChanges(this ICEFInfraWrapper iw, Func<ICEFInfraWrapper, bool> check)
        {
            if (check == null)
                throw new ArgumentNullException("check");

            WrappingHelper.NodeVisiter(iw, (iw2) =>
            {
                if (check(iw2))
                {
                    iw2.AcceptChanges();
                }
            });
        }

        /// <summary>
        /// Traverses object graph looking for cases where collections can be replaced with EntitySet.
        /// </summary>
        /// <param name="o">Starting object for traversal.</param>
        /// <param name="isNew">If true, assumes objects being added represent "new" rows to insert in the database.</param>
        public static void CreateLists(this object o, bool isNew = false)
        {
            if (o == null)
                return;

            foreach (var pi in o.FastGetAllProperties(true, null))
            {
                if (o.FastPropertyReadable(pi.name))
                {
                    var val = o.FastGetValue(pi.name);

                    if (WrappingHelper.IsWrappableListType(pi.type, val))
                    {
                        if (!(val is ICEFList))
                        {
                            var wrappedCol = WrappingHelper.CreateWrappingList(CEF.CurrentServiceScope, pi.type, o, pi.name);
                            o.FastSetValue(pi.name, wrappedCol);

                            if (val != null)
                            {
                                // Based on the above type checks, we know this thing supports IEnumerable
                                var sValEnum = ((System.Collections.IEnumerable)val).GetEnumerator();

                                while (sValEnum.MoveNext())
                                {
                                    // Need to use non-generic method for this!
                                    var wi = CEF.CurrentServiceScope.InternalCreateAddBase(sValEnum.Current, isNew, null, null, null, null);
                                    wrappedCol.AddWrappedItem(wi);
                                }
                            }

                            ((ISupportInitializeNotification)wrappedCol).EndInit();
                        }
                    }
                }
            }
        }
    }
}
