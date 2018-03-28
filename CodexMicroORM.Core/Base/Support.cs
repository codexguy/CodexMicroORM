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
4/2018     0.5     Addition of locking helpers
***********************************************************************/
using System;
using System.Threading;

namespace CodexMicroORM.Core
{
    /// <summary>
    /// Mainly intended for internal use by lock classes implemented here.
    /// </summary>
    public sealed class RWLockInfo
    {
        public static int GlobalTimeout
        {
            get;
            set;
        } = 15000;

        public ReaderWriterLockSlim Lock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
        public int Timeout = GlobalTimeout;

        // Mainly for debugging purposes, but leaving in release too
        public int LastWriter;
    }

    /// <summary>
    /// Creates/destroys an upgradeable reader lock (use using pattern).
    /// </summary>
    public sealed class UpgradeableReaderLock : IDisposable
    {
        RWLockInfo _info;
        bool _active = false;

        public bool IsActive => _active;

        public UpgradeableReaderLock(RWLockInfo info, bool active = true)
        {
            _info = info;

            if (active)
            {
                if (!info.Lock.TryEnterUpgradeableReadLock(info.Timeout))
                {
                    if (!info.Lock.TryEnterUpgradeableReadLock(100))
                    {
                        throw new TimeoutException("Failed to obtain a read lock in timeout interval.");
                    }
                }

                _active = true;
            }
        }

        public void Release()
        {
            if (_active)
            {
                _info.Lock.ExitUpgradeableReadLock();
                _active = false;
            }
        }

        public void Dispose()
        {
            if (_active)
            {
                _info.Lock.ExitUpgradeableReadLock();
                _active = false;
            }
        }
    }

    /// <summary>
    /// Creates/destroys a reader lock (use using pattern).
    /// </summary>
    public sealed class ReaderLock : IDisposable
    {
        RWLockInfo _info;
        bool _active = false;

        public bool IsActive => _active;

        public ReaderLock(RWLockInfo info, bool active = true)
        {            
            _info = info;

            if (active)
            {
                if (!info.Lock.TryEnterReadLock(info.Timeout))
                {
                    // We give it one more try, this seems to be necessary sometimes
                    if (!info.Lock.TryEnterReadLock(100))
                    {
                        throw new TimeoutException("Failed to obtain a read lock in timeout interval.");
                    }
                }

                _active = true;
            }
        }

        public void Release()
        {
            if (_active)
            {
                _info.Lock.ExitReadLock();
                _active = false;
            }
        }

        public void Dispose()
        {
            if (_active)
            {
                _info.Lock.ExitReadLock();
                _active = false;
            }
        }
    }

    /// <summary>
    /// Creates/destroys a writer lock (use using pattern). If lock cannot be acquired immediately, returns and IsActive will be false.
    /// </summary>
    public sealed class QuietWriterLock : IDisposable
    {
        RWLockInfo _info;
        bool _active = false;

        public bool IsActive => _active;

        // Writers block both readers and writers - wait for all other readers and writers to finish
        public QuietWriterLock(RWLockInfo info, bool active = true)
        {
            _info = info;

            if (active)
            {
                if (info.Lock.TryEnterWriteLock(0))
                {
                    _active = true;
                    _info.LastWriter = Environment.CurrentManagedThreadId;
                }
            }
        }

        public void Release()
        {
            if (_active)
            {
                _info.Lock.ExitWriteLock();
                _active = false;
            }
        }

        public void Dispose()
        {
            if (_active)
            {
                _info.Lock.ExitWriteLock();
                _active = false;
            }
        }
    }

    /// <summary>
    /// Creates/destroys a writer lock (use using pattern).
    /// </summary>
    public sealed class WriterLock : IDisposable
    {
        RWLockInfo _info;
        bool _active = false;

        public bool IsActive => _active;

        // Writers block both readers and writers - wait for all other readers and writers to finish
        public WriterLock(RWLockInfo info, bool active = true)
        {
            _info = info;

            if (active)
            {
                if (!info.Lock.TryEnterWriteLock(info.Timeout))
                {
                    throw new TimeoutException("Failed to obtain a write lock in timeout interval.");
                }

                _active = true;
                _info.LastWriter = Environment.CurrentManagedThreadId;
            }
        }

        public void Release()
        {
            if (_active)
            {
                _info.Lock.ExitWriteLock();
                _active = false;
            }
        }

        public void Dispose()
        {
            if (_active)
            {
                _info.Lock.ExitWriteLock();
                _active = false;
            }
        }
    }

    /// <summary>
    /// This class affords us a way to do equality comparisons with WeakReference's that are based on the Target, not the WeakReference itself.
    /// This is critical when using it as a dictionary key: I care about finding based on the Target, not the WeakReference itself.
    /// </summary>
    public sealed class CEFWeakReference<T> : WeakReference where T : class
    {
        private int? _hash;

        public CEFWeakReference(T target) : base(target)
        {
            _hash = target?.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if (obj == null)
                return this.Target == null;

            var wr = obj as CEFWeakReference<T>;

            if (wr != null)
            {
                return this.Target.IsSame(wr.Target);
            }

            return obj.Equals(this.Target);
        }

        public override int GetHashCode()
        {
            return _hash.GetValueOrDefault();
        }
    }

}
