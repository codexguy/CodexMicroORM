using System;

namespace CodexMicroORM.Core.Services
{
    /// <summary>
    /// A value type that can wrap both value and reference types in an orthogonal manner, as part of indexing.
    /// The main driver is a need to allow us to include null as the key in directionaries, namely indexing dictionaries.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public struct FieldWrapper<T> : IComparable<T>, IComparable
    {
        private readonly T _value;

        public FieldWrapper(T val)
        {
            if (Globals.IndexedSetCaseInsensitiveStringCompares && typeof(T) == typeof(string) && val != null)
            {
                _value = (T)((object)val.ToString().ToLower());
            }
            else
            {
                _value = val;
            }
        }

        public T Value => _value;

        public int CompareTo(object other)
        {
            if (other == null && _value == null)
                return 0;

            if (other == null)
                return 1;

            if (_value == null)
                return -1;

            if (other is FieldWrapper<T>)
                other = ((FieldWrapper<T>)other).Value;

            if (other is IComparable && _value is IComparable)
                return ((IComparable)_value).CompareTo(other);

            return _value.ToString().CompareTo(other.ToString());
        }

        public int CompareTo(T other)
        {
            if (other == null && _value == null)
                return 0;

            if (other == null)
                return 1;

            if (_value == null)
                return -1;

            if (other is IComparable<T> && _value is IComparable<T>)
                return ((IComparable<T>)_value).CompareTo(other);

            if (other is IComparable && _value is IComparable)
                return ((IComparable)_value).CompareTo(other);

            return _value.ToString().CompareTo(other.ToString());
        }

        public override int GetHashCode()
        {
            if (!typeof(T).IsValueType && _value == null)
            {
                return int.MinValue;
            }

            return _value.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            T toCompare;

            if (obj is FieldWrapper<T>)
            {
                toCompare = ((FieldWrapper<T>)obj).Value;
            }
            else
            {
                if (obj is T t)
                {
                    toCompare = t;
                }
                else
                {
                    return false;
                }
            }

            if (!typeof(T).IsValueType)
            {
                if (toCompare == null && _value == null)
                {
                    return true;
                }

                if (toCompare == null || _value == null)
                {
                    return false;
                }
            }

            return toCompare.Equals(_value);
        }

        public static bool operator ==(FieldWrapper<T> left, FieldWrapper<T> right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(FieldWrapper<T> left, FieldWrapper<T> right)
        {
            return !(left == right);
        }

        public static bool operator <(FieldWrapper<T> left, FieldWrapper<T> right)
        {
            return left.CompareTo(right) < 0;
        }

        public static bool operator <=(FieldWrapper<T> left, FieldWrapper<T> right)
        {
            return left.CompareTo(right) <= 0;
        }

        public static bool operator >(FieldWrapper<T> left, FieldWrapper<T> right)
        {
            return left.CompareTo(right) > 0;
        }

        public static bool operator >=(FieldWrapper<T> left, FieldWrapper<T> right)
        {
            return left.CompareTo(right) >= 0;
        }
    }
}
