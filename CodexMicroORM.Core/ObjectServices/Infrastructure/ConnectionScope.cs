﻿/***********************************************************************
Copyright 2024 CodeX Enterprises LLC

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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace CodexMicroORM.Core.Services
{
    /// <summary>
    /// Why separate connection scopes from service scopes? You could for example have a need for a tx that spans service scopes. You could have a need for different connections within the same service scope.
    /// A requirement: never penalize someone for forgetting to create a connection scope - we can create one-offs that act like short-lived connections (usually for the duration of a save).
    /// </summary>
    [Serializable]
    public class ConnectionScope : IDisposable
    {
        private IDBProviderConnection? _conn = null;
        private bool _canCommit = false;

        [NonSerialized]
        private readonly string? _connStringOverride = null;

        public ConnectionScope(bool tx = true, string? connStringOverride = null, int? timeoutOverride = null)
        {
            if (DBService.DefaultProvider == null)
            {
                throw new CEFInvalidStateException(InvalidStateType.MissingInit, "Default data provider not set.");
            }

            Provider = DBService.DefaultProvider;
            _connStringOverride = connStringOverride;
            TimeoutOverride = timeoutOverride;
            IsTransactional = tx;
        }

        #region "Properties"

#if DEBUG
        [NonSerialized]
        public string ID = Guid.NewGuid().ToString();
#endif

        public ConcurrentDictionary<string, object?> LastOutputVariables
        {
            get;
            private set;
        } = new ConcurrentDictionary<string, object?>();

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

        public int? TimeoutOverride
        {
            get;
            internal set;
        } = null;

        public IDBProvider Provider { get; }

        public bool ContinueOnError
        {
            get;
            set;
        }

        public HashSet<ICEFInfraWrapper> ToAcceptList
        {
            get;
            set;
        } = [];

        public ConcurrentBag<(ICEFInfraWrapper row, ObjectState prevstate, IList<(string name, object? value)> data)> ToRollbackList
        {
            get;
            set;
        } = [];

        public IDBProviderConnection CurrentConnection
        {
            get
            {
                lock (this)
                {
                    if (_conn != null && _conn.IsOpen())
                    {
                        return _conn;
                    }

                    _conn = Provider.CreateOpenConnection("default", IsTransactional, _connStringOverride, null);
                    return _conn;
                }
            }
        }

        #endregion

        #region "Methods"

        /// <summary>
        /// This is not the same as a full Dispose, retaining the accept list, etc.
        /// </summary>
        public void ResetConnection(bool deepreset = false)
        {
            if (IsTransactional)
            {
                throw new CEFInvalidStateException(InvalidStateType.BadAction, "Cannot use ResetConnection with IsTransactional is true.");
            }

            IDBProviderConnection? conn = null;

            lock (this)
            {
                conn = _conn;
                _conn = null;
            }

            if (conn != null)
            {
                // Avoid disposal while something might be executing against it
                while (conn.IsWorking())
                {
                    Thread.Sleep(5);
                }

                conn.IncrementWorking();

                try
                {
                    conn.Dispose();
                }
                finally
                {
                    conn.DecrementWorking();
                }
            }
        }

        public void CanCommit()
        {
            _canCommit = true;
        }

        public void DoneWork()
        {
            // If not standalone or not transactional - we don't need to discard connection, keep it alive
            if (IsStandalone)
            {
                if (IsTransactional)
                {
                    CanCommit();
                }

                Dispose();
            }
        }

        #endregion

        #region IDisposable Support

        protected virtual void Dispose(bool disposing)
        {
            Disposing?.Invoke();

            IDBProviderConnection? conn = null;

            lock (this)
            {
                conn = _conn;
                _conn = null;
            }

            if (conn != null)
            {
                bool isRB = false;

                if (IsTransactional)
                {
                    if (_canCommit)
                    {
                        conn.Commit();
                    }
                    else
                    {
                        conn.Rollback();
                        ToAcceptList = null!;
                        isRB = true;
                    }
                }

                if (ToAcceptList != null)
                {
                    foreach (var r in ToAcceptList)
                    {
                        r.AcceptChanges();
                    }

                    ToAcceptList = null!;
                }

                if (isRB && ToRollbackList != null)
                {
                    foreach (var (row, prevstate, data) in ToRollbackList)
                    {
                        foreach (var (name, value) in data)
                        {
                            row.SetValue(name, value);
                        }

                        // We also restore original row state
                        row.SetRowState(prevstate);
                    }
                }

                //CEFDebug.WriteInfo($"Dispose connection: " + conn.ID() + " for " + ID);
                conn.Dispose();
            }

            Disposed?.Invoke();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public Action? Disposed
        {
            get;
            set;
        }

        public Action? Disposing
        {
            get;
            set;
        }

        #endregion
    }
}
