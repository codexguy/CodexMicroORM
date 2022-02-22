/***********************************************************************
Copyright 2022 CodeX Enterprises LLC

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
06/2018    0.7     Initial release (Joel Champagne)
***********************************************************************/
#nullable enable
using CodexMicroORM.Core;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CodeXFramework.BaseEntity
{
    public sealed class ParallelWorkload
    {
#pragma warning disable IDE0060 // Remove unused parameter
        public static IterateResult RunUnorderedWorkload(IList src, int smalllist, int dop, Func<object, int, int, object[], object> body)
#pragma warning restore IDE0060 // Remove unused parameter
        {
            int idx = 0;
            List<Exception> errors = new();
            List<object> data = new();

            if (src.Count <= smalllist)
            {
                foreach (var a in src)
                {
                    try
                    {
                        var lidx = Interlocked.Add(ref idx, 1);
                        var d = body.Invoke(a, lidx - 1, src.Count - lidx, Array.Empty<object>());

                        lock (data)
                        {
                            data.Add(d);
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add(ex);
                    }
                }
            }
            else
            {
                var ss = CEF.CurrentServiceScope;

                _ = Parallel.ForEach(src.Cast<object>(), (a) =>
                  {
                      try
                      {
                          using (CEF.UseServiceScope(ss))
                          {
                              var lidx = Interlocked.Add(ref idx, 1);
                              var d = body.Invoke(a, lidx - 1, src.Count - lidx, Array.Empty<object>());

                              lock (data)
                              {
                                  data.Add(d);
                              }
                          }
                      }
                      catch (Exception ex)
                      {
                          lock (errors)
                          {
                              errors.Add(ex);
                          }
                      }
                  });
            }

            return new IterateResult(data.ToArray(), errors.ToArray());
        }

        public static void RunWorkloadFunctions(bool ordered, params Action[] args)
        {
            if (ordered)
            {
                foreach (var a in args)
                {
                    a.Invoke();
                }
            }
            else
            {
                var ss = CEF.CurrentServiceScope;

                Parallel.ForEach(args, (a) =>
                {
                    using (CEF.UseServiceScope(ss))
                    {
                        a.Invoke();
                    }
                });
            }
        }

        public static void RunWorkloadFunctions(params Action[] args)
        {
            var ss = CEF.CurrentServiceScope;

            Parallel.ForEach(args, (a) =>
            {
                using (CEF.UseServiceScope(ss))
                {
                    a.Invoke();
                }
            });
        }
    }

    public sealed class IterateResult
    {
        private readonly object?[]? _data;
        private readonly Exception[] _errors;

        internal IterateResult(bool cancelled)
        {
            Cancelled = cancelled;
            _errors = Array.Empty<Exception>();
        }

        internal IterateResult(object[] data, Exception[] errors)
        {
            _data = data;
            _errors = errors;
        }

        public bool Cancelled { get; } = false;

        public IList<T?> GetData<T>() where T : class
        {
            return GetData<T>(true);
        }

        public IList<T?> GetData<T>(bool rethrowFirst) where T : class
        {
            if (rethrowFirst)
                RethrowFirstException();

            if (_data == null)
                return Array.Empty<T>();
            else
                return Array.ConvertAll<object?, T>(_data, p => (T?)p!);
        }

        public IList<Exception> GetErrors()
        {
            return _errors;
        }

        public bool HasErrors()
        {
            return _errors.Count() > 0;
        }

        public void RethrowFirstException()
        {
            if (_errors.Count() > 0)
                throw new AggregateException(_errors);
        }
    }

}
