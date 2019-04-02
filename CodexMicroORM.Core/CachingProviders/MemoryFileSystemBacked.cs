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
1/2018     0.2.3   Primary implementation (Joel Champagne)
***********************************************************************/
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Security.Cryptography;

using CodexMicroORM.Core;
using CodexMicroORM.Core.Collections;
using CodexMicroORM.Core.Services;
using CodexMicroORM.Core.Helper;
using Newtonsoft.Json;
using System.Runtime.Serialization.Formatters.Binary;

namespace CodexMicroORM.Providers
{
    /// <summary>
    /// The general principle here is we store everything in memory, committing to disk in the background only. (After that, we evict based on read quantity asc, last read asc till go below the limit.)
    /// The idea is the file system cache can be larger than the memory cache - no size restrictions by default, other than expiry dates, set by object (type, normally).
    /// Queries for data would first check the cache - if in memory, return a copy from there, otherwise check disk and rebuild from there if found - otherwise it's a cache miss and let the DB access happen and cache the results.
    /// There are two main types of cache entry: by query and by identity. By query can cache collections of rows based on some source query (retreve by query). By identity caches one object at a time based on its key (retrieve by key).
    /// Query-based caching can optionally translate each cached row into "by identity" entries, improving performance.
    /// </summary>
    public class MemoryFileSystemBacked : ICEFCachingHost
    {
        /// <summary>
        /// Specific to this caching implementation, we can describe placement of objects - either in memory only or in file system with directory splitting as an option.
        /// </summary>
        public enum CacheStorageStrategy
        {
            OnlyMemory = 1,
            SingleDirectory = 2,
            DirPerType = 3,
            DirPerDay = 4
        }

        /// <summary>
        /// Cache entries contain some persistent properties, some that are only in-memory.
        /// </summary>
        private sealed class MFSEntry : ICEFIndexedListItem
        {
            public object GetValue(string propName, bool unwrap)
            {
                return this.FastGetValue(propName);
            }

            public string FileName { get; set; }

            public string ObjectTypeName { get; set; }

            public string ByQuerySHA { get; set; }

            public bool QueryForAll { get; set; }

            public string ByIdentityComposite { get; set; }

            public DateTime ExpiryDate { get; set; }

            // Everything after this point should NOT be stored in persisted index...

            // If this is null, need to fetch from disk (can be nullified when evicted due to memory pressure)
            // Should have 1 of either Properties or Rows, not both...
            public IDictionary<string, object> Properties { get; set; }

            public IEnumerable<IDictionary<string, object>> Rows { get; set; }

            public IEnumerable<object> SourceList { get; set; }

            public object ObjSync { get; } = new object();

            public DateTime LastRead { get; set; }

            public long ReadCount { get; set; }

            public bool Active { get; set; }

            public bool Persisted { get; set; }

            public long Sequence { get; set; }
        }

        #region "Internal state"

        private ConcurrentDictionary<Type, long> _totalReadCounter = new ConcurrentDictionary<Type, long>();

        private string _rootDir;

        private ConcurrentIndexedList<MFSEntry> _index = new ConcurrentIndexedList<MFSEntry>(nameof(MFSEntry.ByIdentityComposite), nameof(MFSEntry.ByQuerySHA), nameof(MFSEntry.ObjectTypeName), nameof(MFSEntry.FileName));

        private System.Timers.Timer _monitor;

        private double? _lastMonitorTimerDuration;

        private long _working = 0;

        private long _stopping = 0;

        private object _indexLock = new object();

        [ThreadStatic]
        private BinaryFormatter _formatter = new BinaryFormatter();
        
        #endregion

        public MemoryFileSystemBacked(string rootDirUnderTemp = null, CacheStorageStrategy? storage = null, int? startMonitorInterval = null)
        {
            RootDirectory = Path.Combine(Path.GetTempPath(), rootDirUnderTemp ?? "CEF_ObjCache");

            if (startMonitorInterval.HasValue)
            {
                MonitorTimerMillisecondInterval = startMonitorInterval.Value;
            }

            if (storage.HasValue)
            {
                DefaultStorageStrategy = storage.Value;
            }

            Start();
        }

        public string RootDirectory
        {
            get
            {
                return _rootDir;
            }
            set
            {
                _rootDir = value;

                if (!Directory.Exists(_rootDir))
                {
                    Directory.CreateDirectory(_rootDir);
                }
            }
        }

        public int MonitorTimerMillisecondInterval
        {
            get;
            set;
        } = 500;

        public CacheStorageStrategy DefaultStorageStrategy
        {
            get;
            set;
        } = CacheStorageStrategy.OnlyMemory;

        public int DefaultCacheIntervalSeconds
        {
            get;
            set;
        } = Globals.DefaultGlobalCacheIntervalSeconds;

        public bool MonitorTimerUseDynamicInterval
        {
            get;
            set;
        } = true;

        /// <summary>
        /// After this limit, background monitor starts to discard items from memory. Default is a crude estimator.
        /// </summary>
        public int MaximumItemCount
        {
            get;
            set;
        } = 20000 * Environment.ProcessorCount;

        #region "Static methods"

        public static void FlushAll(string rootDir)
        {
            InternalFlush(Path.Combine(Path.GetTempPath(), rootDir));
            Directory.CreateDirectory(rootDir);
        }

        private static void InternalFlush(string dir)
        {
            if (Directory.Exists(dir))
            {
                foreach (var d in Directory.GetDirectories(dir))
                {
                    InternalFlush(d);
                }
                foreach (var f in Directory.GetFiles(dir))
                {
                    File.Delete(f);
                }
                Directory.Delete(dir);
            }
        }

        #endregion

        #region "Public operations"

        public bool IsCacheBusy()
        {
            return Interlocked.Read(ref _working) > 0;
        }

        /// <summary>
        /// Cache users who need to perform actions that span multiple statements should indicate they're interacting with the cache. Prevents halting the monitor and allowing shutdown until cache users are done their work.
        /// </summary>
        public void DoingWork()
        {
            Interlocked.Increment(ref _working);
        }

        public void DoneWork()
        {
            Interlocked.Decrement(ref _working);
        }

        /// <summary>
        /// Invalidates zero, one or many query-based cache entries based on either a type or for all. Typically called when saving data where updates may invalidate cache contents.
        /// </summary>
        /// <param name="t"></param>
        /// <param name="typeSpecific"></param>
        public void InvalidateForByQuery(Type t, bool typeSpecific)
        {
            if (t == null)
                throw new ArgumentNullException("t");

            if (typeSpecific)
            {
                foreach (var c in _index.GetAllByName(nameof(MFSEntry.ObjectTypeName), t.Name))
                {
                    lock (c.ObjSync)
                    {
                        if (!string.IsNullOrEmpty(c.ByQuerySHA))
                        {
                            c.Active = false;
                            c.Rows = null;
                        }
                    }
                }
            }
            else
            {
                foreach (var c in _index.GetAllByName(nameof(MFSEntry.ByIdentityComposite), ConcurrentIndexedList<MFSEntry>.NullValue))
                {
                    lock (c.ObjSync)
                    {
                        if (string.Compare(t.Name, c.ObjectTypeName) == 0 || !c.QueryForAll)
                        {
                            c.Active = false;
                            c.Rows = null;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Invalidates zero or one identity-based cache entry based on the identity of a specific object. Typically called when saving data where updates may invalidate cache contents.
        /// </summary>
        /// <param name="baseType"></param>
        /// <param name="props"></param>
        public void InvalidateIdentityEntry(Type baseType, IDictionary<string, object> props)
        {
            StringBuilder sb = new StringBuilder(128);
            sb.Append(baseType.Name);

            var key = (from a in KeyService.ResolveKeyDefinitionForType(baseType) select props[a]).ToArray();

            if (!key.Any())
            {
                throw new CEFInvalidOperationException("No primary key for this object makes it impossible to cache.");
            }

            foreach (var k in key)
            {
                sb.Append(k);
            }

            foreach (var c in _index.GetAllByName(nameof(MFSEntry.ByIdentityComposite), sb.ToString()))
            {
                lock (c.ObjSync)
                {
                    c.Active = false;
                    c.Properties = null;
                }
            }
        }

        /// <summary>
        /// Updates the identity-cached object based on its current property values.
        /// </summary>
        /// <param name="baseType"></param>
        /// <param name="props"></param>
        /// <param name="key"></param>
        /// <param name="expirySeconds"></param>
        public void UpdateByIdentity(Type baseType, IDictionary<string, object> props, object[] key = null, int? expirySeconds = null)
        {
            StringBuilder sb = new StringBuilder(128);
            sb.Append(baseType.Name);

            if (key == null)
            {
                key = (from a in KeyService.ResolveKeyDefinitionForType(baseType) select props[a]).ToArray();
            }

            if (!key.Any())
            {
                throw new CEFInvalidOperationException("No primary key for this object makes it impossible to cache.");
            }

            foreach (var k in key)
            {
                sb.Append(k);
            }

            var c = _index.GetFirstByName(nameof(MFSEntry.ByIdentityComposite), sb.ToString());

            if (c != null)
            {
                if (!expirySeconds.HasValue)
                {
                    expirySeconds = CEF.CurrentServiceScope.ResolvedCacheDurationForType(baseType);
                }

                var newExpDate = DateTime.Now.AddSeconds(expirySeconds.GetValueOrDefault(DefaultCacheIntervalSeconds));

                lock (c.ObjSync)
                {
                    c.ExpiryDate = newExpDate;
                    c.Properties = props;
                    c.Persisted = false;
                    c.Active = true;
                }
            }
        }

        /// <summary>
        /// Returns one or more "rows" of data that were previously cached based on a query (text and parameters). If not in cache, returns null.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="text"></param>
        /// <param name="parms"></param>
        /// <returns></returns>
        public IEnumerable<T> GetByQuery<T>(string text, object[] parms) where T : class, new()
        {
            StringBuilder sb = new StringBuilder(128);
            sb.Append(typeof(T).Name);
            sb.Append(text.ToUpperInvariant());

            if (parms != null)
            {
                foreach (var k in parms)
                {
                    sb.Append(k);
                }
            }

            string hash;

            using (SHA256Managed hasher = new SHA256Managed())
            {
                hash = Convert.ToBase64String(hasher.ComputeHash(Encoding.ASCII.GetBytes(sb.ToString())));
            }

            var c = _index.GetFirstByName(nameof(MFSEntry.ByQuerySHA), hash);

            if (!IsValid(c))
            {
                return null;
            }

            return GetRows<T>(c);
        }

        /// <summary>
        /// Returns a single "row" of data that was previously cached based on identity (key). If not in cache, returns null.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key"></param>
        /// <returns></returns>
        public T GetByIdentity<T>(object[] key) where T : class, new()
        {
            StringBuilder sb = new StringBuilder(128);
            sb.Append(typeof(T).Name);

            foreach (var k in key)
            {
                sb.Append(k);
            }

            var c = _index.GetFirstByName(nameof(MFSEntry.ByIdentityComposite), sb.ToString());

            if (!IsValid(c))
            {
                return null;
            }

            return GetSingle<T>(c);
        }

        public void AddByQuery<T>(IEnumerable<T> list, string text, object[] parms = null, int? expirySeconds = null) where T : class, new()
        {
            var ss = CEF.CurrentServiceScope;
            var mode = ss.ResolvedCacheBehaviorForType(typeof(T));

            if ((mode & CacheBehavior.QueryBased) == 0 && (mode & CacheBehavior.ConvertQueryToIdentity) != 0 && ((mode & CacheBehavior.ForAllDoesntConvertToIdentity) == 0 || string.Compare(text, "All", true) != 0))
            {
                // All we can do is for all list items, add to identity cache
                Action act2 = () =>
                {
                    try
                    {
                        Interlocked.Increment(ref _working);

                        Parallel.ForEach(list, (i) =>
                        {
                            using (CEF.UseServiceScope(ss))
                            {
                                AddByIdentity<T>(i, expirySeconds: expirySeconds);
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        CEFDebug.WriteInfo($"Exception in cache serializer: {ex.Message}");
                    }
                    finally
                    {
                        Interlocked.Decrement(ref _working);
                    }
                };

                if (Globals.AsyncCacheUpdates)
                {
                    Task.Factory.StartNew(act2);
                }
                else
                {
                    act2.Invoke();
                }

                return;
            }

            if ((mode & CacheBehavior.OnlyForAllQuery) != 0 && string.Compare(text, "All", true) != 0)
            {
                return;
            }

            StringBuilder sb = new StringBuilder(128);
            sb.Append(typeof(T).Name);
            sb.Append(text.ToUpperInvariant());

            if (parms != null)
            {
                foreach (var k in parms)
                {
                    sb.Append(k);
                }
            }

            string hash;

            using (SHA256Managed hasher = new SHA256Managed())
            {
                hash = Convert.ToBase64String(hasher.ComputeHash(Encoding.ASCII.GetBytes(sb.ToString())));
            }

            var c = _index.GetFirstByName(nameof(MFSEntry.ByQuerySHA), hash);

            if (!expirySeconds.HasValue)
            {
                expirySeconds = CEF.CurrentServiceScope.ResolvedCacheDurationForType(typeof(T));
            }

            var newExpDate = DateTime.Now.AddSeconds(expirySeconds.GetValueOrDefault(DefaultCacheIntervalSeconds));

            if (c == null)
            {
                c = new MFSEntry();

                if (list.Any())
                {
                    c.ObjectTypeName = list.First().GetBaseType().Name;
                }
                else
                {
                    c.ObjectTypeName = typeof(T).Name;
                }

                c.ByQuerySHA = hash;
                c.FileName = BuildNewFileName(typeof(T));
                c.QueryForAll = string.Compare(text, "All", true) == 0;
                _index.Add(c);
            }

            long current;

            lock (c.ObjSync)
            {
                current = ++c.Sequence;
                c.ExpiryDate = newExpDate;
                c.SourceList = list;
                c.Active = true;
            }

            Action act = () =>
            {
                try
                {
                    Interlocked.Increment(ref _working);

                    using (CEF.UseServiceScope(ss))
                    {
                        // Process all items in parallel, building a list we'll turn into json but also potentially caching "by identity" per row
                        ConcurrentBag<IDictionary<string, object>> rows = new ConcurrentBag<IDictionary<string, object>>();

                        var aiw = list.AllAsInfraWrapped().ToArray();

                        Parallel.ForEach(aiw, (iw) =>
                        {
                            using (CEF.UseServiceScope(ss))
                            {
                                rows.Add(iw.GetAllValues(true, true));

                                if ((mode & CacheBehavior.ConvertQueryToIdentity) != 0 && ((mode & CacheBehavior.ForAllDoesntConvertToIdentity) == 0 || string.Compare(text, "All", true) != 0))
                                {
                                    var uw = iw.AsUnwrapped() as T;

                                    if (uw != null)
                                    {
                                        AddByIdentity<T>(uw, expirySeconds: expirySeconds);
                                    }
                                }
                            }
                        });

                        lock (c.ObjSync)
                        {
                            if (c.Sequence == current)
                            {
                                c.Rows = rows;
                                c.SourceList = null;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    CEFDebug.WriteInfo($"Exception in cache serializer: {ex.Message}");

                    // Invalidate the entry is probably the safest
                    c.Active = false;
                    c.Properties = null;
                    c.Rows = null;
                }
                finally
                {
                    Interlocked.Decrement(ref _working);
                }
            };

            if (Globals.AsyncCacheUpdates)
            {
                Task.Factory.StartNew(act);
            }
            else
            {
                act.Invoke();
            }
        }

        public void AddByIdentity<T>(T o, object[] key = null, int? expirySeconds = null) where T : class, new()
        {
            // We can only cache tracked objects!
            var iw = o?.AsInfraWrapped();

            if (iw == null)
            {
                return;
            }

            StringBuilder sb = new StringBuilder(128);
            sb.Append(o.GetBaseType().Name);

            if (key == null)
            {
                key = (from a in CEF.CurrentKeyService()?.GetKeyValues(o) select a.value).ToArray();
            }

            if (!key.Any())
            {
                throw new CEFInvalidOperationException("No primary key for this object makes it impossible to cache.");
            }

            foreach (var k in key)
            {
                sb.Append(k);
            }

            var c = _index.GetFirstByName(nameof(MFSEntry.ByIdentityComposite), sb.ToString());

            if (!expirySeconds.HasValue)
            {
                expirySeconds = CEF.CurrentServiceScope.ResolvedCacheDurationForType(typeof(T));
            }

            var newExpDate = DateTime.Now.AddSeconds(expirySeconds.GetValueOrDefault(DefaultCacheIntervalSeconds));

            if (c == null)
            {
                c = new MFSEntry();
                c.ObjectTypeName = o.GetBaseType().Name;
                c.ByIdentityComposite = sb.ToString();
                c.FileName = BuildNewFileName(o.GetBaseType());
                _index.Add(c);
            }

            long current;

            lock (c.ObjSync)
            {
                current = ++c.Sequence;
                c.ExpiryDate = newExpDate;
                c.Properties = iw.GetAllValues(true, true);
                c.Persisted = false;
                c.Active = true;
            }
        }

        public int GetActiveCount()
        {
            return (from a in _index where a.Active select a).Count();
        }

        /// <summary>
        /// Shuts down monitor and applies clean-up rules - typically done for you when connection and/or service scopes end.
        /// </summary>
        public void Shutdown()
        {
            _monitor.Elapsed -= _monitor_Elapsed;

            // Request stop - if monitor is running, it should honor this and stop as early as it can (safely)
            Interlocked.Increment(ref _stopping);

            // Wait until no longer "working"
            while (Interlocked.Read(ref _working) > 0)
            {
                Thread.Sleep(10);
            }

            // Let's be sure everything that we've promised to save is actually saved!
            Cleanup(false);
        }

        /// <summary>
        /// On start, we use the persisted index to determine what's still valid in the cache and reconstitute it in memory. We also start our background monitor (on a timer).
        /// </summary>
        /// <returns></returns>
        public string Start()
        {
            RestoreIndex();

            _monitor = new System.Timers.Timer(MonitorTimerMillisecondInterval);
            _monitor.AutoReset = false;
            _monitor.Elapsed += _monitor_Elapsed;
            _monitor.Start();

            return null;
        }

        #endregion

        private bool IsValid(MFSEntry c)
        {
            if (c == null)
            {
                return false;
            }

            return (c.Active && c.ExpiryDate > DateTime.Now);
        }

        /// <summary>
        /// Returns a reconstituted object based on a cache entry.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="c"></param>
        /// <returns></returns>
        private T GetSingle<T>(MFSEntry c) where T : class, new()
        {
            IDictionary<string, object> props = null;
            string cfn = null;

            lock (c.ObjSync)
            {
                props = c.Properties;
                cfn = c.FileName;

                c.ReadCount++;
                c.LastRead = DateTime.Now;
            }

            if (props == null)
            {
                if (!string.IsNullOrEmpty(cfn))
                {
                    var fn = Path.Combine(_rootDir, cfn);

                    if (File.Exists(fn))
                    {
                        props = GetPropertiesFromFile(_formatter, fn);

                        lock (c.ObjSync)
                        {
                            c.Properties = props;
                        }
                    }
                }
            }

            if (props != null)
            {
                return CEF.CurrentServiceScope.InternalCreateAddBase(new T(), false, ObjectState.Unchanged, c.Properties, null, null, false) as T;
            }

            return null;
        }

        /// <summary>
        /// Returns one or more reconstituted "rows" based on a cache entry.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="c"></param>
        /// <returns></returns>
        private IEnumerable<T> GetRows<T>(MFSEntry c) where T : class, new()
        {
            IEnumerable<IDictionary<string, object>> rows = null;
            IEnumerable<object> list = null;
            string cfn = null;

            lock (c.ObjSync)
            {
                rows = c.Rows;
                list = c.SourceList;
                cfn = c.FileName;

                c.ReadCount++;
                c.LastRead = DateTime.Now;
            }

            if (rows == null)
            {
                // If the reason it's null is we're still waiting for it to be parsed, just do so now
                if (list != null)
                {
                    rows = GetParsedRows(list);

                    lock (c.ObjSync)
                    {
                        c.Rows = rows;
                        c.SourceList = null;
                    }
                }

                if (rows == null && !string.IsNullOrEmpty(cfn))
                {
                    var fn = Path.Combine(_rootDir, cfn);

                    if (File.Exists(fn))
                    {
                        rows = GetRowsFromFile(_formatter, fn);

                        lock (c.ObjSync)
                        {
                            c.Rows = rows;
                        }
                    }
                }
            }

            if (rows != null)
            {
                foreach (var rowdata in rows)
                {
                    yield return CEF.CurrentServiceScope.InternalCreateAddBase(new T(), false, ObjectState.Unchanged, rowdata, null, null, false) as T;
                }
            }
        }

        private IEnumerable<IDictionary<string, object>> GetParsedRows(IEnumerable<object> source)
        {
            foreach (var o in source)
            {
                yield return o.AsInfraWrapped().GetAllValues(true, true);
            }
        }

        private void SaveProperties(BinaryFormatter bf, string fn, IDictionary<string, object> r)
        {
            using (var fs = File.OpenWrite(fn))
            {
                bf.Serialize(fs, r);
            }
        }

        private IDictionary<string, object> GetPropertiesFromFile(BinaryFormatter bf, string fn)
        {
            using (var fs = File.OpenRead(fn))
            {
                return (IDictionary<string, object>)bf.Deserialize(fs);
            }
        }

        private void SaveRows(BinaryFormatter bf, string fn, IEnumerable<IDictionary<string, object>> rows)
        {
            using (var fs = File.OpenWrite(fn))
            {
                foreach (var r in rows)
                {
                    bf.Serialize(fs, r);
                }
            }
        }

        private IEnumerable<IDictionary<string, object>> GetRowsFromFile(BinaryFormatter bf, string fn)
        {
            using (var fs = File.OpenRead(fn))
            {
                while (fs.Position < fs.Length)
                {
                    yield return (IDictionary<string, object>)bf.Deserialize(fs);
                }
            }
        }

        private string BuildNewFileName(Type t)
        {
            string subdir = "";

            switch (DefaultStorageStrategy)
            {
                case CacheStorageStrategy.DirPerDay:
                    subdir = DateTime.Today.ToString("yyyyMMdd");
                    break;

                case CacheStorageStrategy.DirPerType:
                    subdir = t.Name;
                    break;

                case CacheStorageStrategy.OnlyMemory:
                    return null;
            }

            return Path.Combine(subdir, Regex.Replace(Guid.NewGuid().ToString(), @"\W", "") + ".dat");
        }

        /// <summary>
        /// For cases where persisting to file, ensures items that are eligible to be cached to disk actually are persisted to disk.
        /// </summary>
        /// <param name="honorShutdown"></param>
        private void SaveUncommitted(bool honorShutdown)
        {
            if (DefaultStorageStrategy == CacheStorageStrategy.OnlyMemory)
            {
                return;
            }

            List<MFSEntry> items = new List<MFSEntry>();

            foreach (var i in _index)
            {
                if (honorShutdown && Interlocked.Read(ref _stopping) > 0)
                {
                    return;
                }

                lock (i.ObjSync)
                {
                    if (!i.Persisted && i.Active && !string.IsNullOrEmpty(i.FileName) && (i.Properties != null || i.Rows != null) && i.ExpiryDate > DateTime.Now)
                    {
                        items.Add(i);
                    }
                }
            }

            Parallel.ForEach(items, new ParallelOptions() { MaxDegreeOfParallelism = honorShutdown ? Environment.ProcessorCount > 4 ? Environment.ProcessorCount >> 2 : 1 : -1 }, (i, pls) =>
            {
                try
                {
                    if (honorShutdown && Interlocked.Read(ref _stopping) > 0)
                    {
                        pls.Break();
                        return;
                    }

                    if (i.Properties != null)
                    {
                        SaveProperties(_formatter, Path.Combine(_rootDir, i.FileName), i.Properties);
                    }
                    else
                    {
                        if (i.Rows != null)
                        {
                            SaveRows(_formatter, Path.Combine(_rootDir, i.FileName), i.Rows);
                        }
                    }

                    lock (i.ObjSync)
                    {
                        i.Persisted = true;
                    }
                }
                catch (Exception ex)
                {
                    CEFDebug.WriteInfo($"Exception in cache save uncommitted: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Reads any existing cache index file, building the in-memory representation, without bringing in actual item data until actually requested.
        /// </summary>
        private void RestoreIndex()
        {
            if (DefaultStorageStrategy == CacheStorageStrategy.OnlyMemory)
            {
                return;
            }

            try
            {
                using (var jr = new JsonTextReader(new StreamReader(File.OpenRead(Path.Combine(_rootDir, "index.json")))))
                {
                    // Should be start array
                    jr.Read();

                    while (jr.Read() && jr.TokenType != JsonToken.EndArray)
                    {
                        // Should be start object
                        jr.Read();

                        var i = new MFSEntry();

                        i.FileName = jr.ReadAsString();
                        jr.Read();
                        i.ObjectTypeName = jr.ReadAsString();
                        jr.Read();
                        i.ByQuerySHA = jr.ReadAsString();
                        jr.Read();
                        i.QueryForAll = jr.ReadAsBoolean().GetValueOrDefault();
                        jr.Read();
                        i.ByIdentityComposite = jr.ReadAsString();
                        jr.Read();
                        i.ExpiryDate = jr.ReadAsDateTime().GetValueOrDefault();

                        jr.Read();

                        if (i.ExpiryDate > DateTime.Now)
                        {
                            i.Active = true;
                            i.Persisted = true;
                            _index.Add(i);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                CEFDebug.WriteInfo($"Exception in cache restore index: {ex.Message}");

                // Any problems, go with an empty cache
                FlushAll(RootDirectory);
                _index.Clear();
            }
        }

        /// <summary>
        /// This is our "permanent" way to recognize what's "in the cache". This becomes important in the event of an app restart, etc.
        /// </summary>
        private void SaveIndex(bool honorShutdown)
        {
            if (DefaultStorageStrategy == CacheStorageStrategy.OnlyMemory)
            {
                return;
            }

            StringBuilder sb = new StringBuilder();
            var d = DateTime.Now;

            using (var jw = new JsonTextWriter(new StringWriter(sb)))
            {
                jw.WriteStartArray();

                // There's no point in trying to save index for items where there's no backing file - these were just fleeting memory cached items
                foreach (var i in (from a in _index
                                   where a.Active && 
                                   !string.IsNullOrEmpty(a.FileName) && 
                                   a.ExpiryDate > d && 
                                   a.Persisted
                                   select new { a.FileName, a.ObjectTypeName, a.ByIdentityComposite, a.ByQuerySHA, a.QueryForAll, a.ExpiryDate }).ToList())
                {
                    if (honorShutdown && Interlocked.Read(ref _stopping) > 0)
                    {
                        return;
                    }

                    jw.WriteStartObject();

                    jw.WritePropertyName(nameof(MFSEntry.FileName));
                    jw.WriteValue(i.FileName);

                    jw.WritePropertyName(nameof(MFSEntry.ObjectTypeName));
                    jw.WriteValue(i.ObjectTypeName);

                    jw.WritePropertyName(nameof(MFSEntry.ByQuerySHA));
                    jw.WriteValue(i.ByQuerySHA);

                    jw.WritePropertyName(nameof(MFSEntry.QueryForAll));
                    jw.WriteValue(i.QueryForAll);

                    jw.WritePropertyName(nameof(MFSEntry.ByIdentityComposite));
                    jw.WriteValue(i.ByIdentityComposite);

                    jw.WritePropertyName(nameof(MFSEntry.ExpiryDate));
                    jw.WriteValue(i.ExpiryDate);

                    jw.WriteEndObject();
                }

                jw.WriteEndArray();
            }

            if (honorShutdown && Interlocked.Read(ref _stopping) > 0)
            {
                return;
            }

            lock (_indexLock)
            {
                File.WriteAllText(Path.Combine(_rootDir, "index.json"), sb.ToString());
            }
        }

        private IEnumerable<string> GetAllFiles(string dir)
        {
            foreach (var d in Directory.GetDirectories(dir))
            {
                foreach (var f in GetAllFiles(d))
                {
                    yield return f;
                }
            }
            foreach (var f in Directory.GetFiles(dir))
            {
                if (Path.GetFileName(f) != "index.json")
                {
                    yield return f.Replace(RootDirectory + @"\", "");
                }
            }
        }

        /// <summary>
        /// Performs evictions based on expiry dates, cache size, etc. then ensures the persisted cache index is updated.
        /// </summary>
        /// <param name="honorShutdown"></param>
        private void Cleanup(bool honorShutdown)
        {
            SaveUncommitted(honorShutdown);

            if (honorShutdown && Interlocked.Read(ref _stopping) > 0)
            {
                return;
            }

            // In final cleanup don't worry about evictions
            if (honorShutdown)
            {
                DateTime d = DateTime.Now;

                var remove = (from a in _index where !a.Active || a.ExpiryDate < d || (!string.IsNullOrEmpty(a.FileName) && !File.Exists(Path.Combine(RootDirectory, a.FileName))) select a).ToList();

                if ((_index.Count - remove.Count) > MaximumItemCount)
                {
                    var discard = MaximumItemCount - _index.Count + remove.Count;
                    remove.AddRange((from a in _index where a.Active orderby a.ReadCount ascending, a.LastRead select a).Take(discard));
                }

                if (Interlocked.Read(ref _stopping) > 0)
                {
                    return;
                }

                Parallel.ForEach(remove, new ParallelOptions() { MaxDegreeOfParallelism = Environment.ProcessorCount > 4 ? Environment.ProcessorCount >> 2: 1 }, (c, pls) =>
                {
                    if (Interlocked.Read(ref _stopping) > 0)
                    {
                        pls.Break();
                        return;
                    }

                    if (!string.IsNullOrEmpty(c.FileName) && File.Exists(Path.Combine(RootDirectory, c.FileName)))
                    {
                        File.Delete(Path.Combine(RootDirectory, c.FileName));
                    }

                    _index.Remove(c);
                });

                if (Interlocked.Read(ref _stopping) > 0)
                {
                    return;
                }
            }

            SaveIndex(honorShutdown);
        }

        private void _monitor_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (Interlocked.Read(ref _stopping) > 0)
            {
                return;
            }

            // Will actually use a background thread that's low priority
            Thread bwt = new Thread(() =>
            {
                if (Interlocked.Read(ref _stopping) > 0)
                {
                    return;
                }

                try
                {
                    Interlocked.Increment(ref _working);

                    if (Interlocked.Read(ref _working) > 1)
                    {
                        return;
                    }

                    var d = DateTime.Now;

                    if (DefaultStorageStrategy != CacheStorageStrategy.OnlyMemory)
                    {
                        Parallel.ForEach(GetAllFiles(RootDirectory), new ParallelOptions() { MaxDegreeOfParallelism = Environment.ProcessorCount > 4 ? Environment.ProcessorCount >> 2 : 1 }, (f) =>
                        {
                            if (_index.GetFirstByName(nameof(MFSEntry.FileName), f) == null)
                            {
                                File.Delete(f);
                            }
                        });
                    }

                    Cleanup(true);

                    _lastMonitorTimerDuration = DateTime.Now.Subtract(d).TotalMilliseconds;

                    if (MonitorTimerUseDynamicInterval)
                    {
                        MonitorTimerMillisecondInterval = Convert.ToInt32(Math.Pow(_lastMonitorTimerDuration.GetValueOrDefault() + (_working * 5), 0.9));

                        if (MonitorTimerMillisecondInterval < 200)
                        {
                            MonitorTimerMillisecondInterval *= 3;
                        }
                        else
                        {
                            if (MonitorTimerMillisecondInterval > 50000)
                            {
                                MonitorTimerMillisecondInterval /= 2;
                            }
                        }

                        _monitor.Interval = MonitorTimerMillisecondInterval;
                    }
                }
                catch (Exception ex)
                {
                    CEFDebug.WriteInfo($"Exception in cache monitor: {ex.Message}");
                }
                finally
                {
                    Interlocked.Decrement(ref _working);
                    _monitor.Start();
                }
            });

            bwt.Priority = ThreadPriority.Lowest;
            bwt.Start();
        }

        #region IDisposable Support
        private bool _disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                Shutdown();
                _disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
        #endregion

        IList<Type> ICEFService.RequiredServices()
        {
            return null;
        }

        Type ICEFService.IdentifyStateType(object o, ServiceScope ss, bool isNew)
        {
            return null;
        }

        WrappingSupport ICEFService.IdentifyInfraNeeds(object o, object replaced, ServiceScope ss, bool isNew)
        {
            return WrappingSupport.None;
        }

        void ICEFService.FinishSetup(ServiceScope.TrackedObject to, ServiceScope ss, bool isNew, IDictionary<string, object> props, ICEFServiceObjState state, bool initFromTemplate)
        {
        }

        public virtual void Disposing(ServiceScope ss)
        {
            Cleanup(false);
        }
    }
}
