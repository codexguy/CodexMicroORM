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
12/2017    0.2     Initial release (Joel Champagne)
***********************************************************************/
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using CodexMicroORM.Core;
using CodexMicroORM.Core.Services;
using System.Collections.Concurrent;
using System.Data;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Threading;

namespace CodexMicroORM.Providers
{
    /// <summary>
    /// Implements the functionality expected by CEF for dealing with high-level database operations, for MS SQL Server.
    /// </summary>
    public sealed class MSSQLProcBasedProvider : IDBProvider
    {
        private const string DEFAULT_DB_SCHEMA = "dbo";
        private const string SAVE_RETRY_MESSAGE_REGEX = @"successfully\sestablished|timeout|is\sbroken|occurred\son\sthe\scurrent\scommand|Connection\swas\sterminated|requires\san\sopen\sand\savailable|Collection\salready\scontains|connection\sis\s(?:closed|not\susable)";
        private const string OPEN_RETRY_MESSAGE_REGEX = @"timeout\sexpired|\spool\s|successfully\sestablished|was\snot\sfound|was\snot\saccessible|not\scurrently\savailable|is\snot\susable|please\sretry|too\sbusy";

        public delegate void RowActionPreviewCallback(CommandType action, string objname, ICEFInfraWrapper row);
        public delegate void SetActionPreviewCallback(DBSaveSettings saveSettings, IEnumerable<ICEFInfraWrapper> rows, bool? success);

        private static long _dbSaveTime = 0;
        private static long _delayedTime = 0;
        private static long _saveCounter = 0;

        public static long DelayedTime
        {
            get
            {
                return Interlocked.Read(ref _delayedTime);
            }
        }

        public static long DatabaseTime
        {
            get
            {
                return Interlocked.Read(ref _dbSaveTime);
            }
        }

        public static double? AverageDatabaseTime
        {
            get
            {
                var sc = Interlocked.Read(ref _saveCounter);

                if (sc > 0)
                {
                    return 1.0 * Interlocked.Read(ref _dbSaveTime) / Interlocked.Read(ref _saveCounter);
                }

                return null;
            }
        }

#if DEBUG
        public string ID = Guid.NewGuid().ToString();
#endif

        public static SetActionPreviewCallback? GlobalSetActionPreview
        {
            get;
            set;
        } = null;

        public static RowActionPreviewCallback? GlobalRowActionPreview
        {
            get;
            set;
        } = null;

        public static Action<string, string, int>? GlobalRetryHandler
        {
            get;
            set;
        } = null;

        public static int? SaveRetryCount
        {
            get;
            set;
        }

        public static int? OpenRetryCount
        {
            get;
            set;
        }

        public static int SaveRetryDelayMs
        {
            get;
            set;
        } = 2000;

        public static int OpenRetryDelayMs
        {
            get;
            set;
        } = 3000;

        private readonly static ConcurrentDictionary<string, string> _csMap = new(Globals.DefaultCollectionConcurrencyLevel, Globals.DefaultDictionaryCapacity, Globals.CurrentStringComparer);

        public enum CommandType
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

        public static string? ProcedurePrefix
        {
            get;
            set;
        } = "up_";

        public static string? InsertPrefix
        {
            get;
            set;
        }

        public static string? UpdatePrefix
        {
            get;
            set;
        }

        public static string? DeletePrefix
        {
            get;
            set;
        }

        public static string? InsertSuffix
        {
            get;
            set;
        } = "_i";

        public static string? UpdateSuffix
        {
            get;
            set;
        } = "_u";

        public static string? DeleteSuffix
        {
            get;
            set;
        } = "_d";

        public static string? ByKeyPrefix
        {
            get;
            set;
        }

        public static string? ByKeySuffix
        {
            get;
            set;
        } = "_ByKey";

        public static string? ForListSuffix
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

        public static string? DefaultSchema
        {
            get;
            set;
        } = DEFAULT_DB_SCHEMA;

        #endregion

        public int? CommandTimeout
        {
            get;
            set;
        } = null;

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

        public T ExecuteScalar<T>(ConnectionScope cs, string cmdText)
        {
            var firstRow = CreateRawCommand((MSSQLConnection)cs.CurrentConnection, System.Data.CommandType.Text, cmdText, null, cs.TimeoutOverride).ExecuteReadRows().FirstOrDefault();

            if (firstRow != null && firstRow.Count > 0)
            {
                return (T)Convert.ChangeType(firstRow.First().Value.value, typeof(T));
            }

            return default!;
        }

        public void ExecuteRaw(ConnectionScope cs, string cmdText, bool doThrow = true, bool stopOnError = true)
        {
            Exception? lastEx = null;

            // Split into multiple commands if GO separated
            foreach (Match mat in Regex.Matches(cmdText, @"(?<t>(?:.|\n)+?)(?:[\n\r]GO[\n\r]|\s*$)", RegexOptions.IgnoreCase))
            {
                try
                {
                    CreateRawCommand((MSSQLConnection)cs.CurrentConnection, System.Data.CommandType.Text, mat.Groups["t"].Value, null, cs.TimeoutOverride).ExecuteNoResultSet();
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

        public IDBProviderConnection CreateOpenConnection(string config = "default", bool transactional = true, string? connStringOverride = null, int? timeoutOverride = null)
        {
            string? cs = null;
            CommandTimeout = timeoutOverride;

            if (connStringOverride == null && !_csMap.TryGetValue(config, out cs))
            {
                throw new CEFInvalidStateException(InvalidStateType.SQLLayer, $"Connection token {config} is not recognized / was not registered.");
            }

            var connString = connStringOverride ?? cs ?? throw new CEFInvalidStateException(InvalidStateType.SQLLayer, $"Connection token {config} is not recognized / was not registered.");

            int trycnt = OpenRetryCount.GetValueOrDefault(0) + 1;
            SqlConnection conn;
            int trydelay = 0;

            while (1 == 1)
            {
                try
                {
                    if (trydelay > 0)
                    {
                        if (trydelay > 60000)
                        {
                            trydelay = 60000;
                        }

                        Thread.Sleep(trydelay);
                    }

                    conn = new SqlConnection(connString);
                    conn.Open();
                    break;
                }
                catch (Exception ex)
                {
                    --trycnt;

                    if (trycnt == 0 || !Regex.IsMatch(ex.Message, OPEN_RETRY_MESSAGE_REGEX, RegexOptions.IgnoreCase))
                    {
#if DEBUG
                        if (System.Diagnostics.Debugger.IsAttached)
                        {
                            System.Diagnostics.Debugger.Break();
                        }
#endif
                        throw;
                    }
                    else
                    {
                        SqlConnection.ClearAllPools();

                        if (trydelay == 0)
                        {
                            trydelay = OpenRetryDelayMs;
                        }
                        else
                        {
                            trydelay *= 2;
                        }
                    }
                }
            }

            SqlTransaction? tx = null;

            if (transactional)
            {
                tx = conn.BeginTransaction();
            }

            return new MSSQLConnection(conn, tx);
        }

        private MSSQLCommand CreateRawCommand(MSSQLConnection conn, System.Data.CommandType cmdType, string cmdText, IList<object?>? parms, int? timeoutOverride)
        {
            var cmd = new MSSQLCommand(conn, cmdText, cmdType, timeoutOverride ?? CommandTimeout);
            
            if (parms != null)
            {
                return cmd.MapParameters(parms);
            }

            return cmd;
        }

        private MSSQLCommand CreateProcCommand(MSSQLConnection conn, CommandType cmdType, string? schemaName, string objName, ICEFInfraWrapper? row, IList<object?>? parms, int? timeoutOverride)
        {
            string proc = "";

            if (!string.IsNullOrEmpty(schemaName))
            {
                proc = string.Concat("[", schemaName, "].");
            }

            switch (cmdType)
            {
                case CommandType.Insert:
                    proc = string.Concat(proc, "[", ProcedurePrefix, InsertPrefix, objName, InsertSuffix, "]");
                    break;
                case CommandType.Update:
                    proc = string.Concat(proc, "[", ProcedurePrefix, UpdatePrefix, objName, UpdateSuffix, "]");
                    break;
                case CommandType.Delete:
                    proc = string.Concat(proc, "[", ProcedurePrefix, DeletePrefix, objName, DeleteSuffix, "]");
                    break;
                case CommandType.RetrieveAll:
                    proc = string.Concat(proc, "[", ProcedurePrefix, objName, ForListSuffix, "]");
                    break;
                case CommandType.RetrieveByKey:
                    proc = string.Concat(proc, "[", ProcedurePrefix, objName, ByKeySuffix, "]");
                    break;
                case CommandType.RetrieveByProc:
                    proc = string.Concat(proc, "[", objName, "]");
                    break;
            }

            var cmd = new MSSQLCommand(conn, proc, System.Data.CommandType.StoredProcedure, timeoutOverride ?? CommandTimeout);

            if (row != null)
            {
                cmd.MapParameters(row.GetBaseType(), row.AsUnwrapped() ?? throw new CEFInvalidStateException(InvalidStateType.ObjectTrackingIssue), row.GetAllValues());
            }
            else
            {
                if (parms != null)
                {
                    cmd.MapParameters(parms);
                }
            }

            if (Globals.DeepLogger != null)
            {
                try
                {
                    Globals.DeepLogger(cmd.ToString());
                }
                catch
                {
                    // Not ideal, but logging failures should not blow up whole app!
                }
            }

            return cmd;
        }

        private void InsertRowsWithBulk(ConnectionScope conn, IEnumerable<ICEFInfraWrapper> rows, string? schema, string name, DBSaveSettings settings)
        {
            schema ??= DefaultSchema ?? DEFAULT_DB_SCHEMA;

            using DataTable dt = new();

            // Issue independent SELECT * to get schema from underlying table
            var cc = ((ICloneable?)((MSSQLConnection)conn.CurrentConnection).CurrentConnection) ?? throw new CEFInvalidStateException(InvalidStateType.SQLLayer, "Missing current data connection.");

            using (var discoverConn = (SqlConnection)cc.Clone())
            {
                discoverConn.Open();
                using var da = new SqlDataAdapter($"SELECT * FROM [{schema}].[{name}] WHERE 1=0", discoverConn);
                da.Fill(dt);
            }

            var aud = CEF.CurrentServiceScope.GetService<ICEFAuditHost>();

            if (aud != null)
            {
                // Remove PK if DB-assigned, along with LastUpdatedBy/Date if DB assigned
                if (!string.IsNullOrEmpty(aud.LastUpdatedByField) && aud.IsLastUpdatedByDBAssigned && dt.Columns.Contains(aud.LastUpdatedByField))
                {
                    dt.Columns.Remove(aud.LastUpdatedByField);
                }

                if (!string.IsNullOrEmpty(aud.LastUpdatedDateField) && aud.IsLastUpdatedDateDBAssigned && dt.Columns.Contains(aud.LastUpdatedDateField))
                {
                    dt.Columns.Remove(aud.LastUpdatedDateField);
                }
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

                if (!settings.NoAcceptChanges)
                {
                    if (settings.DeferAcceptChanges.GetValueOrDefault(conn.IsTransactional))
                    {
                        lock (conn.ToAcceptList)
                        {
                            conn.ToAcceptList.Add(r);
                        }
                    }
                    else
                    {
                        r.AcceptChanges();
                    }
                }
            }

            dt.EndLoadData();

            using SqlBulkCopy sbc = new(((MSSQLConnection)conn.CurrentConnection).CurrentConnection, SqlBulkCopyOptions.Default, ((MSSQLConnection)conn.CurrentConnection).CurrentTransaction)
            {
                DestinationTableName = $"[{schema}].[{name}]"
            };

            sbc.ColumnMappings.Clear();

            foreach (DataColumn dc in dt.Columns)
            {
                sbc.ColumnMappings.Add(dc.ColumnName, dc.ColumnName);
            }

            sbc.WriteToServer(dt);
        }

        IEnumerable<(string name, object? value)>? IDBProvider.ExecuteNoResultSet(ConnectionScope conn, System.Data.CommandType cmdType, string cmdText, params object?[] parms)
        {
            var sn = cmdText.SplitIntoSchemaAndName();

            var schema = DefaultSchema ?? DEFAULT_DB_SCHEMA;

            if (!string.IsNullOrEmpty(sn.schema))
            {
                schema = sn.schema;
            }

            IEnumerable<(string name, object? value)>? outVar;

            if (cmdType == System.Data.CommandType.StoredProcedure)
            {
                outVar = CreateProcCommand((MSSQLConnection)conn.CurrentConnection, CommandType.RetrieveByProc, schema, sn.name, null, parms, conn.TimeoutOverride).ExecuteNoResultSet().GetOutputValues();
            }
            else
            {
                outVar = CreateRawCommand((MSSQLConnection)conn.CurrentConnection, cmdType, cmdText, parms, conn.TimeoutOverride).ExecuteNoResultSet().GetOutputValues();
            }

            if (outVar != null)
            {
                conn.LastOutputVariables.Clear();

                foreach (var (name, value) in outVar)
                {
                    conn.LastOutputVariables[name] = value;
                }
            }

            return outVar;
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
        private IEnumerable<(ICEFInfraWrapper row, string? msg, int status)> SaveRows(ServiceScope ss, ConnectionScope conn, IEnumerable<(string schema, string name, Type basetype, ICEFInfraWrapper row)> rows, CommandType cmdType, DBSaveSettings settings)
        {
            ConcurrentBag<(ICEFInfraWrapper row, string? msg, int status)> rowsOut = new();
            Exception? stopEx = null;

            // It's a problem to be doing retries in a transaction!
            if (conn.IsTransactional && SaveRetryCount.GetValueOrDefault() > 0)
            {
                CEFDebug.WriteInfo("Ignoring SaveRetryCount for a transactional save.");
            }

            conn.LastOutputVariables.Clear();

            var materialized = (from a in rows select new { Schema = a.schema ?? DefaultSchema ?? DEFAULT_DB_SCHEMA, Name = a.name, Row = a.row }).ToList();
            long trydelay = 0;

            if (GlobalSetActionPreview != null)
            {
                // Neither success nor failure (yet) - invoke as a true preview
                GlobalSetActionPreview.Invoke(settings, from a in materialized select a.Row, null);
            }

            Parallel.ForEach(materialized, new ParallelOptions() { MaxDegreeOfParallelism = settings.MaxDegreeOfParallelism }, (r, pls) =>
            {
                using (CEF.UseServiceScope(ss))
                {
                    string? msg = null;
                    int status = 0;
                    int trycnt = conn.IsTransactional ? 1 : SaveRetryCount.GetValueOrDefault(0) + 1;
                    bool first = true;

                    while (1 == 1)
                    {
                        try
                        {
                            if (first)
                            {
                                first = false;

                                if (ss.RowActionPreviewEnabled)
                                {
                                    // An opportunity to possibly change row properties ahead of actual save
                                    GlobalRowActionPreview?.Invoke(cmdType, r.Name, r.Row);
                                }
                            }

                            // Every working thread should have to wait on any retry interval
                            var passdelay = Interlocked.Read(ref trydelay);

                            if (passdelay > 0)
                            {
                                if (passdelay > 60000)
                                {
                                    passdelay = 60000;
                                }

                                Thread.Sleep(Convert.ToInt32(passdelay));
                                Interlocked.Add(ref _delayedTime, passdelay);
                            }

                            Interlocked.Increment(ref _saveCounter);

                            var msconn = (MSSQLConnection)conn.CurrentConnection;
                            var pc = CreateProcCommand(msconn, cmdType, r.Schema, r.Name, r.Row, null, conn.TimeoutOverride);
                            var startDB = DateTime.Now;

                            var outVals = pc.ExecuteNoResultSet().GetOutputValues();

                            var dbTime = Convert.ToInt64(DateTime.Now.Subtract(startDB).TotalMilliseconds);
                            Interlocked.Add(ref _dbSaveTime, dbTime);

                            var doAccept = !settings.DeferAcceptChanges.GetValueOrDefault(false);
                            List<(string name, object? value)> toRB = new();

                            foreach (var (name, value) in outVals)
                            {
                                conn.LastOutputVariables[name] = value;

                                if (string.Compare(name, ProcedureMessageParameter, true) == 0)
                                {
                                    msg = value?.ToString();
                                }
                                else
                                {
                                    if (string.Compare(name, ProcedureRetValParameter, true) == 0)
                                    {
                                        if (int.TryParse(value?.ToString(), out int retval))
                                        {
                                            status = retval == 1 ? 0 : retval;
                                        }
                                    }
                                    else
                                    {
                                        if (!doAccept)
                                        {
                                            toRB.Add((name, r.Row.GetValue(name)));
                                        }

                                        r.Row.SetValue(name, value);
                                    }
                                }
                            }

                            if (!settings.NoAcceptChanges)
                            {
                                conn.ToRollbackList.Add((r.Row, r.Row.GetRowState(), toRB));

                                if (doAccept)
                                {
                                    r.Row.AcceptChanges();
                                }
                                else
                                {
                                    lock (conn.ToAcceptList)
                                    {
                                        conn.ToAcceptList.Add(r.Row);
                                    }
                                }
                            }

                            Interlocked.Exchange(ref trydelay, 0);
                            rowsOut.Add((r.Row, msg, status));
                            break;
                        }
                        catch (Exception ex)
                        {
//#if DEBUG
//                            if (System.Diagnostics.Debugger.IsAttached && ex.Message.Contains("The connection's current state is"))
//                            {
//                                System.Diagnostics.Debugger.Break();
//                            }
//#endif
                            --trycnt;

                            // Also check the exception type - only some exception types should flow into else!
                            if (trycnt == 0 || !Regex.IsMatch(ex.Message, SAVE_RETRY_MESSAGE_REGEX, RegexOptions.IgnoreCase))
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
                            else
                            {
                                // Reset connection such that next request for it should create a new connection
                                conn.ResetConnection(Regex.IsMatch(ex.Message, @"\spool\ssize", RegexOptions.IgnoreCase));

                                GlobalRetryHandler?.Invoke(r.GetBaseType().Name, ex.Message, trycnt);

                                if (Interlocked.Read(ref trydelay) == 0)
                                {
                                    Interlocked.Exchange(ref trydelay, SaveRetryDelayMs);
                                }
                                else
                                {
                                    Interlocked.Exchange(ref trydelay, Interlocked.Read(ref trydelay) * 2);
                                }
                            }
                        }
                    }
                }
            });

            if (GlobalSetActionPreview != null)
            {
                // Invoke to advise success or failure
                GlobalSetActionPreview.Invoke(settings, from a in materialized select a.Row, stopEx == null);
            }

            if (stopEx != null)
            {
                throw stopEx;
            }

            return rowsOut;
        }

        IEnumerable<(ICEFInfraWrapper row, string? msg, int status)> IDBProvider.InsertRows(ConnectionScope conn, IEnumerable<(string schema, string name, Type basetype, ICEFInfraWrapper row)> rows, bool isLeaf, DBSaveSettings settings)
        {
            List<(ICEFInfraWrapper row, string? msg, int status)> retVal = new();

            if (settings.BulkInsertRules == BulkRules.Never)
            {
                retVal.AddRange(SaveRows(CEF.CurrentServiceScope, conn, rows, CommandType.Insert, settings));
            }
            else
            {
                // Need to further partition by name since we judge thresholds, etc. by name
                foreach (var byname in (from a in rows group a by new { Name = a.name, Schema = a.schema } into g select new { g.Key.Schema, g.Key.Name, Rows = g, RowsRaw = (from cr in g select cr.row) }))
                {
                    if ((settings.BulkInsertRules & BulkRules.Always) != 0 ||
                        (
                            ((settings.BulkInsertRules & BulkRules.Threshold) == 0 || (byname.RowsRaw.Count() >= settings.BulkInsertMinimumRows))
                            && ((settings.BulkInsertRules & BulkRules.LeafOnly) == 0 || isLeaf)
                            && ((settings.BulkInsertRules & BulkRules.ByType) == 0 || (from a in settings.BulkInsertTypes where string.Compare(byname.Name, a.Name, true) == 0 select a).Any())
                        ))
                    {
                        // Bulk inserts uses table names/schemas
                        InsertRowsWithBulk(conn, byname.RowsRaw, byname.Schema, byname.Name, settings);
                    }
                    else
                    {
                        retVal.AddRange(SaveRows(CEF.CurrentServiceScope, conn, byname.Rows, CommandType.Insert, settings));
                    }
                }
            }

            return retVal;
        }

        IEnumerable<(ICEFInfraWrapper row, string? msg, int status)> IDBProvider.UpdateRows(ConnectionScope conn, IEnumerable<(string schema, string name, Type basetype, ICEFInfraWrapper row)> rows, DBSaveSettings settings)
        {
            List<(ICEFInfraWrapper row, string? msg, int status)> retVal = new();
            retVal.AddRange(SaveRows(CEF.CurrentServiceScope, conn, rows, CommandType.Update, settings));
            return retVal;
        }

        IEnumerable<(ICEFInfraWrapper row, string? msg, int status)> IDBProvider.DeleteRows(ConnectionScope conn, IEnumerable<(string schema, string name, Type basetype, ICEFInfraWrapper row)> rows, DBSaveSettings settings)
        {
            List<(ICEFInfraWrapper row, string? msg, int status)> retVal = new();
            retVal.AddRange(SaveRows(CEF.CurrentServiceScope, conn, rows, CommandType.Delete, settings));
            return retVal;
        }

        private IEnumerable<T> CommonRetrieveRows<T>(ICEFDataHost db, ConnectionScope conn, bool doWrap, string? cmdText, CommandType type, CEF.ColumnDefinitionCallback? cc, IList<object?>? parms) where T: class, new()
        {
            var ss = CEF.CurrentServiceScope;

            // Why do this? "wo" might end up telling us an explicit (overridden) schema/name
            var no = new T();
            var wo = WrappingHelper.CreateWrapper(false, no, ss);

            var schema = wo?.GetSchemaName() ?? db.GetSchemaNameByType(no.GetBaseType()) ?? DefaultSchema ?? DEFAULT_DB_SCHEMA;
            var name = db.GetEntityNameByType(no.GetBaseType(), wo);

            MSSQLCommand sqlcmd;
            var dbconn = (MSSQLConnection)conn.CurrentConnection;

            if (type == CommandType.RetrieveByText)
            {
                if (string.IsNullOrWhiteSpace(cmdText))
                {
                    throw new CEFInvalidStateException(InvalidStateType.SQLLayer, "Missing command text");
                }

                sqlcmd = CreateRawCommand(dbconn, System.Data.CommandType.Text, cmdText!, parms, conn.TimeoutOverride);
            }
            else
            {
                if (type == CommandType.RetrieveByProc)
                {
                    if (string.IsNullOrWhiteSpace(cmdText))
                    {
                        throw new CEFInvalidStateException(InvalidStateType.SQLLayer, "Missing command text");
                    }

                    var sn = cmdText.SplitIntoSchemaAndName();
                    
                    if (!string.IsNullOrEmpty(sn.schema))
                    {
                        schema = sn.schema;
                    }

                    name = sn.name;
                }

                sqlcmd = CreateProcCommand(dbconn, type, schema, name, null, parms, conn.TimeoutOverride);
            }

            try
            {
                dbconn.IncrementWorking();

                CEFDebug.DumpSQLCall(cmdText!, sqlcmd.GetParameterValues());

                bool fetchedOutput = false;
                bool hasData = false;
                HashSet<string>? dateFields = null;

                foreach (var row in sqlcmd.ExecuteReadRows())
                {
                    hasData = true;

                    if (!fetchedOutput)
                    {
                        fetchedOutput = true;

                        var outVals = sqlcmd.GetOutputValues();

                        if (outVals != null)
                        {
                            conn.LastOutputVariables.Clear();

                            foreach (var ov in outVals)
                            {
                                conn.LastOutputVariables[ov.name] = ov.value;
                            }
                        }
                    }

                    // Handle any possible date translation now - to be efficient, we build a list of candidate fields to handle instead of looking at every cell from every row!
                    if (dateFields == null)
                    {
                        dateFields = new HashSet<string>();

                        foreach (var kvp in row)
                        {
                            if (kvp.Value.type == typeof(DateTime))
                            {
                                dateFields.Add(kvp.Key);
                            }
                        }
                    }

                    foreach (string df in dateFields)
                    {
                        if (row[df].value != null)
                        {
                            var dfv = (DateTime)row[df].value!;

                            switch (ServiceScope.ResolvedDateStorageForTypeAndProperty(typeof(T), df))
                            {
                                case PropertyDateStorage.TwoWayConvertUtc:
                                    row[df] = (DateTime.SpecifyKind(dfv, DateTimeKind.Utc).ToLocalTime(), typeof(DateTime));
                                    break;

                                case PropertyDateStorage.TwoWayConvertUtcOnlyWithTime:
                                    if ((dfv).TimeOfDay.Milliseconds != 0)
                                    {
                                        row[df] = (DateTime.SpecifyKind(dfv, DateTimeKind.Utc).ToLocalTime(), typeof(DateTime));
                                    }
                                    break;
                            }
                        }
                    }

                    if (doWrap)
                    {
                        // If "the same" object exists in current scope, this will "merge" it with new values, avoids duplicating it in scope
                        no = CEF.CurrentServiceScope.IncludeObjectWithType(new T(), ObjectState.Unchanged, row);
                    }
                    else
                    {
                        var propVals = new Dictionary<string, object?>(Globals.DefaultDictionaryCapacity);

                        foreach (var kvp in row)
                        {
                            propVals[kvp.Key] = kvp.Value.value;
                        }

                        no = new T();
                        WrappingHelper.CopyParsePropertyValues(propVals, no, false, null, new Dictionary<object, object>(Globals.DefaultDictionaryCapacity), false, false);
                    }

                    // Handle property groups if they exist for this type
                    if ((ss.Settings.RetrievalPostProcessing & RetrievalPostProcessing.PropertyGroups) != 0)
                    {
                        db.ExpandPropertyGroupValues(no);
                    }

                    // Handle properties that are named differently from underlying storage
                    if ((ss.Settings.RetrievalPostProcessing & RetrievalPostProcessing.PropertyNameFixups) != 0)
                    {
                        db.FixupPropertyStorageNames(no);
                    }

                    // Handle populating links to parent instances where there does not exist a CLR property exposed for the key/ID of the parent
                    if ((ss.Settings.RetrievalPostProcessing & RetrievalPostProcessing.ParentInstancesWithoutCLRProperties) != 0)
                    {
                        db.InitializeObjectsWithoutCorrespondingIDs(no);
                    }

                    yield return no;
                }

                if (!hasData && cc != null)
                {
                    foreach (var c in sqlcmd.GetResultSetShape())
                    {
                        cc(c.Key, c.Value);
                    }
                }
            }
            finally
            {
                dbconn.DecrementWorking();
            }
        }

        public IEnumerable<T> RetrieveAll<T>(ICEFDataHost db, ConnectionScope conn, bool doWrap) where T : class, new()
        {
            return CommonRetrieveRows<T>(db, conn, doWrap, null, CommandType.RetrieveAll, null, null);
        }

        public IEnumerable<T> RetrieveByKey<T>(ICEFDataHost db, ConnectionScope conn, bool doWrap, object[] key) where T : class, new()
        {
            return CommonRetrieveRows<T>(db, conn, doWrap, null, CommandType.RetrieveByKey, null, key);
        }

        public IEnumerable<T> RetrieveByQuery<T>(ICEFDataHost db, ConnectionScope conn, bool doWrap, System.Data.CommandType cmdType, string cmdText, CEF.ColumnDefinitionCallback? cc, object?[] parms) where T : class, new()
        {
            return CommonRetrieveRows<T>(db, conn, doWrap, cmdText, cmdType == System.Data.CommandType.StoredProcedure ? CommandType.RetrieveByProc : CommandType.RetrieveByText, cc, parms);
        }
    }
}
