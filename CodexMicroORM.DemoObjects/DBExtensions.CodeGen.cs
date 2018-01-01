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
using System.Data;
using CodexMicroORM.Core;
using CodexMicroORM.Core.Services;

namespace CodexMicroORM.DemoObjects
{
    /// <summary>
    /// The intent of this class is to illustrate what *should* be code generated based on the shape of the result sets from any custom stored procedures in a given database source.
    /// Doing so means we get compile-time errors if signatures change, which is good!
    /// We can also build an automated testing fascade based on these to ensure all procedures remain "unbroken".
    /// </summary>
    public static class GeneratedExtensions
    {
        public static EntitySet<Phone> DBRetrieveAllForFamily(this EntitySet<Phone> set, int ParentPersonID)
        {
            return set.DBRetrieveByQuery<Phone>(CommandType.StoredProcedure, "CEFTest.up_Phone_AllForFamily", ParentPersonID);
        }
        public static EntitySet<Phone> DBAppendAllForFamily(this EntitySet<Phone> set, int ParentPersonID)
        {
            return set.DBAppendByQuery<Phone>(CommandType.StoredProcedure, "CEFTest.up_Phone_AllForFamily", ParentPersonID);
        }
        public static EntitySet<Phone> DBRetrieveByOwner(this EntitySet<Phone> set, int PersonID, PhoneType? PhoneTypeID)
        {
            return set.DBRetrieveByQuery<Phone>(CommandType.StoredProcedure, "CEFTest.up_Phone_ByPersonID", PersonID, PhoneTypeID);
        }
        public static EntitySet<Phone> DBAppendByOwner(this EntitySet<Phone> set, int PersonID, PhoneType? PhoneTypeID)
        {
            return set.DBAppendByQuery<Phone>(CommandType.StoredProcedure, "CEFTest.up_Phone_ByPersonID", PersonID, PhoneTypeID);
        }

        public static EntitySet<Person> DBRetrieveByParentID(this EntitySet<Person> set, int ParentPersonID)
        {
            return set.DBRetrieveByQuery<Person>(CommandType.StoredProcedure, "CEFTest.up_Person_ByParentPersonID", ParentPersonID);
        }
        public static EntitySet<Person> DBAppendByParentID(this EntitySet<Person> set, int ParentPersonID)
        {
            return set.DBAppendByQuery<Person>(CommandType.StoredProcedure, "CEFTest.up_Person_ByParentPersonID", ParentPersonID);
        }
        public static EntitySet<Person> DBRetrieveSummaryForParents(this EntitySet<Person> set, int? MinimumAge)
        {
            return set.DBRetrieveByQuery<Person>(CommandType.StoredProcedure, "CEFTest.up_Person_SummaryForParents", MinimumAge);
        }
        public static EntitySet<Person> DBAppendSummaryForParents(this EntitySet<Person> set, int? MinimumAge)
        {
            return set.DBAppendByQuery<Person>(CommandType.StoredProcedure, "CEFTest.up_Person_SummaryForParents", MinimumAge);
        }

        public static EntitySet<PersonWrapped> DBRetrieveByParentID(this EntitySet<PersonWrapped> set, int ParentPersonID)
        {
            return set.DBRetrieveByQuery<PersonWrapped>(CommandType.StoredProcedure, "CEFTest.up_Person_ByParentPersonID", ParentPersonID);
        }
        public static EntitySet<PersonWrapped> DBAppendByParentID(this EntitySet<PersonWrapped> set, int ParentPersonID)
        {
            return set.DBAppendByQuery<PersonWrapped>(CommandType.StoredProcedure, "CEFTest.up_Person_ByParentPersonID", ParentPersonID);
        }
        public static EntitySet<PersonWrapped> DBRetrieveSummaryForParents(this EntitySet<PersonWrapped> set, int? MinimumAge)
        {
            return set.DBRetrieveByQuery<PersonWrapped>(CommandType.StoredProcedure, "CEFTest.up_Person_SummaryForParents", MinimumAge);
        }
        public static EntitySet<PersonWrapped> DBAppendSummaryForParents(this EntitySet<PersonWrapped> set, int? MinimumAge)
        {
            return set.DBAppendByQuery<PersonWrapped>(CommandType.StoredProcedure, "CEFTest.up_Person_SummaryForParents", MinimumAge);
        }
    }
}
