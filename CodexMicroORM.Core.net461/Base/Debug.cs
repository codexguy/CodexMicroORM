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
using System.Text;
using CodexMicroORM.Core.Services;
using System.Diagnostics;
using System.Linq;
using System.Data.SqlClient;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace CodexMicroORM.Core
{
    /// <summary>
    /// Intended for internal (development) debugging only. Heavy performance penalties when writing debug info, but VERY handy.
    /// </summary>
    public static class CEFDebug
    {
        private static bool _handled = false;
        private static Stopwatch _sw = new Stopwatch();

        public static long LastElapsedTick
        {
            get;
            set;
        }

        public static bool DebugEnabled
        {
            get;
            set;
        } = false;

        public static bool DebugTimeEnabled
        {
            get;
            set;
        } = false;

        [System.Diagnostics.Conditional("DEBUG")]
        public static void StartTimer()
        {
            _sw.Start();
            _sw.Restart();
        }

        [System.Diagnostics.Conditional("DEBUG")]
        public static void LogTime(string descriptor)
        {
            LastElapsedTick = _sw.ElapsedTicks;

            if (DebugEnabled || DebugTimeEnabled)
            {
                Debug.WriteLine($"CEF Timer - {descriptor} - {LastElapsedTick}");
                //File.AppendAllText(@"c:\temp\perf.txt", $"CEF Timer - {descriptor} - {LastElapsedTick}" + Environment.NewLine);
            }

            _sw.Restart();
        }

        [System.Diagnostics.Conditional("DEBUG")]
        public static void WaitAttach()
        {
            while (!Debugger.IsAttached)
            {
                System.Threading.Thread.Sleep(50);
            }
            if (!_handled)
            {
                _handled = true;
                Debugger.Break();
            }
        }

        [System.Diagnostics.Conditional("DEBUG")]
        public static void DebugStop(Func<bool> check)
        {
            if (System.Diagnostics.Debugger.IsAttached)
            {
                try
                {
                    if (check.Invoke())
                    {
                        System.Diagnostics.Debugger.Break();
                    }
                }
                catch
                {
                }
            }
        }

        [System.Diagnostics.Conditional("DEBUG")]
        public static void DumpRelations()
        {
            if (!DebugEnabled)
                return;

            var kss = CEF.CurrentServiceScope.GetServiceState<KeyService.KeyServiceState>();
            var i = 1;

            foreach (var d in kss.AllFK)
            {
                Debug.WriteLine($"{i}. {d.Parent.BaseName} -> {d.Parent.GetWrapperTarget().ToString()}");
                Debug.WriteLine($"   {d.Child.BaseName} -> {d.Child.GetWrapperTarget().ToString()}");
                Debug.WriteLine($"   P: {(string.Join(", ", (from a in d.GetParentKeyValue() select a == null ? "null" : a.ToString()).ToArray()))}");
                Debug.WriteLine($"   C: {(string.Join(", ", (from a in d.GetChildRoleValue() select a == null ? "null" : a.ToString()).ToArray()))}");
                ++i;
            }
        }

        [System.Diagnostics.Conditional("DEBUG")]
        public static void DumpSQLCall(string cmd, IDictionary<string, object> spc)
        {
            if (!DebugEnabled)
                return;

            var parm = (from a in spc select $"{a.Key}={a.Value}").ToArray();
            Debug.WriteLine($"{cmd} {string.Join(", ", parm)}");
        }

        [System.Diagnostics.Conditional("DEBUG")]
        public static void WriteInfo(string info, object o = null)
        {
            if (!DebugEnabled)
                return;

            StringBuilder sb = new StringBuilder();
            if (o != null)
            {
                foreach (var pi in o.GetType().GetProperties())
                {
                    if (sb.Length > 0)
                    {
                        sb.Append(", ");
                    }
                    sb.Append($"{pi.Name}={pi.GetValue(o)}");
                }
            }
            Debug.WriteLine($"I: {info} {sb.ToString()}");
        }

        public static string ReturnIWTextFromList(System.Collections.IEnumerable list, string typename = null, bool dirty = false, bool origvals = true, params string[] fields)
        {
#if !DEBUG
            throw new InvalidOperationException("Not intended for release builds.");
#endif
            StringBuilder sb2 = new StringBuilder();

            // Build list of filter conditions
            List<(string field, string value)> filters = new List<(string field, string value)>();
            List<string> toreturn = new List<string>();

            if (fields != null)
            {
                foreach (var f in fields)
                {
                    var m = Regex.Match(f, @"(?<f>\w+)=(?<v>.*)");

                    if (m.Success)
                    {
                        filters.Add((m.Groups["f"].Value, m.Groups["v"].Value));
                    }
                    else
                    {
                        toreturn.Add(f);
                    }
                }
            }

            int i = 1;

            foreach (object l in list)
            {
                var iw = l.AsInfraWrapped(false);

                if (iw != null)
                {
                    if (typename == null || iw.GetBaseType().Name == typename)
                    {
                        var rs = iw.GetRowState();

                        if (rs != ObjectState.Unchanged || !dirty)
                        {
                            if (!filters.Any() || (from a in filters from p in iw.GetAllValues() where string.Compare(a.field, p.Key, true) == 0 && string.Compare(a.value, p.Value?.ToString() ?? "", true) == 0 select a).Count() == filters.Count())
                            {
                                sb2.AppendLine($"{i}. {iw.GetBaseType().Name}, {iw.GetRowState()}");
                                ++i;

                                foreach (var p in iw.GetAllValues())
                                {
                                    if (!toreturn.Any() || (from a in toreturn where string.Compare(a, p.Key, true) == 0 select a).Any())
                                    {
                                        sb2.Append($"    {p.Key}={p.Value}");

                                        if (rs == ObjectState.Modified || rs == ObjectState.ModifiedPriority)
                                        {
                                            var ov = iw.GetOriginalValue(p.Key, false);

                                            if (ov != null && origvals && ov.ToString() != p.Value?.ToString())
                                            {
                                                sb2.Append(" // ");
                                                sb2.Append(ov);
                                            }
                                        }

                                        sb2.AppendLine();
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return sb2.ToString();
        }

        public static int GetServiceScopeObjectCount(bool dirtyonly = false, params string[] types)
        {
            var ss = CEF.CurrentServiceScope;
            var ol = ss.Objects;
            int cnt = 0;

            foreach (var i in ol)
            {
                if (i.IsAlive)
                {
                    if (types == null || types.Contains(i.BaseName))
                    {
                        if (!dirtyonly || i.GetInfra()?.GetRowState() != ObjectState.Unchanged)
                        {
                            ++cnt;
                        }
                    }
                }
            }

            return cnt;
        }

        public static string ReturnServiceScopeLite(params string[] types)
        {
#if !DEBUG
            throw new InvalidOperationException("Not intended for release builds.");
#endif
            StringBuilder sb2 = new StringBuilder();

            var ss = CEF.CurrentServiceScope;
            var ol = ss.Objects;

            foreach (var i in (from a in ol 
                               where a.IsAlive 
                               && (types == null || types.Contains(a.BaseName))
                               let b = a.GetInfra()?.GetRowState() 
                               where b != ObjectState.Unchanged 
                               group a by a.BaseName into g
                               select new
                               {
                                   g.Key,
                                   Items = (from c in g
                                            let b2 = c.GetInfra()?.GetRowState()
                                            let s = b2 == ObjectState.Added ? "A" : b2 == ObjectState.Deleted ? "D" : b2 == ObjectState.Unlinked ? "X" : "M"
                                            group c by s into g2
                                            select g2)
                               }))
            {
                if (sb2.Length > 0)
                {
                    sb2.Append("; ");
                }

                sb2.Append(i.Key);

                foreach (var i2 in i.Items)
                {
                    sb2.Append(" ");
                    sb2.Append(i2.Key);
                    sb2.Append("/");
                    sb2.Append(i2.Count());
                }
            }

            return sb2.ToString();
        }

        public static string ReturnServiceScope(ServiceScope ss = null, string typename = null, bool dirty = false, bool origvals = true, params string[] fields)
        {
#if !DEBUG
            throw new InvalidOperationException("Not intended for release builds.");
#endif
            StringBuilder sb2 = new StringBuilder();

            if (ss == null)
            {
                ss = CEF.CurrentServiceScope;
            }

            // Build list of filter conditions
            List<(string field, string value)> filters = new List<(string field, string value)>();
            List<string> toreturn = new List<string>();

            if (fields != null)
            {
                foreach (var f in fields)
                {
                    var m = Regex.Match(f, @"(?<f>\w+)=(?<v>.*)");

                    if (m.Success)
                    {
                        filters.Add((m.Groups["f"].Value, m.Groups["v"].Value));
                    }
                    else
                    {
                        toreturn.Add(f);
                    }
                }
            }

            var ol = ss.Objects;
            int i = 1;

            foreach (var d in ol)
            {
                if (d.IsAlive)
                {
                    if (typename == null || d.BaseName == typename)
                    {
                        var rs = d.GetInfra()?.GetRowState();

                        if (rs.GetValueOrDefault(ObjectState.Unlinked) != ObjectState.Unchanged || !dirty)
                        {
                            var iw = d.GetInfra();

                            if (iw != null)
                            {
                                if (!filters.Any() || (from a in filters from p in iw.GetAllValues() where string.Compare(a.field, p.Key, true) == 0 && string.Compare(a.value, p.Value?.ToString() ?? "", true) == 0 select a).Count() == filters.Count())
                                {
                                    sb2.AppendLine($"{i}. {d.BaseName}, {d.GetInfra()?.GetRowState()}, {(d.GetTarget() == null ? "-" : "+")}{(d.GetWrapper() == null ? "-" : "+")}{(d.GetInfra() == null ? "-" : "+")}");
                                    ++i;

                                    foreach (var p in iw.GetAllValues())
                                    {
                                        if (!toreturn.Any() || (from a in toreturn where string.Compare(a, p.Key, true) == 0 select a).Any())
                                        {
                                            sb2.Append($"    {p.Key}={p.Value}");

                                            if (rs == ObjectState.Modified || rs == ObjectState.ModifiedPriority)
                                            {
                                                var ov = iw.GetOriginalValue(p.Key, false);

                                                if (ov != null && origvals && ov.ToString() != p.Value?.ToString())
                                                {
                                                    sb2.Append(" // ");
                                                    sb2.Append(ov);
                                                }
                                            }

                                            sb2.AppendLine();
                                        }
                                    }
                                }
                            }
                            else
                            {
                                var o = d.GetWrapperTarget();

                                if (o != null)
                                {
                                    foreach (var pi in o.GetType().GetProperties())
                                    {
                                        if (!toreturn.Any() || (from a in toreturn where string.Compare(a, pi.Name, true) == 0 select a).Any())
                                        {
                                            sb2.Append($"    {pi.Name}={pi.GetValue(o)}");
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return sb2.ToString();
        }

        public static string ReturnServiceScope(string typename = null)
        {
#if !DEBUG
            throw new InvalidOperationException("Not intended for release builds.");
#endif
            StringBuilder sb2 = new StringBuilder();

            var ol = CEF.CurrentServiceScope.Objects;

            sb2.AppendLine("ServiceScope:");
            int i = 1;

            foreach (var d in ol)
            {
                if (typename == null || d.BaseName == typename)
                {
                    sb2.AppendLine($"{i}. {d.BaseName}, {d.IsAlive}, {d.GetInfra()?.GetRowState()}, {(d.GetTarget() == null ? "-" : "+")}{(d.GetWrapper() == null ? "-" : "+")}{(d.GetInfra() == null ? "-" : "+")}");

                    StringBuilder sb = new StringBuilder();
                    var o = d.GetWrapperTarget();

                    if (o != null)
                    {
                        foreach (var pi in o.GetType().GetProperties())
                        {
                            if (sb.Length > 0)
                            {
                                sb.Append(", ");
                            }
                            sb.Append($"{pi.Name}={pi.GetValue(o)}");
                        }
                        sb2.AppendLine($"    {sb.ToString()}");
                    }

                    sb = new StringBuilder();
                    var iw = d.GetInfra();

                    if (iw != null)
                    {
                        foreach (var p in iw.GetAllValues())
                        {
                            if (sb.Length > 0)
                            {
                                sb.Append(", ");
                            }
                            sb.Append($"{p.Key}={p.Value}");
                        }
                        sb2.AppendLine($"    {sb.ToString()}");
                    }
                }

                ++i;
            }

            return sb2.ToString();
        }

        [System.Diagnostics.Conditional("DEBUG")]
        public static void DumpServiceScope(string typename = null)
        {
            if (!DebugEnabled)
                return;

            var ol = CEF.CurrentServiceScope.Objects;

            Debug.WriteLine("ServiceScope:");
            int i = 1;

            foreach (var d in ol)
            {
                if (typename == null || d.BaseName == typename)
                {
                    Debug.WriteLine($"{i}. {d.BaseName}, {d.IsAlive}, {d.GetInfra()?.GetRowState()}, {(d.GetTarget() == null ? "-" : "+")}{(d.GetWrapper() == null ? "-" : "+")}{(d.GetInfra() == null ? "-" : "+")}");

                    StringBuilder sb = new StringBuilder();
                    var o = d.GetWrapperTarget();

                    if (o != null)
                    {
                        foreach (var pi in o.GetType().GetProperties())
                        {
                            if (sb.Length > 0)
                            {
                                sb.Append(", ");
                            }
                            sb.Append($"{pi.Name}={pi.GetValue(o)}");
                        }
                        Debug.WriteLine($"    {sb.ToString()}");
                    }

                    sb = new StringBuilder();
                    var iw = d.GetInfra();

                    if (iw != null)
                    {
                        foreach (var p in iw.GetAllValues())
                        {
                            if (sb.Length > 0)
                            {
                                sb.Append(", ");
                            }
                            sb.Append($"{p.Key}={p.Value}");
                        }
                        Debug.WriteLine($"    {sb.ToString()}");
                    }
                }

                ++i;
            }
        }
    }
}
