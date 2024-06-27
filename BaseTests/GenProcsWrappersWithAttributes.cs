using System.Data;
using System.Threading.Tasks;
using CodexMicroORM.Core;
using CodexMicroORM.Core.Services;
#nullable enable

// Note: this is a code generated file; you would not typically change this by hand.
// Be aware that this template couples your business objects to CodexMicroORM.

namespace CodexMicroORM.DemoObjects2
{

    public static partial class PhoneTypeMethods
    {

        public async static Task<EntitySet<PhoneType>> RetrieveAllAsync(this EntitySet<PhoneType> target)
        {
            await Task.Run(() => {
                target.DBRetrieveByQuery(CommandType.StoredProcedure, "[CEFTest].[up_PhoneType_ForList]", (n, t) => target.ExternalSchema[n] = t);
            });
            return target;
        }
        public async static Task<EntitySet<PhoneType>> RetrieveAllWithAppendAsync(this EntitySet<PhoneType> target)
        {
            await Task.Run(() => {
                target.DBAppendByQuery(CommandType.StoredProcedure, "[CEFTest].[up_PhoneType_ForList]");
            });
            return target;
        }

        public static EntitySet<PhoneType> RetrieveAll(this EntitySet<PhoneType> target)
        {
            target.DBRetrieveByQuery(CommandType.StoredProcedure, "[CEFTest].[up_PhoneType_ForList]", (n, t) => target.ExternalSchema[n] = t);
            return target;
        }
        public static EntitySet<PhoneType> RetrieveAllWithAppend(this EntitySet<PhoneType> target)
        {
            target.DBAppendByQuery(CommandType.StoredProcedure, "[CEFTest].[up_PhoneType_ForList]");
            return target;
        }

    }

    public partial class PhoneTypeSet
    {
        public async static Task<PhoneTypeSet> RetrieveAllAsync()
        {
            var target = new PhoneTypeSet();
            await Task.Run(() => {
                target.DBRetrieveByQuery(CommandType.StoredProcedure, "[CEFTest].[up_PhoneType_ForList]", (n, t) => target.ExternalSchema[n] = t);
            });
            return target;
        }
        public static PhoneTypeSet RetrieveAll()
        {
            var target = new PhoneTypeSet();
            target.DBRetrieveByQuery(CommandType.StoredProcedure, "[CEFTest].[up_PhoneType_ForList]", (n, t) => target.ExternalSchema[n] = t);
            return target;
        }
    }



    public static partial class PhoneMethods
    {

        public async static Task<EntitySet<Phone>> RetrieveAllAsync(this EntitySet<Phone> target)
        {
            await Task.Run(() => {
                target.DBRetrieveByQuery(CommandType.StoredProcedure, "[CEFTest].[up_Phone_ForList]", (n, t) => target.ExternalSchema[n] = t);
            });
            return target;
        }
        public async static Task<EntitySet<Phone>> RetrieveAllWithAppendAsync(this EntitySet<Phone> target)
        {
            await Task.Run(() => {
                target.DBAppendByQuery(CommandType.StoredProcedure, "[CEFTest].[up_Phone_ForList]");
            });
            return target;
        }

        public static EntitySet<Phone> RetrieveAll(this EntitySet<Phone> target)
        {
            target.DBRetrieveByQuery(CommandType.StoredProcedure, "[CEFTest].[up_Phone_ForList]", (n, t) => target.ExternalSchema[n] = t);
            return target;
        }
        public static EntitySet<Phone> RetrieveAllWithAppend(this EntitySet<Phone> target)
        {
            target.DBAppendByQuery(CommandType.StoredProcedure, "[CEFTest].[up_Phone_ForList]");
            return target;
        }

    }

    public partial class PhoneSet
    {
        public async static Task<PhoneSet> RetrieveAllAsync()
        {
            var target = new PhoneSet();
            await Task.Run(() => {
                target.DBRetrieveByQuery(CommandType.StoredProcedure, "[CEFTest].[up_Phone_ForList]", (n, t) => target.ExternalSchema[n] = t);
            });
            return target;
        }
        public static PhoneSet RetrieveAll()
        {
            var target = new PhoneSet();
            target.DBRetrieveByQuery(CommandType.StoredProcedure, "[CEFTest].[up_Phone_ForList]", (n, t) => target.ExternalSchema[n] = t);
            return target;
        }
    }



    public static partial class PhoneMethods
    {

        public async static Task<EntitySet<Phone>> RetrieveByPersonIDAsync(this EntitySet<Phone> target, int PersonID, int? PhoneTypeID)
        {
            await Task.Run(() => {
                target.DBRetrieveByQuery(CommandType.StoredProcedure, "[CEFTest].[up_Phone_ByPersonID]", (n, t) => target.ExternalSchema[n] = t, PersonID, PhoneTypeID);
            });
            return target;
        }
        public async static Task<EntitySet<Phone>> RetrieveByPersonIDWithAppendAsync(this EntitySet<Phone> target, int PersonID, int? PhoneTypeID)
        {
            await Task.Run(() => {
                target.DBAppendByQuery(CommandType.StoredProcedure, "[CEFTest].[up_Phone_ByPersonID]", PersonID, PhoneTypeID);
            });
            return target;
        }

        public static EntitySet<Phone> RetrieveByPersonID(this EntitySet<Phone> target, int PersonID, int? PhoneTypeID)
        {
            target.DBRetrieveByQuery(CommandType.StoredProcedure, "[CEFTest].[up_Phone_ByPersonID]", (n, t) => target.ExternalSchema[n] = t, PersonID, PhoneTypeID);
            return target;
        }
        public static EntitySet<Phone> RetrieveByPersonIDWithAppend(this EntitySet<Phone> target, int PersonID, int? PhoneTypeID)
        {
            target.DBAppendByQuery(CommandType.StoredProcedure, "[CEFTest].[up_Phone_ByPersonID]", PersonID, PhoneTypeID);
            return target;
        }

    }

    public partial class PhoneSet
    {
        public async static Task<PhoneSet> RetrieveByPersonIDAsync(int PersonID, int? PhoneTypeID)
        {
            var target = new PhoneSet();
            await Task.Run(() => {
                target.DBRetrieveByQuery(CommandType.StoredProcedure, "[CEFTest].[up_Phone_ByPersonID]", (n, t) => target.ExternalSchema[n] = t, PersonID, PhoneTypeID);
            });
            return target;
        }
        public static PhoneSet RetrieveByPersonID(int PersonID, int? PhoneTypeID)
        {
            var target = new PhoneSet();
            target.DBRetrieveByQuery(CommandType.StoredProcedure, "[CEFTest].[up_Phone_ByPersonID]", (n, t) => target.ExternalSchema[n] = t, PersonID, PhoneTypeID);
            return target;
        }
    }



    public static partial class PhoneMethods
    {

        public async static Task<EntitySet<Phone>> RetrieveByKeyAsync(this EntitySet<Phone> target, int? PhoneID)
        {
            await Task.Run(() => {
                target.DBRetrieveByQuery(CommandType.StoredProcedure, "[CEFTest].[up_Phone_ByKey]", (n, t) => target.ExternalSchema[n] = t, PhoneID);
            });
            return target;
        }
        public async static Task<EntitySet<Phone>> RetrieveByKeyWithAppendAsync(this EntitySet<Phone> target, int? PhoneID)
        {
            await Task.Run(() => {
                target.DBAppendByQuery(CommandType.StoredProcedure, "[CEFTest].[up_Phone_ByKey]", PhoneID);
            });
            return target;
        }

        public static EntitySet<Phone> RetrieveByKey(this EntitySet<Phone> target, int? PhoneID)
        {
            target.DBRetrieveByQuery(CommandType.StoredProcedure, "[CEFTest].[up_Phone_ByKey]", (n, t) => target.ExternalSchema[n] = t, PhoneID);
            return target;
        }
        public static EntitySet<Phone> RetrieveByKeyWithAppend(this EntitySet<Phone> target, int? PhoneID)
        {
            target.DBAppendByQuery(CommandType.StoredProcedure, "[CEFTest].[up_Phone_ByKey]", PhoneID);
            return target;
        }

    }

    public partial class PhoneSet
    {
        public async static Task<PhoneSet> RetrieveByKeyAsync(int? PhoneID)
        {
            var target = new PhoneSet();
            await Task.Run(() => {
                target.DBRetrieveByQuery(CommandType.StoredProcedure, "[CEFTest].[up_Phone_ByKey]", (n, t) => target.ExternalSchema[n] = t, PhoneID);
            });
            return target;
        }
        public static PhoneSet RetrieveByKey(int? PhoneID)
        {
            var target = new PhoneSet();
            target.DBRetrieveByQuery(CommandType.StoredProcedure, "[CEFTest].[up_Phone_ByKey]", (n, t) => target.ExternalSchema[n] = t, PhoneID);
            return target;
        }
    }



    public static partial class PhoneMethods
    {

        public async static Task<EntitySet<Phone>> RetrieveForFamilyAsync(this EntitySet<Phone> target, int PersonID, bool Dup)
        {
            await Task.Run(() => {
                target.DBRetrieveByQuery(CommandType.StoredProcedure, "[CEFTest].[up_Phone_AllForFamily]", (n, t) => target.ExternalSchema[n] = t, PersonID, Dup);
            });
            return target;
        }
        public async static Task<EntitySet<Phone>> RetrieveForFamilyWithAppendAsync(this EntitySet<Phone> target, int PersonID, bool Dup)
        {
            await Task.Run(() => {
                target.DBAppendByQuery(CommandType.StoredProcedure, "[CEFTest].[up_Phone_AllForFamily]", PersonID, Dup);
            });
            return target;
        }

        public static EntitySet<Phone> RetrieveForFamily(this EntitySet<Phone> target, int PersonID, bool Dup)
        {
            target.DBRetrieveByQuery(CommandType.StoredProcedure, "[CEFTest].[up_Phone_AllForFamily]", (n, t) => target.ExternalSchema[n] = t, PersonID, Dup);
            return target;
        }
        public static EntitySet<Phone> RetrieveForFamilyWithAppend(this EntitySet<Phone> target, int PersonID, bool Dup)
        {
            target.DBAppendByQuery(CommandType.StoredProcedure, "[CEFTest].[up_Phone_AllForFamily]", PersonID, Dup);
            return target;
        }

    }

    public partial class PhoneSet
    {
        public async static Task<PhoneSet> RetrieveForFamilyAsync(int PersonID, bool Dup)
        {
            var target = new PhoneSet();
            await Task.Run(() => {
                target.DBRetrieveByQuery(CommandType.StoredProcedure, "[CEFTest].[up_Phone_AllForFamily]", (n, t) => target.ExternalSchema[n] = t, PersonID, Dup);
            });
            return target;
        }
        public static PhoneSet RetrieveForFamily(int PersonID, bool Dup)
        {
            var target = new PhoneSet();
            target.DBRetrieveByQuery(CommandType.StoredProcedure, "[CEFTest].[up_Phone_AllForFamily]", (n, t) => target.ExternalSchema[n] = t, PersonID, Dup);
            return target;
        }
    }



    public static partial class PersonMethods
    {

        public async static Task<EntitySet<Person>> RetrieveSummaryForParentsAsync(this EntitySet<Person> target, int? MinimumAge)
        {
            await Task.Run(() => {
                target.DBRetrieveByQuery(CommandType.StoredProcedure, "[CEFTest].[up_Person_SummaryForParents]", (n, t) => target.ExternalSchema[n] = t, MinimumAge);
            });
            return target;
        }
        public async static Task<EntitySet<Person>> RetrieveSummaryForParentsWithAppendAsync(this EntitySet<Person> target, int? MinimumAge)
        {
            await Task.Run(() => {
                target.DBAppendByQuery(CommandType.StoredProcedure, "[CEFTest].[up_Person_SummaryForParents]", MinimumAge);
            });
            return target;
        }

        public static EntitySet<Person> RetrieveSummaryForParents(this EntitySet<Person> target, int? MinimumAge)
        {
            target.DBRetrieveByQuery(CommandType.StoredProcedure, "[CEFTest].[up_Person_SummaryForParents]", (n, t) => target.ExternalSchema[n] = t, MinimumAge);
            return target;
        }
        public static EntitySet<Person> RetrieveSummaryForParentsWithAppend(this EntitySet<Person> target, int? MinimumAge)
        {
            target.DBAppendByQuery(CommandType.StoredProcedure, "[CEFTest].[up_Person_SummaryForParents]", MinimumAge);
            return target;
        }

    }

    public partial class PersonSet
    {
        public async static Task<PersonSet> RetrieveSummaryForParentsAsync(int? MinimumAge)
        {
            var target = new PersonSet();
            await Task.Run(() => {
                target.DBRetrieveByQuery(CommandType.StoredProcedure, "[CEFTest].[up_Person_SummaryForParents]", (n, t) => target.ExternalSchema[n] = t, MinimumAge);
            });
            return target;
        }
        public static PersonSet RetrieveSummaryForParents(int? MinimumAge)
        {
            var target = new PersonSet();
            target.DBRetrieveByQuery(CommandType.StoredProcedure, "[CEFTest].[up_Person_SummaryForParents]", (n, t) => target.ExternalSchema[n] = t, MinimumAge);
            return target;
        }
    }


    [EntityAdditionalProperties(nameof(ForParents))]
    public partial class Person
    {
        public partial class ForParentsClass
        {

            public int? MaleChildren
            {
                get;
                set;
            }

            public int? FemaleChildren
            {
                get;
                set;
            }

            public int? FamilyPhones
            {
                get;
                set;
            }

        }

        private ForParentsClass? _propsForParents;

        [EntityIgnoreBinding]
        public ForParentsClass ForParents
        {
            get
            {
                _propsForParents ??= new();
                return _propsForParents;
            }
        }
    }


    public static partial class PersonMethods
    {

        public async static Task<EntitySet<Person>> RetrieveAllAsync(this EntitySet<Person> target)
        {
            await Task.Run(() => {
                target.DBRetrieveByQuery(CommandType.StoredProcedure, "[CEFTest].[up_Person_ForList]", (n, t) => target.ExternalSchema[n] = t);
            });
            return target;
        }
        public async static Task<EntitySet<Person>> RetrieveAllWithAppendAsync(this EntitySet<Person> target)
        {
            await Task.Run(() => {
                target.DBAppendByQuery(CommandType.StoredProcedure, "[CEFTest].[up_Person_ForList]");
            });
            return target;
        }

        public static EntitySet<Person> RetrieveAll(this EntitySet<Person> target)
        {
            target.DBRetrieveByQuery(CommandType.StoredProcedure, "[CEFTest].[up_Person_ForList]", (n, t) => target.ExternalSchema[n] = t);
            return target;
        }
        public static EntitySet<Person> RetrieveAllWithAppend(this EntitySet<Person> target)
        {
            target.DBAppendByQuery(CommandType.StoredProcedure, "[CEFTest].[up_Person_ForList]");
            return target;
        }

    }

    public partial class PersonSet
    {
        public async static Task<PersonSet> RetrieveAllAsync()
        {
            var target = new PersonSet();
            await Task.Run(() => {
                target.DBRetrieveByQuery(CommandType.StoredProcedure, "[CEFTest].[up_Person_ForList]", (n, t) => target.ExternalSchema[n] = t);
            });
            return target;
        }
        public static PersonSet RetrieveAll()
        {
            var target = new PersonSet();
            target.DBRetrieveByQuery(CommandType.StoredProcedure, "[CEFTest].[up_Person_ForList]", (n, t) => target.ExternalSchema[n] = t);
            return target;
        }
    }



    public static partial class PersonMethods
    {

        public async static Task<EntitySet<Person>> RetrieveByKeyAsync(this EntitySet<Person> target, int? PersonID)
        {
            await Task.Run(() => {
                target.DBRetrieveByQuery(CommandType.StoredProcedure, "[CEFTest].[up_Person_ByKey]", (n, t) => target.ExternalSchema[n] = t, PersonID);
            });
            return target;
        }
        public async static Task<EntitySet<Person>> RetrieveByKeyWithAppendAsync(this EntitySet<Person> target, int? PersonID)
        {
            await Task.Run(() => {
                target.DBAppendByQuery(CommandType.StoredProcedure, "[CEFTest].[up_Person_ByKey]", PersonID);
            });
            return target;
        }

        public static EntitySet<Person> RetrieveByKey(this EntitySet<Person> target, int? PersonID)
        {
            target.DBRetrieveByQuery(CommandType.StoredProcedure, "[CEFTest].[up_Person_ByKey]", (n, t) => target.ExternalSchema[n] = t, PersonID);
            return target;
        }
        public static EntitySet<Person> RetrieveByKeyWithAppend(this EntitySet<Person> target, int? PersonID)
        {
            target.DBAppendByQuery(CommandType.StoredProcedure, "[CEFTest].[up_Person_ByKey]", PersonID);
            return target;
        }

    }

    public partial class PersonSet
    {
        public async static Task<PersonSet> RetrieveByKeyAsync(int? PersonID)
        {
            var target = new PersonSet();
            await Task.Run(() => {
                target.DBRetrieveByQuery(CommandType.StoredProcedure, "[CEFTest].[up_Person_ByKey]", (n, t) => target.ExternalSchema[n] = t, PersonID);
            });
            return target;
        }
        public static PersonSet RetrieveByKey(int? PersonID)
        {
            var target = new PersonSet();
            target.DBRetrieveByQuery(CommandType.StoredProcedure, "[CEFTest].[up_Person_ByKey]", (n, t) => target.ExternalSchema[n] = t, PersonID);
            return target;
        }
    }




}
