using CodexMicroORM.Core;
using CodexMicroORM.Core.Services;
using CodexMicroORM.DemoObjects2;
using CodexMicroORM.Providers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CodexMicroORM.BaseTests
{
    [TestClass]
    public class Modern12
    {
        private const string DB_SERVER = @"(local)\sql2016";

        /// <summary>
        /// Covers classes created from latest code gen templates and most common settings have used lately.
        /// Please note: these tests should be run independently of those found in DBOps.cs - global settings may conflict.
        /// </summary>
        public Modern12()
        {
            Globals.ConnectionScopePerThread = false;
            Globals.UseTransactionsForNewScopes = false;
            Globals.UseAsyncSave = false;
            Globals.ResolveForArbitraryLoadOrder = true;
            Globals.DoCopyParseProperties = false;
            Globals.AddGlobalPropertyExcludedFromDirtyCheck("LastUpdatedDate");
            Globals.AddGlobalPropertyExcludedFromDirtyCheck("LastUpdatedBy");
            Globals.DefaultCacheBehavior = CacheBehavior.Off;
            Globals.DefaultRetrievalPostProcessing = RetrievalPostProcessing.PropertyGroups | RetrievalPostProcessing.PropertyNameFixups;
            MSSQLProcBasedProvider.SaveRetryCount = 3;
            MSSQLProcBasedProvider.OpenRetryCount = 3;
            Globals.CommandTimeoutSeconds = 120;

            CEF.AddGlobalService(DBService.Create(new MSSQLProcBasedProvider($@"Data Source={DB_SERVER};Database=CodexMicroORMTest;Integrated Security=SSPI;MultipleActiveResultSets=true;TrustServerCertificate=true", defaultSchema: "CEFTest")));
            CEF.AddGlobalService(new AuditService(() =>
            {
                return "test";
            }));
            CEF.AddGlobalService(new MemoryFileSystemBacked(null, MemoryFileSystemBacked.CacheStorageStrategy.OnlyMemory));

            // Added 1.2 - test methodology from latest code gen templates
            AttributeInitializer.Apply(typeof(Person).Assembly);
        }

        [TestMethod]
        public void DupRetrievalKeyPropogation()
        {
            using var ss = CEF.NewServiceScope();
            var p = CEF.NewObject<Person>();
            p.Age = 55;
            p.Name = "John";
            p.Gender = "M";
            Assert.AreEqual(1, CEF.DBSave().Count());
            var pc = CEF.NewObject<Person>();
            pc.ParentPersonID = p.PersonID;
            pc.Age = 35;
            pc.Name = "Jane";
            pc.Gender = "F";
            var ph = CEF.NewObject<Phone>();
            ph.PersonID = pc.AsDynamic().PersonID;      // why? since UseShadowPropertiesForNew = true, need to get generated ID
            ph.Number = "555-1111";
            ph.PhoneTypeID = 1;
            Assert.AreEqual(2, CEF.DBSave().Count());

            PhoneSet phset = [];
            try
            {
                ss.SetRetrievalIdentityForObject(phset, RetrievalIdentityMode.ThrowErrorOnDuplicate);
                phset.RetrieveForFamily(p.PersonID, true);
                Assert.Fail("Should have thrown");
            }
            catch (Exception ex)
            {
                Assert.IsTrue(ex.Message.Contains("Duplicate record found"));
            }

            phset.AllowRetrievalDups().RetrieveForFamily(p.PersonID, true);
            Assert.AreEqual(2, phset.Count);
        }

        [TestMethod]
        public void CreatingLoadingSaving()
        {
            using var ss = CEF.NewServiceScope();
            var p = CEF.NewObject<Person>();
            p.Age = 55;
            p.Name = "John";
            p.Gender = "M";
            Assert.AreEqual(1, CEF.DBSave().Count());
            var ph = CEF.NewObject<Phone>();
            ph.PersonID = p.PersonID;
            ph.Number = "555-1212";
            ph.PhoneTypeID = 1;
            Assert.AreEqual(1, CEF.DBSave().Count());
            ph.PhoneTypeID = 2;
            Assert.AreEqual(1, CEF.DBSave().Count());
            var ps = PersonSet.RetrieveByKey(p.PersonID);
            Assert.AreEqual(1, ps.Count);
            Assert.AreEqual("test", ps.First().LastUpdatedBy);
            CEF.DeleteObject(p);
            Assert.AreEqual(2, CEF.DBSave().Count());
            var phs = PhoneSet.RetrieveByPersonID(p.PersonID, null);
            Assert.AreEqual(0, phs.Count);
        }

        [TestMethod]
        public void TestIndexedSetPreformance()
        {
            using var ss = CEF.NewServiceScope();
            IndexedSet<Person> iset = new();
            EntitySet<Person> eset = new();

            for (int i = 1; i < 100000; i++)
            {
                var p = iset.Add();
                p.Age = (i % 80) + 10;
                p.Name = $"John{i}";

                var p2 = eset.Add();
                p2.Age = (i % 80) + 10;
                p2.Name = $"John{i}";
            }

            DateTime starte = DateTime.Now;
            long ecnt = 0;
            for (int j = 0; j < 1000; j++)
            {
                ecnt += (from a in eset where a.Age >= (j % 70) + 10 select a).Count();
            }
            double edur = DateTime.Now.Subtract(starte).TotalMicroseconds;

            long icnt = 0;
            DateTime starti = DateTime.Now;
            for (int j = 0; j < 1000; j++)
            {
                icnt += (from a in iset where a.Age >= (j % 70) + 10 select a).Count();
            }
            double idur = DateTime.Now.Subtract(starti).TotalMicroseconds;

            Assert.AreEqual(57499985, ecnt);
            Assert.AreEqual(57499985, icnt);
            Assert.IsTrue(idur < edur * 0.1);       // indexed set should be >= 10x faster
        }

        public class PoolTestItem
        {
            public Guid Guid { get; set; } = Guid.NewGuid();
        }

        [TestMethod]
        public void PoolableItemTest1()
        {
            var items = new HashSet<Guid>();

            Task.WaitAll(Enumerable.Range(1, 3).Select(async a =>
            {
                using var pi = new PoolableItemWrapper<PoolTestItem>(() => new PoolTestItem());
                items.Add(pi.Item.Guid);
                await Task.Delay(100);
            }).ToArray());

            {
                var pi = new PoolableItemWrapper<PoolTestItem>(() => new PoolTestItem());
                items.Add(pi.Item.Guid);
                pi.Dispose();
            }

            Assert.IsTrue(PoolableItemWrapper<PoolTestItem>.CurrentPoolCount <= 3);
            Assert.IsTrue(items.Count <= 3);
        }


        [TestMethod]
        public void IndexedSetEqualsAndRangeAndLinq()
        {
            using var ss = CEF.NewServiceScope();
            IndexedSet<Person> ps = new();
            var p1 = ps.Add();
            p1.Age = 55;
            p1.Name = "John";
            var p2 = ps.Add();
            p2.Age = 50;
            p2.Name = "Fred";
            var p3 = ps.Add();
            p3.Age = 45;
            p3.Name = "Will";

            Assert.AreEqual(50, ps.FindByEquality("Name", "Fred").First().Age);
            Assert.AreEqual(2, (from a in ps where a.Age >= 50 select a).Count());
            var vp = ps.GetType().GetProperty("View", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(ps) as IndexedSnapshot<Person>;
            Assert.AreEqual(2, vp.IndexCount);      // confirm did really auto-create indexes
        }
    }
}
