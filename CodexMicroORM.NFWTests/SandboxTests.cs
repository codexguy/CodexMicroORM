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
01/2018    0.2.4   Initial release (Joel Champagne)
***********************************************************************/
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodexMicroORM.Core;
using CodexMicroORM.Core.Services;
using CodexMicroORM.Providers;
using CodexMicroORM.DemoObjects;
using System.Linq;
using System;

namespace CodexMicroORM.NFWTests
{
    public class SandboxTests : MarshalByRefObject
    {
        private const string DB_SERVER = @"(local)\sql2016";

        public SandboxTests()
        {
            Globals.WrapperSupports = WrappingSupport.Notifications;
            Globals.WrappingClassNamespace = null;
            Globals.WrapperClassNamePattern = "{0}Wrapped";
            CEF.AddGlobalService(new DBService(new MSSQLProcBasedProvider($@"Data Source={DB_SERVER};Database=CodexMicroORMTest;Integrated Security=SSPI;MultipleActiveResultSets=true", defaultSchema: "CEFTest")));
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
