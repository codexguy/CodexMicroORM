using System;
using System.Collections.Generic;
using System.Text;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
#nullable enable

namespace CodexMicroORM.Core
{
    /// <summary>
    /// This allows any class to be "pooled" for reuse.  This is useful for classes that are expensive to create and/or destroy, OR for classes which are not thread-safe.
    /// Usage: wrap in a using statement, and the item will be returned to the pool when disposed.
    /// Example: using (var item = new PoolableItem<MyClass>(() => new MyClass())) { ... }
    /// The constructor accepts a delegate which will be invoked to create a new instance of the class, when needed.
    /// Pool size will automatically grow to MaxItemCount, if set.  If MaxItemCount is not set, the pool can grow indefinitely.
    /// If pool is exhausted and MaxWaitSeconds is set, the constructor will wait for a slot to open up, up to the specified time, then throw a timeout exception.
    /// If MaxLifeMinutes is set, items will be evicted from the pool after that many minutes have passed (checked every minute; should be set prior to first use).
    /// Access constructed value using .Item property.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class PoolableItemWrapper<T> : IDisposable where T : class
    {
        private readonly static ConcurrentDictionary<T, DateTime> _items = [];
        private static long _runningCount = 0;
        private static Timer? _evictor;

        public delegate T CreateItemDelegate();

        public static int? MaxItemCount { get; set; }

        public static int? MaxLifeMinutes { get; set; }

        public static int? MaxWaitSeconds { get; set; }

        public static int WaitIntervalMs { get; set; } = 5;

        public static int CurrentPoolCount => _items.Count;

        private T? _using;
        private bool _disposed = false;

        public T Item => _using ?? throw new InvalidOperationException("Object has been disposed.");

        public PoolableItemWrapper(CreateItemDelegate ci)
        {
            lock (_items)
            {
                if (MaxLifeMinutes.HasValue && _evictor == null)
                {
                    _evictor = new Timer((o) =>
                    {
                        var now = DateTime.Now;
                        foreach (var item in _items)
                        {
                            if ((now - item.Value).TotalMinutes > MaxLifeMinutes.Value)
                            {
                                _items.TryRemove(item.Key, out _);
                            }
                        }
                    }, null, 0, 60000);
                }
            }

            static T? getfrompool()
            {
                if (!_items.IsEmpty)
                {
                    var touse = _items.Keys.FirstOrDefault();

                    if (touse != null)
                    {
                        if (_items.TryRemove(touse, out _))
                        {
                            return touse;
                        }
                    }
                }

                return null;
            }

            var touse = getfrompool();

            if (touse == null)
            {
                if (MaxItemCount.HasValue && _items.Count + Interlocked.Read(ref _runningCount) >= MaxItemCount.Value)
                {
                    if (MaxWaitSeconds.HasValue)
                    {
                        var start = DateTime.Now;
                        while (_items.Count + Interlocked.Read(ref _runningCount) >= MaxItemCount.Value)
                        {
                            Thread.Sleep(WaitIntervalMs);
                            touse = getfrompool();

                            if (touse != null)
                            {
                                break;
                            }

                            if ((DateTime.Now - start).TotalSeconds > MaxWaitSeconds.Value)
                            {
                                throw new TimeoutException($"Timeout waiting for pool space for type {typeof(T).Name}.");
                            }
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException($"Pool for type {typeof(T).Name} is full.");
                    }
                }

                touse ??= ci();
            }

            _using = touse;
            Interlocked.Increment(ref _runningCount);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _items.TryAdd(_using!, DateTime.Now);
                Interlocked.Decrement(ref _runningCount);
                _using = null;
                GC.SuppressFinalize(this);
            }
        }
    }
}
