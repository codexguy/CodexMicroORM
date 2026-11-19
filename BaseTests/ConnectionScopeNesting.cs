using CodexMicroORM.Core;
using CodexMicroORM.Core.Services;
using CodexMicroORM.Providers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Threading.Tasks;

namespace CodexMicroORM.BaseTests
{
    /// <summary>
    /// A connection scope opened around a service scope belongs to its opener. With per-thread connection scopes (the
    /// setting a web host uses), a service scope opened and disposed INLINE inside a connection scope used to dispose and
    /// pop that enclosing connection scope on its way out, so every later command in the caller ran without the scope's
    /// CommandTimeoutOverride (and on a fresh standalone scope). A service scope now closes only a connection scope it did
    /// not inherit. Please note: like Modern12, these set globals and should be run independently of DBOps.cs.
    /// </summary>
    [TestClass]
    public class ConnectionScopeNesting
    {
        private const string DB_SERVER = @"(local)\sql2016";

        public ConnectionScopeNesting()
        {
            Globals.ConnectionScopePerThread = true;
            Globals.UseTransactionsForNewScopes = false;
            Globals.UseAsyncSave = false;
            Globals.CommandTimeoutSeconds = 120;

            CEF.AddGlobalService(DBService.Create(new MSSQLProcBasedProvider($@"Data Source={DB_SERVER};Database=CodexMicroORMTest;Integrated Security=SSPI;MultipleActiveResultSets=true;TrustServerCertificate=true", defaultSchema: "CEFTest")));
        }

        [TestMethod]
        public void TimeoutOverride_SurvivesAnInlineNestedServiceScope()
        {
            using var ss = CEF.NewServiceScope();
            using (CEF.NewConnectionScope(new ConnectionScopeSettings() { CommandTimeoutOverride = 1 }))
            {
                using (CEF.NewServiceScope())
                {
                    Assert.AreEqual(1, CEF.CurrentDBService().ExecuteScalar<int>("SELECT 1"));
                }

                Assert.AreEqual(1, CEF.CurrentConnectionScope.TimeoutOverride, "the enclosing connection scope must still be current after the nested service scope");

                // and on the wire: the 1 s override still stops a 3 s command
                var ex = Assert.ThrowsExactly<Microsoft.Data.SqlClient.SqlException>(() => CEF.CurrentDBService().ExecuteScalar<int>("WAITFOR DELAY '00:00:03'; SELECT 1"));
                StringAssert.Contains(ex.Message, "imeout");
            }
        }

        [TestMethod]
        public async Task TimeoutOverride_SurvivesANestedServiceScopeInAnAwaitedMethod()
        {
            using var ss = CEF.NewServiceScope();
            using (CEF.NewConnectionScope(new ConnectionScopeSettings() { CommandTimeoutOverride = 7 }))
            {
                await NestedRead();
                Assert.AreEqual(7, CEF.CurrentConnectionScope.TimeoutOverride);
            }
        }

        [TestMethod]
        public void ANestedServiceScope_StillClosesAConnectionScopeOpenedInsideIt()
        {
            using var ss = CEF.NewServiceScope();
            using (CEF.NewConnectionScope(new ConnectionScopeSettings() { CommandTimeoutOverride = 7 }))
            {
                ConnectionScope inner;
                using (CEF.NewServiceScope())
                {
                    // opened and never disposed by its caller: the service scope around it is what closes it
                    inner = CEF.NewConnectionScope(new ConnectionScopeSettings() { CommandTimeoutOverride = 9 });
                    Assert.AreEqual(1, CEF.CurrentDBService().ExecuteScalar<int>("SELECT 1"));
                    Assert.AreEqual(9, CEF.CurrentConnectionScope.TimeoutOverride);
                }

                Assert.AreNotSame(inner, CEF.CurrentConnectionScope, "the inner connection scope is closed and popped");
                Assert.AreEqual(7, CEF.CurrentConnectionScope.TimeoutOverride, "and the enclosing one is current again");
            }
        }

        private static async Task NestedRead()
        {
            await Task.Yield();
            using var inner = CEF.NewServiceScope();
            Assert.AreEqual(1, CEF.CurrentDBService().ExecuteScalar<int>("SELECT 1"));
        }
    }
}
