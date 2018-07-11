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
using CodexMicroORM.Core;
using CodexMicroORM.Core.Services;
using System;
using System.Linq;

namespace CodeXFramework.BaseEntity
{
    public class GenericSet : EntitySet<GenericSetRow>
    {
        public int RetrieveByQuery(string procName, params object[] args)
        {
            this.DBRetrieveByQuery(procName, args);
            return this.Count;
        }

        public T GetItem<T>(int rowNum, string fieldName)
        {
            if (rowNum >= this.Count)
            {
                throw new ArgumentOutOfRangeException("rowNum is larger than the colleciton size.");
            }

            var row = this.Skip(rowNum).FirstOrDefault();

            if (row == null)
            {
                throw new InvalidOperationException("Could not find row.");
            }

            var iw = row.AsInfraWrapped();

            if (iw == null)
            {
                throw new InvalidOperationException("Could not find wrapped row.");
            }

            if (Nullable.GetUnderlyingType(typeof(T)) != null)
            {
                return (T)Activator.CreateInstance(typeof(T), iw.GetValue(fieldName));
            }
            else
            {
                return (T)Convert.ChangeType(iw.GetValue(fieldName), typeof(T));
            }
        }
    }

    public class GenericSetRow
    {
    }
}
