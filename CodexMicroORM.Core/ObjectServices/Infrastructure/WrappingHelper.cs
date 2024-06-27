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
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Reflection;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Collections;
using CodexMicroORM.Core.Helper;
using System.Threading;
using CodexMicroORM.Core.Collections;

namespace CodexMicroORM.Core.Services
{
    /// <summary>
    /// Wrappers are specialized infrastructure objects that extend the capabilities of "regular wrappers". (Regular wrappers are typically the code generated variety which may add some services but not everything needed by all services.)
    /// The main benefit of "regular wrappers": strong typing and intellisense. This extends to use of stored procedures where wrappers for calls can be included against the regular wrapper infrastructure (although we could as easily create a static library of calls, too, which offers similar benefits).
    /// You may choose not to use any regular wrappers, so you might have only your POCO objects and infra(structure) wrappers.
    /// You may choose to use poco objects, regular wrappers, AND infra wrappers. (Use of infra wrappers is transparent - you should only care about your poco in and some cases, your regular wrappers for data binding.)
    /// You may choose to treat your generated regular wrappers as if your poco objects (i.e. you're fine with using what's generated from the database: database-first design); you'll likely also use infra wrappers under the covers too unless you build very heavy biz objects and advertise this capability.
    /// You may choose to just work directly with infra wrappers only. I personally dislike this: no strong typing means schema changes can cut you.
    /// </summary>
    public static class WrappingHelper
    {
        private readonly static ConcurrentDictionary<Type, Type> _directTypeMap = new(Globals.DefaultCollectionConcurrencyLevel, Globals.DefaultDictionaryCapacity);
        private readonly static ConcurrentDictionary<Type, string> _cachedTypeMap = new(Globals.DefaultCollectionConcurrencyLevel, Globals.DefaultDictionaryCapacity);
        private readonly static ConcurrentDictionary<Type, object?> _defValMap = new(Globals.DefaultCollectionConcurrencyLevel, Globals.DefaultDictionaryCapacity);
        private readonly static ConcurrentDictionary<Type, bool> _isWrapListCache = new(Globals.DefaultCollectionConcurrencyLevel, Globals.DefaultDictionaryCapacity);
        private readonly static ConcurrentDictionary<Type, IDictionary<string, (Type, Boolean)>> _propCache = new(Globals.DefaultCollectionConcurrencyLevel, Globals.DefaultDictionaryCapacity);
        private readonly static ConcurrentDictionary<Type, bool> _sourceValTypeOk = new(Globals.DefaultCollectionConcurrencyLevel, Globals.DefaultDictionaryCapacity);
        private readonly static SlimConcurrentDictionary<string, Type?> _typeByName = [];
        private static long _copyNesting = 0;


        public static object? GetDefaultForType(Type t)
        {
            if (t == null)
                return null;

            if (_defValMap.TryGetValue(t, out object? val))
            {
                return val;
            }

            MethodInfo mi = typeof(WrappingHelper).GetMethod("InternalGetDefaultForType", BindingFlags.NonPublic | BindingFlags.InvokeMethod | BindingFlags.Static)!;
            mi = mi.MakeGenericMethod(t);
            var val2 = mi.Invoke(null, []);
            _defValMap[t] = val2;
            return val2;
        }

#pragma warning disable IDE0051 // Remove unused private members
        private static object? InternalGetDefaultForType<T>()
#pragma warning restore IDE0051 // Remove unused private members
        {
            return default(T);
        }

        private static string GetFullyQualifiedWrapperName(object o)
        {
            var ot = o?.GetType() ?? throw new CEFInvalidStateException(InvalidStateType.ArgumentNull, nameof(o));

            if (_directTypeMap.TryGetValue(ot, out var fn))
            {
                return fn.FullName!;
            }

            if (_cachedTypeMap.TryGetValue(ot, out var fn2))
            {
                return fn2;
            }

            string? cn;

            if (!string.IsNullOrEmpty(Globals.WrapperClassNamePattern))
            {
                if (!string.IsNullOrEmpty(Globals.WrapperClassNamePattern) && Globals.WrapperClassNamePattern!.StartsWith("{0}") && ot.Name.EndsWith(Globals.WrapperClassNamePattern.Replace("{0}", "")))
                {
                    cn = ot.Name;
                }
                else
                {
                    cn = string.Format(Globals.WrapperClassNamePattern, ot.Name);
                }
            }
            else
            {
                cn = ot.Name;
            }

            string? ns;

            if (!string.IsNullOrEmpty(Globals.WrappingClassNamespace))
            {
                ns = string.Format(Globals.WrappingClassNamespace, ot.Namespace);
            }
            else
            {
                ns = ot.Namespace;
            }

            string? ass;

            if (!string.IsNullOrEmpty(Globals.WrapperClassAssembly))
            {
                ass = string.Format(Globals.WrapperClassAssembly, ot.Assembly.GetName().Name);
            }
            else
            {
                ass = ot.Assembly.GetName().Name;
            }

            var fullName = $"{ns}.{cn}, {ass}";

            _cachedTypeMap[ot] = fullName;

            return fullName;
        }

        internal static bool IsWrappableListType(Type sourceType, object? sourceVal)
        {
            if (_isWrapListCache.TryGetValue(sourceType, out bool v))
            {
                return v;
            }

            if (sourceType.IsValueType || !sourceType.IsConstructedGenericType || sourceType.GenericTypeArguments?.Length != 1 || sourceType.GenericTypeArguments[0].IsValueType)
            {
                _isWrapListCache[sourceType] = false;
                return false;
            }

            if (sourceVal != null && sourceVal.GetType().Name.StartsWith(Globals.PreferredEntitySetType.Name))
            {
                // Should already be wrapped when added to an EntitySet
                _isWrapListCache[sourceType] = false;
                return false;
            }

            var v2 = (sourceType.Name.StartsWith("IList`") || sourceType.Name.StartsWith("ICollection`") || sourceType.Name.StartsWith("IEnumerable`"));
            _isWrapListCache[sourceType] = v2;
            return v2;
        }

        internal static ICEFList? CreateWrappingList(ServiceScope ss, Type sourceType, object? host, string? propName)
        {
            var setWrapType = Globals.PreferredEntitySetType.MakeGenericType(sourceType.GenericTypeArguments[0]);
            var wrappedCol = setWrapType.FastCreateNoParm() as ICEFList;

            if (wrappedCol != null)
            {
                ((ISupportInitializeNotification)wrappedCol).BeginInit();
                wrappedCol.Initialize(ss, host, host?.GetBaseType()?.Name, propName);
            }

            return wrappedCol;
        }

        public static HashSet<string> PopulateNestedPropertiesFromValues(object o, IDictionary<string, object?> props)
        {
            HashSet<string> wasSet = [];

            foreach (var extprop in ServiceScope.ResolvedAdditionalPropertyHosts(o.GetType()))
            {
                var nestprop = o.FastGetValue(extprop);

                if (nestprop != null)
                {
                    var tprops = nestprop.FastGetAllProperties(true, true);

                    foreach (var (name, _, _, _) in tprops)
                    {
                        if (props.TryGetValue(name, out var nv))
                        {
                            nestprop.FastSetValue(name, nv);
                            wasSet.Add(name);
                        }
                    }
                }
            }

            return wasSet;
        }

        /// <summary>
        /// Recursively parse property values for an object graph. This not only adjusts collection types to be trackable concrete types, but registers child objects into the current service scope.
        /// </summary>
        /// <param name="sourceProps"></param>
        /// <param name="target"></param>
        /// <param name="isNew"></param>
        /// <param name="ss"></param>
        /// <param name="visits"></param>
        /// <param name="justTraverse"></param>
        public static void CopyParsePropertyValues(IDictionary<string, object?>? sourceProps, object target, bool isNew, ServiceScope? ss, IDictionary<object, object> visits, bool justTraverse, bool simpleCopy)
        {
            // Can disable this to improve performance - default is enabled
            if (!Globals.DoCopyParseProperties && !simpleCopy)
            {
                return;
            }

            _propCache.TryGetValue(target.GetType(), out var dic);

            if (dic == null)
            {
                dic = (from a in target.FastGetAllProperties(true, true)
                       select new { Name = a.name, PropertyType = a.type, RO = CEF.RegisteredPropertyNameTreatReadOnly.Contains(a.name) }).ToDictionary((p) => p.Name, (p) => (p.PropertyType, p.RO));

                _propCache[target.GetType()] = dic;
            }

            var iter = sourceProps == null ?
                (from t in dic where (!t.Value.Item2 || isNew) select (t.Key, target.FastGetValue(t.Key), t.Value))
                : (from s in sourceProps from t in dic where (!t.Value.Item2 || isNew) && s.Key == t.Key select (s.Key, s.Value, t.Value));

            var maxdop = Globals.EnableParallelPropertyParsing && Environment.ProcessorCount > 4 && iter.Count() >= Environment.ProcessorCount ? Environment.ProcessorCount >> 2 : 1;

            Interlocked.Add(ref _copyNesting, maxdop);

            try
            {
                void a((string PropName, object SourceVal, (Type TargPropType, bool ReadOnly) inner) info)
                {
                    object? wrapped = null;

                    if (ss != null && !simpleCopy && IsWrappableListType(info.inner.TargPropType, info.SourceVal))
                    {
                        ICEFList? wrappedCol = null;

                        if (ss.Settings.InitializeNullCollections || info.SourceVal != null)
                        {
                            // This by definition represents CHILDREN
                            // Use an observable collection we control - namely EntitySet
                            wrappedCol = CreateWrappingList(ss, info.inner.TargPropType, target, info.PropName);
                            target.FastSetValue(info.PropName, wrappedCol);
                        }
                        else
                        {
                            wrappedCol = info.SourceVal as ICEFList;
                        }

                        // Merge any existing data into the collection - as we do this, recursively construct wrappers!
                        if (info.SourceVal != null && wrappedCol != null)
                        {
                            // Based on the above type checks, we know this thing supports IEnumerable
                            var sValEnum = ((IEnumerable)info.SourceVal).GetEnumerator();

                            while (sValEnum.MoveNext())
                            {
                                var cur = sValEnum.Current;

                                if (visits.TryGetValue(cur, out object? val))
                                {
                                    wrapped = val ?? cur;
                                }
                                else
                                {
                                    wrapped = ss.InternalCreateAddBase(cur, isNew, null, null, null, visits, true, true, ss.ResolvedRetrievalIdentityMode(cur));
                                }

                                if (wrapped != null)
                                {
                                    wrappedCol.AddWrappedItem(wrapped);
                                }
                            }
                        }

                        if (wrappedCol != null && (ss.Settings.InitializeNullCollections || info.SourceVal != null))
                        {
                            ((ISupportInitializeNotification)wrappedCol).EndInit();
                        }
                    }
                    else
                    {
                        // If the type is a ref type that we manage, then this property represents a PARENT and we should replace/track it (only if we have a PK for it: without one, can't be tracked)
                        if (ss != null && info.SourceVal != null)
                        {
                            var svt = info.SourceVal.GetType();
                            bool svtok = false;

                            if (!simpleCopy && !_sourceValTypeOk.TryGetValue(svt, out svtok))
                            {
                                svtok = !svt.IsValueType && svt != typeof(string) && KeyService.ResolveKeyDefinitionForType(svt).Any();
                                _sourceValTypeOk[svt] = svtok;
                            }

                            if (simpleCopy && !justTraverse)
                            {
                                target.FastSetValue(info.PropName, info.SourceVal);
                            }
                            else
                            {
                                if (svtok)
                                {
                                    if (visits.TryGetValue(info.SourceVal, out object? sv))
                                    {
                                        wrapped = sv ?? info.SourceVal;
                                    }
                                    else
                                    {
                                        var to = ss.GetTrackedByWrapperOrTarget(info.SourceVal);

                                        if (to == null)
                                        {
                                            wrapped = ss.InternalCreateAddBase(info.SourceVal, isNew, null, null, null, visits, true, true, ss?.ResolvedRetrievalIdentityMode(info.SourceVal) ?? Globals.DefaultRetrievalIdentityMode);
                                        }
                                        else
                                        {
                                            wrapped = to.GetWrapperTarget();
                                        }
                                    }

                                    if (wrapped != null)
                                    {
                                        target.FastSetValue(info.PropName, wrapped);
                                    }
                                }
                                else
                                {
                                    if (!justTraverse)
                                    {
                                        target.FastSetValue(info.PropName, info.SourceVal);
                                    }
                                }
                            }
                        }
                        else
                        {
                            if (!justTraverse)
                            {
                                target.FastSetValue(info.PropName, info.SourceVal);
                            }
                        }
                    }
                }

                int resdop = Interlocked.Read(ref _copyNesting) > 12 ? 1 : maxdop;

                if (resdop == 1)
                {
                    foreach (var info in iter)
                    {
                        a(info);
                    }
                }
                else
                {
                    ss ??= CEF.CurrentServiceScope;

                    Parallel.ForEach(iter, new ParallelOptions() { MaxDegreeOfParallelism = resdop }, (info) =>
                    {
                        using (CEF.UseServiceScope(ss))
                        {
                            a(info);
                        }
                    });
                }
            }
            finally
            {
                Interlocked.Add(ref _copyNesting, -maxdop);
            }
        }

        internal static void CopyPropertyValuesObject(object source, object target, bool isNew, ServiceScope ss, IDictionary<string, object>? removeIfSet, IDictionary<object, object> visits)
        {
            Dictionary<string, object?> props = new(Globals.DefaultDictionaryCapacity);

            var pkFields = KeyService.ResolveKeyDefinitionForType(source.GetBaseType());

            foreach (var (name, _, _, _) in source.FastGetAllProperties(true, true))
            {
                // For new rows, ignore the PK since it should be assigned by key service
                if ((!isNew || !pkFields.Contains(name)) && (isNew || !CEF.RegisteredPropertyNameTreatReadOnly.Contains(name)))
                {
                    props[name] = source.FastGetValue(name);

                    if (removeIfSet != null && removeIfSet.ContainsKey(name))
                    {
                        removeIfSet.Remove(name);
                    }
                }
            }

            CopyParsePropertyValues(props, target, isNew, ss, visits, false, false);
        }

        internal static void CopyReadOnlyCollectionsBackFromWrapper(object srcwrapped, object target)
        {
            // This is a special case where we want to copy back collections which may have been instantiated in a wrapper class, so that consumers can use the unwrapped instances as if they were wrapped
            foreach (var name in (from a in srcwrapped.FastGetAllProperties(true, true)
                                  join b in target.FastGetAllProperties(true, true) on a.name equals b.name
                                  where CEF.RegisteredPropertyNameTreatReadOnly.Contains(a.name)
                                  && (a.type == b.type || Nullable.GetUnderlyingType(b.type) == a.type)
                                  select a.name))
            {
                var sv = srcwrapped.FastGetValue(name);
                var tv = target.FastGetValue(name);

                if (sv != null && tv == null && sv.GetType().Name.StartsWith(Globals.PreferredEntitySetType.Name))
                {
                    try
                    {
                        target.FastSetValue(name, sv);
                    }
                    catch
                    {
                        // consider this non-fatal, may revisit
                    }
                }
            }
        }

        private static ICEFWrapper? InternalCreateWrapper(bool isNew, object o, bool missingAllowed, ServiceScope ss, Dictionary<object, ICEFWrapper> wrappers, IDictionary<object, object> visits)
        {
            // Try to not duplicate wrappers: return one if previously generated in this parsing instance
            if (wrappers.TryGetValue(o, out var w))
            {
                return w;
            }

            ICEFWrapper? replwrap = null;

            if (Globals.DefaultWrappingAction == WrappingAction.PreCodeGen)
            {
                var fqn = GetFullyQualifiedWrapperName(o);

                if (string.IsNullOrEmpty(fqn))
                {
                    throw new CEFInvalidStateException(InvalidStateType.ObjectTrackingIssue, $"Failed to determine name of wrapper class for object of type {o.GetType().Name}.");
                }

                if (!_typeByName.TryGetValue(fqn, out Type? t))
                {
                    t = Type.GetType(fqn, false, true);
                    _typeByName[fqn] = t;
                }

                if (t == null)
                {
                    if (missingAllowed)
                    {
                        return null;
                    }
                    throw new CEFInvalidStateException(InvalidStateType.ObjectTrackingIssue, $"Failed to create wrapper object of type {fqn} for object of type {o.GetType().Name}.");
                }

                // Relies on parameterless constructor
                var wrapper = t.FastCreateNoParm();

                if (wrapper == null)
                {
                    if (missingAllowed)
                    {
                        return null;
                    }
                    throw new CEFInvalidStateException(InvalidStateType.ObjectTrackingIssue, $"Failed to create wrapper object of type {fqn} for object of type {o.GetType().Name}.");
                }

                if (wrapper is not ICEFWrapper)
                {
                    if (missingAllowed)
                    {
                        return null;
                    }
                    throw new CEFInvalidStateException(InvalidStateType.ObjectTrackingIssue, $"Wrapper object of type {fqn} for object of type {o.GetType().Name} does not implement ICEFWrapper.");
                }

                visits[o] = wrapper;

                replwrap = (ICEFWrapper)wrapper;

                // Effectively presents all current values - we assume codegen has properly implemented use of storage
                replwrap.SetCopyTo(o);

                // Deep copy of properties on this object
                CopyPropertyValuesObject(o, replwrap, isNew, ss, null, visits);
            }

            return replwrap;
        }

        public static ICEFWrapper? CreateWrapper(bool isNew, object o, ServiceScope ss, IDictionary<object, object>? visits = null)
        {
            return InternalCreateWrapper(isNew, o, Globals.MissingWrapperAllowed, ss, new Dictionary<object, ICEFWrapper>(Globals.DefaultDictionaryCapacity), visits ?? new Dictionary<object, object>(Globals.DefaultDictionaryCapacity));
        }

        public static ICEFInfraWrapper? CreateInfraWrapper(WrappingSupport need, WrappingAction action, bool isNew, object o, ObjectState? initState, IDictionary<string, object?>? props, IDictionary<string, Type>? types, ServiceScope? ss)
        {
            // Goal is to provision the lowest overhead object based on need!
            ICEFInfraWrapper? infrawrap = null;

            // New case supported: type has extended properties we need to explicitly set
            if (props != null)
            {
                PopulateNestedPropertiesFromValues(o, props);
            }

            if (action != WrappingAction.NoneOrProvisionedAlready)
            {
                if ((o is INotifyPropertyChanged) || (need & WrappingSupport.Notifications) == 0)
                {
                    if ((need & WrappingSupport.DataErrors) != 0)
                    {
                        infrawrap = new DynamicWithValuesBagErrors(o, initState.GetValueOrDefault(isNew ? ObjectState.Added : ObjectState.Unchanged), props, types);
                    }
                    else
                    {
                        if ((need & WrappingSupport.OriginalValues) != 0)
                        {
                            infrawrap = new DynamicWithValuesAndBag(o, initState.GetValueOrDefault(isNew ? ObjectState.Added : ObjectState.Unchanged), props, types);
                        }
                        else
                        {
                            infrawrap = new DynamicWithBag(o, props, types);
                        }
                    }
                }
                else
                {
                    infrawrap = new DynamicWithAll(o, initState.GetValueOrDefault(isNew ? ObjectState.Added : ObjectState.Unchanged), props, types);
                }
            }

            return infrawrap;
        }

        /// <summary>
        /// A traverser of any object graph, looking for specific types to invoke delegate for.
        /// </summary>
        /// <param name="o"></param>
        /// <param name="types"></param>
        /// <param name="toRun"></param>
        public static void NodeVisiter(object o, HashSet<Type> types, Action<object> toRun)
        {
            if (o == null)
            {
                return;
            }

            var visits = new ConcurrentDictionary<object, bool>(Globals.DefaultCollectionConcurrencyLevel, Globals.DefaultDictionaryCapacity);

            InternalNodeVisiter(o, types, toRun, visits);
        }

        private static void InternalNodeVisiter(object o, HashSet<Type> types, Action<object> toRun, IDictionary<object, bool> visits)
        {
            if (visits.ContainsKey(o))
            {
                return;
            }

            visits[o] = true;

            var props = o.FastGetAllProperties(true, true);
            var maxdop = Globals.EnableParallelPropertyParsing && Environment.ProcessorCount > 4 && props.Count() > Environment.ProcessorCount ? Environment.ProcessorCount >> 2 : 1;

            Parallel.ForEach(props, new ParallelOptions() { MaxDegreeOfParallelism = maxdop }, (prop) =>
            {
                var name = prop.name;
                var type = prop.type;
                var sv = o.FastGetValue(name);

                if (sv != null && !type.IsValueType && (sv is IEnumerable || !type.FullName!.StartsWith("System.")))
                {
                    // Either type matches, or is an enumerable of a type we're interested in, in which case loop for each
                    if (types.Contains(type))
                    {
                        toRun(sv);
                    }
                    else
                    {
                        if (sv is IEnumerable asEnum)
                        {
                            var sValEnum = asEnum.GetEnumerator();

                            while (sValEnum.MoveNext())
                            {
                                var sv2 = sValEnum.Current;
                                var sv2t = sv2.GetType();

                                if (sv2 != null && !sv2t.IsValueType && (sv2 is IEnumerable || !sv2t.FullName!.StartsWith("System.")))
                                {
                                    if (types.Contains(sv2t))
                                    {
                                        toRun(sv2);
                                    }

                                    InternalNodeVisiter(sv2, types, toRun, visits);
                                }
                            }
                        }
                    }
                }
            });
        }

        /// <summary>
        /// Performs a depth-first traversal of object graph, invoking a delegate of choice for each infrastructure wrapper found.
        /// </summary>
        /// <param name="iw">Root object to start traversal.</param>
        /// <param name="toRun">A delegate to invoke for each infrastructure wrapper found.</param>
        public static void NodeVisiter(ICEFInfraWrapper iw, Action<ICEFInfraWrapper> toRun)
        {
            InternalNodeVisiter(iw, toRun, new ConcurrentDictionary<object, bool>(Globals.DefaultCollectionConcurrencyLevel, Globals.DefaultDictionaryCapacity));
        }

        private static void InternalNodeVisiter(ICEFInfraWrapper iw, Action<ICEFInfraWrapper> toRun, IDictionary<object, bool> visits)
        {
            if (visits.ContainsKey(iw))
            {
                return;
            }

            visits[iw] = true;

            var av = (from a in iw.GetAllValues() where a.Value != null && !a.Value.GetType().IsValueType && !a.Value.GetType().FullName!.StartsWith("System.") select a).ToList();
            var maxdop = Globals.EnableParallelPropertyParsing && Environment.ProcessorCount > 4 && av.Count > Environment.ProcessorCount ? Environment.ProcessorCount >> 2 : 1;

            Parallel.ForEach(av, new ParallelOptions() { MaxDegreeOfParallelism = maxdop }, (kvp) =>
            {
                if (kvp.Value is IEnumerable asEnum)
                {
                    var sValEnum = asEnum.GetEnumerator();

                    while (sValEnum.MoveNext())
                    {
                        var iw2 = CEF.CurrentServiceScope.GetOrCreateInfra(sValEnum.Current, false);

                        if (iw2 != null)
                        {
                            InternalNodeVisiter(iw2, toRun, visits);
                        }
                    }
                }
                else
                {
                    // If it's a tracked object, recurse
                    var iw2 = CEF.CurrentServiceScope.GetOrCreateInfra(kvp.Value, false);

                    if (iw2 != null)
                    {
                        InternalNodeVisiter(iw2, toRun, visits);
                    }
                }
            });

            toRun.Invoke(iw);
        }
    }
}
