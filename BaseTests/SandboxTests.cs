using CodexMicroORM.Core;
using CodexMicroORM.Core.Services;
using CodexMicroORM.DemoObjects;
using CodexMicroORM.Providers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BaseTests
{
    public class SandboxTests : MarshalByRefObject
    {
        private const string DB_SERVER = @"(local)\sql2016";

        public SandboxTests()
        {
            Globals.WrapperSupports = WrappingSupport.Notifications;
            Globals.WrappingClassNamespace = null;
            Globals.WrapperClassNamePattern = "{0}Wrapped";
            CEF.AddGlobalService(DBService.Create(new MSSQLProcBasedProvider($@"Data Source={DB_SERVER};Database=CodexMicroORMTest;Integrated Security=SSPI;MultipleActiveResultSets=true", defaultSchema: "CEFTest")));
            CEF.AddGlobalService(new AuditService());

            KeyService.RegisterKey<Person>(nameof(Person.PersonID));
            KeyService.RegisterKey<Phone>("PhoneID");

            KeyService.RegisterRelationship<Person>(TypeChildRelationship.Create<Person>("ParentPersonID").MapsToChildProperty(nameof(Person.Kids)));
            KeyService.RegisterRelationship<Person>(TypeChildRelationship.Create<Phone>().MapsToParentProperty(nameof(Phone.Owner)).MapsToChildProperty(nameof(Person.Phones)));
        }

        public string CaseInsensitivePropAccessAndTLSGlobals()
        {
            try
            {
                Globals.CaseSensitiveDictionaries = false;

                using (CEF.NewServiceScope())
                {
                    var p = CEF.NewObject(new Person() { Name = "Test1", Age = 11, Gender = "M" });
                    Assert.AreEqual(CEF.DBSave().Count(), 1);
                    var ps = new EntitySet<Person>().DBRetrieveByKey(p.PersonID);
                    var p2 = ps.First().AsInfraWrapped();
                    p2.SetValue("myArbitrary", 123, typeof(int));
                    Assert.AreEqual("Test1", p2.AsDynamic().name);
                    Assert.AreEqual(123, p2.AsDynamic().Myarbitrary);
                    return null;
                }
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }
    }
}
