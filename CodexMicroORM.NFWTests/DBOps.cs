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
using System.Threading.Tasks;
using System.Diagnostics;

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
            CEF.AddGlobalService(new AuditService());

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
        public void SerializeDeserializeSave()
        {
            string serTextPerson;
            string serTextScope;

            using (CEF.NewServiceScope(new ServiceScopeSettings() { SerializationMode = SerializationMode.OverWireOnlyChanges, InitializeNullCollections = true }))
            {
                var p1 = CEF.NewObject(new Person() { Name = "STFred", Age = 44 });
                p1.Phones.Add(new Phone() { Number = "222-3333", PhoneTypeID = PhoneType.Mobile });
                var p2 = CEF.NewObject(new Person() { Name = "STSam", Age = 44 });
                p2.Phones.Add(new Phone() { Number = "222-8172", PhoneTypeID = PhoneType.Mobile });
                var p3 = new Person() { Name = "STCarol", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3335", PhoneTypeID = PhoneType.Mobile } } };
                var p4 = new Person() { Name = "STKylo", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3336", PhoneTypeID = PhoneType.Mobile } } };
                var p5 = new Person() { Name = "STPerry", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3337", PhoneTypeID = PhoneType.Mobile } } };
                var p6 = new Person() { Name = "STWilliam", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3338", PhoneTypeID = PhoneType.Mobile } } };
                p1.Kids.Add(p2);
                p1.Kids.Add(p3);
                p2.Kids.Add(p4);
                p3.Kids.Add(p5);
                Assert.AreEqual(10, CEF.DBSave().Count());

                // Previous op should have assigned keys from DB, but now let's make some changes
                p2.Age += 1;
                p2.Phones.RemoveAt(0);
                p3.Kids.Add(p6);

                // Serializing a single object (and all its child info)
                serTextPerson = p1.AsJSON();

                // Serialize every tracked object in scope (with serialization mode caveats)
                serTextScope = CEF.CurrentServiceScope.AsJSON();
            }

            using (CEF.NewServiceScope())
            {
                // Should reconstitute only items needed to support saving the person change (adding of child and their phone, and increment of age) - notably, does not include the disassociated phone object since we're using a serialized *person* object and all their directly related data
                var start = DateTime.UtcNow;
                var p1b = CEF.Deserialize<Person>(serTextPerson);
                Assert.AreEqual(3, CEF.DBSave().Count());
                Assert.AreEqual("222-3338", (from a in p1b.Kids where a.Name == "STCarol" from b in a.Kids where b.Name == "STWilliam" select b.Phones.First()).First().Number);

                // There were 3 changed items, yes, but had to have 5 in total to properly "root" all objects in the hierarchy
                Assert.AreEqual(5, CEF.GetAllTracked().Count());

                var end1 = (from a in p1b.Kids where a.Age == 45 select a).First().AsDynamic().LastUpdatedDate;
                var end2 = (from a in p1b.Kids where a.Name == "STCarol" from b in a.Kids where b.Name == "STWilliam" select b).First().AsDynamic().LastUpdatedDate;
                Assert.IsTrue(end1 > start);
                Assert.IsTrue(end2 > start);
            }

            using (CEF.NewServiceScope())
            {
                // Reconstituting for everything that was in scope includes the disassociated phone object - let's mark everything as clean then dirty up that one up again so it should cause a single update when we save
                var start = DateTime.UtcNow;
                CEF.DeserializeScope(serTextScope);
                CEF.AcceptAllChanges();
                var phone = (from a in CEF.GetAllTracked() where a.HasProperty(nameof(Phone.Number)) && string.Compare(a.GetValue(nameof(Phone.Number))?.ToString(), "222-8172") == 0 select a).First().AsDynamic();
                phone.Number = "222-8173";
                Assert.AreEqual(1, CEF.DBSave().Count());
                var end = phone.LastUpdatedDate;
                Assert.IsTrue(end > start);

                // Service scope serialization is a bit different than it was for the person: we're only expecting 4 objects in the scope (not doing as an object graph, doing everything as a list, effectively)
                Assert.AreEqual(4, CEF.GetAllTracked().Count());
            }
        }

        [TestMethod]
        public void MemFileCacheRetrieves()
        {
            int p1id;
            int ph2id;

            // Create some data specific to this test
            using (CEF.NewServiceScope())
            {
                var p1 = new Person() { Name = "Freddie", Age = 48, Phones = new Phone[] { new Phone() { Number = "678-3333", PhoneTypeID = PhoneType.Mobile } } };
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
                p1id = p1.PersonID;
                ph2id = p2.Phones.First().AsDynamic().PhoneID;
            }

            // DB speed (still in db cache, though)
            long dbspeed = 0;

            using (CEF.NewServiceScope())
            {
                new EntitySet<Phone>().DBRetrieveByKey(ph2id);
                var sw = new Stopwatch();
                sw.Start();
                var t1 = new EntitySet<Phone>().DBRetrieveByKey(ph2id).First().Number == "222-3334";
                dbspeed = sw.ElapsedTicks;
                Assert.IsTrue(t1);
            }

            MemoryFileSystemBacked.FlushAll("CEF_Testing");

            using (CEF.NewServiceScope(new ServiceScopeSettings() { CacheBehavior = CacheBehavior.MaximumDefault }, new MemoryFileSystemBacked("CEF_Testing", MemoryFileSystemBacked.CacheStorageStrategy.SingleDirectory)))
            {
                // By retrieving from db, we're caching it by identity
                var p1b = new EntitySet<Person>().DBRetrieveByKey(p1id).First();
                Assert.AreEqual(48, p1b.Age);

                // Make a change in the object and "recache" its changed form
                p1b.Age = 49;
                CEF.CurrentCacheService().AddByIdentity(p1b);
            }

            using (CEF.NewServiceScope(new ServiceScopeSettings() { CacheBehavior = CacheBehavior.MaximumDefault }, new MemoryFileSystemBacked("CEF_Testing", MemoryFileSystemBacked.CacheStorageStrategy.SingleDirectory)))
            {
                // Having gone out of scope, the previous object no longer exists, right? Wrong - it does in the file-backed cache. Verify it exists by attempting to retrieve the object as if from the database - instead, we get it from the cache since we've initialized it at the scope level.
                // How can I be sure it came from the cache and not DB? I cached something that should be mismatched from DB (the age)
                var p1bset = new EntitySet<Person>().DBRetrieveByKey(p1id);
                Assert.AreEqual(49, p1bset.First().Age);

                // Now let's try to load phones - using a custom query to load all phones for a given family
                var sw2 = new Stopwatch();
                sw2.Start();
                var phones = new EntitySet<Phone>().DBRetrieveAllForFamily(p1id);
                var elapsed2 = sw2.ElapsedTicks;

                // We do some of the important work in the background so be sure that's done before continue test. In a real-world setting, it would be fine to continue without the work complete - we might end up reading from the DB when could have gotten from cache but having the main work be background is likely to give better overall results.
                Task.Delay(7000).Wait();

                // Doing a retrieve now for a specific phone should be based on cache
                var sw = new Stopwatch();
                sw.Start();
                var ph2 = new EntitySet<Phone>().DBRetrieveByKey(ph2id).First();
                var elapsed = sw.ElapsedTicks;
                Assert.AreEqual("222-3334", ph2.Number);
            }

            using (CEF.NewServiceScope(new ServiceScopeSettings() { CacheBehavior = CacheBehavior.MaximumDefault }, new MemoryFileSystemBacked("CEF_Testing", MemoryFileSystemBacked.CacheStorageStrategy.SingleDirectory)))
            {
                // Doing a retrieve now for a specific phone should be fast (direct from disk - slower than memory but faster than a database where the call has to go through a bunch of sql parsing/processing)
                var sw = new Stopwatch();
                sw.Start();
                var ph2 = new EntitySet<Phone>().DBRetrieveByKey(ph2id).First().Number;
                var elapsed = sw.ElapsedTicks;
                Assert.AreEqual("222-3334", ph2);

                // Should be memory-cached at this point... better be fast!
                var sw2 = new Stopwatch();
                sw2.Start();
                var ph2a = new EntitySet<Phone>().DBRetrieveByKey(ph2id).First();
                var elapsed2 = sw2.ElapsedTicks;

                // From disk cache for a set
                var sw3 = new Stopwatch();
                sw3.Start();
                var phCnt = new EntitySet<Phone>().DBRetrieveAllForFamily(p1id).Count;
                var elapsed3 = sw3.ElapsedTicks;
                Assert.AreEqual(3, phCnt);

                // Do an update to a single phone and save it - should update the cache, invalidate any phone queries
                Assert.AreEqual(5, CEF.CurrentCacheService().GetActiveCount());
                ph2a.Number = "223-3334";
                CEF.DBSave();
                Assert.AreEqual(4, CEF.CurrentCacheService().GetActiveCount());

                // Delete/save/retrieve - should not come back from cache
                var ph2aid = (int)ph2a.AsDynamic().PhoneID;
                CEF.DeleteObject(ph2a);
                CEF.DBSave();
                Assert.AreEqual(3, CEF.CurrentCacheService().GetActiveCount());

                var delCnt = new EntitySet<Phone>().DBRetrieveByKey(ph2aid).Count;
                Assert.AreEqual(0, delCnt);

                Task.Delay(5000).Wait();
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
                Assert.AreEqual(2, CEF.CurrentKeyService().GetObjectNestLevel(p6));
                Assert.AreEqual(3, CEF.CurrentKeyService().GetObjectNestLevel(p6.Phones.First()));
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
