using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Data;
using CodexMicroORM.Core;
using CodexMicroORM.Core.Services;

namespace TestHarness
{
    public static class GeneratedExtensions
    {
        public static EntitySet<Person> DBRetrieveByParentID(this EntitySet<Person> set, int ParentPersonID)
        {
            return set.DBRetrieveByQuery<Person>(CommandType.StoredProcedure, "CEFTest.up_Person_ByParentPersonID", ParentPersonID);
        }
        public static EntitySet<Person> DBAppendByParentID(this EntitySet<Person> set, int ParentPersonID)
        {
            return set.DBAppendByQuery<Person>(CommandType.StoredProcedure, "CEFTest.up_Person_ByParentPersonID", ParentPersonID);
        }
        public static EntitySet<Phone> DBRetrieveAllForFamily(this EntitySet<Phone> set, int ParentPersonID)
        {
            return set.DBRetrieveByQuery<Phone>(CommandType.StoredProcedure, "CEFTest.up_Phone_AllForFamily", ParentPersonID);
        }
        public static EntitySet<Phone> DBAppendAllForFamily(this EntitySet<Phone> set, int ParentPersonID)
        {
            return set.DBAppendByQuery<Phone>(CommandType.StoredProcedure, "CEFTest.up_Phone_AllForFamily", ParentPersonID);
        }
        public static EntitySet<Person> DBRetrieveSummaryForParents(this EntitySet<Person> set, int? MinimumAge)
        {
            return set.DBRetrieveByQuery<Person>(CommandType.StoredProcedure, "CEFTest.up_Person_SummaryForParents", MinimumAge);
        }
        public static EntitySet<Person> DBAppendSummaryForParents(this EntitySet<Person> set, int? MinimumAge)
        {
            return set.DBAppendByQuery<Person>(CommandType.StoredProcedure, "CEFTest.up_Person_SummaryForParents", MinimumAge);
        }
    }
}
