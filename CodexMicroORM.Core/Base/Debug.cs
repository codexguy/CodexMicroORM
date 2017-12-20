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
using System.Text;
using CodexMicroORM.Core.Services;
using System.Diagnostics;
using System.Linq;
using System.Data.SqlClient;

namespace CodexMicroORM.Core
{
    /// <summary>
    /// Intended for internal (development) debugging only. Heavy performance penalties when writing debug info, but VERY handy.
    /// </summary>
    public static class CEFDebug
    {
        public static bool DebugEnabled
        {
            get;
            set;
        } = false;

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
        public static void DumpSQLCall(string cmd, SqlParameterCollection spc)
        {
            if (!DebugEnabled)
                return;

            var parm = (from a in spc.Cast<SqlParameter>() select $"{a.ParameterName}={a.Value}").ToArray();
            Debug.WriteLine($"SQL: {cmd} {string.Join(", ", parm)}");
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

        [System.Diagnostics.Conditional("DEBUG")]
        public static void DumpServiceScope()
        {
            if (!DebugEnabled)
                return;

            var ol = CEF.CurrentServiceScope.Objects;

            Debug.WriteLine("ServiceScope:");
            int i = 1;

            foreach (var d in ol)
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

                ++i;
            }
        }
    }
}
