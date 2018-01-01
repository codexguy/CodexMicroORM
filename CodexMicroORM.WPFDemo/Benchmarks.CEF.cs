using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CodexMicroORM.Core;
using CodexMicroORM.Core.Services;
using CodexMicroORM.DemoObjects;

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
                        var child = CEF.NewObject(new PersonWrapped() { Name = $"NC{parentcnt}{childcnt}", Age = parent.Age - 20, Gender = (parentcnt % 2) == 0 ? "F" : "M"
                            , Phones = new Phone[] { new Phone() { Number = "999-8888", PhoneTypeID = PhoneType.Mobile } } });

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
                        var child = CEF.NewObject(new PersonWrapped() { Name = $"NC{parentcnt}{childcnt}", Age = parent.Age - 20, Gender = (parentcnt % 2) == 0 ? "F" : "M"
                            , Phones = new Phone[] { new Phone() { Number = "999-8888", PhoneTypeID = PhoneType.Mobile } } });

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
                        var child = CEF.NewObject(new PersonWrapped() { Name = $"NC{parentcnt}{childcnt}", Age = parent.Age - 20, Gender = (parentcnt % 2) == 0 ? "F" : "M"
                            , Phones = new Phone[] { new Phone() { Number = "999-8888", PhoneTypeID = PhoneType.Mobile } } });

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
    }
}
