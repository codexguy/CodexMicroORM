using CodexMicroORM.Core;
using CodexMicroORM.Core.Services;
using CodexMicroORM.DemoObjects;
using CodexMicroORM.Providers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

namespace BaseTests
{
    [TestClass]
    public class StandaloneTests
    {
        [TestMethod]
        public async Task ExecWithWaitAsync()
        {
            StringBuilder sb = new();

            var d = async (CancellationToken ct) =>
            {
                sb.Append("point 0;");
                await Task.Delay(6000);
                sb.Append("point 1;");
                ct.ThrowIfCancellationRequested();
                sb.Append("point 2;");
            };

            var r1 = await d.ExecuteWithMaxWaitAsync(5000);
            sb.Append($"result {r1};");
            sb = new();

            var d2 = async (CancellationToken ct) =>
            {
                for (int i = 1; i < 6; ++i)
                {
                    await Task.Delay(1000, ct);
                    sb.Append($"point2 {i};");
                }
            };

            var r2 = await d2.ExecuteWithMaxWaitAsync(5000);
            sb.Append($"result {r2};");
            sb = new();

            var d3 = (CancellationToken ct) =>
            {
                sb.Append("point3a;");
                System.Threading.Thread.Sleep(100);
                sb.Append("point3b;");
                ct.ThrowIfCancellationRequested();
                sb.Append("point3c;");
            };

            var r3 = await d3.ExecuteWithMaxWaitAsync(5000);
            sb.Append($"result {r3};");
            sb = new();

            int c = 0;

            var d4 = (CancellationToken ct) =>
            {
                sb.Append($"point4 {c};");
                ++c;
                ct.ThrowIfCancellationRequested();
                if (c < 3)
                {
                    throw new ApplicationException("some issue");
                }
                System.Threading.Thread.Sleep(100);
                ct.ThrowIfCancellationRequested();
                sb.Append("done;");
                ct.ThrowIfCancellationRequested();
            };

            var r4 = await d4.ExecuteWithMaxWaitAsync(10000, true, null, 5);
            sb.Append($"result {r4};");

            Console.WriteLine(sb.ToString());
        }
    }

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
