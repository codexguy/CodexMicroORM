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
2/2020     0.9.5   Enabled nullable (Joel Champagne)
***********************************************************************/
#nullable enable
using System;
using System.ComponentModel;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Text;
using System.Collections.Concurrent;
using System.Data;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using System.Threading;
using System.Linq.Expressions;

#if CGEH
namespace CodexMicroORM.Core.CG
#else
using CodexMicroORM.Core.Services;
using CodexMicroORM.Core.Helper;

namespace CodexMicroORM.Core
#endif
{
    /// <summary>
    /// Not intended for general consumption, helper functions for the framework.
    /// </summary>
    internal static class InternalExtensions
    {
#if !CGEH
        public static bool HasProperty(this object o, string propName)
        {
            if (o == null)
                return false;

            if (o is ICEFInfraWrapper wrapper)
            {
                return wrapper.HasProperty(propName);
            }

            return o.FastPropertyReadable(propName);
        }
#endif

        public static TV AssignReturn<TK, TV>(this Dictionary<TK, TV> dic, TK key, TV val) where TK : notnull
        {
            dic[key] = val;
            return val;
        }

        public static TV TestAssignReturn<TK, TV>(this Dictionary<TK, TV> dic, TK key, Func<TV> getval) where TK : notnull
        {
            if (dic.TryGetValue(key, out var v))
            {
                return v;
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
        private static readonly ConcurrentDictionary<Type, Type> _typeMap = new();

        private static readonly Regex _splitter = new(@"^(?:\[?(?<s>.+?)\]?\.)?\[?(?<n>.+?)\]?$", RegexOptions.Compiled);

        private static long _parallelCount = 0;

        public static (string schema, string name) SplitIntoSchemaAndName(this string? fullname)
        {
            if (string.IsNullOrWhiteSpace(fullname))
            {
                return ("", "");
            }

            var matObj = _splitter.Match(fullname);
            return (matObj.Groups["s"].Value, matObj.Groups["n"].Value);
        }

        public static int MinOf(this int i1, int i2)
        {
            if (i1 < i2)
            {
                return i1;
            }

            return i2;
        }

        public static int MaxOf(this int i1, int i2)
        {
            if (i1 > i2)
            {
                return i1;
            }

            return i2;
        }

        public static T[] Arrayize<T>(this T item)
        {
            return [ item ];
        }

        /// <summary>
        /// Implements AddRange for HashSet.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="hs"></param>
        /// <param name="toAdd"></param>
        /// <param name="withClear"></param>
        public static void AddRange<T>(this HashSet<T> hs, IEnumerable<T> toAdd, bool withClear = false)
        {
            if (withClear)
            {
                hs.Clear();
            }

            foreach (var i in toAdd)
            {
                hs.Add(i);
            }
        }

        /// <summary>
        /// Implements AddRange for ObservableCollection.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="c"></param>
        /// <param name="toAdd"></param>
        /// <param name="withClear"></param>
        public static void AddRange<T>(this ObservableCollection<T> c, IEnumerable<T> toAdd, bool withClear = false)
        {
            if (withClear)
            {
                c.Clear();
            }

            foreach (var i in toAdd)
            {
                c.Add(i);
            }
        }

        /// <summary>
        /// Processes elements of source enumerable, sequentially. Supports gauranteed exec over all elements or stop on error (default). (Signature similar enough to async varieties to make transitioning easier.)
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="items"></param>
        /// <param name="work"></param>
        /// <param name="earlystop"></param>
        public static void Sequential<T>(this IEnumerable<T> items, Action<T> work, bool earlystop = true)
        {
            if (items == null || !items.Any())
            {
                return;
            }

            List<Exception> faults = [];

            foreach (var i in items)
            {
                try
                {
                    work(i);
                }
                catch (Exception ex)
                {
                    if (earlystop)
                    {
                        throw;
                    }

                    faults.Add(ex);
                }
            }

            if (faults.Count > 0)
            {
                throw new AggregateException(faults);
            }
        }

        /// <summary>
        /// Processes elements of source enumerable, sequentially using async. Supports gauranteed exec over all elements or stop on error (default).
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="items"></param>
        /// <param name="work"></param>
        /// <param name="earlystop"></param>
        /// <returns></returns>
        public static async Task SequentialAsync<T>(this IEnumerable<T> items, Action<T> work, bool earlystop = true)
        {
            if (items == null || !items.Any())
            {
                return;
            }

            List<Exception> faults = [];

            foreach (var i in items)
            {
                try
                {
                    await Task.Run(() =>
                    {
                        work.Invoke(i);
                    });
                }
                catch (Exception ex)
                {
                    if (earlystop)
                    {
                        throw;
                    }

                    faults.Add(ex);
                }
            }

            if (faults.Count > 0)
            {
                throw new AggregateException(faults);
            }
        }

        /// <summary>
        /// Processes elements of source enumerable, sequentially using async. Supports gauranteed exec over all elements or stop on error (default).
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="items"></param>
        /// <param name="work"></param>
        /// <param name="earlystop"></param>
        /// <returns></returns>
        public static async Task SequentialAsync<T>(this IEnumerable<T> items, Func<T, Task> work, bool earlystop = true)
        {
            if (items == null || !items.Any())
            {
                return;
            }

            List<Exception> faults = [];

            // Why include Task.Run here? Without it, observe that work can end up carried out NOT in background where wish to use this to, for example, avoid work on UI thread, etc.
            await Task.Run<int>(async () =>
            {
                foreach (var i in items)
                {
                    try
                    {
                        await work.Invoke(i);
                    }
                    catch (Exception ex)
                    {
                        if (earlystop)
                        {
                            throw;
                        }

                        faults.Add(ex);
                    }
                }

                return items.Count();
            });

            if (faults.Count > 0)
            {
                throw new AggregateException(faults);
            }
        }

        private static int ParallelToRun()
        {
            var pc = Environment.ProcessorCount;
            var cc = Interlocked.Read(ref _parallelCount);
            var torun = 2.0 * (pc * pc) / (cc == 0 ? 1 : cc);
            var res = Convert.ToInt32(torun < 2 ? 2 : torun > pc * 2 ? pc * 2 : torun);
            return res;
        }

        /// <summary>
        /// Processes elements of source enumerable, in parallel using async. Any exceptions are collected and thrown in an AggregateException at end. Supports gauranteed exec over all elements or stop on error.
        /// </summary>
        /// <typeparam name="TSource"></typeparam>
        /// <param name="source"></param>
        /// <param name="work"></param>
        /// <param name="dop">Max number of worker threads; defaults to number of CPU.</param>
        /// <param name="earlystop"></param>
        /// <returns></returns>
        public static async Task ParallelAsync<TSource>(this IEnumerable<TSource> source, Action<TSource> work, int? dop = null, bool earlystop = false)
        {
            if (source == null || !source.Any())
            {
                return;
            }

            if (dop.GetValueOrDefault() <= 0)
            {
                dop = ParallelToRun();
            }

            List<Exception> faults = [];
            List<Task<Exception?>> worklist = [];
            var senum = source.GetEnumerator();
            var morework = true;

            for (int i = 0; i < dop!.Value && morework; ++i)
            {
                morework = senum.MoveNext();

                if (morework)
                {
                    var wi = senum.Current;
                    worklist.Add(Task.Run(async () => { return await ProcessAsync(wi, work).ConfigureAwait(false); }));
                }
            }

            while (morework)
            {
                await Task.WhenAny(worklist);

                for (int i = 0; i < worklist.Count && morework; ++i)
                {
                    var wli = worklist[i];

                    if (wli.IsCompleted)
                    {
                        var ex = await wli;

                        if (ex != null)
                        {
                            faults.Add(ex);

                            if (earlystop)
                            {
                                morework = false;
                                break;
                            }
                        }

                        morework = senum.MoveNext();

                        if (morework)
                        {
                            var wi = senum.Current;
                            worklist[i] = Task.Run(async () => { return await ProcessAsync(wi, work).ConfigureAwait(false); });
                        }
                    }
                }
            }

            await Task.WhenAll(worklist);

            foreach (var wli in worklist)
            {
                var ex = await wli;

                if (ex != null)
                {
                    if (!earlystop || !faults.Contains(ex))
                    {
                        faults.Add(ex);
                    }
                }
            }

            if (faults.Count > 0)
            {
                throw new AggregateException(faults);
            }
        }

        /// <summary>
        /// Processes elements of source enumerable, in parallel using async. Any exceptions are collected and thrown in an AggregateException at end. Supports gauranteed exec over all elements or stop on error. Works with async lambda's.
        /// </summary>
        /// <typeparam name="TSource"></typeparam>
        /// <param name="source"></param>
        /// <param name="work"></param>
        /// <param name="dop">Max number of worker threads; defaults to number of CPU.</param>
        /// <param name="earlystop"></param>
        /// <returns></returns>
        public static async Task ParallelAsync<TSource>(this IEnumerable<TSource> source, Func<TSource, Task> work, int? dop = null, bool earlystop = false)
        {
            if (source == null || !source.Any())
            {
                return;
            }

            if (dop.GetValueOrDefault() <= 0)
            {
                dop = ParallelToRun();
            }

            List<Exception> faults = [];
            List<Task<Exception?>> worklist = [];
            var senum = source.GetEnumerator();
            var morework = true;

            for (int i = 0; i < dop!.Value && morework; ++i)
            {
                morework = senum.MoveNext();

                if (morework)
                {
                    var wi = senum.Current;
                    worklist.Add(Task.Run(async () => { return await ProcessAsync(wi, work).ConfigureAwait(false); }));
                }
            }

            while (morework)
            {
                await Task.WhenAny(worklist);

                for (int i = 0; i < worklist.Count && morework; ++i)
                {
                    var wli = worklist[i];

                    if (wli.IsCompleted)
                    {
                        var ex = await wli;

                        if (ex != null)
                        {
                            faults.Add(ex);

                            if (earlystop)
                            {
                                morework = false;
                                break;
                            }
                        }

                        morework = senum.MoveNext();

                        if (morework)
                        {
                            var wi = senum.Current;
                            worklist[i] = Task.Run(async () => { return await ProcessAsync(wi, work).ConfigureAwait(false); });
                        }
                    }
                }
            }

            await Task.WhenAll(worklist);

            foreach (var wli in worklist)
            {
                var ex = await wli;

                if (ex != null)
                {
                    if (!earlystop || !faults.Contains(ex))
                    {
                        faults.Add(ex);
                    }
                }
            }

            if (faults.Count > 0)
            {
                throw new AggregateException(faults);
            }
        }

        private static async Task<Exception?> ProcessAsync<TSource>(TSource item, Action<TSource> taskSelector)
        {
            try
            {
                await Task.Run(() =>
                {
                    taskSelector(item);
                });

                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        private static async Task<Exception?> ProcessAsync<TSource>(TSource item, Func<TSource, Task> taskSelector)
        {
            try
            {
                Interlocked.Increment(ref _parallelCount);
                await taskSelector(item);
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
            finally
            {
                Interlocked.Decrement(ref _parallelCount);
            }
        }

        /// <summary>
        /// A safe way to take left-most characters in string; if overflows, appends ellipsis.
        /// </summary>
        /// <param name="str"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        public static string? LeftWithEllipsis(this string? str, int count)
        {
            if (string.IsNullOrEmpty(str))
            {
                return str;
            }

            if (str!.Length <= count - 4)
            {
                return str;
            }

            return str.Substring(0, count - 4) + " ...";
        }

        /// <summary>
        /// A safe way to take left-most characters in string.
        /// </summary>
        /// <param name="str"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        public static string? Left(this string? str, int count)
        {
            if (string.IsNullOrEmpty(str))
            {
                return str;
            }

            if (str!.Length <= count)
            {
                return str;
            }

            return str.Substring(0, count);
        }

        /// <summary>
        /// A safe way to take right-mist characters in string.
        /// </summary>
        /// <param name="str"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        public static string? Right(this string? str, int count)
        {
            if (string.IsNullOrEmpty(str))
            {
                return str;
            }

            if (str!.Length <= count)
            {
                return str;
            }

            return str.Substring(str.Length - count, count);
        }

        /// <summary>
        /// A safe way to extract InnerText from an XmlNode.
        /// </summary>
        /// <param name="xn"></param>
        /// <param name="defval"></param>
        /// <returns></returns>
        public static string InnerTextSafe(this System.Xml.XmlNode xn, string defval = "")
        {
            return xn == null ? defval : xn.InnerText ?? defval;
        }

        /// <summary>
        /// An all-encompassing way to compare two objects for equality, with intelligence around casting.
        /// </summary>
        /// <param name="o1"></param>
        /// <param name="o2"></param>
        /// <param name="canCompareNull"></param>
        /// <returns></returns>
        public static bool IsSame(this object? o1, object? o2, bool canCompareNull = true)
        {
            if (o1 == DBNull.Value)
            {
                o1 = null;
            }

            if (o2 == DBNull.Value)
            {
                o2 = null;
            }

            if (canCompareNull)
            {
                if (o1 == null && o2 == null)
                {
                    return true;
                }

                if (o1 == null && o2 != null)
                {
                    return false;
                }

                if (o2 == null && o1 != null)
                {
                    return false;
                }
            }
            else
            {
                if (o1 == null || o2 == null)
                {
                    return false;
                }
            }

            if (o1!.GetType() == o2!.GetType())
            {
                return o1.Equals(o2);
            }

            if (o1 is IConvertible && o2 is IConvertible)
            {
                return Convert.ChangeType(o1, o2.GetType()).Equals(o2);
            }

            return o1.ToString() == o2.ToString();
        }

        /// <summary>
        /// Attempts to change input string into a desired type using rules that go beyond BCL Convert.
        /// </summary>
        /// <param name="source"></param>
        /// <param name="prefType"></param>
        /// <param name="defaultIfFail"></param>
        /// <returns></returns>
        public static object? CoerceType(this string? source, Type prefType, object? defaultIfFail = null)
        {
            if (source == null)
            {
                return null;
            }

            if (prefType == typeof(string))
            {
                return source;
            }

            var nt = Nullable.GetUnderlyingType(prefType);

            if (nt != null)
            {
                if (string.IsNullOrEmpty(source))
                {
                    return Activator.CreateInstance(prefType);
                }

                prefType = nt;
            }

            if (prefType.IsEnum)
            {
                return Enum.Parse(prefType, source);
            }

            if (prefType == typeof(TimeSpan))
            {
                if (TimeSpan.TryParse(source, out TimeSpan ts))
                {
                    return ts;
                }
            }

            if (prefType == typeof(OnlyDate))
            {
                if (OnlyDate.TryParse(source, out OnlyDate dov))
                {
                    return dov;
                }
            }

            if (source is IConvertible)
            {
                // Special conversion possibilities for booleans
                if (prefType == typeof(bool))
                {
                    if (source == "0")
                    {
                        return false;
                    }
                    if (source == "-1")
                    {
                        return true;
                    }
                    if (source == "1")
                    {
                        return true;
                    }
                    if (string.Compare(source, "false", true) == 0)
                    {
                        return false;
                    }
                    if (string.Compare(source, "true", true) == 0)
                    {
                        return true;
                    }
                }
                else
                {
                    if (prefType == typeof(Guid))
                    {
                        return new Guid(source);
                    }
                }

                return Convert.ChangeType(source, prefType);
            }

            if (!source.GetType().IsValueType)
            {
                return source;
            }

            if (defaultIfFail != null)
            {
                return defaultIfFail;
            }

            throw new InvalidCastException("Cannot coerce type.");
        }

        public static object EnsureNullable(this object source, Type nullType)
        {
            if (source == null)
            {
                return Activator.CreateInstance(nullType) ?? throw new InvalidOperationException($"Failed to instantiate {nullType.Name}.");
            }

            if (source.GetType().Equals(nullType))
            {
                return source;
            }

            return Activator.CreateInstance(nullType, source) ?? throw new InvalidOperationException($"Failed to instantiate {nullType.Name}.");
        }

        /// <summary>
        /// Attempts to change input object into a desired type using rules that go beyond BCL Convert.
        /// </summary>
        /// <param name="source"></param>
        /// <param name="prefType"></param>
        /// <returns></returns>
        public static object? CoerceObjectType(this object? source, Type prefType, bool checkValidNull = false)
        {
            if (source == null || DBNull.Value.Equals(source))
            {
                // We can only return null if return type is ref type or nullable value type - otherwise we might revert to using default or could get exception
                if (checkValidNull && prefType != null)
                {
                    if (prefType.IsValueType && Nullable.GetUnderlyingType(prefType) == null)
                    {
                        return Activator.CreateInstance(prefType);
                    }
                }

                return null;
            }

            var st = source.GetType();
            var bt = Nullable.GetUnderlyingType(st);
            var pbt = Nullable.GetUnderlyingType(prefType);

            if (bt == null && pbt == null && prefType.IsValueType)
            {
                bt = typeof(Nullable<>).MakeGenericType(prefType);
            }

            // Avoid casting to string if the types match
            if (st == prefType || bt == prefType || st == pbt)
            {
                return source;
            }

            if (prefType == typeof(string))
            {
                return source.ToString();
            }

            if (st == typeof(OnlyDate) || bt == typeof(OnlyDate))
            {
                return ((OnlyDate)source).ToDateTime().ToString("O").CoerceType(prefType);
            }

            if (st == typeof(DateTime) || bt == typeof(DateTime))
            {
                return ((DateTime)source).ToString("O").CoerceType(prefType);
            }

            return source.ToString().CoerceType(prefType);
        }

        /// <summary>
        /// Attempts to change input string into a desired type using rules that go beyond BCL Convert.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="source"></param>
        /// <returns></returns>
        public static T? CoerceType<T>(this string? source)
        {
            return (T?)CoerceType(source, typeof(T));
        }

#if !CGEH
        /// <summary>
        /// Save a specific entity set. Restricts/fitlers to rows present in the collection.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="toSave">EntitySet to use as a save filter.</param>
        /// <param name="settings">Optional save config settings.</param>
        /// <returns></returns>
        public static EntitySet<T> DBSave<T>(this EntitySet<T> toSave, DBSaveSettings? settings = null) where T: class, new()
        {
            settings ??= new DBSaveSettings();
            settings.SourceList = toSave;
            settings.EntityPersistName ??= CEF.GetEntityPersistName<T>(toSave);
            settings.EntityPersistType = typeof(T);

            CEF.DBSave(settings);
            return toSave;
        }

        public static void ValidateOrAssignMandatoryValue<T>(this EntitySet<T> toCheck, string field, object value) where T : class, new()
        {
            foreach (var t in toCheck)
            {
                var iw = t.AsInfraWrapped();
                var ov = iw?.GetValue(field);

                if (string.Compare(value?.ToString(), ov?.ToString(), true) != 0)
                {
                    if (ov?.ToString()?.Length == 0 && value?.ToString()?.Length > 0)
                    {
                        iw?.SetValue(field, value);
                    }
                    else
                    {
                        throw new CEFInvalidStateException(InvalidStateType.LowLevelState);
                    }
                }
            }

            foreach (var to in CEF.CurrentServiceScope.GetAllTrackedByType(typeof(T)))
            {
                var iw = to.GetCreateInfra();

                if (iw != null)
                {
                    if (iw.GetRowState() == ObjectState.Deleted)
                    {
                        var ov = iw.GetOriginalValue(field, false);

                        if (string.Compare(value?.ToString(), ov?.ToString(), true) != 0)
                        {
                            if (ov?.ToString()?.Length != 0 || value?.ToString()?.Length == 0)
                            {
                                throw new CEFInvalidStateException(InvalidStateType.LowLevelState);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Takes a potentially unwrapped object and returns a dynamic (DLR) object that exposes the same properties plus all available "extended properties".
        /// Note: this is not always "bindable" for UI data binding (see GenericBindableSet for WPF, for example).
        /// </summary>
        /// <param name="unwrapped">Any object that's tracked within the current service scope.</param>
        /// <returns></returns>
        public static dynamic? AsDynamic(this object unwrapped)
        {
            return CEF.CurrentServiceScope.GetDynamicWrapperFor(unwrapped);
        }

        /// <summary>
        /// Derivative of AsInfraWrapped, returns non-nullable value and throws exception if for any reason wrapper creation was not possible.
        /// </summary>
        /// <param name="o">Any object that's tracked within the current service scope.</param>
        /// <param name="canCreate">If false, the object must have an existing infrastructure wrapper or null is returned; if true, a new wrapper can be created.</param>
        /// <param name="ss">Optional explicit service scope tracking object.</param>
        /// <returns></returns>
        public static ICEFInfraWrapper MustInfraWrap(this object o, bool canCreate = true, ServiceScope? ss = null)
        {
            return AsInfraWrapped(o, canCreate, ss) ?? throw new CEFInvalidStateException("Could not create wrapper object. This indicates a likely programming issue.");
        }

        /// <summary>
        /// Infrastructure wrappers offer extended information about tracked objects, such as their "row state" (added, modified, etc.).
        /// </summary>
        /// <param name="o">Any object that's tracked within the current service scope.</param>
        /// <param name="canCreate">If false, the object must have an existing infrastructure wrapper or null is returned; if true, a new wrapper can be created.</param>
        /// <param name="ss">Optional explicit service scope tracking object.</param>
        /// <returns></returns>
        public static ICEFInfraWrapper? AsInfraWrapped(this object o, bool canCreate = true, ServiceScope? ss = null)
        {
            ss ??= CEF.CurrentServiceScope;
            var rim = ss.ResolvedRetrievalIdentityMode(o);

            ICEFInfraWrapper? w = ss.GetOrCreateInfra(o, canCreate);

            if (w == null && canCreate)
            {
                var t = ss.IncludeObjectNonGeneric(o, null, rim);
                
                if (t != null)
                {
                    w = ss.GetOrCreateInfra(t, canCreate);
                }

                if (w == null)
                {
                    throw new CEFInvalidStateException(InvalidStateType.ObjectTrackingIssue);
                }
            }

            return w;
        }

        public static T? AsNullValue<T>(this object? v) where T : struct
        {
            if (v == null)
            {
                return null;
            }

            if (v is T?)
            {
                return (T?)v;
            }

            if (v is T t)
            {
                return new T?(t);
            }

            throw new CEFInvalidStateException(InvalidStateType.DataTypeIssue, $"Invalid cast of type {typeof(T).Name}?.");
        }

        public static object? TypeFixup(this object? v, Type tt)
        {
            if (v == null)
            {
                return null;
            }

            if (tt == typeof(OnlyDate?) || tt == typeof(OnlyDate))
            {
                if (v.GetType() == typeof(DateTime))
                {
                    return new OnlyDate((DateTime)v);
                }
            }

            return v;
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
            var iw = o.AsInfraWrapped(true) ?? throw new CEFInvalidStateException(InvalidStateType.ObjectTrackingIssue);

            if (iw.HasProperty(propName))
            {
                var v = iw.GetValue(propName);

                if (v == null)
                {
                    return null;
                }

                if (v is T?)
                {
                    return (T?)v;
                }

                if (v is T t)
                {
                    return new T?(t);
                }

                throw new CEFInvalidStateException(InvalidStateType.DataTypeIssue, $"Invalid cast of type {typeof(T).Name}?.");
            }

            return null;
        }

        public static T WrappedValueTypeValue<T>(this ICEFInfraWrapper iw, string propName) where T : struct
        {
            if (iw.HasProperty(propName))
            {
                var v = iw.GetValue(propName);

                if (v != null)
                {
                    if (v is T?)
                    {
                        return (T)v;
                    }

                    if (v is T t)
                    {
                        return t;
                    }

                    throw new CEFInvalidStateException(InvalidStateType.DataTypeIssue, $"Invalid cast of type {typeof(T).Name}?.");
                }
            }

            return default;
        }

        public static T? WrappedValueTypeNullValue<T>(this ICEFInfraWrapper iw, string propName) where T : struct
        {
            if (iw.HasProperty(propName))
            {
                var v = iw.GetValue(propName);

                if (v == null)
                {
                    return default;
                }

                if (v is T?)
                {
                    return (T?)v;
                }

                if (v is T t)
                {
                    return new T?(t);
                }

                throw new CEFInvalidStateException(InvalidStateType.DataTypeIssue, $"Invalid cast of type {typeof(T).Name}?.");
            }

            return default;
        }

        public static T WrappedRefTypeValue<T>(this ICEFInfraWrapper iw, string propName) where T : class
        {
            if (iw.HasProperty(propName))
            {
                var v = iw.GetValue(propName);

                if (v != null)
                {
                    if (typeof(T) == typeof(string))
                    {
                        return (v.ToString() as object) as T ?? throw new CEFInvalidStateException(InvalidStateType.DataTypeIssue, $"Invalid cast of type {typeof(T).Name}."); ;
                    }

                    if (v is T t)
                    {
                        return t;
                    }

                    return (T)Convert.ChangeType(v, typeof(T));
                }
            }

            throw new NullReferenceException($"Property {propName} expects a non-null value.");
        }

        public static T? WrappedRefTypeNullValue<T>(this ICEFInfraWrapper iw, string propName) where T : class
        {
            if (iw.HasProperty(propName))
            {
                var v = iw.GetValue(propName);

                if (v == null)
                {
                    return default;
                }

                if (typeof(T) == typeof(string))
                {
                    return (v.ToString() as object) as T;
                }

                if (v is T t)
                {
                    return t;
                }

                return (T)Convert.ChangeType(v, typeof(T));
            }

            return default;
        }

        public static void SetMultipleProperties(this object target, object src, ServiceScope? ss = null)
        {
            ss ??= CEF.CurrentServiceScope;

            using (CEF.UseServiceScope(ss))
            {
                var iw = src.AsInfraWrapped(false);

                if (iw != null)
                {
                    foreach (var (name, _, _, _) in target.FastGetAllProperties(true, true))
                    {
                        if (iw.BagValuesOnly().TryGetValue(name, out var sv))
                        {
                            if (!CEF.RegisteredPropertyNameTreatReadOnly.Contains(name))
                            {
                                target.FastSetValue(name, sv);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Abstracts property value access to work with virtually any type of object, accessing a named property that's a reference type.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="o"></param>
        /// <param name="propName"></param>
        /// <returns></returns>
        public static T PropertyValue<T>(this object o, string propName)
        {
            var iw = o.AsInfraWrapped(true) ?? throw new CEFInvalidStateException(InvalidStateType.ObjectTrackingIssue);

            if (iw.HasProperty(propName))
            {
                var v = iw.GetValue(propName);

                if (v == null)
                {
                    return default!;
                }

                if (v is T t)
                {
                    return t;
                }

                throw new CEFInvalidStateException(InvalidStateType.DataTypeIssue, $"Invalid cast of type {typeof(T).Name}.");
            }

            return default!;
        }

        public static IEnumerable<ICEFInfraWrapper> AllAsInfraWrapped<T>(this IEnumerable<T> items) where T: class, new()
        {
            foreach (var i in items)
            {
                var iw = i.AsInfraWrapped();

                if (iw != null)
                {
                    yield return iw;
                }
            }
        }

        public static IEnumerable<dynamic> AllAsDynamic<T>(this IEnumerable<T> items) where T : class, new()
        {
            foreach (var i in items)
            {
                var d = i.AsDynamic();

                if (d != null)
                {
                    yield return d;
                }
            }
        }

        public static (int code, string message) AsString(this IEnumerable<(ValidationErrorCode error, string? message)> msgs, ValidationErrorCode? only = null, string concat = " ")
        {
            int code = 0;
            StringBuilder sb = new();

            foreach (var (error, message) in msgs)
            {
                if (!only.HasValue || (only.Value & error) != 0)
                {
                    if (sb.Length > 0)
                    {
                        sb.Append(concat);
                    }

                    sb.Append(message);
                    code |= (int)error;
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
                throw new CEFInvalidStateException(InvalidStateType.ArgumentNull, nameof(wrapped));
            }

            if (wrapped is ICEFInfraWrapper wrapper)
            {
                var wo = wrapper.GetWrappedObject();

                if (wo != null)
                {
                    wrapped = wo;
                }
            }

            if (wrapped is ICEFWrapper wrapper1)
            {
                return wrapper1.GetBaseType();
            }

            var wt = wrapped.GetType();

            if (_typeMap.TryGetValue(wt, out var rt2))
            {
                return rt2;
            }

            var uw = CEF.CurrentServiceScope.GetWrapperOrTarget(wrapped);

            if (uw is ICEFWrapper wrapper2)
            {
                var rt = wrapper2.GetBaseType();
                _typeMap[wt] = rt;
                return rt;
            }

            if (uw == null)
            {
                // It's an error if the wrapped object presents itself as an IW object at this point!
                if (wrapped is ICEFInfraWrapper)
                {
                    throw new CEFInvalidStateException(InvalidStateType.ObjectTrackingIssue);
                }

                _typeMap[wt] = wt;
                return wt;
            }

            _typeMap[wt] = uw.GetType();
            return uw.GetType();
        }

        /// <summary>
        /// A short-hand way to apply the RetrievalIdentityMode.AllowMultipleWithShadowProp setting for a specific set in the current service scope.
        /// This has the effect of ignoring any pre-existing entity key (use surrogate shadow key _ID instead), so duplicates are allowed when retrieving.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="es"></param>
        /// <returns></returns>
        public static EntitySet<T> AllowRetrievalDups<T>(this EntitySet<T> es) where T : class, new()
        {
            CEF.CurrentServiceScope.SetRetrievalIdentityForObject(es, RetrievalIdentityMode.AllowMultipleWithShadowProp);
            return es;
        }

        /// <summary>
        /// Similar to AsUnwrapped but will throw an exception if null would have been returned.
        /// </summary>
        /// <param name="wrapped">An object in the current service scope (can be wrapped or unwrapped).</param>
        /// <returns>The "least wrapped" instance of the input object.</returns>
        public static object MustUnwrap(this object wrapped)
        {
            return AsUnwrapped(wrapped) ?? throw new CEFInvalidStateException("Could not unwrap object. This indicates a likely programming issue.");
        }

        /// <summary>
        /// Returns the "least wrapped" version if the input (potentially) wrapped object.
        /// </summary>
        /// <param name="wrapped">An object in the current service scope (can be wrapped or unwrapped).</param>
        /// <returns>The "least wrapped" instance of the input object.</returns>
        public static object? AsUnwrapped(this object wrapped)
        {
            if (wrapped != null)
            {
                ICEFWrapper? w;

                if ((w = (wrapped as ICEFWrapper)) != null)
                {
                    var uw = w.GetCopyTo();

                    if (uw != null)
                    {
                        return uw;
                    }
                }

                if (wrapped is ICEFInfraWrapper iw)
                {
                    var wo = iw.GetWrappedObject();

                    if (wo != null)
                    {
                        w = wo as ICEFWrapper;

                        if (w != null)
                        {
                            wo = w.GetCopyTo();
                            wo ??= w;
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
        public static T? AsWrapped<T>(this object unwrapped) where T : class, ICEFWrapper
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
        public static string? AsJSON(this object o, SerializationMode? mode = null)
        {
            if (o == null)
                return null;

            // Special case - if o is a session scope, we're asking to serialize everything in scope, as one big array of objects!
            if (o is ServiceScope scope)
            {
                return scope.GetScopeSerializationText(mode);
            }

            if (o is ICEFList list)
            {
                return list.GetSerializationText(mode);
            }

            if (o.AsInfraWrapped(false) is not ICEFSerializable iw)
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
                throw new CEFInvalidStateException(InvalidStateType.ArgumentNull, nameof(onlyForType));

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
                throw new CEFInvalidStateException(InvalidStateType.ArgumentNull, nameof(check));

            WrappingHelper.NodeVisiter(iw, (iw2) =>
            {
                if (check(iw2))
                {
                    iw2.AcceptChanges();
                }
            });
        }

        public static string DictionaryKeyFromColumns(this ICEFInfraWrapper iw, IEnumerable<string> cols)
        {
            StringBuilder sb = new(128);

            foreach (var c in cols)
            {
                if (sb.Length > 0)
                {
                    sb.Append('~');
                }

                sb.Append(iw.GetValue(c));
            }

            return sb.ToString();
        }

        /// <summary>
        /// A target EntitySet is updated to look "the same" as a source EntitySet (e.g. a "model"). Objects may be added, updated or removed in the target EntitySet based on the primary key for type T.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="source"></param>
        /// <param name="target"></param>
        /// <exception cref="CEFInvalidStateException"></exception>
        public static void ReconcileEntitySetToEntitySet<T>(this EntitySet<T> source, EntitySet<T> target) where T : class, new()
        {
            var ain = target.AddedIsNew;

            try
            {
                target.AddedIsNew = false;

                // A natural key must be available!
                var key = KeyService.ResolveKeyDefinitionForType(typeof(T));

                if (key == null || key.Count == 0)
                {
                    throw new CEFInvalidStateException(InvalidStateType.BadParameterValue, $"Type {typeof(T).Name} does not have a key defined.");
                }

                var allCol = typeof(T).FastGetAllProperties(true, true);
                var nonKeyCol = (from a in allCol where !key.Contains(a.name) select a);
                var ss = CEF.CurrentServiceScope;

                // Build a dictionary for faster lookup of target, and keep track of what was insert/update
                var setData = target.ToDictionary(key);
                HashSet<T> matched = [];

                // First pass for inserts, updates
                foreach (var s in source)
                {
                    StringBuilder sb = new(128);

                    foreach (var k in key)
                    {
                        if (sb.Length > 0)
                        {
                            sb.Append('~');
                        }
                        sb.Append(s.FastGetValue(k));
                    }

                    if (!setData.TryGetValue(sb.ToString(), out T? entRow))
                    {
                        // Order is important here - establish prop values prior to adding to tracking
                        entRow = new T();
                        s.CopySharedTo(entRow);
                        target.Add(CEF.NewObject(entRow));
                    }

                    matched.Add(entRow.AsUnwrapped() as T ?? throw new InvalidOperationException("Failed to use AsUnwrapped."));
                    var iw = entRow.AsInfraWrapped();

                    if (iw != null)
                    {
                        foreach (var (name, type, _, _) in nonKeyCol)
                        {
                            var setter = ss.GetSetter(iw, name);
                            setter.setter?.Invoke(s.FastGetValue(name).CoerceObjectType(setter.type ?? type, true));
                        }
                    }
                }

                // Second pass for deletes - anything unvisited from above
                foreach (var r in target.ToList())
                {
                    if (r.AsUnwrapped() is T t && !matched.Contains(t))
                    {
                        CEF.DeleteObject(r);
                        target.Remove(r);
                    }
                }
            }
            finally
            {
                target.AddedIsNew = ain;
            }
        }

        /// <summary>
        /// A target EntitySet is updated to look "the same" as a source DataView. Rows may be added, updated or deleted in the EntitySet based on the primary key set for type T.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="source">A source DataView containing data to be merged into target.</param>
        /// <param name="target">A collection of entities that will be updated to match the source DataView.</param>
        public static void ReconcileDataViewToEntitySet<T>(this DataView source, EntitySet<T> target) where T : class, new()
        {
            var ain = target.AddedIsNew;

            try
            {
                target.AddedIsNew = false;

                // A natural key must be available!
                var key = KeyService.ResolveKeyDefinitionForType(typeof(T));

                if (key == null || key.Count == 0)
                {
                    throw new CEFInvalidStateException(InvalidStateType.BadParameterValue, $"Type {typeof(T).Name} does not have a key defined.");
                }

                var allCol = (source.Table ?? throw new InvalidOperationException("Missing Table reference in DataView.")).Columns.Cast<DataColumn>();
                var nonKeyCol = (from a in allCol where !(from b in key where b == a.ColumnName select b).Any() select a);
                var ss = CEF.CurrentServiceScope;

                // Build a dictionary for faster lookup
                var setData = target.ToDictionary(key);
                HashSet<T> matched = [];
                var targProps = typeof(T).FastGetAllPropertiesAsDictionary();

                // First pass for inserts, updates
                foreach (DataRowView drv in source)
                {
                    StringBuilder sb = new(128);

                    foreach (var k in key)
                    {
                        if (sb.Length > 0)
                        {
                            sb.Append('~');
                        }
                        sb.Append(drv[k]);
                    }

                    if (!setData.TryGetValue(sb.ToString(), out T? entRow))
                    {
                        entRow = CEF.NewObject(new T());

                        foreach (DataColumn dc in allCol)
                        {
                            if (targProps.TryGetValue(dc.ColumnName, out var info) && (info.writeable || key.Contains(dc.ColumnName)))
                            {
                                entRow.FastSetValue(dc.ColumnName, drv[dc.ColumnName].CoerceObjectType(info.type ?? dc.DataType, true));
                            }
                        }

                        target.Add(entRow);
                    }
                    else
                    {
                        var iw = entRow.AsInfraWrapped();

                        if (iw != null)
                        {
                            foreach (DataColumn dc in nonKeyCol)
                            {
                                var setter = ss.GetSetter(iw, dc.ColumnName);
                                setter.setter?.Invoke(drv[dc.ColumnName].CoerceObjectType(setter.type ?? dc.DataType, true));
                            }
                        }
                    }

                    matched.Add(entRow.AsUnwrapped() as T ?? throw new InvalidOperationException("Failed to use AsUnwrapped."));
                }

                // Second pass for deletes - anything unvisited from above
                foreach (var r in target.ToList())
                {
                    if (!matched.Contains((r.AsUnwrapped() as T)!))
                    {
                        CEF.DeleteObject(r);
                        target.Remove(r);
                    }
                }
            }
            finally
            {
                target.AddedIsNew = ain;
            }
        }

        /// <summary>
        /// Creates a deep copy of the source object, returning it.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="src"></param>
        /// <returns></returns>
        public static T DeepCopyObject<T>(this T? src, bool tracked = true) where T : class, new()
        {
            var n = (T)(typeof(T).FastCreateNoParm() ?? throw new InvalidOperationException("Could not instantiate object."));

            if (src != null)
            {
                foreach (var (name, _, _, _) in typeof(T).FastGetAllProperties(true, true))
                {
                    if (!CEF.RegisteredPropertyNameTreatReadOnly.Contains(name))
                    {
                        n.FastSetValue(name, src.FastGetValue(name));
                    }
                }
            }

            if (tracked)
            {
                CEF.IncludeObject(n);
            }

            return n;
        }

        /// <summary>
        /// Creates a deep copy of an enumerable.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="src"></param>
        /// <param name="tracked"></param>
        /// <returns></returns>
        public static IEnumerable<T> DeepCopyList<T>(this IEnumerable<T> src, bool tracked = true) where T : class, new()
        {
            foreach (var i in src)
            {
                yield return DeepCopyObject(i, tracked);
            }
        }

        /// <summary>
        /// This overload for CopySharedTo allows you to inspect additional possible property matches (even if types do not match) and let you decide how to handle in the delegate. If the delegate returns true, you are indicating that you handled the property in the delegate; if false is returned, default behavior will be used (value is copied if the types match).
        /// </summary>
        /// <param name="src"></param>
        /// <param name="dest"></param>
        /// <param name="previewHandler"></param>
        /// <exception cref="CEFInvalidStateException"></exception>
        public static void CopySharedTo(this object src, object dest, Func<string, object?, object?, bool, bool> previewHandler)
        {
            if (src == null)
                throw new CEFInvalidStateException(InvalidStateType.ArgumentNull, nameof(src));

            if (dest == null)
                throw new CEFInvalidStateException(InvalidStateType.ArgumentNull, nameof(dest));

            // We can copy from non-nullable to nullable (always)
            foreach (var prop in (from a in src.FastGetAllProperties(true, true)
                                  join b in dest.FastGetAllProperties(true, true) on a.name equals b.name
                                  where !CEF.RegisteredPropertyNameTreatReadOnly.Contains(a.name)
                                  let tm = (a.type == b.type || Nullable.GetUnderlyingType(b.type) == a.type)
                                  select new { a.name, typematch = tm }))
            {
                var sv = src.FastGetValue(prop.name);
                var dv = dest.FastGetValue(prop.name);

                if (!previewHandler(prop.name, sv, dv, prop.typematch) && prop.typematch)
                {
                    dest.FastSetValue(prop.name, sv);
                }
            }

            // We can additionally copy from nullable to non-nullable, but only if the nullable actually holds a non-null value
            foreach (var fi in (from a in src.FastGetAllProperties(true, true)
                                join b in dest.FastGetAllProperties(true, true) on a.name equals b.name
                                where !CEF.RegisteredPropertyNameTreatReadOnly.Contains(a.name)
                                let tm = a.type != b.type && Nullable.GetUnderlyingType(a.type) == b.type
                                let v = src.FastGetValue(a.name)
                                where v != null
                                select new { a.name, value = v, typematch = tm }))
            {
                var sv = fi.value;
                var dv = dest.FastGetValue(fi.name);

                if (!previewHandler(fi.name, sv, dv, fi.typematch) && fi.typematch)
                {
                    dest.FastSetValue(fi.name, sv);
                }
            }
        }

        /// <summary>
        /// Copies all shared properties from one instance to another instance of any arbitrary type. This overload supports preview of before/after values, allows selective overwrite.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="src"></param>
        /// <param name="dest"></param>
        public static void CopySharedTo(this object src, object dest, Func<string, object?, object?, bool> previewHandler)
        {
            if (src == null)
                throw new CEFInvalidStateException(InvalidStateType.ArgumentNull, nameof(src));

            if (dest == null)
                throw new CEFInvalidStateException(InvalidStateType.ArgumentNull, nameof(dest));

            // We can copy from non-nullable to nullable (always)
            foreach (var name in (from a in src.FastGetAllProperties(true, true)
                                  join b in dest.FastGetAllProperties(true, true) on a.name equals b.name
                                  where (a.type == b.type || Nullable.GetUnderlyingType(b.type) == a.type)
                                  && !CEF.RegisteredPropertyNameTreatReadOnly.Contains(a.name)
                                  select a.name))
            {
                var sv = src.FastGetValue(name);
                var dv = dest.FastGetValue(name);

                if (previewHandler(name, sv, dv))
                {
                    dest.FastSetValue(name, sv);
                }
            }

            // We can additionally copy from nullable to non-nullable, but only if the nullable actually holds a non-null value
            foreach (var fi in (from a in src.FastGetAllProperties(true, true)
                                join b in dest.FastGetAllProperties(true, true) on a.name equals b.name
                                where a.type != b.type && Nullable.GetUnderlyingType(a.type) == b.type
                                    && !CEF.RegisteredPropertyNameTreatReadOnly.Contains(a.name)
                                let v = src.FastGetValue(a.name)
                                where v != null
                                select new { a.name, value = v }))
            {
                var sv = fi.value;
                var dv = dest.FastGetValue(fi.name);

                if (previewHandler(fi.name, sv, dv))
                {
                    dest.FastSetValue(fi.name, sv);
                }
            }
        }

        /// <summary>
        /// Copies all shared properties from one instance to another instance of any arbitrary type.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="src"></param>
        /// <param name="dest"></param>
        public static void CopySharedTo(this object src, object dest)
        {
            if (src == null)
                throw new CEFInvalidStateException(InvalidStateType.ArgumentNull, nameof(src));

            if (dest == null)
                throw new CEFInvalidStateException(InvalidStateType.ArgumentNull, nameof(dest));

            // We can copy from non-nullable to nullable (always)
            foreach (var name in (from a in src.FastGetAllProperties(true, true)
                                  join b in dest.FastGetAllProperties(true, true) on a.name equals b.name
                                  where (a.type == b.type || Nullable.GetUnderlyingType(b.type) == a.type)
                                  && !CEF.RegisteredPropertyNameTreatReadOnly.Contains(a.name)
                                  select a.name))
            {
                dest.FastSetValue(name, src.FastGetValue(name));
            }

            // We can additionally copy from nullable to non-nullable, but only if the nullable actually holds a non-null value
            foreach (var fi in (from a in src.FastGetAllProperties(true, true)
                                join b in dest.FastGetAllProperties(true, true) on a.name equals b.name
                                where a.type != b.type && Nullable.GetUnderlyingType(a.type) == b.type
                                    && !CEF.RegisteredPropertyNameTreatReadOnly.Contains(a.name)
                                let v = src.FastGetValue(a.name)
                                where v != null
                                select new { a.name, value = v }))
            {
                dest.FastSetValue(fi.name, fi.value);
            }
        }

        /// <summary>
        /// Copies all shared properties from one instance to another instance of any arbitrary type.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="src"></param>
        /// <param name="dest"></param>
        public static void CopySharedTo(this object src, object dest, bool isExclude, params string[] fields)
        {
            if (src == null)
                throw new CEFInvalidStateException(InvalidStateType.ArgumentNull, nameof(src));

            if (dest == null)
                throw new CEFInvalidStateException(InvalidStateType.ArgumentNull, nameof(dest));

            foreach (var name in (from a in src.FastGetAllProperties(true, true) join b in dest.FastGetAllProperties(true, true) on a.name equals b.name
                                  where (a.type == b.type || Nullable.GetUnderlyingType(b.type) == a.type) &&
                                    (fields?.Length == 0 || (isExclude && !fields!.Contains(a.name)) || (!isExclude && fields!.Contains(a.name)))
                                    && !CEF.RegisteredPropertyNameTreatReadOnly.Contains(a.name)
                                  select a.name))
            {
                dest.FastSetValue(name, src.FastGetValue(name));
            }

            foreach (var fi in (from a in src.FastGetAllProperties(true, true)
                                join b in dest.FastGetAllProperties(true, true) on a.name equals b.name
                                where (a.type != b.type && Nullable.GetUnderlyingType(a.type) == b.type) &&
                                    (fields?.Length == 0 || (isExclude && !fields!.Contains(a.name)) || (!isExclude && fields!.Contains(a.name)))
                                    && !CEF.RegisteredPropertyNameTreatReadOnly.Contains(a.name)
                                let v = src.FastGetValue(a.name)
                                where v != null
                                select new { a.name, value = v }))
            {
                dest.FastSetValue(fi.name, fi.value);
            }
        }

        /// <summary>
        /// Similar to CopySharedTo but is less restrictive about how empty strings are compared to null, causing less dirty state changes.
        /// </summary>
        /// <param name="src"></param>
        /// <param name="dest"></param>
        /// <param name="isExclude"></param>
        /// <param name="fields"></param>
        public static void CopySharedToNullifyEmptyStrings(this object src, object dest, bool isExclude, params string[] fields)
        {
            if (src == null)
                throw new CEFInvalidStateException(InvalidStateType.ArgumentNull, nameof(src));

            if (dest == null)
                throw new CEFInvalidStateException(InvalidStateType.ArgumentNull, nameof(dest));

            foreach (var info in (from a in src.FastGetAllProperties(true, true)
                                  join b in dest.FastGetAllProperties(true, true) on a.name equals b.name
                                  where (a.type == b.type || Nullable.GetUnderlyingType(b.type) == a.type) &&
                                    (fields?.Length == 0 || (isExclude && !fields!.Contains(a.name)) || (!isExclude && fields!.Contains(a.name)))
                                    && !CEF.RegisteredPropertyNameTreatReadOnly.Contains(a.name)
                                  select new { a.name, a.type }))
            {
                var v = src.FastGetValue(info.name);

                if (info.type == typeof(string) && string.IsNullOrEmpty(v?.ToString()))
                {
                    var dv = dest.FastGetValue(info.name);

                    if (string.IsNullOrEmpty(dv?.ToString()))
                    {
                        continue;
                    }

                    v = null;
                }

                dest.FastSetValue(info.name, v);
            }

            foreach (var fi in (from a in src.FastGetAllProperties(true, true)
                                join b in dest.FastGetAllProperties(true, true) on a.name equals b.name
                                where (a.type != b.type && Nullable.GetUnderlyingType(a.type) == b.type) &&
                                    (fields?.Length == 0 || (isExclude && !fields!.Contains(a.name)) || (!isExclude && fields!.Contains(a.name)))
                                    && !CEF.RegisteredPropertyNameTreatReadOnly.Contains(a.name)
                                let v = src.FastGetValue(a.name)
                                where v != null
                                select new { a.name, value = v }))
            {
                dest.FastSetValue(fi.name, fi.value);
            }
        }

        /// <summary>
        /// Returns a list of property names where shared property values differ between two instances of arbitrary types.
        /// </summary>
        /// <param name="src"></param>
        /// <param name="dest"></param>
        /// <param name="isExclude"></param>
        /// <param name="fields"></param>
        /// <returns></returns>
        public static IEnumerable<string> FindDifferentSharedValues(this object src, object dest)
        {
            if (src == null)
                throw new CEFInvalidStateException(InvalidStateType.ArgumentNull, nameof(src));

            if (dest == null)
                throw new CEFInvalidStateException(InvalidStateType.ArgumentNull, nameof(dest));

            foreach (var name in (from a in src.FastGetAllProperties(true, true)
                                  join b in dest.FastGetAllProperties(true, true) on a.name equals b.name
                                  where (a.type == b.type || Nullable.GetUnderlyingType(b.type) == a.type)
                                    && !CEF.RegisteredPropertyNameTreatReadOnly.Contains(a.name)
                                  select a.name))
            {
                if (!dest.FastGetValue(name).IsSame(src.FastGetValue(name)))
                {
                    yield return name;
                }
            }

            foreach (var fi in (from a in src.FastGetAllProperties(true, true)
                                join b in dest.FastGetAllProperties(true, true) on a.name equals b.name
                                where a.type != b.type && Nullable.GetUnderlyingType(a.type) == b.type
                                    && !CEF.RegisteredPropertyNameTreatReadOnly.Contains(a.name)
                                let v = src.FastGetValue(a.name)
                                where v != null
                                select new { a.name, value = v }))
            {
                if (!dest.FastGetValue(fi.name).IsSame(fi.value))
                {
                    yield return fi.name;
                }
            }
        }

        /// <summary>
        /// Copies all shared properties from one instance to another instance of any arbitrary type - except does not overwrite values that are not null/default in target.
        /// </summary>
        /// <param name="src"></param>
        /// <param name="dest"></param>
        public static void CopySharedToOnlyOverwriteDefault(this object src, object dest)
        {
            if (src == null)
                throw new CEFInvalidStateException(InvalidStateType.ArgumentNull, nameof(src));

            if (dest == null)
                throw new CEFInvalidStateException(InvalidStateType.ArgumentNull, nameof(dest));

            foreach (var name in (from a in src.FastGetAllProperties(true, true)
                                  join b in dest.FastGetAllProperties(true, true) on a.name equals b.name
                                  where (a.type == b.type || Nullable.GetUnderlyingType(b.type) == a.type)
                                    && !CEF.RegisteredPropertyNameTreatReadOnly.Contains(a.name)
                                  select a.name))
            {
                var sv = src.FastGetValue(name);
                var dv = dest.FastGetValue(name);

                if (sv.IsSame(dv))
                {
                    continue;
                }

                if (dv != null)
                {
                    var di = dv.GetType().FastCreateNoParm();

                    if (di == dv)
                    {
                        continue;
                    }
                }

                dest.FastSetValue(name, sv);
            }
        }

        /// <summary>
        /// Copies all properties from one instance to another instance of the same type.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="src"></param>
        /// <param name="dest"></param>
        public static void CopyToObject<T>(this T src, T dest) where T : class, new()
        {
            if (src == null)
                throw new CEFInvalidStateException(InvalidStateType.ArgumentNull, nameof(src));

            if (dest == null)
                throw new CEFInvalidStateException(InvalidStateType.ArgumentNull, nameof(dest));

            foreach (var (name, _, _, _) in typeof(T).FastGetAllProperties(true, true))
            {
                if (!CEF.RegisteredPropertyNameTreatReadOnly.Contains(name))
                {
                    dest.FastSetValue(name, src.FastGetValue(name));
                }
            }
        }

        /// <summary>
        /// Creates a DataTable with the same structure as the source EntitySet collection. Columns are determined based on properties (CLR and extended). Changes to the DataTable do NOT reflect back to the EntitySet instance.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="source"></param>
        /// <returns></returns>
        public static DataTable DeepCopyDataTable<T>(this EntitySet<T> source) where T : class, new()
        {
            List<(string name, Type type, bool nullable)> columns = [];

            if (source.Any())
            {
                // Use first row's properties (could include extended props)
                var iw = source.First().AsInfraWrapped();

                if (iw != null)
                {
                    columns.AddRange(from a in iw.GetAllValues(false, true)
                                     let pt = (from b in iw.GetAllPreferredTypes(false, true) where b.Key == a.Key select b.Value).FirstOrDefault() ?? (a.Value == null ? typeof(object) : a.Value.GetType())
                                     select (a.Key, pt, pt.IsGenericType && pt.GetGenericTypeDefinition() == typeof(Nullable<>)));
                }
            }
            else
            {
                // Use type's properties only, nothing else to go on
                columns.AddRange(from a in typeof(T).GetProperties() select (a.Name, a.PropertyType, a.PropertyType.IsGenericType && a.PropertyType.GetGenericTypeDefinition() == typeof(Nullable<>)));
            }

            DataTable dt = new();

            foreach (var (name, type, nullable) in columns)
            {
                if (nullable)
                {
                    dt.Columns.Add(name, Nullable.GetUnderlyingType(type) ?? throw new InvalidOperationException("Failed to get underlying type."));
                }
                else
                {
                    dt.Columns.Add(name, type);
                }
            }

            foreach (var i in source)
            {
                var iw = i.AsInfraWrapped();

                if (iw != null)
                {
                    var rs = iw.GetRowState();

                    if (rs != ObjectState.Deleted && rs != ObjectState.Unlinked)
                    {
                        var dr = dt.NewRow();

                        if (rs == ObjectState.Modified || rs == ObjectState.ModifiedPriority)
                        {
                            dr.AcceptChanges();
                        }

                        foreach (var v in iw.GetAllValues(true))
                        {
                            if (v.Value != null && dt.Columns.Contains(v.Key))
                            {
                                dr[v.Key] = v.Value;
                            }
                        }

                        dt.Rows.Add(dr);

                        if (rs == ObjectState.Unchanged)
                        {
                            dr.AcceptChanges();
                        }
                    }
                }
            }

            return dt;
        }

        /// <summary>
        /// Creates a DataView with the same structure as the source EntitySet collection. Columns are determined based on properties (CLR and extended). Changes to the DataView do NOT reflect back to the EntitySet instance.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="source"></param>
        /// <param name="sort"></param>
        /// <param name="filter"></param>
        /// <returns></returns>
        public static DataView DeepCopyDataView<T>(this EntitySet<T> source, string? sort = null, string? filter = null) where T : class, new()
        {
            var dv = DeepCopyDataTable(source).DefaultView;
            dv.Sort = sort;
            dv.RowFilter = filter;
            return dv;
        }

        /// <summary>
        /// Offers a way to perform the "forget" portion of a "fire and forget" task. This is wrapped with an empty catch to avoid problems which might otherwise be possible if task throws an exception.
        /// </summary>
        /// <param name="task"></param>
        public static async void ForgetTask(this Task task)
        {
            try
            {
                await task;
            }
            catch
            {
            }
        }

        /// <summary>
        /// Offers a way to execute a section of code with a maximum execution time for that code. If it does not complete in time, either exception thrown or false is returned; otherwise, task has been awaited to completion and true is returned. Action should accept cancellation token, although optional for it to use it. Also supports retries - wait time supplied is total duration allowed including retries. If task is not completed in time, cancellation token signals to workload to cancel; it will be left as "forgotten" and may continue to run (if it does not handle cancellation, for example).
        /// </summary>
        /// <param name="work">The delegate referencing code to run. Should accept a cancellation token and ideally perform cooperative cancellation but this is not required. This overload allows the parameter to be async method.</param>
        /// <param name="maxWaitMs">The maximum time to wait for completion of the code, including any retries.</param>
        /// <param name="continueSilent">When true, method returns without throwing timeout exception; use return value to identify success or timeout.</param>
        /// <param name="checkCancel">An optional delegate that can short-circuit waiting for completion (return true to force early cancel).</param>
        /// <param name="retries">The number of retries that are allowed. Zero implies no retries allowed (default).</param>
        /// <param name="retryHandler">Optional delegate can preview exception and return false to force exception to short-circuit retries.</param>
        /// <param name="pollMs">Delay time between checks for completion. A large value here implies longer execution time since completion may have happened while still waiting.</param>
        /// <param name="retryBackoffMs">A factor multiplied by square of try count to determine how long will wait between possible retries.</param>
        /// <returns></returns>
        public static async Task<bool> ExecuteWithMaxWaitAsync(this Func<CancellationToken, Task> work, int maxWaitMs, bool continueSilent = true, Func<bool>? checkCancel = null, int retries = 0, Func<Exception, bool>? retryHandler = null, int pollMs = 2, int retryBackoffMs = 100)
        {
            return await InternalExecuteWithMaxWaitAsync(work.Invoke, maxWaitMs, continueSilent, checkCancel, retries, retryHandler, pollMs, retryBackoffMs);
        }

        private static async Task<bool> InternalExecuteWithMaxWaitAsync(Func<CancellationToken, Task> getwork, int maxWaitMs, bool continueSilent, Func<bool>? checkCancel, int retries, Func<Exception, bool>? retryHandler, int pollMs, int retryBackoffMs)
        {
            DateTime start = DateTime.Now;
            CancellationTokenSource cts = new();
            int pass = 0;

            while (retries >= 0)
            {
                try
                {
                    var t = getwork.Invoke(cts.Token);

                    while (!t.IsCompleted)
                    {
                        if (DateTime.Now.Subtract(start).TotalMilliseconds > maxWaitMs || (checkCancel != null && checkCancel.Invoke()))
                        {
                            cts.Cancel();
                            t.ForgetTask();

                            if (continueSilent)
                            {
                                return false;
                            }

                            throw new TimeoutException($"Waited for {DateTime.Now.Subtract(start).TotalMilliseconds:0} milliseconds; task did not complete in time.");
                        }

                        await Task.Delay(pollMs);
                    }

                    await t;
                    break;
                }
                catch (Exception ex)
                {
                    --retries;

                    if (retries < 0)
                    {
                        throw;
                    }

                    if (retryHandler != null && !retryHandler.Invoke(ex))
                    {
                        throw;
                    }

                    ++pass;
                    await Task.Delay(pass * pass * retryBackoffMs);
                }
            }

            cts.Dispose();
            return true;
        }

        /// <summary>
        /// Offers a way to execute a section of code with a maximum execution time for that code. If it does not complete in time, either exception thrown or false is returned; otherwise, task has been awaited to completion and true is returned. Action should accept cancellation token, although optional for it to use it. Also supports retries - wait time supplied is total duration allowed including retries. If task is not completed in time, cancellation token signals to workload to cancel; it will be left as "forgotten" and may continue to run (if it does not handle cancellation, for example).
        /// </summary>
        /// <param name="work">The delegate referencing code to run. Should accept a cancellation token and ideally perform cooperative cancellation but this is not required.</param>
        /// <param name="maxWaitMs">The maximum time to wait for completion of the code, including any retries.</param>
        /// <param name="continueSilent">When true, method returns without throwing timeout exception; use return value to identify success or timeout.</param>
        /// <param name="checkCancel">An optional delegate that can short-circuit waiting for completion (return true to force early cancel).</param>
        /// <param name="retries">The number of retries that are allowed. Zero implies no retries allowed (default).</param>
        /// <param name="retryHandler">Optional delegate can preview exception and return false to force exception to short-circuit retries.</param>
        /// <param name="pollMs">Delay time between checks for completion. A large value here implies longer execution time since completion may have happened while still waiting.</param>
        /// <param name="retryBackoffMs">A factor multiplied by square of try count to determine how long will wait between possible retries.</param>
        /// <returns></returns>
        public static async Task<bool> ExecuteWithMaxWaitAsync(this Action<CancellationToken> work, int maxWaitMs, bool continueSilent = true, Func<bool>? checkCancel = null, int retries = 0, Func<Exception, bool>? retryHandler = null, int pollMs = 2, int retryBackoffMs = 100)
        {
            return await InternalExecuteWithMaxWaitAsync((ct) =>
            {
                return Task.Run(() => work.Invoke(ct), CancellationToken.None);
            }, maxWaitMs, continueSilent, checkCancel, retries, retryHandler, pollMs, retryBackoffMs);
        }

        /// <summary>
        /// Traverses object graph looking for cases where collections can be replaced with EntitySet.
        /// </summary>
        /// <param name="o">Starting object for traversal.</param>
        /// <param name="isNew">If true, assumes objects being added represent "new" rows to insert in the database.</param>
        internal static void CreateLists(this object o, bool isNew = false)
        {
            if (o == null)
                return;

            var ss = CEF.CurrentServiceScope;

            foreach (var (name, type, _, _) in o.FastGetAllProperties(true, null))
            {
                if (o.FastPropertyReadable(name))
                {
                    var val = o.FastGetValue(name);

                    if (WrappingHelper.IsWrappableListType(type, val))
                    {
                        if (val is not ICEFList)
                        {
                            var wrappedCol = WrappingHelper.CreateWrappingList(CEF.CurrentServiceScope, type, o, name);
                            o.FastSetValue(name, wrappedCol);

                            if (wrappedCol != null)
                            {
                                if (val != null)
                                {

                                    // Based on the above type checks, we know this thing supports IEnumerable
                                    var sValEnum = ((System.Collections.IEnumerable)val).GetEnumerator();

                                    while (sValEnum.MoveNext())
                                    {
                                        var cur = sValEnum.Current;
                                        var rim = ss.ResolvedRetrievalIdentityMode(cur);

                                        // Need to use non-generic method for this!
                                        var wi = ss.InternalCreateAddBase(cur, isNew, null, null, null, null, true, false, rim);

                                        if (wi != null)
                                        {
                                            wrappedCol.AddWrappedItem(wi);
                                        }
                                    }
                                }

                                ((ISupportInitializeNotification)wrappedCol).EndInit();
                            }
                        }
                    }
                }
            }
        }
#endif

    }

    /// <summary>
    /// Enables efficient Linq to Objects queries on indexed sets.
    /// </summary>
    public static class IndexedSetExtensions
    {
        public static IndexedSnapshot<T> AsIndexSnapshot<T>(this IEnumerable<T> source) where T : class, new()
        {
            return new IndexedSnapshot<T>(source, null);
        }

        public static IEnumerable<TSource> Where<TSource>(this IndexedSet<TSource> source, Expression<Func<TSource, bool>> predicate) where TSource : class, new()
        {
            if (source == null)
            {
#if DEBUG
                if (System.Diagnostics.Debugger.IsAttached)
                {
                    System.Diagnostics.Debugger.Break();
                }
#else
                throw new ArgumentNullException(nameof(source));
#endif
            }

            if (source != null && source.IsLiveTracked)
            {
                using (new ReaderLock(source.LockInfo))
                {
                    var sv = source.View;

                    if (sv != null && (sv.AutoInferIndexes).GetValueOrDefault(Globals.AutoInferIndexes) || sv!.IndexCount > 0)
                    {
                        return sv.InternalWhere(predicate);
                    }
                }
            }

            return Enumerable.Where(source ?? [], predicate.Compile());
        }

        public static IEnumerable<TResult> Join<TOuter, TInner, TKey, TResult>(this IndexedSet<TOuter> outer, IndexedSet<TInner> inner, Expression<Func<TOuter, TKey>> outerKeySelector, Expression<Func<TInner, TKey>> innerKeySelector, Expression<Func<TOuter, TInner, TResult>> resultSelector) where TOuter : class, new() where TInner : class, new()
        {
            if (outer != null && inner != null && outer.IsLiveTracked && inner.IsLiveTracked)
            {
                if (outer.SupportsJoinRules.GetValueOrDefault(Globals.IndexedSetsSupportJoins) && inner.SupportsJoinRules.GetValueOrDefault(Globals.IndexedSetsSupportJoins))
                {
                    using (new ReaderLock(inner.LockInfo))
                    {
                        using (new ReaderLock(outer.LockInfo))
                        {
                            var osv = outer.View;
                            var isv = inner.View;

                            if (osv != null && isv != null)
                            {
                                if (((osv.AutoInferIndexes).GetValueOrDefault(Globals.AutoInferIndexes) || osv.IndexCount > 0) && ((isv.AutoInferIndexes).GetValueOrDefault(Globals.AutoInferIndexes) || isv.IndexCount > 0))
                                {
                                    // Future: address possible deadlocks here... currently should timeout but would not get further detail
                                    return osv.InternalJoin(isv, outerKeySelector, innerKeySelector, resultSelector);
                                }
                            }
                        }
                    }
                }
            }

            return Enumerable.Join(outer ?? [], inner ?? [], outerKeySelector.Compile(), innerKeySelector.Compile(), resultSelector.Compile());
        }

        /// <summary>
        /// Replaces LINQ Where for indexed sets.
        /// </summary>
        /// <typeparam name="TSource"></typeparam>
        /// <param name="source"></param>
        /// <param name="predicate"></param>
        /// <returns></returns>
        public static IEnumerable<TSource> Where<TSource>(this IndexedSnapshot<TSource> source, Expression<Func<TSource, bool>> predicate) where TSource : class, new()
        {
            return source.InternalWhere(predicate);
        }

        /// <summary>
        /// Replaces LINQ Join for indexed sets.
        /// </summary>
        /// <typeparam name="TOuter"></typeparam>
        /// <typeparam name="TInner"></typeparam>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TResult"></typeparam>
        /// <param name="outer"></param>
        /// <param name="inner"></param>
        /// <param name="outerKeySelector"></param>
        /// <param name="innerKeySelector"></param>
        /// <param name="resultSelector"></param>
        /// <returns></returns>
        public static IEnumerable<TResult> Join<TOuter, TInner, TKey, TResult>(this IndexedSnapshot<TOuter> outer, IndexedSnapshot<TInner> inner, Expression<Func<TOuter, TKey>> outerKeySelector, Expression<Func<TInner, TKey>> innerKeySelector, Expression<Func<TOuter, TInner, TResult>> resultSelector) where TOuter : class, new() where TInner : class, new()
        {
            return outer.InternalJoin(inner, outerKeySelector, innerKeySelector, resultSelector);
        }
    }

}
