using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CodexMicroORM.Core;
using CodexMicroORM.Core.Services;
using FluentNHibernate.Mapping;
using NHibernate;

using CodexMicroORM.NHObjectModel;
using FluentNHibernate.Cfg;
using FluentNHibernate.Cfg.Db;
using FluentNHibernate.Conventions.Helpers;

namespace CodexMicroORM.WPFDemo
{
    internal static class NHBenchmarks
    {
        private static ISessionFactory CreateSessionFactory()
        {
            ISessionFactory isessionFactory = Fluently.Configure()
                .Database(MsSqlConfiguration.MsSql2012
                .ConnectionString(@"Server=(local)\sql2016;Database=CodexMicroORMTest;Integrated Security=SSPI;MultipleActiveResultSets=true"))
                .Mappings(m => m
                    .FluentMappings.AddFromAssembly(typeof(Person).Assembly))
                .BuildSessionFactory();

            return isessionFactory;
        }

        public static void Benchmark1(int total_parents, List<long> testTimes, List<long> testTimes2, ref int rowcount, ref int rowcount2)
        {
            var watch = new System.Diagnostics.Stopwatch();
            watch.Start();

            HashSet<Person> people = new HashSet<Person>();

            // Attempting to parallelize: "Object reference not set to an instance of an object." deep inside nHibernate
            using (var ss = CreateSessionFactory().OpenSession())
            {
                for (int parentcnt = 1; parentcnt <= total_parents; ++parentcnt)
                {
                    var parent = new Person() { Name = $"NP{parentcnt}", Age = (parentcnt % 60) + 20, Gender = (parentcnt % 2) == 0 ? "F" : "M", LastUpdatedBy = Environment.UserName, LastUpdatedDate = DateTime.UtcNow };

                    parent.Phones.Add(new Phone() { Number = "888-7777", PhoneTypeID = PhoneTypeEnum.Mobile, LastUpdatedBy = Environment.UserName, LastUpdatedDate = DateTime.UtcNow });
                    parent.Phones.Add(new Phone() { Number = "777-6666", PhoneTypeID = PhoneTypeEnum.Work, LastUpdatedBy = Environment.UserName, LastUpdatedDate = DateTime.UtcNow });

                    if ((parentcnt % 12) == 0)
                    {
                        ss.Save(new Phone() { Number = "666-5555", PhoneTypeID = PhoneTypeEnum.Home, LastUpdatedBy = Environment.UserName, LastUpdatedDate = DateTime.UtcNow });
                    }
                    else
                    {
                        parent.Phones.Add(new Phone() { Number = "777-6666", PhoneTypeID = PhoneTypeEnum.Home, LastUpdatedBy = Environment.UserName, LastUpdatedDate = DateTime.UtcNow });
                    }

                    rowcount += 4;

                    ss.Save(parent);

                    for (int childcnt = 1; childcnt <= (parentcnt % 4); ++childcnt)
                    {
                        var child = new Person() { Name = $"NC{parentcnt}{childcnt}", Age = parent.Age - 20, Gender = (parentcnt % 2) == 0 ? "F" : "M", LastUpdatedBy = Environment.UserName, LastUpdatedDate = DateTime.UtcNow, ParentPersonID = parent.PersonID,
                            Phones = new Phone[] { new Phone() { Number = "999-8888", PhoneTypeID = PhoneTypeEnum.Mobile, LastUpdatedBy = Environment.UserName, LastUpdatedDate = DateTime.UtcNow } }};

                        parent.Kids.Add(child);
                        ss.Save(child);
                        rowcount += 2;
                    }

                    people.Add(parent);
                }

                ss.Flush();
            }

            testTimes.Add(watch.ElapsedMilliseconds);
            watch.Restart();

            // For purposes of benchmarking, treat this as a completely separate operation
            using (var ss = CreateSessionFactory().OpenSession())
            {
                // For everyone who's a parent of at least 30 yo, if at least 2 children of same sex, remove work phone, increment age
                var people2 = ss.CreateSQLQuery("EXEC CEFTest.up_Person_SummaryForParents NULL, NULL, 30").AddEntity(typeof(up_Person_SummaryForParents_Result)).List<up_Person_SummaryForParents_Result>();

                foreach (var p in (from a in people2 where a.MaleChildren > 1 || a.FemaleChildren > 1 select a))
                {
                    var p2 = ss.Get<Person>(p.PersonID);

                    p2.Age += 1;
                    p2.LastUpdatedBy = Environment.UserName;
                    p2.LastUpdatedDate = DateTime.UtcNow;

                    var ph2 = (from a in p2.Phones where a.PhoneTypeID == PhoneTypeEnum.Work select a).FirstOrDefault();

                    if (ph2 != null)
                    {
                        ss.Delete(ph2);
                        p2.Phones.Remove(ph2);
                        rowcount2 += 1;
                    }

                    ss.SaveOrUpdate(p2);
                    rowcount2 += 1;
                }

                ss.Flush();
            }

            watch.Stop();
            testTimes2.Add(watch.ElapsedMilliseconds);
        }

        public static void Benchmark1SavePer(int total_parents, List<long> testTimes, List<long> testTimes2, ref int rowcount, ref int rowcount2)
        {
            var watch = new System.Diagnostics.Stopwatch();
            watch.Start();

            HashSet<Person> people = new HashSet<Person>();

            // Attempting to parallelize: "Object reference not set to an instance of an object." deep inside nHibernate
            using (var ss = CreateSessionFactory().OpenSession())
            {
                for (int parentcnt = 1; parentcnt <= total_parents; ++parentcnt)
                {
                    var parent = new Person() { Name = $"NP{parentcnt}", Age = (parentcnt % 60) + 20, Gender = (parentcnt % 2) == 0 ? "F" : "M", LastUpdatedBy = Environment.UserName, LastUpdatedDate = DateTime.UtcNow };

                    parent.Phones.Add(new Phone() { Number = "888-7777", PhoneTypeID = PhoneTypeEnum.Mobile, LastUpdatedBy = Environment.UserName, LastUpdatedDate = DateTime.UtcNow });
                    parent.Phones.Add(new Phone() { Number = "777-6666", PhoneTypeID = PhoneTypeEnum.Work, LastUpdatedBy = Environment.UserName, LastUpdatedDate = DateTime.UtcNow });

                    if ((parentcnt % 12) == 0)
                    {
                        ss.Save(new Phone() { Number = "666-5555", PhoneTypeID = PhoneTypeEnum.Home, LastUpdatedBy = Environment.UserName, LastUpdatedDate = DateTime.UtcNow });
                    }
                    else
                    {
                        parent.Phones.Add(new Phone() { Number = "777-6666", PhoneTypeID = PhoneTypeEnum.Home, LastUpdatedBy = Environment.UserName, LastUpdatedDate = DateTime.UtcNow });
                    }

                    rowcount += 4;

                    ss.Save(parent);

                    for (int childcnt = 1; childcnt <= (parentcnt % 4); ++childcnt)
                    {
                        var child = new Person() { Name = $"NC{parentcnt}{childcnt}", Age = parent.Age - 20, Gender = (parentcnt % 2) == 0 ? "F" : "M", LastUpdatedBy = Environment.UserName, LastUpdatedDate = DateTime.UtcNow, ParentPersonID = parent.PersonID,
                            Phones = new Phone[] { new Phone() { Number = "999-8888", PhoneTypeID = PhoneTypeEnum.Mobile, LastUpdatedBy = Environment.UserName, LastUpdatedDate = DateTime.UtcNow } }};

                        parent.Kids.Add(child);
                        rowcount += 2;
                        ss.Save(child);
                    }

                    people.Add(parent);
                    ss.Flush();
                }
            }

            testTimes.Add(watch.ElapsedMilliseconds);
            watch.Restart();

            // For purposes of benchmarking, treat this as a completely separate operation
            using (var ss = CreateSessionFactory().OpenSession())
            {
                // For everyone who's a parent of at least 30 yo, if at least 2 children of same sex, remove work phone, increment age
                var people2 = ss.CreateSQLQuery("EXEC CEFTest.up_Person_SummaryForParents NULL, NULL, 30").AddEntity(typeof(up_Person_SummaryForParents_Result)).List<up_Person_SummaryForParents_Result>();

                foreach (var p in (from a in people2 where a.MaleChildren > 1 || a.FemaleChildren > 1 select a))
                {
                    var p2 = ss.Get<Person>(p.PersonID);

                    p2.Age += 1;
                    p2.LastUpdatedBy = Environment.UserName;
                    p2.LastUpdatedDate = DateTime.UtcNow;

                    var ph2 = (from a in p2.Phones where a.PhoneTypeID == PhoneTypeEnum.Work select a).FirstOrDefault();

                    if (ph2 != null)
                    {
                        ss.Delete(ph2);
                        p2.Phones.Remove(ph2);
                        rowcount2 += 1;
                    }

                    ss.SaveOrUpdate(p2);
                    rowcount2 += 1;
                    ss.Flush();
                }
            }

            watch.Stop();
            testTimes2.Add(watch.ElapsedMilliseconds);
        }
    }
}
