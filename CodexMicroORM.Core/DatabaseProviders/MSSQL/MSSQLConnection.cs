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
using System.Data.SqlClient;
using CodexMicroORM.Core;

namespace CodexMicroORM.Providers
{
    /// <summary>
    /// Implements the functionality expected by CEF for dealing with database connections, for MS SQL Server.
    /// </summary>
    public class MSSQLConnection : IDBProviderConnection
    {
        private SqlConnection _conn;
        private SqlTransaction _tx;

        internal MSSQLConnection(SqlConnection conn, SqlTransaction tx)
        {
            _conn = conn;
            _tx = tx;
        }

        public SqlConnection CurrentConnection
        {
            get
            {
                return _conn;
            }
        }

        public SqlTransaction CurrentTransaction
        {
            get
            {
                return _tx;
            }
        }

        public void Commit()
        {
            if (_tx != null)
            {
                _tx.Commit();
                _tx = null;
            }
        }

        public void Rollback()
        {
            if (_tx != null)
            {
                _tx.Rollback();
                _tx = null;
            }
        }

        #region IDisposable Support
        private bool disposedValue = false;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (_tx != null)
                {
                    _tx.Dispose();
                    _tx = null;
                }

                if (_conn != null)
                {
                    _conn.Dispose();
                    _conn = null;
                }

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }

        #endregion
    }
}
