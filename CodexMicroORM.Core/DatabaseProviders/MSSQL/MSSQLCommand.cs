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
using System.Collections.Generic;
using System.Linq;
using System.Data.SqlClient;
using System.Data;
using System.Text.RegularExpressions;
using CodexMicroORM.Core;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace CodexMicroORM.Providers
{
    /// <summary>
    /// Implements the functionality expected by CEF for dealing with database commands, for MS SQL Server.
    /// </summary>
    public class MSSQLCommand : IDBProviderCommand
    {
        private static ConcurrentDictionary<string, IEnumerable<SqlParameter>> _paramCache = new ConcurrentDictionary<string, IEnumerable<SqlParameter>>(Globals.CurrentStringComparer);
        private SqlCommand _cmd;

        public static void FlushCaches()
        {
            _paramCache.Clear();
        }

        public MSSQLCommand(MSSQLConnection conn, string cmdText, CommandType cmdType)
        {
            _cmd = new SqlCommand(cmdText, conn.CurrentConnection);
            _cmd.CommandType = cmdType;
            _cmd.CommandTimeout = Globals.CommandTimeoutSeconds.GetValueOrDefault(conn.CurrentConnection.ConnectionTimeout);
            _cmd.Transaction = conn.CurrentTransaction;
        }

        public IDictionary<string, object> GetParameterValues()
        {
            Dictionary<string, object> parms = new Dictionary<string, object>();

            if (_cmd.Parameters != null)
            {
                foreach (var p in _cmd.Parameters.Cast<SqlParameter>())
                {
                    parms[p.ParameterName] = p.Value;
                }
            }

            return parms;
        }

        public string LastMessage
        {
            get;
            private set;
        }

        public int LastStatus
        {
            get;
            private set;
        }

        private static Regex _splitter = new Regex(@"^(?:\[?(?<s>.+?)\]?\.)?\[?(?<n>.+?)\]?$", RegexOptions.Compiled);

        public static (string schema, string name) SplitIntoSchemaAndName(string fullname)
        {
            var matObj = _splitter.Match(fullname);
            return (matObj.Groups["s"].Value, matObj.Groups["n"].Value);
        }

        private void DiscoverParameters()
        {
            // Would like to use SqlCommandBuilder.DeriveParameters but not available in netstandard2.0 - we will assume sql 2012 at least
            using (var discoverConn = new SqlConnection(_cmd.Connection.ConnectionString))
            {
                discoverConn.Open();

                using (var discoverCmd = new SqlCommand("[sys].[sp_procedure_params_100_managed]", discoverConn))
                {
                    discoverCmd.CommandType = CommandType.StoredProcedure;

                    var sn = SplitIntoSchemaAndName(_cmd.CommandText);

                    if (string.IsNullOrEmpty(sn.name))
                    {
                        throw new CEFInvalidOperationException($"Unable to determine stored procedure name from {_cmd.CommandText}.");
                    }

                    discoverCmd.Parameters.AddWithValue("@procedure_name", sn.name);

                    if (!string.IsNullOrEmpty(sn.schema))
                    {
                        discoverCmd.Parameters.AddWithValue("@procedure_schema", sn.schema);
                    }

                    using (var da = new SqlDataAdapter(discoverCmd))
                    {
                        DataTable dtParm = new DataTable();
                        da.Fill(dtParm);
                        _cmd.Parameters.Clear();

                        foreach (DataRow dr in dtParm.Rows)
                        {
                            var p = new SqlParameter(dr["PARAMETER_NAME"].ToString(), (SqlDbType)Convert.ToInt32(dr["MANAGED_DATA_TYPE"]));

                            switch (Convert.ToInt32(dr["PARAMETER_TYPE"]))
                            {
                                case 4:
                                    p.Direction = ParameterDirection.ReturnValue;
                                    break;
                                case 1:
                                    p.Direction = ParameterDirection.Input;
                                    break;
                                default:
                                    p.Direction = ParameterDirection.InputOutput;
                                    break;
                            }

                            int.TryParse(dr["CHARACTER_MAXIMUM_LENGTH"].ToString(), out int len);

                            if (len > 0)
                            {
                                p.Size = len;
                            }

                            byte.TryParse(dr["NUMERIC_PRECISION"].ToString(), out byte prec);

                            if (prec > 0)
                            {
                                p.Precision = prec;
                            }

                            byte.TryParse(dr["NUMERIC_SCALE"].ToString(), out byte scale);

                            if (scale > 0)
                            {
                                p.Scale = scale;
                            }

                            _cmd.Parameters.Add(p);
                        }
                    }
                }
            }
        }

        private SqlParameter CloneParameterNoValue(SqlParameter source)
        {
            return new SqlParameter()
            {
                DbType = source.DbType,
                Direction = source.Direction,
                LocaleId = source.LocaleId,
                Offset = source.Offset,
                ParameterName = source.ParameterName,
                Precision = source.Precision,
                Scale = source.Scale,
                Size = source.Size,
                SqlDbType = source.SqlDbType
            };
        }

        private void BuildParameters()
        {
            if (_cmd.CommandType == CommandType.StoredProcedure)
            {
                if (!_paramCache.ContainsKey(_cmd.CommandText))
                {
                    DiscoverParameters();
                    _paramCache[_cmd.CommandText] = _cmd.Parameters.Cast<SqlParameter>();
                }
                else
                {
                    _cmd.Parameters.Clear();
                    _cmd.Parameters.AddRange((from a in _paramCache[_cmd.CommandText] select CloneParameterNoValue(a)).ToArray());
                }
            }
        }

        public MSSQLCommand MapParameters(IList<object> parms)
        {
            BuildParameters();

            // We will do this positionally - skip any fields that are strictly framework managed
            // We can do no name mapping since we have no source names!
            int idx = 0;
            foreach (var p in (from a in _cmd.Parameters.Cast<SqlParameter>() select new { Name = a.ParameterName.StartsWith("@") ? a.ParameterName.Substring(1) : a.ParameterName, Parm = a }))
            {
                if (p.Parm.Direction != ParameterDirection.ReturnValue && string.Compare(p.Name, MSSQLProcBasedProvider.ProcedureMessageParameter, true) != 0 && string.Compare(p.Name, MSSQLProcBasedProvider.ProcedureRetValParameter,true) != 0)
                {
                    p.Parm.Value = parms[idx];
                    idx++;
                }
            }

            return this;
        }

        public MSSQLCommand MapParameters(Type baseType, object forObject, IDictionary<string, object> parms)
        {
            BuildParameters();

            var db = CEF.CurrentDBService(forObject);

            // The procedure signature is king - map underlying object to it, using and supplied name translations in scope
            foreach (var p in (from a in _cmd.Parameters.Cast<SqlParameter>()
                               let sn = a.ParameterName.StartsWith("@") ? a.ParameterName.Substring(1) : a.ParameterName
                               let pn = db?.GetPropertyNameFromStorageName(baseType, sn) ?? sn
                               where parms.ContainsKey(pn) select new { Parm = a, Value = parms[pn] }))
            {
                p.Parm.Value = p.Value ?? DBNull.Value;
            }

            return this;
        }

        public IEnumerable<Dictionary<string, (object value, Type type)>> ExecuteReadRows()
        {
            using (var da = new SqlDataAdapter(_cmd))
            {
                using (var r = _cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        Dictionary<string, (object, Type)> values = new Dictionary<string, (object, Type)>();

                        for (int i = 0; i < r.FieldCount; ++i)
                        {
                            var prefType = r.GetFieldType(i);
                            var val = r.GetValue(i);

                            if (DBNull.Value.Equals(val))
                            {
                                val = null;
                            }

                            values[r.GetName(i)] = (val, prefType);
                        }

                        yield return values;
                    }
                }
            }
        }

        public IDBProviderCommand ExecuteNoResultSet()
        {
            CEFDebug.DumpSQLCall(_cmd.CommandText, this.GetParameterValues());

            try
            {
                _cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                CEFDebug.WriteInfo($"SQL Call Error: " + ex.Message);
                throw;
            }

            return this;
        }

        public IEnumerable<(string name, object value)> GetOutputValues()
        {
            return (from a in _cmd.Parameters.Cast<SqlParameter>()
                    where a.Direction == ParameterDirection.Output || a.Direction == ParameterDirection.InputOutput
                    let pn = a.ParameterName.StartsWith("@") ? a.ParameterName.Substring(1) : a.ParameterName
                    select (pn, a.Value));
        }
    }
}
