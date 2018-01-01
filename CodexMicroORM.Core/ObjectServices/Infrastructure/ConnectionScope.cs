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

namespace CodexMicroORM.Core.Services
{
    /// <summary>
    /// Why separate connection scopes from service scopes? You could for example have a need for a tx that spans service scopes. You could have a need for different connections within the same service scope.
    /// A requirement: never penalize someone for forgetting to create a connection scope - we can create one-offs that act like short-lived connections (usually for the duration of a save).
    /// </summary>
    public class ConnectionScope : IDisposable
    {
        private IDBProvider _provider = null;
        private IDBProviderConnection _conn = null;
        private bool _canCommit = false;
        private string _connStringOverride = null;
        private object _sync = new object();

        public ConnectionScope(bool tx = true, string connStringOverride = null)
        {
            _provider = DBService.DefaultProvider;
            _connStringOverride = connStringOverride;
            IsTransactional = tx;
        }

        #region "Properties"

        public bool IsStandalone
        {
            get;
            internal set;
        }

        public bool IsTransactional
        {
            get;
            internal set;
        } = true;

        public IDBProvider Provider => _provider;

        public bool ContinueOnError
        {
            get;
            set;
        }

        public IDBProviderConnection CurrentConnection
        {
            get
            {
                lock (_sync)
                {
                    if (_conn == null)
                    {
                        _conn = Provider.CreateOpenConnection("default", IsTransactional, _connStringOverride);
                    }

                    return _conn;
                }
            }
        }

        #endregion

        #region "Methods"

        public void CanCommit()
        {
            _canCommit = true;
        }

        public void DoneWork()
        {
            if (IsStandalone)
            {
                CanCommit();
                Dispose();
            }
        }

        #endregion

        #region IDisposable Support

        public bool IsDisposed
        {
            get;
            private set;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!IsDisposed)
            {
                IDBProviderConnection conn = null;

                lock (_sync)
                {
                    conn = _conn;
                }

                if (conn != null)
                {
                    if (IsTransactional)
                    {
                        if (_canCommit)
                        {
                            conn.Commit();
                        }
                        else
                        {
                            conn.Rollback();
                        }
                    }

                    conn.Dispose();
                    _conn = null;
                }

                IsDisposed = true;

                Disposed?.Invoke();
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }

        public Action Disposed
        {
            get;
            set;
        }

        #endregion
    }
}
