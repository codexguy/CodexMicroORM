/***********************************************************************
Copyright 2021 CodeX Enterprises LLC

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
using System.Linq;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Text.RegularExpressions;
using CodexMicroORM.Core;
using System.Collections.Concurrent;
using System.Text;

namespace CodexMicroORM.Providers
{
    /// <summary>
    /// Implements the functionality expected by CEF for dealing with database commands, for MS SQL Server.
    /// </summary>
    public sealed class MSSQLCommand : IDBProviderCommand
    {
        private static readonly ConcurrentDictionary<string, IEnumerable<SqlParameter>> _paramCache = new(Globals.DefaultCollectionConcurrencyLevel, Globals.DefaultLargerDictionaryCapacity, Globals.CurrentStringComparer);
        private readonly SqlCommand _cmd;

        public static bool UseNullForMissingValues
        {
            get;
            set;
        } = true;

        public static void FlushCaches()
        {
            _paramCache.Clear();
        }

        public MSSQLCommand(MSSQLConnection conn, string cmdText, CommandType cmdType, int? timeoutOverride)
        {
            if (conn.CurrentConnection == null)
            {
                throw new CEFInvalidStateException(InvalidStateType.SQLLayer, "CurrentConnection is not set.");
            }

            _cmd = new SqlCommand(cmdText, conn.CurrentConnection)
            {
                CommandType = cmdType,
                CommandTimeout = timeoutOverride.GetValueOrDefault(Globals.CommandTimeoutSeconds.GetValueOrDefault(conn.CurrentConnection.ConnectionTimeout)),
                Transaction = conn.CurrentTransaction
            };
        }

        public override string ToString()
        {
            StringBuilder sb = new();
            sb.Append(_cmd.CommandText);
            sb.Append("?");
            bool first = true;

            foreach (SqlParameter p in _cmd.Parameters)
            {
                if (!first)
                {
                    sb.Append("&");
                }

                first = false;
                sb.Append(p.ParameterName);
                sb.Append("=");
                sb.Append(p.Value);
                sb.Append(" (");
                sb.Append(p.Size);
                sb.Append(")");
            }

            return sb.ToString();
        }

        public IDictionary<string, object?> GetParameterValues()
        {
            Dictionary<string, object?> parms = new(Globals.DefaultDictionaryCapacity);

            if (_cmd.Parameters != null)
            {
                foreach (var p in _cmd.Parameters.Cast<SqlParameter>())
                {
                    parms[p.ParameterName] = p.Value;
                }
            }

            return parms;
        }

        public string? LastMessage
        {
            get;
            private set;
        }

        public int LastStatus
        {
            get;
            private set;
        }

        private static readonly Regex _splitter = new(@"^(?:\[?(?<s>.+?)\]?\.)?\[?(?<n>.+?)\]?$", RegexOptions.Compiled);

        public static (string schema, string name) SplitIntoSchemaAndName(string fullname)
        {
            var matObj = _splitter.Match(fullname);
            return (matObj.Groups["s"].Value, matObj.Groups["n"].Value);
        }

        private void DiscoverParameters()
        {
            // Would like to use SqlCommandBuilder.DeriveParameters but not available in netstandard2.0 - we will assume sql 2012 at least
            using var discoverConn = (SqlConnection)((ICloneable)_cmd.Connection).Clone();
            discoverConn.Open();

            using var discoverCmd = new SqlCommand("[sys].[sp_procedure_params_100_managed]", discoverConn)
            {
                CommandType = CommandType.StoredProcedure
            };

            var (schema, name) = SplitIntoSchemaAndName(_cmd.CommandText);

            if (string.IsNullOrEmpty(name))
            {
                throw new CEFInvalidStateException(InvalidStateType.SQLLayer, $"Unable to determine stored procedure name from {_cmd.CommandText}.");
            }

            discoverCmd.Parameters.AddWithValue("@procedure_name", name);

            if (!string.IsNullOrEmpty(schema))
            {
                discoverCmd.Parameters.AddWithValue("@procedure_schema", schema);
            }

            using var da = new SqlDataAdapter(discoverCmd);
            DataTable dtParm = new();
            da.Fill(dtParm);
            _cmd.Parameters.Clear();

            foreach (DataRow dr in dtParm.Rows)
            {
                var p = new SqlParameter(dr["PARAMETER_NAME"].ToString(), (SqlDbType)Convert.ToInt32(dr["MANAGED_DATA_TYPE"]))
                {
                    Direction = (Convert.ToInt32(dr["PARAMETER_TYPE"])) switch
                    {
                        4 => ParameterDirection.ReturnValue,
                        1 => ParameterDirection.Input,
                        _ => ParameterDirection.InputOutput,
                    }
                };

                if (int.TryParse(dr["CHARACTER_MAXIMUM_LENGTH"].ToString(), out int len))
                {
                    if (len > 0)
                    {
                        p.Size = len;
                    }
                    else
                    {
                        if (len <= 0)
                        {
                            p.Size = -1;
                        }
                    }
                }
                else
                {
                    switch (dr["TYPE_NAME"].ToString().ToLower())
                    {
                        case "xml":
                        case "sql_variant":
                        case "binary":
                            p.Size = -1;
                            break;
                    }
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

        public MSSQLCommand MapParameters(IList<object?> parms)
        {
            BuildParameters();

            // We will do this positionally - skip any fields that are strictly framework managed
            // We can do no name mapping since we have no source names!
            int idx = 0;
            foreach (var p in (from a in _cmd.Parameters.Cast<SqlParameter>() select new { Name = a.ParameterName.StartsWith("@") ? a.ParameterName.Substring(1) : a.ParameterName, Parm = a }))
            {
                if (p.Parm.Direction != ParameterDirection.ReturnValue && string.Compare(p.Name, MSSQLProcBasedProvider.ProcedureMessageParameter, true) != 0 && string.Compare(p.Name, MSSQLProcBasedProvider.ProcedureRetValParameter,true) != 0)
                {
                    if (p.Parm.IsNullable)
                    {
                        p.Parm.Value = parms[idx];
                    }
                    else
                    {
                        p.Parm.Value = parms[idx] ?? DBNull.Value;
                    }

                    idx++;
                }
            }

            return this;
        }

        public MSSQLCommand MapParameters(Type baseType, object forObject, IDictionary<string, object?> parms)
        {
            BuildParameters();

            var db = CEF.CurrentDBService(forObject);

            // The procedure signature is king - map underlying object to it, using and supplied name translations in scope
            foreach (var p in (from a in _cmd.Parameters.Cast<SqlParameter>()
                               let sn = a.ParameterName.StartsWith("@") ? a.ParameterName.Substring(1) : a.ParameterName
                               let pn = db?.GetPropertyNameFromStorageName(baseType, sn) ?? sn
                               let hasVal = parms.ContainsKey(pn)
                               where hasVal || UseNullForMissingValues
                               select new { Parm = a, Value = hasVal ? parms[pn] : null, Name = pn }))
            {
                var val = p.Value;

                if (val != null)
                {
                    if (val.GetType().Equals(typeof(DateTime)))
                    {
                        switch (ServiceScope.ResolvedDateStorageForTypeAndProperty(baseType, p.Name))
                        {
                            case PropertyDateStorage.TwoWayConvertUtc:
                                val = ((DateTime)val).ToUniversalTime();
                                break;

                            case PropertyDateStorage.TwoWayConvertUtcOnlyWithTime:
                                if (((DateTime)val).TimeOfDay.TotalMilliseconds != 0)
                                {
                                    val = ((DateTime)val).ToUniversalTime();
                                }
                                break;
                        }
                    }
                }

                p.Parm.Value = val ?? DBNull.Value;
            }

            return this;
        }

        public IEnumerable<Dictionary<string, (object? value, Type type)>> ExecuteReadRows()
        {
            using var da = new SqlDataAdapter(_cmd);
            using var r = _cmd.ExecuteReader();

            while (r.Read())
            {
                Dictionary<string, (object?, Type)> values = new(Globals.DefaultLargerDictionaryCapacity);

                for (int i = 0; i < r.FieldCount; ++i)
                {
                    var prefType = r.GetFieldType(i);
                    object? val = r.GetValue(i);

                    if (DBNull.Value.Equals(val))
                    {
                        val = null;
                    }

                    var name = r.GetName(i);
                    values[name] = (val, prefType);
                }

                yield return values;
            }
        }

        public IDBProviderCommand ExecuteNoResultSet()
        {
            CEFDebug.DumpSQLCall(_cmd.CommandText, this.GetParameterValues());

            try
            {
                // Should never be closed, but just in case
                if (_cmd.Connection.State == ConnectionState.Closed)
                {
                    _cmd.Connection.Open();
                }

                _cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                CEFDebug.WriteInfo($"SQL Call Error: " + ex.Message);
                throw;
            }

            return this;
        }

        public IEnumerable<(string name, object? value)> GetOutputValues()
        {
            return (from a in _cmd.Parameters.Cast<SqlParameter>()
                    where a.Direction == ParameterDirection.Output || a.Direction == ParameterDirection.InputOutput
                    let pn = a.ParameterName.StartsWith("@") ? a.ParameterName.Substring(1) : a.ParameterName
                    select (pn, a.Value));
        }

        public IDictionary<string, Type> GetResultSetShape()
        {
            using var da = new SqlDataAdapter(_cmd);
            DataTable dt = new();
            da.Fill(dt);

            Dictionary<string, Type> ret = new();

            foreach (DataColumn dc in dt.Columns)
            {
                ret[dc.ColumnName] = dc.DataType;
            }

            return ret;
        }
    }
}
