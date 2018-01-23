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
using System.Linq;
using System.Threading.Tasks;

namespace CodexMicroORM.Core.Services
{
    public class DBService : ICEFDataHost
    {
        private static IDBProvider _defaultProvider;

        public DBService()
        {
        }

        public DBService(IDBProvider defaultProvider)
        {
            _defaultProvider = defaultProvider;
        }

        public IDBProvider GetProviderForType<T>(T o)
        {
            // todo - of course, need to examine by type
            return _defaultProvider;
        }

        private ConcurrentQueue<Task> _completions = new ConcurrentQueue<Task>();
        private List<Exception> _completionEx = new List<Exception>();

        public static IDBProvider DefaultProvider => _defaultProvider;

        IList<Type> ICEFService.RequiredServices()
        {
            return new Type[] { typeof(PCTService), typeof(KeyService) };
        }

        public void WaitOnCompletions()
        {
            while (_completions.TryDequeue(out Task t))
            {
                t.Wait();
            }

            lock (_completionEx)
            {
                if (_completionEx.Count > 0)
                {
                    var toThrow = _completionEx.ToArray();
                    _completionEx.Clear();
                    throw new AggregateException(toThrow);
                }
            }
        }

        public void AddCompletionException(Exception ex)
        {
            lock (_completionEx)
            {
                _completionEx.Add(ex);
            }
        }

        public void ExecuteRaw(ConnectionScope cs, string cmdText, bool doThrow = true, bool stopOnError = true)
        {
            _defaultProvider.ExecuteRaw(cs, cmdText, doThrow, stopOnError);
        }

        public IList<(object item, string message, int status)> Save(IList<ICEFInfraWrapper> rows, ServiceScope ss, DBSaveSettings settings)
        {
            List<(object item, string message, int status)> results = new List<(object item, string message, int status)>();

            var cs = CEF.CurrentConnectionScope;
            var aud = CEF.CurrentAuditService();
            var ks = CEF.CurrentKeyService();

            // Ordering of rows supports foreign key dependencies: insert/update top-down, delete bottom-up.
            // Different order #'s require sequential processing, so we group by order # - within an order group/table, we should be able to issue parallel requests
            // We also offer a way to "preview" what will be saved and adjust if needed
            var ordRows = (from a in rows
                           let uw = a.AsUnwrapped()
                           where uw != null
                           let level = ks.GetObjectNestLevel(uw)
                           let sp = (settings.RowSavePreview == null ? (true, null) : settings.RowSavePreview.Invoke(a))
                           let rs = sp.treatas.GetValueOrDefault(a.GetRowState())
                           where (rs != ObjectState.Unchanged && rs != ObjectState.Unlinked) && sp.cansave
                           let w = a.GetWrappedObject() as ICEFWrapper
                           let bt = uw.GetBaseType()
                           let rd = new { Row = a, Schema = w?.GetSchemaName(), Name = (w != null ? w.GetBaseType().Name : bt?.Name) }
                           group rd by new
                           {
                               Level = level,
                               RowState = rs
                           }
                           into g
                           select new { g.Key.Level, g.Key.RowState, Rows = (from asp in g select (asp.Schema, asp.Name, Row: (aud == null ? asp.Row : aud.SavePreview(ss, asp.Row, g.Key.RowState)))) });

            if ((settings.AllowedOperations & DBSaveSettings.Operations.Delete) != 0)
            {
                results.AddRange(from a in _defaultProvider.DeleteRows(cs, (from a in ordRows where a.RowState == ObjectState.Deleted select (a.Level, a.Rows)), settings) select (a.row.GetWrappedObject(), a.msg, a.status));
            }
            if ((settings.AllowedOperations & DBSaveSettings.Operations.Insert) != 0)
            {
                results.AddRange(from a in _defaultProvider.InsertRows(cs, (from a in ordRows where a.RowState == ObjectState.Added select (a.Level, a.Rows)), settings) select (a.row.GetWrappedObject(), a.msg, a.status));
            }
            if ((settings.AllowedOperations & DBSaveSettings.Operations.Update) != 0)
            {
                results.AddRange(from a in _defaultProvider.UpdateRows(cs, (from a in ordRows where a.RowState == ObjectState.Modified select (a.Level, a.Rows)), settings) select (a.row.GetWrappedObject(), a.msg, a.status));
            }

            cs.DoneWork();

            // We need to do the cache update at the end since we could have assigned values, etc. during the above save step - cache the final values!
            ICEFCachingHost cache = CEF.CurrentCacheService();

            if (cache != null && results.Any())
            {
                var forCaching = (from a in results
                                  let bt = a.item.GetBaseType()
                                  let cb = ss.ResolvedCacheBehaviorForType(bt)
                                  where cb != 0
                                  let iw = a.item.AsInfraWrapped()
                                  where iw != null
                                  let rs = iw.GetRowState()
                                  group new { a, iw } by new { Type = bt, CacheMode = cb, RowState = rs } into g
                                  select (g.Key.Type, g.Key.CacheMode, g.Key.RowState, Rows: (from c in g select c.iw.GetAllValues(true, true)).ToList())).ToList();

                Action<object> act = (object state) =>
                {
                    try
                    {
                        cache.DoingWork();

                        using (CEF.UseServiceScope(ss))
                        {
                            var list = ((IEnumerable<(Type Type, CacheBehavior CacheMode, ObjectState RowState, List<IDictionary<string, object>> Rows)>)state);

                            // Update by identity entries with new values (or invalidate for deletions)
                            foreach (var ci in (from a in list where (a.CacheMode & CacheBehavior.IdentityBased) != 0 select a))
                            {
                                if (ci.RowState == ObjectState.Deleted || ci.RowState == ObjectState.Unlinked)
                                {
                                    foreach (var r in ci.Rows)
                                    {
                                        cache.InvalidateIdentityEntry(ci.Type, r);
                                    }
                                }
                                else
                                {
                                    foreach (var r in ci.Rows)
                                    {
                                        cache.UpdateByIdentity(ci.Type, r);
                                    }
                                }
                            }

                            // Invalidate all by query entries for any types contained in the save
                            foreach (var invType in (from a in list where (a.CacheMode & (CacheBehavior.QueryBased | CacheBehavior.OnlyForAllQuery)) != 0 select a.Type).Distinct())
                            {
                                cache.InvalidateForByQuery(invType, Globals.TypeSpecificCacheEvictionsOnUpdates);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        AddCompletionException(ex);
                        CEFDebug.WriteInfo($"Cache error on DBSave: {ex.Message}");
                    }
                    finally
                    {
                        cache.DoneWork();
                    }
                };

                if (settings.AsyncCacheUpdates.GetValueOrDefault(ss.Settings.AsyncCacheUpdates.GetValueOrDefault(Globals.AsyncCacheUpdates)))
                {
                    AddCompletionTask(Task.Factory.StartNew(act, forCaching));
                }
                else
                {
                    act.Invoke(forCaching);
                }
            }

            return results;
        }

        public void AddCompletionTask(Task t)
        {
            if (Globals.MaximumCompletionItemsQueued > 0)
            {
                while (_completions.Count >= Globals.MaximumCompletionItemsQueued)
                {
                    if (_completions.TryDequeue(out Task t2))
                    {
                        t2.Wait();
                    }
                }
            }

            _completions.Enqueue(t);

            // TODO - understand why this is not a "good thing" in real world - async saving appears faster without this
            //t.ContinueWith((t2) =>
            //{
            //    while (_completions.Count > 0)
            //    {
            //        if (_completions.TryPeek(out Task check))
            //        {
            //            if (check.IsCanceled || check.IsCompleted || check.IsFaulted)
            //            {
            //                if (!_completions.TryDequeue(out Task rem))
            //                {
            //                    return;
            //                }
            //            }
            //            else
            //            {
            //                return;
            //            }
            //        }
            //        else
            //        {
            //            return;
            //        }
            //    }
            //});
        }

        public T ExecuteScalar<T>(string cmdText)
        {
            var cs = CEF.CurrentConnectionScope;
            var res = _defaultProvider.ExecuteScalar<T>(cs, cmdText);
            cs.DoneWork();
            return res;
        }

        public void ExecuteRaw(string command, bool doThrow = true, bool stopOnError = true)
        {
            var cs = CEF.CurrentConnectionScope;
            _defaultProvider.ExecuteRaw(cs, command, doThrow, stopOnError);
            cs.DoneWork();
        }

        public IEnumerable<T> RetrieveByKey<T>(params object[] key) where T : class, new()
        {
            var ss = CEF.CurrentServiceScope;

            // Special situation we look for: async saving can result in unsaved objects, where we're better off to pull from that which might still be in memory
            if (_completions.Count > 0 && ((ss.Settings.MergeBehavior & MergeBehavior.CheckScopeIfPending) != 0))
            {
                var kss = ss.GetServiceState<KeyService.KeyServiceState>();

                if (kss != null)
                {
                    var eto = kss.GetTrackedByPKValue(ss, typeof(T), key);

                    if (eto != null && eto.IsAlive)
                    {
                        return new T[] { eto.GetWrapperTarget() as T };
                    }
                }
            }

            ICEFCachingHost cache = null;

            if ((ss.Settings.CacheBehavior & CacheBehavior.IdentityBased) != 0)
            {
                // If caching is present, see if this identity is already in cache and return it if so
                cache = CEF.CurrentCacheService();
                var cached = cache?.GetByIdentity<T>(key);

                if (cached != null)
                {
                    return new T[] { cached };
                }
            }

            var cs = CEF.CurrentConnectionScope;
            var res = _defaultProvider.RetrieveByKey<T>(cs, true, key);
            cs.DoneWork();

            // If caching is present, call out to it to potentially cache results (by identity)
            if (cache != null)
            {
                res = res.ToArray();

                var fr = res.FirstOrDefault();

                if (fr != null)
                {
                    cache.AddByIdentity(fr, key);
                }
            }

            return res;
        }

        public IEnumerable<T> RetrieveByQuery<T>(CommandType cmdType, string cmdText, params object[] parms) where T : class, new()
        {
            ICEFCachingHost cache = null;
            var cb = CEF.CurrentServiceScope.Settings.CacheBehavior;

            if ((cb & CacheBehavior.QueryBased) != 0 && (cb & CacheBehavior.OnlyForAllQuery) == 0)
            {
                // If caching is present, see if this identity is already in cache and return it if so
                cache = CEF.CurrentCacheService();
                var cached = cache?.GetByQuery<T>(cmdText, parms);

                if (cached != null)
                {
                    return cached;
                }
            }

            var cs = CEF.CurrentConnectionScope;
            var res = _defaultProvider.RetrieveByQuery<T>(cs, true, cmdType, cmdText, parms);
            cs.DoneWork();

            if (cache == null && (cb & CacheBehavior.ConvertQueryToIdentity) != 0)
            {
                cache = CEF.CurrentCacheService();
            }

            if (cache != null)
            {
                res = res.ToArray();

                if (res.Any())
                {
                    cache.AddByQuery(res, cmdText, parms);
                }
            }

            return res;
        }

        public IEnumerable<T> RetrieveAll<T>() where T: class, new()
        {
            ICEFCachingHost cache = null;

            if ((CEF.CurrentServiceScope.Settings.CacheBehavior & CacheBehavior.QueryBased) != 0)
            {
                // If caching is present, see if this identity is already in cache and return it if so
                cache = CEF.CurrentCacheService();
                var cached = cache?.GetByQuery<T>("ALL", null);

                if (cached != null)
                {
                    return cached;
                }
            }

            var cs = CEF.CurrentConnectionScope;
            var res = _defaultProvider.RetrieveAll<T>(cs, true);
            cs.DoneWork();

            if (cache != null)
            {
                res = res.ToArray();

                if (res.Any())
                {
                    cache.AddByQuery(res, "ALL", null);
                }
            }

            return res;
        }

        Type ICEFService.IdentifyStateType(object o, ServiceScope ss, bool isNew)
        {
            return null;
        }

        WrappingSupport ICEFService.IdentifyInfraNeeds(object o, object replaced, ServiceScope ss, bool isNew)
        {
            if ((replaced ?? o) is INotifyPropertyChanged)
            {
                return WrappingSupport.OriginalValues | WrappingSupport.PropertyBag;
            }
            else
            {
                return WrappingSupport.Notifications | WrappingSupport.OriginalValues | WrappingSupport.PropertyBag;
            }
        }

        void ICEFService.FinishSetup(ServiceScope.TrackedObject to, ServiceScope ss, bool isNew, IDictionary<string, object> props, ICEFServiceObjState state)
        {
        }

        public virtual void Disposing(ServiceScope ss)
        {
            WaitOnCompletions();
        }

        #region IDisposable Support
        private bool _disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    WaitOnCompletions();
                }

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
