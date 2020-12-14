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
#nullable enable
using System;
using System.Data.SqlClient;
using System.Threading;
using CodexMicroORM.Core;

namespace CodexMicroORM.Providers
{
    /// <summary>
    /// Implements the functionality expected by CEF for dealing with database connections, for MS SQL Server.
    /// </summary>
    public class MSSQLConnection : IDBProviderConnection
    {
#if DEBUG
        private readonly string _id = Guid.NewGuid().ToString();

        public string ID()
        {
            return _id;
        }
#else
        public string ID()
        {
            return string.Empty;
        }
#endif

        private int _working = 0;
        private readonly object _worklock = new object();

        internal MSSQLConnection(SqlConnection conn, SqlTransaction? tx)
        {
            CurrentConnection = conn;
            CurrentTransaction = tx;
        }

        public void IncrementWorking()
        {
            lock (_worklock)
            {
                ++_working;
            }
        }

        public void DecrementWorking()
        {
            lock (_worklock)
            {
                if (_working > 0)
                {
                    --_working;
                }
            }
        }

        public bool IsOpen()
        {
            return CurrentConnection != null && CurrentConnection.State == System.Data.ConnectionState.Open;
        }

        public void DeepReset()
        {
            SqlConnection.ClearAllPools();
        }

        public bool IsWorking()
        {
            lock (_worklock)
            {
                if (_working > 0)
                {
                    return true;
                }
            }

            var ccs = CurrentConnection?.State;
            return ccs == System.Data.ConnectionState.Executing || ccs == System.Data.ConnectionState.Fetching;
        }

        public SqlConnection? CurrentConnection { get; private set; }

        public SqlTransaction? CurrentTransaction { get; private set; }

        public void Commit()
        {
            if (CurrentTransaction != null)
            {
                CurrentTransaction.Commit();
                CurrentTransaction = null;
            }
        }

        public void Rollback()
        {
            if (CurrentTransaction != null)
            {
                CurrentTransaction.Rollback();
                CurrentTransaction = null;
            }
        }

        #region IDisposable Support
        protected virtual void Dispose(bool disposing)
        {
            // If it's working, give it a few seconds to see if can finish
            for (int i = 1; IsWorking() && i <= 300; ++i)
            {
                Thread.Sleep(10);
            }

            if (!IsWorking())
            {
                if (CurrentTransaction != null)
                {
                    CurrentTransaction.Dispose();
                    CurrentTransaction = null;
                }

                if (CurrentConnection != null)
                {
                    CurrentConnection.Dispose();
                    CurrentConnection = null;
                }
            }
            else
            {
//#if DEBUG
//                if (System.Diagnostics.Debugger.IsAttached)
//                {
//                    System.Diagnostics.Debugger.Break();
//                }
//#endif
                CurrentTransaction = null;
                CurrentConnection = null;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }

        #endregion
    }
}
