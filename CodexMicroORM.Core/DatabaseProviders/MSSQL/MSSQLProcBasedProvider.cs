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
using System.Text;
using System.Threading.Tasks;
using System.Data.SqlClient;
using CodexMicroORM.Core;
using CodexMicroORM.Core.Services;
using System.Collections.Concurrent;
using System.Data;
using System.Text.RegularExpressions;

namespace CodexMicroORM.Providers
{
    /// <summary>
    /// Implements the functionality expected by CEF for dealing with high-level database operations, for MS SQL Server.
    /// </summary>
    public class MSSQLProcBasedProvider : IDBProvider
    {
        private const string DEFAULT_DB_SCHEMA = "dbo";

        private static ConcurrentDictionary<string, string> _csMap = new ConcurrentDictionary<string, string>();

        private enum CommandType
        {
            Insert = 0,
            Update = 1,
            Delete = 2,
            RetrieveAll = 3,
            RetrieveByKey = 4,
            RetrieveByProc = 5,
            RetrieveByText = 6
        }

        public enum ProcReturnValue
        {
            Success = 1,
            Warning = 2,
            Failure = 3,
            Information = 4,
            SQLError = 5
        }

        #region "Global settings"

        // These global settings control the interaction of the framework with the data layer - e.g. naming conventions of procedures, optional standard status parameters, etc.

        public static string ProcedurePrefix
        {
            get;
            set;
        } = "up_";

        public static string InsertPrefix
        {
            get;
            set;
        }

        public static string UpdatePrefix
        {
            get;
            set;
        }

        public static string DeletePrefix
        {
            get;
            set;
        }

        public static string InsertSuffix
        {
            get;
            set;
        } = "_i";

        public static string UpdateSuffix
        {
            get;
            set;
        } = "_u";

        public static string DeleteSuffix
        {
            get;
            set;
        } = "_d";

        public static string ByKeyPrefix
        {
            get;
            set;
        }

        public static string ByKeySuffix
        {
            get;
            set;
        } = "_ByKey";

        public static string ForListSuffix
        {
            get;
            set;
        } = "_ForList";

        public static string ProcedureMessageParameter
        {
            get;
            set;
        } = "Msg";

        public static string ProcedureRetValParameter
        {
            get;
            set;
        } = "RetVal";

        public static string DefaultSchema
        {
            get;
            set;
        } = DEFAULT_DB_SCHEMA;

        #endregion

        public MSSQLProcBasedProvider(string connString, string name = "default", string defaultSchema = "dbo")
        {
            _csMap[name] = connString;
            DefaultSchema = defaultSchema;

            // If our connection string does not enable MARS, we should not allow parallel save operations!
            if (!Regex.IsMatch(connString, @"MultipleActiveResultSets\s*=\s*true", RegexOptions.IgnoreCase))
            {
                Globals.DefaultDBSaveDOP = 1;
            }
        }

        public class ListItem
        {
            public ListItem Next;
            public int ID;
        }

        public T ExecuteScalar<T>(ConnectionScope cs, string cmdText)
        {
            var firstRow = CreateRawCommand((MSSQLConnection)cs.CurrentConnection, System.Data.CommandType.Text, cmdText, null).ExecuteReadRows().FirstOrDefault();

            if (firstRow != null && firstRow.Count > 0)
            {
                return (T)Convert.ChangeType(firstRow.First().Value.value, typeof(T));
            }

            return default(T);
        }

        public void ExecuteRaw(ConnectionScope cs, string cmdText, bool doThrow = true, bool stopOnError = true)
        {
            Exception lastEx = null;

            // Split into multiple commands if GO separated
            foreach (Match mat in Regex.Matches(cmdText, @"(?<t>(?:.|\n)+?)(?:[\n\r]GO[\n\r]|\s*$)", RegexOptions.IgnoreCase))
            {
                try
                {
                    CreateRawCommand((MSSQLConnection)cs.CurrentConnection, System.Data.CommandType.Text, mat.Groups["t"].Value, null).ExecuteNoResultSet();
                }
                catch (Exception ex)
                {
                    lastEx = ex;

                    if (stopOnError)
                    {
                        break;
                    }
                }
            }

            if (lastEx != null && doThrow)
            {
                throw lastEx;
            }
        }

        public IDBProviderConnection CreateOpenConnection(string config = "default", bool transactional = true, string connStringOverride = null)
        {
            if (connStringOverride == null && !_csMap.ContainsKey(config))
            {
                throw new CEFInvalidOperationException($"Connection string {config} is not recognized / was not registered.");
            }

            var connString = connStringOverride ?? _csMap[config];
            var conn = new SqlConnection(connString);
            conn.Open();

            SqlTransaction tx = null;

            if (transactional)
            {
                tx = conn.BeginTransaction();
            }

            return new MSSQLConnection(conn, tx);
        }

        private MSSQLCommand CreateRawCommand(MSSQLConnection conn, System.Data.CommandType cmdType, string cmdText, IList<object> parms)
        {
            var cmd = new MSSQLCommand(conn, cmdText, cmdType);
            return cmd.MapParameters(parms);
        }

        private MSSQLCommand CreateProcCommand(MSSQLConnection conn, CommandType cmdType, string schemaName, string objName, ICEFInfraWrapper row, IList<object> parms)
        {
            StringBuilder proc = new StringBuilder(128);

            if (!string.IsNullOrEmpty(schemaName))
            {
                proc.Append($"[{schemaName}].");
            }

            switch (cmdType)
            {
                case CommandType.Insert:
                    proc.Append($"[{ProcedurePrefix}{InsertPrefix}{objName}{InsertSuffix}]");
                    break;
                case CommandType.Update:
                    proc.Append($"[{ProcedurePrefix}{UpdatePrefix}{objName}{UpdateSuffix}]");
                    break;
                case CommandType.Delete:
                    proc.Append($"[{ProcedurePrefix}{DeletePrefix}{objName}{DeleteSuffix}]");
                    break;
                case CommandType.RetrieveAll:
                    proc.Append($"[{ProcedurePrefix}{objName}{ForListSuffix}]");
                    break;
                case CommandType.RetrieveByKey:
                    proc.Append($"[{ProcedurePrefix}{objName}{ByKeySuffix}]");
                    break;
                case CommandType.RetrieveByProc:
                    proc.Append($"[{objName}]");
                    break;
            }

            var cmd = new MSSQLCommand(conn, proc.ToString(), System.Data.CommandType.StoredProcedure);

            if (row != null)
            {
                cmd.MapParameters(row.GetAllValues());
            }
            else
            {
                if (parms != null)
                {
                    cmd.MapParameters(parms);
                }
            }

            return cmd;
        }

        private void InsertRowsWithBulk(ConnectionScope conn, IEnumerable<ICEFInfraWrapper> rows, string schema, string name)
        {
            schema = schema ?? DefaultSchema ?? DEFAULT_DB_SCHEMA;

            using (DataTable dt = new DataTable())
            {
                // Issue independent SELECT * to get schema from underlying table
                using (var discoverConn = new SqlConnection(((MSSQLConnection)conn.CurrentConnection).CurrentConnection.ConnectionString))
                {
                    discoverConn.Open();

                    using (var da = new SqlDataAdapter($"SELECT * FROM [{schema}].[{name}] WHERE 1=0", discoverConn))
                    {
                        da.Fill(dt);
                    }
                }

                // Remove PK if DB-assigned, along with LastUpdatedBy/Date if DB assigned
                if (!string.IsNullOrEmpty(AuditService.LastUpdatedByField) && AuditService.IsLastUpdatedByDBAssigned && dt.Columns.Contains(AuditService.LastUpdatedByField))
                {
                    dt.Columns.Remove(AuditService.LastUpdatedByField);
                }

                if (!string.IsNullOrEmpty(AuditService.LastUpdatedDateField) && AuditService.IsLastUpdatedDateDBAssigned && dt.Columns.Contains(AuditService.LastUpdatedDateField))
                {
                    dt.Columns.Remove(AuditService.LastUpdatedDateField);
                }

                if (KeyService.DefaultPrimaryKeysCanBeDBAssigned)
                {
                    var rowType = rows.FirstOrDefault()?.GetBaseType();

                    if (rowType != null)
                    {
                        foreach (string pkf in KeyService.ResolveKeyDefinitionForType(rowType))
                        {
                            if (dt.Columns.Contains(pkf))
                            {
                                dt.Columns.Remove(pkf);
                            }
                        }
                    }
                }

                dt.BeginLoadData();

                foreach (var r in rows)
                {
                    var data = r.GetAllValues();
                    var dr = dt.NewRow();

                    foreach (DataColumn dc in dt.Columns)
                    {
                        if (data.ContainsKey(dc.ColumnName))
                        {
                            dr[dc] = data[dc.ColumnName] ?? DBNull.Value;
                        }
                    }

                    dt.Rows.Add(dr);
                    r.AcceptChanges();
                }

                dt.EndLoadData();

                using (SqlBulkCopy sbc = new SqlBulkCopy(((MSSQLConnection)conn.CurrentConnection).CurrentConnection, SqlBulkCopyOptions.Default, ((MSSQLConnection)conn.CurrentConnection).CurrentTransaction))
                {
                    sbc.DestinationTableName = $"[{schema}].[{name}]";
                    sbc.ColumnMappings.Clear();

                    foreach (DataColumn dc in dt.Columns)
                    {
                        sbc.ColumnMappings.Add(dc.ColumnName, dc.ColumnName);
                    }

                    sbc.WriteToServer(dt);
                }
            }
        }

        /// <summary>
        /// It's up to the caller to partition the rows into a saveable sequence (if there are multiple rows here, they can all be saved in parallel, it is assumed).
        /// </summary>
        /// <param name="ss">Service scope rows apply to.</param>
        /// <param name="conn">Connection scope to use for calls.</param>
        /// <param name="rows">List of rows to save.</param>
        /// <param name="cmdType">Nature of the command being issued.</param>
        /// <param name="settings">Database save settings for this request.</param>
        /// <returns>A compatible number of "rows" as input with per row save status.</returns>
        private IEnumerable<(ICEFInfraWrapper row, string msg, int status)> SaveRows(ServiceScope ss, ConnectionScope conn, IEnumerable<(string schema, string name, ICEFInfraWrapper row)> rows, CommandType cmdType, DBSaveSettings settings)
        {
            ConcurrentBag<(ICEFInfraWrapper row, string msg, int status)> rowsOut = new ConcurrentBag<(ICEFInfraWrapper row, string msg, int status)>();
            Exception stopEx = null;

            var materialized = (from a in rows select new { Schema = a.schema ?? DefaultSchema ?? DEFAULT_DB_SCHEMA, Name = a.name, Row = a.row }).ToList();

            Parallel.ForEach(materialized, new ParallelOptions() { MaxDegreeOfParallelism = settings.MaxDegreeOfParallelism }, (r, pls) =>
            {
                using (CEF.UseServiceScope(ss))
                {
                    string msg = null;
                    int status = 0;

                    try
                    {
                        foreach (var v in CreateProcCommand((MSSQLConnection)conn.CurrentConnection, cmdType, r.Schema, r.Name, r.Row, null).ExecuteNoResultSet().GetOutputValues())
                        {
                            if (string.Compare(v.name, ProcedureMessageParameter, true) == 0)
                            {
                                msg = v.value?.ToString();
                            }
                            else
                            {
                                if (string.Compare(v.name, ProcedureRetValParameter, true) == 0)
                                {
                                    if (int.TryParse(v.value?.ToString(), out int retval))
                                    {
                                        status = retval;
                                    }
                                }
                                else
                                {
                                    r.Row.SetValue(v.name, v.value);
                                }
                            }
                        }

                        r.Row.AcceptChanges();
                    }
                    catch (Exception ex)
                    {
                        if (!conn.ContinueOnError)
                        {
                            pls.Break();
                            stopEx = ex;
                            return;
                        }

                        msg = ex.Message;
                        status = (int)ProcReturnValue.SQLError;
                    }

                    rowsOut.Add((r.Row, msg, status));
                }
            });

            materialized = null;

            if (stopEx != null)
            {
                throw stopEx;
            }

            return rowsOut;
        }

        IEnumerable<(ICEFInfraWrapper row, string msg, int status)> IDBProvider.InsertRows(ConnectionScope conn, IEnumerable<(int level, IEnumerable<(string schema, string name, ICEFInfraWrapper row)> rows)> rows, DBSaveSettings settings)
        {
            List<(ICEFInfraWrapper row, string msg, int status)> retVal = new List<(ICEFInfraWrapper row, string msg, int status)>();

            // Apply sequential break by level, inserts are ascending level counts
            foreach (var level in (from a in rows orderby a.level select a))
            {
                if (settings.BulkInsertRules != BulkRules.Never)
                {
                    // Need to further partition by name since we judge thresholds, etc. by name
                    foreach (var byname in (from a in rows from b in a.rows group b by new { Level = a.level, Name = b.name, Schema = b.schema } into g orderby g.Key.Level select new { g.Key.Level, g.Key.Schema, g.Key.Name, Rows = g, RowsRaw = g.Select((p) => p.row) }))
                    {
                        if ((settings.BulkInsertRules & BulkRules.Always) != 0 ||
                            (
                                ((settings.BulkInsertRules & BulkRules.Threshold) == 0 || (level.rows.Count() >= settings.BulkInsertMinimumRows))
                                && ((settings.BulkInsertRules & BulkRules.LeafOnly) == 0 || (level.level == (from a in rows select a.level).Max()))
                                && ((settings.BulkInsertRules & BulkRules.ByType) == 0 || (from a in settings.BulkInsertTypes where string.Compare(byname.Name, a.Name, true) == 0 select a).Any())
                            ))
                        {
                            // Bulk inserts uses table names/schemas
                            InsertRowsWithBulk(conn, byname.RowsRaw, byname.Schema, byname.Name);
                        }
                        else
                        {
                            retVal.AddRange(SaveRows(CEF.CurrentServiceScope, conn, byname.Rows, CommandType.Insert, settings));
                        }
                    }
                }
                else
                {
                    retVal.AddRange(SaveRows(CEF.CurrentServiceScope, conn, level.rows, CommandType.Insert, settings));
                }
            }

            return retVal;
        }

        IEnumerable<(ICEFInfraWrapper row, string msg, int status)> IDBProvider.UpdateRows(ConnectionScope conn, IEnumerable<(int level, IEnumerable<(string schema, string name, ICEFInfraWrapper row)> rows)> rows, DBSaveSettings settings)
        {
            List<(ICEFInfraWrapper row, string msg, int status)> retVal = new List<(ICEFInfraWrapper row, string msg, int status)>();

            // Apply sequential break by level, updates are ascending level counts
            foreach (var rowsforlevel in (from a in rows orderby a.level select a.rows))
            {
                retVal.AddRange(SaveRows(CEF.CurrentServiceScope, conn, rowsforlevel, CommandType.Update, settings));
            }

            return retVal;
        }

        IEnumerable<(ICEFInfraWrapper row, string msg, int status)> IDBProvider.DeleteRows(ConnectionScope conn, IEnumerable<(int level, IEnumerable<(string schema, string name, ICEFInfraWrapper row)> rows)> rows, DBSaveSettings settings)
        {
            List<(ICEFInfraWrapper row, string msg, int status)> retVal = new List<(ICEFInfraWrapper row, string msg, int status)>();

            // Apply sequential break by level, deletes are descending level counts
            foreach (var rowsforlevel in (from a in rows orderby a.level descending select a.rows))
            {
                retVal.AddRange(SaveRows(CEF.CurrentServiceScope, conn, rowsforlevel, CommandType.Delete, settings));
            }

            return retVal;
        }

        private IEnumerable<T> CommonRetrieveRows<T>(ConnectionScope conn, bool doWrap, string cmdText, CommandType type, IList<object> parms) where T: class, new()
        {
            // Why do this? "wo" might end up telling us an explicit (overridden) schema/name
            var no = new T();
            var wo = WrappingHelper.CreateWrapper(WrappingSupport.PropertyBag | WrappingSupport.OriginalValues | WrappingSupport.Notifications, Globals.DefaultWrappingAction, false, no, CEF.CurrentServiceScope);

            string schema = wo == null ? DefaultSchema ?? DEFAULT_DB_SCHEMA : wo.GetSchemaName() ?? DEFAULT_DB_SCHEMA;
            string name = wo == null ? no.GetBaseType().Name : wo.GetBaseType().Name;

            MSSQLCommand sqlcmd = null;

            if (type == CommandType.RetrieveByText)
            {
                sqlcmd = CreateRawCommand((MSSQLConnection)conn.CurrentConnection, System.Data.CommandType.Text, cmdText, parms);
            }
            else
            {
                if (type == CommandType.RetrieveByProc)
                {
                    var sn = MSSQLCommand.SplitIntoSchemaAndName(cmdText);
                    
                    if (!string.IsNullOrEmpty(sn.schema))
                    {
                        schema = sn.schema;
                    }

                    name = sn.name;
                }

                sqlcmd = CreateProcCommand((MSSQLConnection)conn.CurrentConnection, type, schema, name, null, parms);
            }

            foreach (var row in sqlcmd.ExecuteReadRows())
            {
                if (doWrap)
                {
                    // If "the same" object exists in current scope, this will "merge" it with new values, avoids duplicating it in scope
                    no = CEF.CurrentServiceScope.IncludeObjectWithType<T>(new T(), ObjectState.Unchanged, row);
                }
                else
                {
                    var propVals = new Dictionary<string, object>();

                    foreach (var kvp in row)
                    {
                        propVals[kvp.Key] = kvp.Value.value;
                    }

                    no = new T();
                    WrappingHelper.CopyParsePropertyValues(propVals, null, no, false, null, new ConcurrentDictionary<object, object>());
                }

                yield return no;
            }
        }

        IEnumerable<T> IDBProvider.RetrieveAll<T>(ConnectionScope conn, bool doWrap) 
        {
            return CommonRetrieveRows<T>(conn, doWrap, null, CommandType.RetrieveAll, null);
        }

        IEnumerable<T> IDBProvider.RetrieveByKey<T>(ConnectionScope conn, bool doWrap, object[] key)
        {
            return CommonRetrieveRows<T>(conn, doWrap, null, CommandType.RetrieveByKey, key);
        }

        IEnumerable<T> IDBProvider.RetrieveByQuery<T>(ConnectionScope conn, bool doWrap, System.Data.CommandType cmdType, string cmdText, object[] parms) 
        {
            return CommonRetrieveRows<T>(conn, doWrap, cmdText, cmdType == System.Data.CommandType.StoredProcedure ? CommandType.RetrieveByProc : CommandType.RetrieveByText, parms);
        }
    }
}
