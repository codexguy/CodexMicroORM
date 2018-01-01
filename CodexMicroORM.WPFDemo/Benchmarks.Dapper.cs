using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using CodexMicroORM.Core;
using CodexMicroORM.Core.Services;
using CodexMicroORM.DemoObjects;
using Dapper;
using System.Data.SqlClient;
using System.Data;
using System.Collections.Concurrent;

namespace CodexMicroORM.WPFDemo
{
    internal static class DapperBenchmarks
    {
        public static void Benchmark1(int total_parents, List<long> testTimes, List<long> testTimes2, ref int rowcount, ref int rowcount2)
        {
            var watch = new System.Diagnostics.Stopwatch();
            watch.Start();
            long cnt1 = 0;

            string connstring = @"Data Source=(local)\sql2016;Database=CodexMicroORMTest;Integrated Security=SSPI;MultipleActiveResultSets=true";
            ConcurrentBag<PersonWrapped> people = new ConcurrentBag<PersonWrapped>();

            Parallel.For(1, total_parents + 1, (parentcnt) =>
            {
                using (IDbConnection db = new SqlConnection(connstring))
                {
                    var parent = new PersonWrapped() { Name = $"NP{parentcnt}", Age = (parentcnt % 60) + 20, Gender = (parentcnt % 2) == 0 ? "F" : "M", LastUpdatedBy = Environment.UserName, LastUpdatedDate = DateTime.UtcNow };
                    parent.PersonID = db.ExecuteScalar<int>("INSERT CEFTest.Person ([Name],Age,Gender,LastUpdatedBy,LastUpdatedDate) VALUES (@Name,@Age,@Gender,@LastUpdatedBy,@LastUpdatedDate); SELECT SCOPE_IDENTITY();", parent);

                    Interlocked.Add(ref cnt1, 4);

                    var ph1 = new Phone() { Number = "888-7777", PhoneTypeID = PhoneType.Mobile };
                    parent.Phones.Add(ph1);
                    db.Execute("INSERT CEFTest.Phone (Number,PhoneTypeID,PersonID,LastUpdatedBy,LastUpdatedDate) VALUES (@Number,@PhoneTypeID,@PersonID,@LastUpdatedBy,@LastUpdatedDate)", new { ph1.Number, PhoneTypeID = (int)ph1.PhoneTypeID, parent.PersonID, LastUpdatedBy = Environment.UserName, LastUpdatedDate = DateTime.UtcNow });

                    var ph2 = new Phone() { Number = "777-6666", PhoneTypeID = PhoneType.Work };
                    parent.Phones.Add(ph2);
                    db.Execute("INSERT CEFTest.Phone (Number,PhoneTypeID,PersonID,LastUpdatedBy,LastUpdatedDate) VALUES (@Number,@PhoneTypeID,@PersonID,@LastUpdatedBy,@LastUpdatedDate)", new { ph2.Number, PhoneTypeID = (int)ph2.PhoneTypeID, parent.PersonID, LastUpdatedBy = Environment.UserName, LastUpdatedDate = DateTime.UtcNow });

                    if ((parentcnt % 12) == 0)
                    {
                        db.Execute($"INSERT CEFTest.Phone (Number,PhoneTypeID,LastUpdatedBy,LastUpdatedDate) VALUES (@Number,@PhoneTypeID,@LastUpdatedBy,@LastUpdatedDate)", new { Number = "666-5555", PhoneTypeID = PhoneType.Home, LastUpdatedBy = Environment.UserName, LastUpdatedDate = DateTime.UtcNow });
                    }
                    else
                    {
                        var ph3 = new Phone() { Number = "777-6666", PhoneTypeID = PhoneType.Home };
                        parent.Phones.Add(ph3);
                        db.Execute("INSERT CEFTest.Phone (Number,PhoneTypeID,PersonID,LastUpdatedBy,LastUpdatedDate) VALUES (@Number,@PhoneTypeID,@PersonID,@LastUpdatedBy,@LastUpdatedDate)", new { ph3.Number, PhoneTypeID = (int)ph3.PhoneTypeID, parent.PersonID, LastUpdatedBy = Environment.UserName, LastUpdatedDate = DateTime.UtcNow });
                    }

                    for (int childcnt = 1; childcnt <= (parentcnt % 4); ++childcnt)
                    {
                        var child = new PersonWrapped() { Name = $"NC{parentcnt}{childcnt}", Age = parent.Age - 20, Gender = (parentcnt % 2) == 0 ? "F" : "M", LastUpdatedBy = Environment.UserName, LastUpdatedDate = DateTime.UtcNow, ParentPersonID = parent.PersonID };
                        child.PersonID = db.ExecuteScalar<int>("INSERT CEFTest.Person ([Name],Age,Gender,ParentPersonID,LastUpdatedBy,LastUpdatedDate) VALUES (@Name,@Age,@Gender,@ParentPersonID,@LastUpdatedBy,@LastUpdatedDate); SELECT SCOPE_IDENTITY();", child);
                        parent.Kids.Add(child);

                        var ph4 = new Phone() { Number = "999-8888", PhoneTypeID = PhoneType.Mobile };
                        child.Phones = new Phone[] { ph4 };
                        db.Execute("INSERT CEFTest.Phone (Number,PhoneTypeID,LastUpdatedBy,LastUpdatedDate) VALUES (@Number,@PhoneTypeID,@LastUpdatedBy,@LastUpdatedDate)", new { ph4.Number, PhoneTypeID = (int)ph4.PhoneTypeID, LastUpdatedBy = Environment.UserName, LastUpdatedDate = DateTime.UtcNow });

                        Interlocked.Add(ref cnt1, 2);
                    }

                    people.Add(parent);
                }
            });

            rowcount += (int)cnt1;
            testTimes.Add(watch.ElapsedMilliseconds);
            watch.Restart();
            long cnt2 = 0;

            // For everyone who's a parent of at least 30 yo, if at least 2 children of same sex, remove work phone, increment age
            using (IDbConnection db = new SqlConnection(connstring))
            {
                var people2 = db.Query("CEFTest.up_Person_SummaryForParents", new { RetVal = 1, Msg = "", MinimumAge = 30 }, commandType: CommandType.StoredProcedure);

                Parallel.ForEach((from d in people2 where d.MaleChildren > 1 || d.FemaleChildren > 1 select d).ToList(), (p) =>
                {
                    using (IDbConnection db2 = new SqlConnection(connstring))
                    {
                        p.Age += 1;
                        db2.Execute("UPDATE CEFTest.Person SET Age = @Age, LastUpdatedBy = @LastUpdatedBy, LastUpdatedDate = @LastUpdatedDate WHERE PersonID = @PersonID", new { p.PersonID, p.Age, LastUpdatedBy = Environment.UserName, LastUpdatedDate = DateTime.UtcNow });
                        Interlocked.Add(ref cnt2, 1);

                        var ph2 = db2.Query("CEFTest.up_Phone_ByPersonID", new { RetVal = 1, Msg = "", p.PersonID, PhoneTypeID = (int)PhoneType.Work }, commandType: CommandType.StoredProcedure).FirstOrDefault();

                        if (ph2 != null)
                        {
                            db2.Execute("DELETE CEFTest.Phone WHERE PhoneID=@PhoneID", new { ph2.PhoneID });
                            Interlocked.Add(ref cnt2, 1);
                        }
                    }
                });
            }

            rowcount2 += (int)cnt2;
            watch.Stop();
            testTimes2.Add(watch.ElapsedMilliseconds);
        }
    }
}
