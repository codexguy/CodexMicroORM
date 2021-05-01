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
06/2018    0.7     Initial release (Joel Champagne)
***********************************************************************/
#nullable enable
using CodexMicroORM.Core;
using CodexMicroORM.Core.Services;
using System;

namespace CodeXFramework.BaseEntity
{
    public static class Extensions
    {
        public static void Save<T>(this EntitySet<T> target) where T : class, new()
        {
            target.DBSave();
        }
        public static void RetrieveByKey<T>(this EntitySet<T> target, params object[] args) where T : class, new()
        {
            target.DBRetrieveByKey(args);
        }

        public static DateTime? ConvertToTimezone(this DateTime? dt, string tzid)
        {
            if (dt.HasValue && !string.IsNullOrEmpty(tzid))
            {
                if (dt.Value.Kind == DateTimeKind.Local)
                {
                    dt = dt.Value.ToUniversalTime();
                }

                var tzi = TimeZoneInfo.FindSystemTimeZoneById(tzid);

                if (tzi != null)
                {
                    return TimeZoneInfo.ConvertTimeFromUtc(dt.Value, tzi);
                }
            }

            return dt;
        }

        public static string Format(this DateTime? dt, string fmt)
        {
            if (dt.HasValue)
                return dt.Value.ToString(fmt);

            return string.Empty;
        }

        public static DateTime ConvertToTimezone(this DateTime dt, string tzid)
        {
            if (!string.IsNullOrEmpty(tzid))
            {
                if (dt.Kind == DateTimeKind.Local)
                {
                    dt = dt.ToUniversalTime();
                }

                var tzi = TimeZoneInfo.FindSystemTimeZoneById(tzid);

                if (tzi != null)
                {
                    return TimeZoneInfo.ConvertTimeFromUtc(dt, tzi);
                }
            }

            return dt;
        }
    }
}
