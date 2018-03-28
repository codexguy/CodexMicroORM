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
12/2017    0.2.1   Initial release (Joel Champagne)
***********************************************************************/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Data.Entity;

using CodexMicroORM.WPFDemo.EFObjectModel;
using System.Windows;

namespace CodexMicroORM.WPFDemo
{
    internal static class EFBenchmarks
    {
        public static void Benchmark1(int total_parents, List<long> testTimes, List<long> testTimes2, ref int rowcount, ref int rowcount2)
        {
            var watch = new System.Diagnostics.Stopwatch();
            watch.Start();

            using (var ctx = new CodexMicroORMTestEntities())
            {
                for (int parentcnt = 1; parentcnt <= total_parents; ++parentcnt)
                {
                    var parent = new Person() { Name = $"NP{parentcnt}", Age = (parentcnt % 60) + 20, Gender = (parentcnt % 2) == 0 ? "F" : "M", LastUpdatedBy = Environment.UserName, LastUpdatedDate = DateTime.UtcNow };

                    parent.Phones.Add(new Phone() { Number = "888-7777", PhoneTypeID = PhoneTypeEnum.Mobile, LastUpdatedBy = Environment.UserName, LastUpdatedDate = DateTime.UtcNow });
                    parent.Phones.Add(new Phone() { Number = "777-6666", PhoneTypeID = PhoneTypeEnum.Work, LastUpdatedBy = Environment.UserName, LastUpdatedDate = DateTime.UtcNow });

                    if ((parentcnt % 12) == 0)
                    {
                        ctx.Phones.Add(new Phone() { Number = "666-5555", PhoneTypeID = PhoneTypeEnum.Home, LastUpdatedBy = Environment.UserName, LastUpdatedDate = DateTime.UtcNow });
                    }
                    else
                    {
                        parent.Phones.Add(new Phone() { Number = "777-6666", PhoneTypeID = PhoneTypeEnum.Home, LastUpdatedBy = Environment.UserName, LastUpdatedDate = DateTime.UtcNow });
                    }

                    rowcount += 4;

                    for (int childcnt = 1; childcnt <= (parentcnt % 4); ++childcnt)
                    {
                        var child = new Person() { Name = $"NC{parentcnt}{childcnt}", Age = parent.Age - 20, Gender = (parentcnt % 2) == 0 ? "F" : "M", LastUpdatedBy = Environment.UserName, LastUpdatedDate = DateTime.UtcNow,
                            Phones = new Phone[] { new Phone() { Number = "999-8888", PhoneTypeID = PhoneTypeEnum.Mobile, LastUpdatedBy = Environment.UserName, LastUpdatedDate = DateTime.UtcNow } }};

                        parent.Kids.Add(child);
                        rowcount += 2;
                    }

                    ctx.People.Add(parent);
                }

                ctx.SaveChanges();
            }

            testTimes.Add(watch.ElapsedMilliseconds);
            watch.Restart();

            // For purposes of benchmarking, treat this as a completely separate operation
            using (var ctx = new CodexMicroORMTestEntities())
            {
                // For everyone who's a parent of at least 30 yo, if at least 2 children of same sex, remove work phone, increment age
                var list = (from p in ctx.People where p.Age >= 30 from c in p.Kids group c by new { Person = p, c.Gender } into g where g.Count() > 1 select g.Key.Person);

                foreach (var p in list)
                {
                    p.Age += 1;
                    p.LastUpdatedDate = DateTime.UtcNow;
                    p.LastUpdatedBy = Environment.UserName;

                    var toDelete = (from a in p.Phones where a.PhoneTypeID == PhoneTypeEnum.Work select a).FirstOrDefault();

                    if (toDelete != null)
                    {
                        ctx.Entry(toDelete).State = EntityState.Deleted;
                    }
                }

                rowcount2 += ctx.SaveChanges();
            }

            watch.Stop();
            testTimes2.Add(watch.ElapsedMilliseconds);
        }

        public static void Benchmark1SavePer(int total_parents, List<long> testTimes, List<long> testTimes2, ref int rowcount, ref int rowcount2)
        {
            var watch = new System.Diagnostics.Stopwatch();
            watch.Start();

            // Attempted parallelization gives "New transaction is not allowed because there are other threads running in the session." deep inside EF
            for (int parentcnt = 1; parentcnt <= total_parents; ++parentcnt)
            {
                using (var ctx = new CodexMicroORMTestEntities())
                {
                    var parent = new Person() { Name = $"NP{parentcnt}", Age = (parentcnt % 60) + 20, Gender = (parentcnt % 2) == 0 ? "F" : "M", LastUpdatedBy = Environment.UserName, LastUpdatedDate = DateTime.UtcNow };

                    parent.Phones.Add(new Phone() { Number = "888-7777", PhoneTypeID = PhoneTypeEnum.Mobile, LastUpdatedBy = Environment.UserName, LastUpdatedDate = DateTime.UtcNow });
                    parent.Phones.Add(new Phone() { Number = "777-6666", PhoneTypeID = PhoneTypeEnum.Work, LastUpdatedBy = Environment.UserName, LastUpdatedDate = DateTime.UtcNow });

                    if ((parentcnt % 12) == 0)
                    {
                        ctx.Phones.Add(new Phone() { Number = "666-5555", PhoneTypeID = PhoneTypeEnum.Home, LastUpdatedBy = Environment.UserName, LastUpdatedDate = DateTime.UtcNow });
                    }
                    else
                    {
                        parent.Phones.Add(new Phone() { Number = "777-6666", PhoneTypeID = PhoneTypeEnum.Home, LastUpdatedBy = Environment.UserName, LastUpdatedDate = DateTime.UtcNow });
                    }

                    rowcount += 4;

                    for (int childcnt = 1; childcnt <= (parentcnt % 4); ++childcnt)
                    {
                        var child = new Person() { Name = $"NC{parentcnt}{childcnt}", Age = parent.Age - 20, Gender = (parentcnt % 2) == 0 ? "F" : "M", LastUpdatedBy = Environment.UserName, LastUpdatedDate = DateTime.UtcNow,
                            Phones = new Phone[] { new Phone() { Number = "999-8888", PhoneTypeID = PhoneTypeEnum.Mobile, LastUpdatedBy = Environment.UserName, LastUpdatedDate = DateTime.UtcNow } }};

                        parent.Kids.Add(child);
                        rowcount += 2;
                    }

                    ctx.People.Add(parent);
                    ctx.SaveChanges();
                }
            }

            testTimes.Add(watch.ElapsedMilliseconds);
            watch.Restart();

            // For purposes of benchmarking, treat this as a completely separate operation
            using (var ctx = new CodexMicroORMTestEntities())
            {
                // For everyone who's a parent of at least 30 yo, if at least 2 children of same sex, remove work phone, increment age
                // Note: ToList required or can lead to errors on save
                var list = (from p in ctx.People where p.Age >= 30 from c in p.Kids group c by new { Person = p, c.Gender } into g where g.Count() > 1 select g.Key.Person).ToList();

                // Attempted parallelization gives "Object reference not set to an instance of an object." deep inside EF
                foreach (var p in list)
                {
                    p.Age += 1;
                    p.LastUpdatedDate = DateTime.UtcNow;
                    p.LastUpdatedBy = Environment.UserName;

                    var toDelete = (from a in p.Phones where a.PhoneTypeID == PhoneTypeEnum.Work select a).FirstOrDefault();

                    if (toDelete != null)
                    {
                        ctx.Entry(toDelete).State = EntityState.Deleted;
                    }

                    rowcount2 += ctx.SaveChanges();
                }
            }

            watch.Stop();
            testTimes2.Add(watch.ElapsedMilliseconds);
        }

        public static void Benchmark3SavePer(int total_parents, List<long> testTimes, List<long> testTimes2, ref int rowcount, ref int rowcount2)
        {
            var watch = new System.Diagnostics.Stopwatch();
            watch.Start();

            // Attempted parallelization gives "New transaction is not allowed because there are other threads running in the session." deep inside EF
            for (int parentcnt = 1; parentcnt <= total_parents; ++parentcnt)
            {
                using (var ctx = new CodexMicroORMTestEntities())
                {
                    var parent = new Person() { Name = $"NP{parentcnt}", Age = (parentcnt % 60) + 20, Gender = (parentcnt % 2) == 0 ? "F" : "M", LastUpdatedBy = Environment.UserName, LastUpdatedDate = DateTime.UtcNow };

                    parent.Phones.Add(new Phone() { Number = "888-7777", PhoneTypeID = PhoneTypeEnum.Mobile, LastUpdatedBy = Environment.UserName, LastUpdatedDate = DateTime.UtcNow });
                    parent.Phones.Add(new Phone() { Number = "777-6666", PhoneTypeID = PhoneTypeEnum.Work, LastUpdatedBy = Environment.UserName, LastUpdatedDate = DateTime.UtcNow });

                    if ((parentcnt % 12) == 0)
                    {
                        ctx.Phones.Add(new Phone() { Number = "666-5555", PhoneTypeID = PhoneTypeEnum.Home, LastUpdatedBy = Environment.UserName, LastUpdatedDate = DateTime.UtcNow });
                    }
                    else
                    {
                        parent.Phones.Add(new Phone() { Number = "777-6666", PhoneTypeID = PhoneTypeEnum.Home, LastUpdatedBy = Environment.UserName, LastUpdatedDate = DateTime.UtcNow });
                    }

                    rowcount += 4;

                    for (int childcnt = 1; childcnt <= (parentcnt % 4); ++childcnt)
                    {
                        var child = new Person()
                        {
                            Name = $"NC{parentcnt}{childcnt}",
                            Age = parent.Age - 20,
                            Gender = (parentcnt % 2) == 0 ? "F" : "M",
                            LastUpdatedBy = Environment.UserName,
                            LastUpdatedDate = DateTime.UtcNow,
                            Phones = new Phone[] { new Phone() { Number = "999-8888", PhoneTypeID = PhoneTypeEnum.Mobile, LastUpdatedBy = Environment.UserName, LastUpdatedDate = DateTime.UtcNow } }
                        };

                        parent.Kids.Add(child);
                        rowcount += 2;
                    }

                    ctx.People.Add(parent);
                    ctx.SaveChanges();
                }
            }

            testTimes.Add(watch.ElapsedMilliseconds);
            watch.Restart();

            // For purposes of benchmarking, treat this as a completely separate operation
            using (var ctx = new CodexMicroORMTestEntities())
            {
                int? id = null;

                // For everyone who's a parent of at least 30 yo, if at least 2 children of same sex, remove work phone, increment age
                // Note: ToList required or can lead to errors on save
                var list = (from p in ctx.People where p.Age >= 30 from c in p.Kids group c by new { Person = p, c.Gender } into g where g.Count() > 1 select g.Key.Person).ToList();

                // Attempted parallelization gives "Object reference not set to an instance of an object." deep inside EF
                foreach (var p in list)
                {
                    if (!id.HasValue)
                    {
                        id = p.PersonID;
                    }

                    p.Age += 1;
                    p.LastUpdatedDate = DateTime.UtcNow;
                    p.LastUpdatedBy = Environment.UserName;

                    var toDelete = (from a in p.Phones where a.PhoneTypeID == PhoneTypeEnum.Work select a).FirstOrDefault();

                    if (toDelete != null)
                    {
                        ctx.Entry(toDelete).State = EntityState.Deleted;
                    }

                    rowcount2 += ctx.SaveChanges();
                }

                // Simulate "later heavy read access"...
                for (int c = 0; c < 50000; ++c)
                {
                    var work = (from p in list where p.PersonID == c + id.GetValueOrDefault() from ph in p.Phones where ph.PhoneTypeID == PhoneTypeEnum.Work select ph).FirstOrDefault();

                    if (work != null && work.Number == "123")
                    {
                        MessageBox.Show("Found (should never!)");
                    }
                }
            }

            rowcount2 += 50000;

            watch.Stop();
            testTimes2.Add(watch.ElapsedMilliseconds);
        }
    }
}
