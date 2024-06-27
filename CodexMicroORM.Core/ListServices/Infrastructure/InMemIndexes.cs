using System;
using System.Collections.Generic;
using System.Linq;
using CodexMicroORM.Core.Collections;
using CodexMicroORM.Core.Helper;
#nullable enable

namespace CodexMicroORM.Core.Services
{
    internal class EqualityHashIndex<TO, TP> : ICEFDataIndex, ICEFDataIndex<TO> where TO : class, new()
    {
        private readonly ICEFIndexed _source;
        private readonly string _propName;
        private readonly TO[] _empty = [];
        private readonly SlimConcurrentDictionary<FieldWrapper<TP>, ICollection<TO>> _data = new();

        public EqualityHashIndex(ICEFIndexed source, string propName)
        {
            _source = source;
            _propName = propName;

            foreach (TO i in (IEnumerable<TO>)_source)
            {
                Add(i);
            }
        }

        public void ClearAll()
        {
            _data.Clear();
        }

        private FieldWrapper<TP> GetFieldWrapper(TO obj)
        {
            var (readable, value) = obj.FastPropertyReadableWithValue(_propName);

            object? v;

            if (readable)
            {
                v = value;
            }
            else
            {
                v = obj.MustInfraWrap().GetValue(_propName);
            }

            if (v != null && v.GetType().IsEnum)
            {
                var tpbt = Nullable.GetUnderlyingType(typeof(TP)) ?? throw new InvalidOperationException("Unable to get underlying type.");
                v = Convert.ChangeType(v, tpbt);
            }

            return new FieldWrapper<TP>((TP?)(v));
        }

        public void UpdateKey(object? oldval, object? newval, object row)
        {
            if (row is not TO)
            {
                throw new ArgumentException("Row is not a valid type.");
            }

            var oldvalw = new FieldWrapper<TP>((TP?)oldval);

            _data.TryGetValue(oldvalw, out var items);

            if (items != null)
            {
                lock (items)
                {
                    items.Remove((TO)row);

                    if (!items.Any())
                    {
                        _data.Remove(oldvalw);
                    }
                }
            }

            var newvalw = new FieldWrapper<TP>((TP?)newval);

            _data.TryGetValue(newvalw, out items);

            if (items == null)
            {
                items = new HashSet<TO>();
                _data[newvalw] = items;
            }

            lock (items)
            {
                items.Add((TO)row);
            }
        }

        public void Remove(TO obj)
        {
            if (obj == null)
                throw new ArgumentNullException(nameof(obj));

            var val = GetFieldWrapper(obj);

            _data.TryGetValue(val, out var items);

            if (items != null)
            {
                lock (items)
                {
                    items.Remove(obj);
                }

                if (!items.Any())
                {
                    _data.Remove(val);
                }
            }
        }

        public void Add(TO obj)
        {
            if (obj == null)
                throw new ArgumentNullException(nameof(obj));

            var val = GetFieldWrapper(obj);

            if (_data.TryGetValue(val, out var v))
            {
                lock (v)
                {
                    v.Add(obj);
                }
            }
            else
            {
                HashSet<TO> items = new()
                {
                    obj
                };

                _data[val] = items;
            }
        }

        public IEnumerable<TO> GetEqualItems(TP value)
        {
            _data.TryGetValue(new FieldWrapper<TP>(value), out var list);
            return list ?? _empty;
        }

        public IEnumerable<object> GetEqualItems(object? value)
        {
            if (typeof(TP) == typeof(byte?) && value != null && value.GetType() == typeof(int))
            {
                value = Convert.ToByte(value);
            }

            if (typeof(TP) == typeof(OnlyDate?) && value != null && value.GetType() == typeof(DateTime))
            {
                value = new OnlyDate((DateTime)value);
            }

            var v = (TP?)value;
            _data.TryGetValue(new FieldWrapper<TP>(v), out var list);
            return list ?? _empty;
        }

        public bool OnlyEqualitySupport()
        {
            return true;
        }
    }

    internal class ComparisonSortedIndex<TO, TP> : ICEFDataIndex, ICEFDataIndex<TO>, ICEFComparisonDataIndex where TO : class, new()
    {
        private readonly ICEFIndexed _source;
        private readonly string _propName;
        private readonly TO[] _empty = [];
        private readonly C5.TreeDictionary<FieldWrapper<TP>, ICollection<TO>> _data = new();
        private readonly RWLockInfo _lock = new();

        public ComparisonSortedIndex(ICEFIndexed source, string propName)
        {
            _source = source;
            _propName = propName;

            foreach (TO i in (IEnumerable<TO>)_source)
            {
                Add(i);
            }
        }

        public void ClearAll()
        {
            _data.Clear();
        }

        public void UpdateKey(object? oldval, object? newval, object row)
        {
            if (row is not TO)
            {
                throw new ArgumentException("Row is not a valid type.");
            }

            var oldvalw = new FieldWrapper<TP>((TP?)oldval);

            using (new WriterLock(_lock))
            {
                if (_data.Contains(oldvalw))
                {
                    var items = _data[oldvalw];

                    if (items != null)
                    {
                        items.Remove((TO)row);

                        if (!items.Any())
                        {
                            _data.Remove(oldvalw);
                        }
                    }
                }

                var newvalw = new FieldWrapper<TP>((TP?)newval);

                if (_data.Contains(newvalw))
                {
                    _data[newvalw].Add((TO)row);
                }
                else
                {
                    HashSet<TO> items = new()
                    {
                        (TO)row
                    };
                    _data[newvalw] = items;
                }
            }
        }

        public void Remove(TO obj)
        {
            if (obj == null)
                throw new ArgumentNullException("obj");

            var val = GetFieldWrapper(obj);

            using (new WriterLock(_lock))
            {
                if (_data.Contains(val))
                {
                    var items = _data[val];

                    if (items != null)
                    {
                        items.Remove(obj);

                        if (!items.Any())
                        {
                            _data.Remove(val);
                        }
                    }
                }
            }
        }

        private FieldWrapper<TP> GetFieldWrapper(TO obj)
        {
            var (readable, value) = obj.FastPropertyReadableWithValue(_propName);

            object? v;

            if (readable)
            {
                v = value;
            }
            else
            {
                v = obj.MustInfraWrap().GetValue(_propName);
            }

            if (v != null && v.GetType().IsEnum)
            {
                v = Activator.CreateInstance(typeof(TP), v);
            }

            return new FieldWrapper<TP>((TP?)(v));
        }

        public void Add(TO obj)
        {
            if (obj == null)
                throw new ArgumentNullException("obj");

            var val = GetFieldWrapper(obj);

            using (new WriterLock(_lock))
            {
                if (_data.Contains(val))
                {
                    _data[val].Add(obj);
                }
                else
                {
                    HashSet<TO> items = new()
                    {
                        obj
                    };
                    _data[val] = items;
                }
            }
        }

        public IEnumerable<TO> GetEqualItems(TP? value)
        {
            var key = new FieldWrapper<TP>((TP?)value.TypeFixup(typeof(TP)));

            using (new ReaderLock(_lock))
            {
                if (_data.Contains(key))
                {
                    return _data[key];
                }
            }

            return _empty;
        }

        public IEnumerable<object> GetEqualItems(object? value)
        {
            var key = new FieldWrapper<TP>((TP?)value.TypeFixup(typeof(TP)));

            using (new ReaderLock(_lock))
            {
                if (_data.Contains(key))
                {
                    return _data[key];
                }
            }

            return _empty;
        }

        public IEnumerable<object> GetGreaterThanItems(object? value)
        {
            using (new ReaderLock(_lock))
            {
                return _data.RangeFrom(new FieldWrapper<TP>((TP?)value.TypeFixup(typeof(TP)))).SelectMany((p) => p.Value).Except(GetEqualItems(value));
            }
        }

        public IEnumerable<object> GetLessThanItems(object? value)
        {
            using (new ReaderLock(_lock))
            {
                return _data.RangeTo(new FieldWrapper<TP>((TP?)value.TypeFixup(typeof(TP)))).SelectMany((p) => p.Value);
            }
        }

        public IEnumerable<object> GetGreaterThanEqualItems(object? value)
        {
            using (new ReaderLock(_lock))
            {
                return _data.RangeFrom(new FieldWrapper<TP>((TP?)value.TypeFixup(typeof(TP)))).SelectMany((p) => p.Value);
            }
        }

        public IEnumerable<object> GetLessThanEqualItems(object? value)
        {
            using (new ReaderLock(_lock))
            {
                return _data.RangeTo(new FieldWrapper<TP>((TP?)value.TypeFixup(typeof(TP)))).SelectMany((p) => p.Value).Union(GetEqualItems(value));
            }
        }

        public bool OnlyEqualitySupport()
        {
            return false;
        }
    }
}
