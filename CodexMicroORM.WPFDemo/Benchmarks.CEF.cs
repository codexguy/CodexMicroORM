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
12/2017    0.2.1   Initial release (Joel Champagne)
***********************************************************************/
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CodexMicroORM.Core;
using CodexMicroORM.Core.Services;
using CodexMicroORM.DemoObjects;
using CodexMicroORM.Providers;

namespace CodexMicroORM.WPFDemo
{
    internal static class CEFBenchmarks
    {
        public static void Benchmark1(int total_parents, List<long> testTimes, List<long> testTimes2, ref int rowcount, ref int rowcount2)
        {
            var watch = new System.Diagnostics.Stopwatch();
            watch.Start();

            using (CEF.NewServiceScope())
            {
                var people = new EntitySet<PersonWrapped>();

                for (int parentcnt = 1; parentcnt <= total_parents; ++parentcnt)
                {
                    var parent = new PersonWrapped() { Name = $"NP{parentcnt}", Age = (parentcnt % 60) + 20, Gender = (parentcnt % 2) == 0 ? "F" : "M" };

                    parent.Phones.Add(new Phone() { Number = "888-7777", PhoneTypeID = PhoneType.Mobile });
                    parent.Phones.Add(new Phone() { Number = "777-6666", PhoneTypeID = PhoneType.Work });

                    if ((parentcnt % 12) == 0)
                    {
                        CEF.NewObject(new Phone() { Number = "666-5555", PhoneTypeID = PhoneType.Home });
                    }
                    else
                    {
                        parent.Phones.Add(new Phone() { Number = "777-6666", PhoneTypeID = PhoneType.Home });
                    }

                    rowcount += 4;

                    for (int childcnt = 1; childcnt <= (parentcnt % 4); ++childcnt)
                    {
                        var child = CEF.NewObject(new PersonWrapped()
                        {
                            Name = $"NC{parentcnt}{childcnt}",
                            Age = parent.Age - 20,
                            Gender = (parentcnt % 2) == 0 ? "F" : "M"
                            ,
                            Phones = new Phone[] { new Phone() { Number = "999-8888", PhoneTypeID = PhoneType.Mobile } }
                        });

                        parent.Kids.Add(child);
                        rowcount += 2;
                    }

                    people.Add(parent);
                }

                CEF.DBSave();
            }

            testTimes.Add(watch.ElapsedMilliseconds);
            watch.Restart();

            // For purposes of benchmarking, treat this as a completely separate operation
            using (var ss = CEF.NewServiceScope())
            {
                // For everyone who's a parent of at least 30 yo, if at least 2 children of same sex, remove work phone, increment age
                var people = new EntitySet<Person>().DBRetrieveSummaryForParents(30);

                Parallel.ForEach((from a in people let d = a.AsDynamic() where d.MaleChildren > 1 || d.FemaleChildren > 1 select a).ToList(), (p) =>
                {
                    using (CEF.UseServiceScope(ss))
                    {
                        p.Age += 1;

                        var phones = new EntitySet<Phone>().DBRetrieveByOwner(p.PersonID, PhoneType.Work);

                        if (phones.Any())
                        {
                            CEF.DeleteObject(phones.First());
                        }
                    }
                });

                rowcount2 += CEF.DBSave().Count();
            }

            watch.Stop();
            testTimes2.Add(watch.ElapsedMilliseconds);
        }

        public static void Benchmark1WithBulk(int total_parents, List<long> testTimes, List<long> testTimes2, ref int rowcount, ref int rowcount2)
        {
            var watch = new System.Diagnostics.Stopwatch();
            watch.Start();

            using (CEF.NewServiceScope())
            {
                var people = new EntitySet<Person>();

                for (int parentcnt = 1; parentcnt <= total_parents; ++parentcnt)
                {
                    var parent = new PersonWrapped() { Name = $"NP{parentcnt}", Age = (parentcnt % 60) + 20, Gender = (parentcnt % 2) == 0 ? "F" : "M" };

                    parent.Phones.Add(new Phone() { Number = "888-7777", PhoneTypeID = PhoneType.Mobile });
                    parent.Phones.Add(new Phone() { Number = "777-6666", PhoneTypeID = PhoneType.Work });

                    if ((parentcnt % 12) == 0)
                    {
                        CEF.NewObject(new Phone() { Number = "666-5555", PhoneTypeID = PhoneType.Home });
                    }
                    else
                    {
                        parent.Phones.Add(new Phone() { Number = "777-6666", PhoneTypeID = PhoneType.Home });
                    }

                    rowcount += 4;

                    for (int childcnt = 1; childcnt <= (parentcnt % 4); ++childcnt)
                    {
                        var child = CEF.NewObject(new PersonWrapped()
                        {
                            Name = $"NC{parentcnt}{childcnt}",
                            Age = parent.Age - 20,
                            Gender = (parentcnt % 2) == 0 ? "F" : "M"
                            ,
                            Phones = new Phone[] { new Phone() { Number = "999-8888", PhoneTypeID = PhoneType.Mobile } }
                        });

                        parent.Kids.Add(child);
                        rowcount += 2;
                    }

                    people.Add(parent);
                }

                CEF.DBSave(new DBSaveSettings().UseBulkInsertTypes(typeof(Phone)));
            }

            testTimes.Add(watch.ElapsedMilliseconds);
            watch.Restart();

            // For purposes of benchmarking, treat this as a completely separate operation
            using (var ss = CEF.NewServiceScope())
            {
                // For everyone who's a parent of at least 30 yo, if at least 2 children of same sex, remove work phone, increment age
                EntitySet<Person> people = new EntitySet<Person>().DBRetrieveSummaryForParents(30);

                Parallel.ForEach((from a in people let d = a.AsDynamic() where d.MaleChildren > 1 || d.FemaleChildren > 1 select a).ToList(), (p) =>
                {
                    using (CEF.UseServiceScope(ss))
                    {
                        p.Age += 1;

                        var ph = new EntitySet<Phone>().DBRetrieveByOwner(p.PersonID, PhoneType.Work).FirstOrDefault();

                        if (ph != null)
                        {
                            CEF.DeleteObject(ph);
                        }
                    }
                });

                rowcount2 += CEF.DBSave().Count();
            }

            watch.Stop();
            testTimes2.Add(watch.ElapsedMilliseconds);
        }

        public static void Benchmark1SavePer(int total_parents, List<long> testTimes, List<long> testTimes2, ref int rowcount, ref int rowcount2)
        {
            var watch = new System.Diagnostics.Stopwatch();
            watch.Start();
            long cnt1 = 0;

            var people = new EntitySet<PersonWrapped>();

            Parallel.For(1, total_parents + 1, (parentcnt) =>
            {
                using (CEF.NewServiceScope())
                {
                    var parent = CEF.NewObject(new PersonWrapped() { Name = $"NP{parentcnt}", Age = (parentcnt % 60) + 20, Gender = (parentcnt % 2) == 0 ? "F" : "M" });

                    parent.Phones.Add(new Phone() { Number = "888-7777", PhoneTypeID = PhoneType.Mobile });
                    parent.Phones.Add(new Phone() { Number = "777-6666", PhoneTypeID = PhoneType.Work });

                    if ((parentcnt % 12) == 0)
                    {
                        CEF.NewObject(new Phone() { Number = "666-5555", PhoneTypeID = PhoneType.Home });
                    }
                    else
                    {
                        parent.Phones.Add(new Phone() { Number = "777-6666", PhoneTypeID = PhoneType.Home });
                    }

                    Interlocked.Add(ref cnt1, 4);

                    for (int childcnt = 1; childcnt <= (parentcnt % 4); ++childcnt)
                    {
                        var child = CEF.NewObject(new PersonWrapped()
                        {
                            Name = $"NC{parentcnt}{childcnt}",
                            Age = parent.Age - 20,
                            Gender = (parentcnt % 2) == 0 ? "F" : "M"
                            ,
                            Phones = new Phone[] { new Phone() { Number = "999-8888", PhoneTypeID = PhoneType.Mobile } }
                        });

                        parent.Kids.Add(child);
                        Interlocked.Add(ref cnt1, 2);
                    }

                    CEF.DBSave();

                    lock (people)
                    {
                        people.Add(parent);
                    }
                }
            });

            rowcount += (int)cnt1;
            testTimes.Add(watch.ElapsedMilliseconds);
            watch.Restart();
            long cnt2 = 0;

            // For purposes of benchmarking, treat this as a completely separate operation
            // For everyone who's a parent of at least 30 yo, if at least 2 children of same sex, remove work phone, increment age
            EntitySet<Person> people2;

            using (CEF.NewServiceScope())
            {
                people2 = new EntitySet<Person>().DBRetrieveSummaryForParents(30);

                Parallel.ForEach((from a in people2 let d = a.AsDynamic() where d.MaleChildren > 1 || d.FemaleChildren > 1 select a).ToList(), (p) =>
                {
                    using (CEF.NewServiceScope())
                    {
                        CEF.IncludeObject(p);
                        p.Age += 1;

                        var ph2 = new EntitySet<Phone>().DBRetrieveByOwner(p.PersonID, PhoneType.Work).FirstOrDefault();

                        if (ph2 != null)
                        {
                            CEF.DeleteObject(ph2);
                        }

                        Interlocked.Add(ref cnt2, CEF.DBSave().Count());
                    }
                });
            }

            rowcount2 += (int)cnt2;
            watch.Stop();
            testTimes2.Add(watch.ElapsedMilliseconds);
        }

        public static void Benchmark2Setup(int total_parents)
        {
            for (int i = 1; i <= total_parents; ++i)
            {
                using (CEF.NewServiceScope())
                {
                    var p = CEF.NewObject(new Person()
                    {
                        Name = $"P{i}",
                        Age = (i % 70) + 20,
                        Kids = new Person[] { new Person() { Name = $"C{i}", Age = (i % 70) / 2,
                            Phones = new Phone[] { new Phone() { Number = "333-3333", PhoneTypeID = PhoneType.Home } } } },
                        Phones = new Phone[] { new Phone() { Number = "444-4444", PhoneTypeID = PhoneType.Home },
                            new Phone() { Number = "777-8888", PhoneTypeID = PhoneType.Work } }
                    });

                    if ((i % 2) == 0)
                    {
                        p.Phones.Add(new Phone() { Number = "510-555-5555", PhoneTypeID = PhoneType.Mobile });
                    }

                    CEF.DBSave();
                }
            }
        }

        public static void Benchmark2(int total_parents, List<long> testTimes, List<long> testTimes2, ref int rowcount, ref int rowcount2)
        {
            MemoryFileSystemBacked.FlushAll("CEF_Demo");

            // Data set-up is not timed here...
            Benchmark2Setup(total_parents);

            var watch = new System.Diagnostics.Stopwatch();
            watch.Start();

            // With caching enabled, we make 3 passes over the data where we a) use a query to get all parents (only), b) call a method that represents some API to get all phones for said parent, c) increment age on parents where they have a mobile phone, d) apply a possible update for the phones to modify their numbers based on another api method that only accepts a PhoneID (i.e. need to reretrieve some data)
            for (int j = 1; j <= 2; ++j)
            {
                using (CEF.NewServiceScope(new ServiceScopeSettings() { UseAsyncSave = true }))
                {
                    var parents = new EntitySet<Person>().DBRetrieveSummaryForParents(30);

                    foreach (var parent in parents)
                    {
                        var phones = new EntitySet<Phone>().DBRetrieveByOwner(parent.PersonID, null);
                        rowcount += 1;

                        if ((from a in phones where a.PhoneTypeID == PhoneType.Mobile select a).Any())
                        {
                            parent.Age += 1;
                            parent.DBSave(false);
                            rowcount += 1;
                        }

                        foreach (var phone in phones)
                        {
                            string area = "";

                            switch (phone.PhoneTypeID)
                            {
                                case PhoneType.Home:
                                    area = "707";
                                    break;

                                case PhoneType.Mobile:
                                    area = "415";
                                    break;

                                case PhoneType.Work:
                                    area = "800";
                                    break;
                            }

                            UpdatePhoneAPITest1(phone.AsDynamic().PhoneID, area, ref rowcount);

                            if (!TestValidPhoneAPITest2(phone.AsDynamic().PhoneID, parent.PersonID, ref rowcount))
                            {
                                throw new Exception("Failure!");
                            }
                        }
                    }
                }
            }

            watch.Stop();
            testTimes.Add(watch.ElapsedMilliseconds);

            // Extra verification that results match expected
            if (!Benchmark2Verify(total_parents))
            {
                throw new Exception("Unexpected final result.");
            }
        }

        private static void UpdatePhoneAPITest1(int phoneID, string area, ref int rowcount)
        {
            using (CEF.NewOrCurrentServiceScope())
            {
                var phones = new EntitySet<Phone>().DBRetrieveByKey(phoneID);
                var phone = phones.FirstOrDefault();
                rowcount += 1;

                if (phone != null)
                {
                    string oldPhone = phone.Number;

                    if (!string.IsNullOrEmpty(phone.Number) && (phone.Number.Length != 12 || !phone.Number.StartsWith(area)))
                    {
                        if (phone.Number.Length == 8)
                        {
                            phone.Number = area + "-" + phone.Number;
                        }
                        else
                        {
                            if (phone.Number.Length == 12)
                            {
                                phone.Number = area + "-" + phone.Number.Substring(4, 8);
                            }
                        }

                        if (oldPhone != phone.Number)
                        {
                            phones.DBSave();
                            rowcount += 1;
                        }
                    }
                }
            }
        }

        private static bool TestValidPhoneAPITest2(int phoneID, int personID, ref int rowcount)
        {
            using (CEF.NewOrCurrentServiceScope())
            {
                var phones = new EntitySet<Phone>().DBRetrieveByKey(phoneID);
                var phone = phones.FirstOrDefault();
                rowcount += 1;

                if (phone != null)
                {
                    if (phone.AsDynamic().PersonID == personID)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public static bool Benchmark2Verify(int total_parents)
        {
            using (CEF.NewServiceScope())
            {
                return (CEF.CurrentDBService().ExecuteScalar<long>("SELECT (SELECT SUM(CONVERT(bigint, REPLACE(number, '-', ''))) FROM CEFTest.Phone) + (SELECT SUM(age) FROM CEFTest.Person)") == (total_parents == 3000 ? 45228273544212 : 90442427088467));
            }
        }
    }
}