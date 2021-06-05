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
6/2021     0.9.10  Introduced (Joel Champagne)
***********************************************************************/
using System;
using System.Runtime.Serialization;
#nullable enable

namespace CodexMicroORM.Core
{
    [Serializable]
    public readonly struct DateOnly : IComparable, IComparable<DateOnly>, IComparable<DateTime>, IConvertible, IEquatable<DateOnly>, IEquatable<DateTime>, IFormattable, ISerializable
    {
        private readonly short Year;
        private readonly byte Month;
        private readonly byte Day;

        public static bool UseUtc
        {
            get;
            set;
        } = true;

        private DateOnly(SerializationInfo info, StreamingContext context)
        {
            var i = info.GetInt32("v");
            Year = Convert.ToInt16(i / 10000);
            Month = Convert.ToByte(i / 100 % 100);
            Day = Convert.ToByte(i % 100);
        }

        public DateOnly(DateTime from)
        {
            Year = Convert.ToInt16(from.Year);
            Month = Convert.ToByte(from.Month);
            Day = Convert.ToByte(from.Day);
        }

        public DateOnly(int year, int month, int day)
        {
            try
            {
                Year = Convert.ToInt16(year);
                Month = Convert.ToByte(month);
                Day = Convert.ToByte(day);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Invalid date information ({year},{month},{day}).", ex);
            }
        }

        public DateOnly(byte[] from)
        {
            if (from?.Length != 4)
            {
                throw new InvalidOperationException("DateOnly invalid format.");
            }

            int ai = -1;

            try
            {
                ai = BitConverter.ToInt32(from, 0);
                Year = Convert.ToInt16(ai / 10000);
                Month = Convert.ToByte(ai / 100 % 100);
                Day = Convert.ToByte(ai % 100);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Invalid date information ({ai}).", ex);
            }
        }

        private DateTime GetDateTime()
        {
            if (Year == 0 && Month == 0 && Day == 0)
            {
                return DateTime.MinValue;
            }

            return new(Year, Month, Day, 0, 0, 0, UseUtc ? DateTimeKind.Utc : DateTimeKind.Local);
        }

        public static implicit operator DateTime(DateOnly from) => from.GetDateTime();
        public static implicit operator DateTime?(DateOnly? from) => from.HasValue ? from.Value.GetDateTime() : null;
        public static implicit operator DateTime?(DateOnly from) => from.GetDateTime();
        public static implicit operator DateOnly(DateTime dt) => new(dt.Year, dt.Month, dt.Day);
        public static implicit operator DateOnly?(DateTime? dt) => dt.HasValue ? new(dt.Value.Year, dt.Value.Month, dt.Value.Day) : null;
        public static implicit operator DateOnly?(DateTime dt) => new(dt.Year, dt.Month, dt.Day);

        public static bool TryParse(string? src, out DateOnly parsed)
        {
            parsed = new DateOnly();

            if (string.IsNullOrWhiteSpace(src))
            {
                return false;
            }

            if (DateTime.TryParse(src, out var dt))
            {
                parsed = new DateOnly(dt);
                return true;
            }

            return false;
        }

        public byte[] GetBytes()
        {
            return BitConverter.GetBytes(GetAsInt());
        }

        public int GetAsInt()
        {
            return Year * 10000 + Month * 100 + Day;
        }

        public override string ToString()
        {
            return GetDateTime().ToShortDateString();
        }

        public string ToString(string format, IFormatProvider formatProvider)
        {
            return GetDateTime().ToString(format, formatProvider);
        }

        public string ToString(string fmt)
        {
            return GetDateTime().ToString(fmt);
        }

        public string ToString(IFormatProvider provider)
        {
            return GetDateTime().ToString(provider);
        }

        public override bool Equals(object obj)
        {
            if (obj is DateOnly d)
            {
                return (Year == d.Year && Month == d.Month && Day == d.Day);
            }

            if (obj is DateOnly?)
            {
                var d2 = (DateOnly?)obj;

                if (d2.HasValue)
                {
                    return (Year == d2.Value.Year && Month == d2.Value.Month && Day == d2.Value.Day);
                }

                return false;
            }

            if (obj is DateTime dt)
            {
                return (Year == dt.Year && Month == dt.Month && Day == dt.Day);
            }

            if (obj is DateTime?)
            {
                var d3 = (DateTime?)obj;

                if (d3.HasValue)
                {
                    return (Year == d3.Value.Year && Month == d3.Value.Month && Day == d3.Value.Day);
                }
            }

            return false;
        }

        public DateTime AddDays(double days)
        {
            return GetDateTime().AddDays(days);
        }

        public TimeSpan Subtract(DateTime value)
        {
            var v = new DateTime(value.Year, value.Month, value.Day, 0, 0, 0, UseUtc ? DateTimeKind.Utc : DateTimeKind.Local);
            return GetDateTime().Subtract(v);
        }

        public override int GetHashCode()
        {
            return GetAsInt().GetHashCode();
        }

        public int CompareTo(object obj)
        {
            if (obj == null)
            {
                return 1;
            }

            if (obj.GetType() == typeof(DateOnly))
            {
                return CompareTo((DateOnly)obj);
            }

            if (obj.GetType() == typeof(DateTime))
            {
                return CompareTo((DateTime)obj);
            }

            throw new NotSupportedException();
        }

        TypeCode IConvertible.GetTypeCode()
        {
            return TypeCode.DateTime;
        }

        bool IConvertible.ToBoolean(IFormatProvider provider)
        {
            throw new InvalidCastException();
        }

        byte IConvertible.ToByte(IFormatProvider provider)
        {
            throw new InvalidCastException();
        }

        char IConvertible.ToChar(IFormatProvider provider)
        {
            throw new InvalidCastException();
        }

        public DateTime ToDateTime(IFormatProvider provider)
        {
            return GetDateTime();
        }

        public DateTime ToDateTime()
        {
            return GetDateTime();
        }

        decimal IConvertible.ToDecimal(IFormatProvider provider)
        {
            throw new InvalidCastException();
        }

        double IConvertible.ToDouble(IFormatProvider provider)
        {
            throw new InvalidCastException();
        }

        short IConvertible.ToInt16(IFormatProvider provider)
        {
            throw new InvalidCastException();
        }

        public int ToInt32(IFormatProvider provider)
        {
            return GetAsInt();
        }

        public long ToInt64(IFormatProvider provider)
        {
            return GetDateTime().Ticks;
        }

        sbyte IConvertible.ToSByte(IFormatProvider provider)
        {
            throw new InvalidCastException();
        }

        float IConvertible.ToSingle(IFormatProvider provider)
        {
            throw new InvalidCastException();
        }

        public object ToType(Type conversionType, IFormatProvider provider)
        {
            if (conversionType == typeof(DateOnly) || conversionType == typeof(DateOnly?))
            {
                return this;
            }

            if (conversionType == typeof(DateTime) || conversionType == typeof(DateTime?))
            {
                return GetDateTime();
            }

            throw new InvalidCastException();
        }

        ushort IConvertible.ToUInt16(IFormatProvider provider)
        {
            throw new InvalidCastException();
        }

        uint IConvertible.ToUInt32(IFormatProvider provider)
        {
            throw new InvalidCastException();
        }

        ulong IConvertible.ToUInt64(IFormatProvider provider)
        {
            throw new InvalidCastException();
        }

        public int CompareTo(DateOnly other)
        {
            var od = other.GetDateTime();
            return GetDateTime().CompareTo(od);
        }

        public bool Equals(DateOnly other)
        {
            var od = other.GetDateTime();
            return GetDateTime() == od;
        }

        void ISerializable.GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue("v", GetAsInt());
        }

        public int CompareTo(DateTime other)
        {
            var td = GetDateTime();
            return td.CompareTo(other);
        }

        public bool Equals(DateTime other)
        {
            return GetDateTime() == other;
        }

        //
        public static bool operator ==(DateOnly left, DateOnly right)
        {
            return left.Year == right.Year && left.Month == right.Month && left.Day == right.Day;
        }

        public static bool operator !=(DateOnly left, DateOnly right)
        {
            return left.Year != right.Year || left.Month != right.Month || left.Day != right.Day;
        }

        public static bool operator >(DateOnly left, DateOnly right)
        {
            return left.GetAsInt() > right.GetAsInt();
        }

        public static bool operator <(DateOnly left, DateOnly right)
        {
            return left.GetAsInt() < right.GetAsInt();
        }

        public static bool operator >=(DateOnly left, DateOnly right)
        {
            return left.GetAsInt() >= right.GetAsInt();
        }

        public static bool operator <=(DateOnly left, DateOnly right)
        {
            return left.GetAsInt() <= right.GetAsInt();
        }

        //
        public static bool operator ==(DateOnly left, DateTime right)
        {
            return left.GetDateTime() == right;
        }

        public static bool operator !=(DateOnly left, DateTime right)
        {
            return !(left.GetDateTime() == right);
        }

        public static bool operator >(DateOnly left, DateTime right)
        {
            return left.GetDateTime() > right;
        }

        public static bool operator <(DateOnly left, DateTime right)
        {
            return left.GetDateTime() < right;
        }

        public static bool operator >=(DateOnly left, DateTime right)
        {
            return left.GetDateTime() >= right;
        }

        public static bool operator <=(DateOnly left, DateTime right)
        {
            return left.GetDateTime() <= right;
        }

        //
        public static bool operator ==(DateTime left, DateOnly right)
        {
            return left == right.GetDateTime();
        }

        public static bool operator !=(DateTime left, DateOnly right)
        {
            return !(left == right.GetDateTime());
        }

        public static bool operator >(DateTime left, DateOnly right)
        {
            return left > right.GetDateTime();
        }

        public static bool operator <(DateTime left, DateOnly right)
        {
            return left < right.GetDateTime();
        }

        public static bool operator >=(DateTime left, DateOnly right)
        {
            return left >= right.GetDateTime();
        }

        public static bool operator <=(DateTime left, DateOnly right)
        {
            return left <= right.GetDateTime();
        }

        //
        public static bool operator ==(DateOnly left, DateTime? right)
        {
            return right.HasValue && left.GetDateTime() == right;
        }

        public static bool operator !=(DateOnly left, DateTime? right)
        {
            return right.HasValue && !(left.GetDateTime() == right);
        }

        public static bool operator >(DateOnly left, DateTime? right)
        {
            return !right.HasValue || left.GetDateTime() > right;
        }

        public static bool operator <(DateOnly left, DateTime? right)
        {
            return right.HasValue && left.GetDateTime() < right;
        }

        public static bool operator >=(DateOnly left, DateTime? right)
        {
            return !right.HasValue || left.GetDateTime() >= right;
        }

        public static bool operator <=(DateOnly left, DateTime? right)
        {
            return right.HasValue && left.GetDateTime() <= right;
        }

        //
        public static bool operator ==(DateTime? left, DateOnly right)
        {
            return left.HasValue && left == right.GetDateTime();
        }

        public static bool operator !=(DateTime? left, DateOnly right)
        {
            return left.HasValue && !(left == right.GetDateTime());
        }

        public static bool operator >(DateTime? left, DateOnly right)
        {
            return left.HasValue && left > right.GetDateTime();
        }

        public static bool operator <(DateTime? left, DateOnly right)
        {
            return !left.HasValue || left < right.GetDateTime();
        }

        public static bool operator >=(DateTime? left, DateOnly right)
        {
            return left.HasValue && left >= right.GetDateTime();
        }

        public static bool operator <=(DateTime? left, DateOnly right)
        {
            return !left.HasValue || left <= right.GetDateTime();
        }
    }
}
