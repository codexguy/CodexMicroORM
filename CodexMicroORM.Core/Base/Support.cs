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
4/2018     0.5     Addition of locking helpers
***********************************************************************/
#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace CodexMicroORM.Core
{
    public sealed class SerializationVisitTracker
    {
        public HashSet<object> Objects { get; } = new HashSet<object>();

        public HashSet<Type> Types { get; } = new HashSet<Type>();
    }

    /// <summary>
    /// Mainly intended for internal use by lock classes implemented here.
    /// </summary>
    [Serializable]
    public sealed class RWLockInfo
    {
        [NonSerialized]
        public ReaderWriterLockSlim Lock = new(LockRecursionPolicy.SupportsRecursion);

#if DEBUG
        public static int GlobalTimeout
        {
            get;
            set;
        } = 1500000;
#else
        public static int GlobalTimeout
        {
            get;
            set;
        } = 15000;
#endif

        public bool AllowDirtyReads
        {
            get;
            set;
        } = true;

        public int Timeout = GlobalTimeout;

#if LOCK_TRACE
        // Mainly for debugging purposes, but can be useful in Release too so different compiler directive
        public int LastWriter;
        public int LastReader;
        public int LastWriterRelease;
        public int LastReaderRelease;
        public static int Waits;
        public static long WaitDuration;
        public static double AvgDurationMs => Waits == 0 ? 0 : WaitDuration / 10000.0 / Waits;            
#endif
    }

    /// <summary>
    /// This dictionary type is thread-safe if the only way used to add entries is through the SafeGetSetValue method.
    /// </summary>
    /// <typeparam name="TK"></typeparam>
    /// <typeparam name="TV"></typeparam>
    public class ConcurrentDictionaryEx<TK, TV> : ConcurrentDictionary<TK, TV>
    {
        private readonly object _lock = new();

        public TV SafeGetSetValue(TK key, Func<TK, TV> factoryPred)
        {
            // Why do the get twice? First one is lockless so is safe on read.
            // Second one is effectively an upgraded lock (write lock), where we must make second check to avoid race condition where two threads could have incorrectly assumed no value when the first will actually populate it.
            if (base.TryGetValue(key, out var val))
            {
                return val;
            }

            lock (_lock)
            {
                if (base.TryGetValue(key, out var val2))
                {
                    return val2;
                }

                var v = factoryPred.Invoke(key);

                this[key] = v;
                return v;
            }
        }
    }

    /// <summary>
    /// Creates/destroys a reader lock (use using pattern).
    /// </summary>
    public sealed class ReaderLock : IDisposable
    {
        readonly RWLockInfo _info;
        long _active = 0;

        public bool IsActive => Interlocked.Read(ref _active) > 0;

        public ReaderLock(RWLockInfo info, bool? active = null)
        {
            _info = info;

            if (active.GetValueOrDefault(Globals.UseReaderWriterLocks && (!Globals.AllowDirtyReads || !info.AllowDirtyReads)))
            {
                try
                {
#if LOCK_TRACE
                    RWLockInfo.Waits++;
                    long start = DateTime.Now.Ticks;
#endif
                    Thread.BeginCriticalRegion();

                    if (!_info.Lock.TryEnterReadLock(_info.Timeout))
                    {
#if LOCK_TRACE
                        RWLockInfo.WaitDuration += DateTime.Now.Ticks - start;
#endif
                        throw new TimeoutException("Failed to obtain a read lock in timeout interval.");
                    }

                    Interlocked.Increment(ref _active);

#if LOCK_TRACE
                    _info.LastReader = Environment.CurrentManagedThreadId;
                    RWLockInfo.WaitDuration += DateTime.Now.Ticks - start;
#endif
                }
                finally
                {
                    Thread.EndCriticalRegion();
                }
            }
        }

        public void Release()
        {
            if (IsActive)
            {
                try
                {
                    Thread.BeginCriticalRegion();
                    Interlocked.Decrement(ref _active);

                    if (_info.Lock.IsReadLockHeld)
                    {
                        _info.Lock.ExitReadLock();
                    }

#if LOCK_TRACE
                    _info.LastReaderRelease = Environment.CurrentManagedThreadId;
#endif
                }
                finally
                {
                    Thread.EndCriticalRegion();
                }
            }
        }

        public void Dispose()
        {
            Release();
        }
    }

    /// <summary>
    /// Creates/destroys a writer lock (use using pattern). If lock cannot be acquired immediately, returns and IsActive will be false.
    /// </summary>
    public sealed class QuietWriterLock : IDisposable
    {
        readonly RWLockInfo _info;
        long _active = 0;

        public bool IsActive => Interlocked.Read(ref _active) > 0;

        // Writers block both readers and writers - wait for all other readers and writers to finish
        public QuietWriterLock(RWLockInfo info, bool? active = null)
        {
            _info = info;

            if (active.GetValueOrDefault(Globals.UseReaderWriterLocks))
            {
                try
                {
                    Thread.BeginCriticalRegion();

                    if (_info.Lock.TryEnterWriteLock(0))
                    {
                        Interlocked.Increment(ref _active);

#if LOCK_TRACE
                        _info.LastWriter = Environment.CurrentManagedThreadId;
#endif
                    }
                }
                finally
                {
                    Thread.EndCriticalRegion();
                }
            }
        }

        public void Release()
        {
            if (IsActive)
            {
                try
                {
                    Thread.BeginCriticalRegion();
                    Interlocked.Decrement(ref _active);

                    if (_info.Lock.IsWriteLockHeld)
                    {
                        _info.Lock.ExitWriteLock();
                    }

#if LOCK_TRACE
                    _info.LastWriterRelease = Environment.CurrentManagedThreadId;
#endif
                }
                finally
                {
                    Thread.EndCriticalRegion();
                }
            }
        }

        public void Dispose()
        {
            Release();
        }
    }

    /// <summary>
    /// Creates/destroys a writer lock (use using pattern).
    /// </summary>
    public sealed class WriterLock : IDisposable
    {
        readonly RWLockInfo _info;
        long _active = 0;

        public bool IsActive => Interlocked.Read(ref _active) > 0;

        // Writers block both readers and writers - wait for all other readers and writers to finish
        public WriterLock(RWLockInfo info, bool? active = null)
        {
            _info = info;
            Reacquire(active);
        }

        public void Reacquire(bool? active = null)
        {
            if (!IsActive)
            {
                if (active.GetValueOrDefault(Globals.UseReaderWriterLocks))
                {
                    try
                    {
#if LOCK_TRACE
                        RWLockInfo.Waits++;
                        long start = DateTime.Now.Ticks;
#endif
                        Thread.BeginCriticalRegion();

                        if (!_info.Lock.TryEnterWriteLock(_info.Timeout))
                        {
#if LOCK_TRACE
                            RWLockInfo.WaitDuration += DateTime.Now.Ticks - start;
#endif
                            throw new TimeoutException("Failed to obtain a write lock in timeout interval.");
                        }

                        Interlocked.Increment(ref _active);

#if LOCK_TRACE
                        _info.LastWriter = Environment.CurrentManagedThreadId;
                        RWLockInfo.WaitDuration += DateTime.Now.Ticks - start;
#endif
                    }
                    finally
                    {
                        Thread.EndCriticalRegion();
                    }
                }
            }
        }

        /// <summary>
        /// YieldLock is useful if you're dealing with a long-held write lock and there are points in time where you could allow blocked waiters to get some forward progress.
        /// </summary>
        public void YieldLock()
        {
            if (IsActive)
            {
                try
                {
                    Thread.BeginCriticalRegion();
                    Interlocked.Decrement(ref _active);

                    if (_info.Lock.IsWriteLockHeld)
                    {
                        _info.Lock.ExitWriteLock();
                    }

#if LOCK_TRACE
                    _info.LastWriterRelease = Environment.CurrentManagedThreadId;
                    RWLockInfo.Waits++;
                    long start = DateTime.Now.Ticks;
#endif
                    if (!_info.Lock.TryEnterWriteLock(_info.Timeout))
                    {
#if LOCK_TRACE
                        RWLockInfo.WaitDuration += DateTime.Now.Ticks - start;
#endif
                        throw new TimeoutException("Failed to obtain a write lock in timeout interval.");
                    }

                    Interlocked.Increment(ref _active);

#if LOCK_TRACE
                    _info.LastWriter = Environment.CurrentManagedThreadId;
                    RWLockInfo.WaitDuration += DateTime.Now.Ticks - start;
#endif
                }
                finally
                {
                    Thread.EndCriticalRegion();
                }
            }
        }

        public void Release()
        {
            if (IsActive)
            {
                try
                {
                    Thread.BeginCriticalRegion();
                    Interlocked.Decrement(ref _active);

                    if (_info.Lock.IsWriteLockHeld)
                    {
                        _info.Lock.ExitWriteLock();
                    }

#if LOCK_TRACE
                    _info.LastWriterRelease = Environment.CurrentManagedThreadId;
#endif
                }
                finally
                {
                    Thread.EndCriticalRegion();
                }
            }
        }

        public void Dispose()
        {
            Release();
        }
    }

    /// <summary>
    /// This class affords us a way to do equality comparisons with WeakReference's that are based on the Target, not the WeakReference itself.
    /// This is critical when using it as a dictionary key: I care about finding based on the Target, not the WeakReference itself.
    /// </summary>
    [Serializable]
    public sealed class CEFWeakReference<T> : WeakReference where T : class
    {
        private readonly int? _hash;

        public CEFWeakReference(T? target) : base(target)
        {
            _hash = target?.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if (obj == null)
                return this.Target == null;

            if (obj is CEFWeakReference<T> wr)
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
