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
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodexMicroORM.Core;
using CodexMicroORM.Core.Services;
using CodexMicroORM.Providers;
using CodexMicroORM.DemoObjects;
using CodexMicroORM.BindingSupport;
using System.Linq;
using System.Data;
using System;
using System.Text.RegularExpressions;
using System.IO;

namespace CodexMicroORM.NFWTests
{
    [TestClass]
    public class SelfContainedDBOps
    {
        private const string DB_SERVER = @"(local)\sql2016";

        /// <summary>
        /// All tests here are self-contained and only rely on the shared demo classes.
        /// It's true: these are larger tests than we might ordinarily try for, but we're testing deep into multiple operations against our object models (I encourage the community to develop better tests and tests for more edge cases!)
        /// Many of these initial tests are here to ensure the demo is in working order - many more to follow!
        /// </summary>
        public SelfContainedDBOps()
        {
            // One-time set-up
            Globals.WrapperSupports = WrappingSupport.Notifications;
            Globals.WrappingClassNamespace = null;
            Globals.WrapperClassNamePattern = "{0}Wrapped";
            CEF.AddGlobalService(new DBService(new MSSQLProcBasedProvider($@"Data Source={DB_SERVER};Database=CodexMicroORMTest;Integrated Security=SSPI;MultipleActiveResultSets=true", defaultSchema: "CEFTest")));

            KeyService.RegisterKey<Person>(nameof(Person.PersonID));
            KeyService.RegisterKey<Phone>("PhoneID");

            KeyService.RegisterRelationship<Person>(TypeChildRelationship.Create<Person>("ParentPersonID").MapsToChildProperty(nameof(Person.Kids)));
            KeyService.RegisterRelationship<Person>(TypeChildRelationship.Create<Phone>().MapsToParentProperty(nameof(Phone.Owner)).MapsToChildProperty(nameof(Person.Phones)));

            // This will construct a new test database, if needed - if the script changes, you'll need to drop the CodexMicroORMTest database before running
            using (CEF.NewConnectionScope(new ConnectionScopeSettings() { IsTransactional = false, ConnectionStringOverride = $@"Data Source={DB_SERVER};Database=master;Integrated Security=SSPI;MultipleActiveResultSets=true" }))
            {
                CEF.CurrentDBService().ExecuteRaw(File.ReadAllText("setup.sql"), false);
            }
        }

        [TestMethod]
        public void SingleItemCreateSave()
        {
            using (CEF.NewServiceScope())
            {
                var p = CEF.NewObject(new Person() { Name = "Test1", Age = 11, Gender = "M" });
                Assert.AreEqual(CEF.DBSave().Count(), 1);
                var ps = new EntitySet<Person>().DBRetrieveByKey(p.PersonID);
                Assert.AreEqual(1, ps.Count);
                Assert.AreEqual(ps.First().PersonID, p.PersonID);
            }
        }

        [TestMethod]
        public void UseInitNullCollectionsAddAndSave()
        {
            using (CEF.NewServiceScope(new ServiceScopeSettings() { InitializeNullCollections = true }))
            {
                var p1 = CEF.NewObject<PersonWrapped>();
                p1.Name = "Joe";
                p1.Age = 22;
                p1.Gender = "M";
                p1.Phones.Add(new Phone() { Number = "111-2222", PhoneTypeID = PhoneType.Home, Owner = p1 });
                p1.Phones.Add(new Phone() { Number = "222-3333", PhoneTypeID = PhoneType.Mobile, Owner = p1 });
                var p2 = CEF.NewObject<PersonWrapped>();
                p2.Name = "Mary";
                p2.Age = 2;
                p2.Gender = "F";
                p1.Kids.Add(p2);
                Assert.AreEqual(4, CEF.DBSave().Count());
                var ph1 = (from a in p1.Phones where a.PhoneTypeID == PhoneType.Home select a).First();
                ph1.Owner = null;
                p1.Phones.Remove(ph1);
                p2.Age = 1;
                Assert.AreEqual(2, CEF.DBSave().Count());
            }
        }

        [TestMethod]
        public void NoInitNullCollectionsAddAndSave()
        {
            using (CEF.NewServiceScope())
            {
                var p1 = CEF.NewObject<PersonWrapped>();
                p1.Name = "Joe";
                p1.Age = 22;
                p1.Gender = "M";
                p1.Phones = CEF.CreateList(p1, nameof(Person.Phones), ObjectState.Added, new Phone() { Number = "111-2222", PhoneTypeID = PhoneType.Home, Owner = p1 }, new Phone() { Number = "222-3333", PhoneTypeID = PhoneType.Mobile, Owner = p1 });
                var p2 = CEF.NewObject<PersonWrapped>();
                p2.Name = "Mary";
                p2.Age = 2;
                p2.Gender = "F";
                p1.Kids = CEF.CreateList<Person>(p1, nameof(Person.Kids), ObjectState.Added, p2);
                Assert.AreEqual(4, CEF.DBSave().Count());
                var ph1 = (from a in p1.Phones where a.PhoneTypeID == PhoneType.Home select a).First();
                ph1.Owner = null;
                p1.Phones.Remove(ph1);
                p2.Age = 1;
                Assert.AreEqual(2, CEF.DBSave().Count());
                p2.Phones = CEF.CreateList(p2, nameof(Person.Phones), ObjectState.Modified, ph1);
                Assert.AreEqual(1, CEF.DBSave().Count());
                Assert.IsTrue(ph1.AsDynamic().PhoneID > 0);
                CEF.DeleteObject(p2);
                Assert.AreEqual(2, CEF.DBSave().Count());
            }
        }

        [TestMethod]
        public void LoadIncrementalByIDs()
        {
            int p1ID;

            using (CEF.NewServiceScope())
            {
                var p1 = new Person() { Name = "Fred", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3333", PhoneTypeID = PhoneType.Mobile } } };
                var p2 = new Person() { Name = "Sam", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3334", PhoneTypeID = PhoneType.Mobile } } };
                var p3 = new Person() { Name = "Carol", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3335", PhoneTypeID = PhoneType.Mobile } } };
                var p4 = new Person() { Name = "Kylo", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3336", PhoneTypeID = PhoneType.Mobile } } };
                var p5 = new Person() { Name = "Perry", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3337", PhoneTypeID = PhoneType.Mobile } } };
                var p6 = new Person() { Name = "William", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3338", PhoneTypeID = PhoneType.Mobile } } };
                p1.Kids = new Person[] { p2, p3 };
                p2.Kids = new Person[] { p4 };
                p3.Kids = new Person[] { p5, p6 };
                CEF.NewObject(p1);
                CEF.DBSave();
                p1ID = p1.PersonID;
            }

            using (CEF.NewServiceScope(new ServiceScopeSettings() { InitializeNullCollections = true }))
            {
                var people = new EntitySet<Person>().DBRetrieveByKey(p1ID).DBAppendByParentID(p1ID);

                // Here's where we do *not* offer additional services (yet)
                foreach (var p in people)
                {
                    p.Phones = new EntitySet<Phone>().DBRetrieveByOwner(p.PersonID, null);
                }

                var parent = (from a in people where a.Kids?.Count > 0 select a).FirstOrDefault();
                Assert.AreEqual(2, parent.Kids.Count());
                Assert.AreEqual(2, (from a in parent.Kids select a.Phones.Count()).Sum());
            }
        }

        [TestMethod]
        public void TestLastUpdatedByDateAssignment()
        {
            var p1 = new Person() { Name = "Fred", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3333", PhoneTypeID = PhoneType.Mobile } } };

            using (CEF.NewServiceScope(new ServiceScopeSettings() { GetLastUpdatedBy = () => { return "XYZ"; } }))
            {
                CEF.NewObject(p1);
                Assert.AreEqual(2, CEF.DBSave().Count());
            }

            using (CEF.NewServiceScope())
            {
                var es1 = new EntitySet<Person>().DBRetrieveByKey(p1.PersonID);
                Assert.AreEqual("XYZ", es1.First().AsDynamic().LastUpdatedBy);
                var lud = (DateTime)(es1.First().AsDynamic().LastUpdatedDate);
                var timeDiff = DateTime.UtcNow.Subtract(lud).TotalSeconds;
                Assert.IsTrue(timeDiff >= 0 && timeDiff < 5);
            }
        }

        [TestMethod]
        public void PopulateFromInitialPocoVariousRetrievalsSaves()
        {
            using (CEF.NewServiceScope())
            {
                var p1 = new Person() { Name = "Fred", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3333", PhoneTypeID = PhoneType.Mobile } } };
                var p2 = new Person() { Name = "Sam", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3334", PhoneTypeID = PhoneType.Mobile } } };
                var p3 = new Person() { Name = "Carol", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3335", PhoneTypeID = PhoneType.Mobile } } };
                var p4 = new Person() { Name = "Kylo", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3336", PhoneTypeID = PhoneType.Mobile } } };
                var p5 = new Person() { Name = "Perry", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3337", PhoneTypeID = PhoneType.Mobile } } };
                var p6 = new Person() { Name = "William", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3338", PhoneTypeID = PhoneType.Mobile } } };
                p1.Kids = new Person[] { p2, p3 };
                p2.Kids = new Person[] { p4 };
                p3.Kids = new Person[] { p5, p6 };
                CEF.NewObject(p1);
                Assert.AreEqual(12, CEF.DBSave().Count());
                Assert.AreEqual(2, KeyService.GetObjectNestLevel(p6));
                Assert.AreEqual(3, KeyService.GetObjectNestLevel(p6.Phones.First()));
                var es1 = new EntitySet<Person>().DBRetrieveByKey(p5.PersonID).DBAppendByKey(p6.PersonID);
                Assert.AreEqual(2, es1.Count);
                var es2 = new EntitySet<PersonWrapped>().DBRetrieveByKey(p3.PersonID).DBAppendByParentID(p3.PersonID);
                (from a in es2 where a.Name == "Perry" select a).FirstOrDefault().Age = 45;
                Assert.AreEqual(2, (from a in es2 where a.AsInfraWrapped().GetRowState() == ObjectState.Unchanged select a).Count());
                Assert.IsTrue((from a in es2 where a.Name == "Carol" select a).FirstOrDefault().PersonID == p3.PersonID);
                var es3 = new EntitySet<Phone>();
                es3.Add(p2.Phones.First());
                es3.Add(p4.Phones.First());                
                p2.AsWrapped<PersonWrapped>().Phones.RemoveAt(0);
                p4.AsWrapped<PersonWrapped>().Phones.RemoveAt(0);
                es3.ForAll((ph) => ph.PhoneTypeID = PhoneType.Home);
                Assert.IsTrue(es3.All((p) => p.PhoneTypeID == PhoneType.Home && p.AsDynamic().PersonID == null));
                Assert.AreEqual(2, es3.DBSave().Count());
                CEF.DeleteObject(p1);
                Assert.AreEqual(10, CEF.DBSave(new DBSaveSettings() { RootObject = p1 }).Count());
                es3.DeleteAll();
                Assert.AreEqual(2, CEF.DBSave().Count());
            }
        }

        [TestMethod]
        public void MultipleNestedServiceScopesAndCanCommit()
        {
            using (CEF.NewServiceScope())
            {
                var p1 = new Person() { Name = "Fred", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3333", PhoneTypeID = PhoneType.Mobile } } };
                var p2 = new Person() { Name = "Sam", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3334", PhoneTypeID = PhoneType.Mobile } } };
                var p3 = new Person() { Name = "Carol", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3335", PhoneTypeID = PhoneType.Mobile } } };
                var p4 = new Person() { Name = "Kylo", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3336", PhoneTypeID = PhoneType.Mobile } } };
                var p5 = new Person() { Name = "Perry", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3337", PhoneTypeID = PhoneType.Mobile } } };
                var p6 = new Person() { Name = "William", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3338", PhoneTypeID = PhoneType.Mobile } } };
                p1.Kids = new Person[] { p2, p3 };
                p2.Kids = new Person[] { p4 };
                p3.Kids = new Person[] { p5, p6 };
                CEF.NewObject(p1);

                using (CEF.NewServiceScope())
                {
                    // Nested service scope contains no objects!
                    Assert.AreEqual(0, CEF.DBSave().Count());
                }

                using (var cs = CEF.NewConnectionScope())
                {
                    // Do not call CanCommit - should not have saved anything!
                    Assert.AreEqual(12, CEF.DBSave().Count());
                }

                var es1 = new EntitySet<Person>().DBRetrieveByKey(p1.PersonID);
                Assert.AreEqual(0, es1.Count);
            }

            using (CEF.NewServiceScope())
            {
                var p1 = new Person() { Name = "Fred", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3333", PhoneTypeID = PhoneType.Mobile } } };
                var p2 = new Person() { Name = "Sam", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3334", PhoneTypeID = PhoneType.Mobile } } };
                var p3 = new Person() { Name = "Carol", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3335", PhoneTypeID = PhoneType.Mobile } } };
                var p4 = new Person() { Name = "Kylo", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3336", PhoneTypeID = PhoneType.Mobile } } };
                var p5 = new Person() { Name = "Perry", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3337", PhoneTypeID = PhoneType.Mobile } } };
                var p6 = new Person() { Name = "William", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3338", PhoneTypeID = PhoneType.Mobile } } };
                p1.Kids = new Person[] { p2, p3 };
                p2.Kids = new Person[] { p4 };
                p3.Kids = new Person[] { p5, p6 };
                CEF.NewObject(p1);

                using (var cs = CEF.NewConnectionScope())
                {
                    Assert.AreEqual(12, CEF.DBSave().Count());
                    cs.CanCommit();
                }

                var es1 = new EntitySet<Person>().DBRetrieveByKey(p1.PersonID);
                Assert.AreEqual(1, es1.Count);
            }
        }

        [TestMethod]
        public void SavingWithBulkInsert()
        {
            var tag = Regex.Replace(Guid.NewGuid().ToString(), @"\W", "").Substring(0, 8);

            using (CEF.NewServiceScope())
            {
                EntitySet<Person> people = new EntitySet<Person>();

                for (int i = 1; i <= 10000; ++i)
                {
                    people.Add(new Person() { Name = $"{tag}{i}", Age = (i % 50) + 10 });
                }

                CEF.DBSave(new DBSaveSettings() { BulkInsertMinimumRows = 10000 });
            }

            using (CEF.NewServiceScope())
            {
                EntitySet<Phone> phones = new EntitySet<Phone>();
                EntitySet<Person> people = new EntitySet<Person>();
                people.DBRetrieveByQuery(CommandType.Text, $"SELECT PersonID FROM CEFTest.Person WHERE Name LIKE '{tag}%'");

                foreach (var p in people)
                {
                    if ((p.PersonID % 4) == 0)
                    {
                        phones.Add(new Phone() { Number = tag, PhoneTypeID = PhoneType.Mobile, Owner = p });
                    }
                }

                Assert.AreEqual(2500, CEF.DBSave().Count());

                CEF.CurrentDBService().ExecuteRaw($@"
DELETE CEFTest.Phone WHERE Number='{tag}';
GO
UPDATE CEFTest.Person SET IsDeleted=1, LastUpdatedDate=GETUTCDATE(), LastUpdatedBy='Test' WHERE Name LIKE '{tag}%';
GO
");
            }
        }

        [TestMethod]
        public void GenericBindableSetLoading()
        {
            var tag = Regex.Replace(Guid.NewGuid().ToString(), @"\W", "").Substring(0, 8);

            using (CEF.NewServiceScope(new ServiceScopeSettings() { InitializeNullCollections = true }))
            {
                var p1 = CEF.NewObject<Person>();
                p1.Name = "Joe";
                p1.Age = 22;
                p1.Gender = "M";
                var ph1 = CEF.NewObject(new Phone() { Number = "111-2222", PhoneTypeID = PhoneType.Home, Owner = p1 });
                p1.Phones.Add(new Phone() { Number = "222-3333", PhoneTypeID = PhoneType.Mobile });
                var p2 = CEF.NewObject<PersonWrapped>();
                p2.Name = "Mary";
                p2.Age = 2;
                p2.Gender = "F";
                p1.Kids.Add(p2);
                Assert.AreEqual(4, CEF.DBSave().Count());
                GenericBindableSet gbs = new EntitySet<Person>().DBRetrieveByKey(p1.PersonID).AsDynamicBindable();
                Assert.AreEqual(1, gbs.Count);
                Assert.AreEqual("Joe", gbs.First().GetPropertyValue(nameof(Person.Name)));
                string newName = $"Joel{tag}";
                gbs.First().SetPropertyValue(nameof(Person.Name), newName);
                Assert.AreEqual(1, CEF.DBSave().Count());
                var es1 = new EntitySet<Person>().DBRetrieveSummaryForParents(20);
                Assert.AreEqual(2, (from a in es1 where a.Name == newName select a).First().AsDynamic().FamilyPhones);
                Assert.AreEqual(p1.PersonID, (from a in es1 where a.Name == newName select a).First().PersonID);
            }
        }
    }
}
