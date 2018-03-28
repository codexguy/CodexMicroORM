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
12/2017    0.2     Initial release (Joel Champagne)
***********************************************************************/
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Data;
using System.IO;
using CodexMicroORM.Core;
using CodexMicroORM.Core.Services;
using CodexMicroORM.DemoObjects;
using CodexMicroORM.Providers;
using CodexMicroORM.BindingSupport;
using System.Collections.Generic;

namespace CodexMicroORM.WPFDemo
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private const string DB_SERVER = @".\sql2016";

        GenericBindableSet _bindableFamilies;

        public MainWindow()
        {
            InitializeComponent();

            InitializeFramework();

            Benchmark.SelectedIndex = 0;
            Rows.SelectedIndex = 0;
        }

        /// <summary>
        /// This is intended to be called once on app startup. The goal is to have this be 99% code generated.
        /// </summary>
        private void InitializeFramework()
        {
            // Tells CEF that we do have code gen available that supports notifications
            // The {0}Wrapped pattern tells CEF that "Person" as a POCO class is wrapped by the "PersonWrapped" class - and/or you can use namespace differences
            Globals.WrapperSupports = WrappingSupport.Notifications;
            Globals.WrappingClassNamespace = null;
            Globals.WrapperClassNamePattern = "{0}Wrapped";

            // Default audit service would use Environment's username but doing this to illustrate; plus for last updated date, default is to use UTC date/time but we've changed here to use local time
            // DBservice in this case initialized based on a single connection string to use throughout, future examples will demo multiple databases; default schema is "dbo", we'll override for demo purposes
            CEF.AddGlobalService(new AuditService(() => { return "Me: " + Environment.UserName; }));
            CEF.AddGlobalService(new DBService(new MSSQLProcBasedProvider($@"Data Source={DB_SERVER};Database=CodexMicroORMTest;Integrated Security=SSPI;MultipleActiveResultSets=true", defaultSchema: "CEFTest")));

            // Establish primary keys based on types (notice for Phone we don't care about the PhoneID in the object model - do in the database but mCEF handles that!)
            KeyService.RegisterKey<Person>(nameof(Person.PersonID));
            KeyService.RegisterKey<Phone>("PhoneID");

            // Establish all the relationships we know about via database and/or object model
            KeyService.RegisterRelationship<Person>(TypeChildRelationship.Create<Person>("ParentPersonID").MapsToChildProperty(nameof(Person.Kids)));
            KeyService.RegisterRelationship<Person>(TypeChildRelationship.Create<Phone>().MapsToParentProperty(nameof(Phone.Owner)).MapsToChildProperty(nameof(Person.Phones)));

            // Restrict person age to a range
            ValidationService.RegisterCustomValidation((Person p) =>
            {
                if (p.Age < 0 || p.Age > 120)
                {
                    return "Age must be between 0 and 120.";
                }
                return null;
            }, nameof(Person.Age));

            // This will construct a new test database, if needed - if the script changes, you'll need to drop the CodexMicroORMTest database before running
            using (CEF.NewConnectionScope(new ConnectionScopeSettings() { IsTransactional = false, ConnectionStringOverride = $@"Data Source={DB_SERVER};Database=master;Integrated Security=SSPI;MultipleActiveResultSets=true" }))
            {
                CEF.CurrentDBService().ExecuteRaw(File.ReadAllText("setup.sql"), false);
            }
        }

        private void ConsoleWriteLine(string line)
        {
            this.Dispatcher.Invoke(new Action(() =>
            {
                var newLine = new ListBoxItem() { Content = line };
                ConsoleList.Items.Add(newLine);
                ConsoleList.ScrollIntoView(newLine);
            }));
        }

        private void StartTests_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ConsoleList.Items.Clear();
                StartTests.IsEnabled = false;
                RunBenchmark.IsEnabled = false;

                // If did a prior run, clear out ambient scope which is bound to the UI thread
                CEF.CurrentServiceScope.Dispose();

                // Execute tear-down/set-up of test SQL objects from script
                CEF.CurrentDBService().ExecuteRaw("DELETE CEFTest.Phone; UPDATE CEFTest.Person SET IsDeleted=1, LastUpdatedDate=GETUTCDATE(), LastUpdatedBy='Test';");

                var random = new Random();
                var watch = new System.Diagnostics.Stopwatch();
                watch.Start();

                // Creates and saves a person in 2 lines of code!
                // Of note: no need for a context, we're using the implicit one created in TLS (great for a simple console app, recommended is to use explicit scopes)
                var tristan = CEF.NewObject(new Person() { Name = "Tristan", Age = 4, Gender = "M" });
                ConsoleWriteLine($"Rows saved: {CEF.DBSave().Count()}");
                ConsoleWriteLine($"A PersonID key as assigned by the database has been round-tripped back to us: {tristan.PersonID}");
                ConsoleWriteLine($"And LastUpdatedDate as assigned by the database too, despite not being in our base POCO: {((PersonWrapped)tristan).LastUpdatedDate}");

                // Creates and saves a person similar to above, but using wrapper object directly is fine too - and we've got an extension method that lets us save in 1 line of code!
                var zella = CEF.NewObject(new PersonWrapped() { Name = "Zella", Age = 7, Gender = "F" }).DBSave();
                ConsoleWriteLine($"Similar to above, but already working with genned wrapper so no need to cast it: {zella.LastUpdatedDate}");

                // We have the option to indicate whether the object should be considered new or not with respect to db, when adding to scope (CreateObject on the other hand is always "new") - in reality we could have used NewObject here too
                var sally = new Person() { Name = "Sally", Age = 34, Gender = "F" };
                sally = CEF.IncludeObject(sally, ObjectState.Added);
                ConsoleWriteLine($"Should be 1: {CEF.DBSave().Count()}");

                // Now make a change: we're changing the source model in a simple way - associating already saved kids to a person - should turn into 3 updates and 2 inserts (we can't stop people from adding non-wrapped items, so we watch for this and Billy gets replaced by a PersonWrapped on addition here, plus his phone gets accounted for as well)
                // Also of note, sally.Kids was previously null, and still is, so we initialize it with a trackable list (EntitySet is prefect) - Global.ReplaceNullCollections could have been set to true to do this automatically for us at the expense of performance
                sally.Kids = CEF.CreateList<Person>(sally, nameof(Person.Kids));
                sally.Kids.Add(tristan);
                sally.Kids.Add(zella);

                var billy = new Person()
                {
                    Name = "Billy",
                    Age = 1,
                    Gender = "M"
                };

                ConsoleWriteLine($"Row state for Tristan: {tristan.AsInfraWrapped().GetRowState()}");
                billy.Phones = new Phone[] { new Phone() { Number = "707-555-1236", PhoneTypeID = PhoneType.Mobile, Owner = billy } };
                sally.Kids.Add(CEF.IncludeObject(billy, ObjectState.Added));
                sally.Age += 1;
                billy.Age += 1;

                // On saving here, of note we're inserting a new person, getting their id back, carrying this down to the child (phone as the owner), saving the child - all this despite the Phone class not even *having* a PersonID on it (it's just the Owner property which assumes this relationship exists, and we established that in the setup)
                dynamic billyPhone = billy.Phones.First().AsDynamic();
                ConsoleWriteLine($"Row state for Billy's phone: {billyPhone.GetRowState()}");
                CEF.DBSave();
                ConsoleWriteLine($"Billy's phone has a PhoneID tracked despite the POCO object model not containing this field: {billyPhone.PhoneID}");

                // Remove Zella from Sally's kids - should just nullify the ParentPersonID
                sally.Kids.Remove(zella);
                CEF.DBSave();

                // Put it back now
                sally.Kids.Add(zella);
                CEF.DBSave();

                // Swap ownership of Billy's phone to Zella - saving should reflect Zella's new ownership (and Billy's non-ownership)
                // Note: our POCO here does not implement INotifyPropertyChanged, so this change in row state is not reflected until we do something meaningful (e.g. save)
                var phone = billy.Phones.First();
                phone.Owner = zella;
                ConsoleWriteLine($"Row state for phone in question (unchanged): {phone.AsInfraWrapped().GetRowState()}");
                CEF.DBSave();

                // Ok, we're done..? If not, the local scope will be rebuilt next time it's used. In this case, all our prior work is wiped out, we're "starting fresh"
                CEF.CurrentServiceScope.Dispose();

                // One way to repopulate our service scope is to load people with 2 retrievals: start with the parent (by key), then a second call (by parent id) for children - merge into the same set (extension method names help make that clearer)
                // The "by parent" retrieval is done as an extension method that we propose can and should be code generated based on the db object - forms a stronger contract with the db so if say a parameter changes, we could get a compile-time error to track down and fix
                // (We could also have created a procedure to "load family" in one call - a union for parent and children. In many cases, reducing DB round-trips helps performance at cost of a slightly more complex data layer.)
                // Next, delete parent (with cascade option), save (notice it properly deletes children first, then parent)
                // Framework has automtically wired up the relationships between parent and child such that marking the parent for deletion has automatically marked children for deletion as well
                // Note that removal from collection is not considered deletion - there could be other reasons you're removing from a collection, but might offer a way to interpret this as deletion on a one-off basis in future
                // Also note that we use Tristan's PersonID not "tristan" itself - scope was disposed above, no longer has wrappers, etc.
                // And what about Billy's phone? If it had audit history, we'd prefer to have the framework manage/delete it too (versus say leaving it to cascaded deletes in the database) - and we do achieve that because of an extra call to load Phones for a parent and all their kids
                // Important question: does a phone *require* an owner? This will be left to key service in vnext as it has an important implication on deletion here: cascade deletes or set to null where able to (for this example, cascades the deletion to the Phone)

                var sallysFamily = new EntitySet<Person>().DBRetrieveByKey(sally.PersonID).DBAppendByParentID(sally.PersonID);
                var sallysFamilyPhones = new EntitySet<Phone>().DBRetrieveAllForFamily(sally.PersonID);
                var newSally = (from a in sallysFamily where a.Kids != null && a.Kids.Any() select a).First();
                var newTristan = (from a in sallysFamily where a.PersonID == tristan.PersonID select a).First();
                ConsoleWriteLine($"Row state for Tristan (unchanged): {newTristan.AsInfraWrapped().GetRowState()}");
                CEF.DeleteObject(newSally);
                ConsoleWriteLine($"Row state for Tristan (deleted): {newTristan.AsInfraWrapped().GetRowState()}");
                ConsoleWriteLine($"Saved rows: {CEF.DBSave().Count()}");

                ConsoleWriteLine("Please wait, starting background process.");
                Exception toReport = null;

                var backgroundTask = new Task(() =>
                {
                    try
                    {
                        using (CEF.NewServiceScope(new ServiceScopeSettings() { InitializeNullCollections = true }))
                        {
                            // Create an entire object graph using POCO's
                            // Note that our arrays will end up getting coverted to EntitySet's automatically when we add the root (greatgrandpa) to the scope later
                            // We can also intermix POCO and wrapper types at will
                            var greatgrandpa = new Person() { Name = "Zeke", Age = 92, Gender = "M" };
                            var grandpa = new Person() { Name = "Zeke Jr.", Age = 70, Gender = "M" };
                            var mom = new PersonWrapped() { Name = "Wilma", Age = 48, Gender = "F" };
                            var me = new Person() { Name = "Joe", Age = 29, Gender = "M" };

                            // Notice, using linkages here that could/should be recognized either way as related data - this is testing the relationship existing *both* ways, should not fail
                            var myphone = new Phone() { Owner = me, Number = "707-555-1919", PhoneTypeID = PhoneType.Home };
                            me.Phones = new Phone[] { myphone };

                            var auntie = new Person() { Name = "Betty", Age = 50, Gender = "F", Phones = new Phone[] { new Phone() { Number = "707-555-1240", PhoneTypeID = PhoneType.Home } } };
                            var cuz1 = new Person { Name = "Knarf", Age = 40, Gender = "M", Phones = new Phone[] { new Phone() { Number = "510-555-5555", PhoneTypeID = PhoneType.Mobile } } };
                            var cuz2 = new Person { Name = "Hazel", Age = 40, Gender = "F", Phones = new Phone[] { new Phone() { Number = "510-555-8888", PhoneTypeID = PhoneType.Mobile } } };
                            greatgrandpa.Kids = new Person[] { grandpa };
                            grandpa.Kids = new Person[] { auntie, mom };
                            auntie.Kids = new Person[] { cuz1, cuz2 };
                            CEF.IncludeObject(greatgrandpa, ObjectState.Added);
                            myphone.PhoneTypeID = PhoneType.Mobile;

                            // But wait, we didn't initialize mom.Kids with a collection instance! - some of the details of the current scope can be adjusted, such as above where we use InitializeNullCollections=true
                            mom.Kids.Add(me);
                            mom.Phones.Add(CEF.NewObject(new Phone() { Number = "510-555-2222", PhoneTypeID = PhoneType.Work }));

                            // Creates 3 records in DB with parent-child relationship - we'll use an explict new scope (no visibility to any pending changes above)
                            // Also demonstrates using 2 different connection scopes - default is transactional = true which is why we call CanCommit a la System.Transactions
                            using (var ss = CEF.NewServiceScope())
                            {
                                var joel = CEF.NewObject(new Person() { Name = "Joel", Age = 44, Gender = "M" });
                                var cellnum = CEF.NewObject(new Phone() { Owner = joel, PhoneTypeID = PhoneType.Mobile, Number = "707-555-1234" });
                                var worknum = CEF.NewObject(new Phone() { Owner = joel, PhoneTypeID = PhoneType.Work, Number = "707-555-1235" });

                                using (var cs = CEF.NewConnectionScope())
                                {
                                    // We could also use CEF.DBSave() since current scope will be "ss" now
                                    ConsoleWriteLine($"Should be 3: {ss.DBSave().Count()}");

                                    // This should do nothing - nothing is actually dirty!
                                    ConsoleWriteLine($"Should be 0: {CEF.DBSave().Count()}");

                                    // Updates 2 records (parent, child), delete other record, saves
                                    // Of note: Phone class is NOT wrapped by a code genned object - but it still gets saved properly (we miss out on notifications, etc. unless we explicitly ask for its infra wrapper which has these)
                                    // Also, we've updated the POCO for Joel - which has no notifications - this might be "lost" during save, but we do check for updates done in this manner and catch it
                                    joel.Age += 1;
                                    cellnum.Number = "707-555-7777";
                                    CEF.DeleteObject(worknum);
                                    CEF.DBSave();

                                    // This *does* reflect a change in the row state prior to saving since the wrapper class implements INotifyPropertyChanged... HOWEVER, we are in a transaction, and the initial row state of "added" remains
                                    joel.AsDynamic().Age += 1;
                                    ConsoleWriteLine($"Row state for Joel: {joel.AsInfraWrapped().GetRowState()}");
                                    CEF.DBSave();

                                    // A catch handler not calling this allows the transaction to naturally roll back
                                    cs.CanCommit();
                                }

                                using (var cs = CEF.NewConnectionScope())
                                {
                                    // Finally, we can use this as a dynamic object as well and expect the same results... notice here, using our INotifyPropertyChanged wrapper does automatically change the row state...
                                    ((PersonWrapped)joel).Age += 1;
                                    ConsoleWriteLine($"Row state for Joel: {joel.AsInfraWrapped().GetRowState()}");

                                    ConsoleWriteLine($"Initial saves, time: {watch.ElapsedMilliseconds} ms");
                                    watch.Restart(); 

                                    // This approach creates 10000 people with 2 phone numbers each. Saving is done using BULK INSERT, and is expectedly very fast. One constraint: we lose round-tripping of key assignment that was illustrated before.
                                    // Couple of options here, but in this case, I will save people, do a direct query to *just* retrieve people ID's (also illustrates partial loading), populate phone numbers based on that list and save again.
                                    EntitySet<Person> city = new EntitySet<Person>();

                                    for (int i = 0; i < 10000; ++i)
                                    {
                                        city.Add(new Person() { Name = $"N{i}", Age = random.Next(1, 90), Gender = random.Next(2) == 0 ? "F" : "M" });
                                    }

                                    // Default insert threshold to revert to BULK INSERT is 100,000 rows, so we use an explicit option to do this with 10,000
                                    CEF.DBSave(new DBSaveSettings() { BulkInsertMinimumRows = 10000 });

                                    ConsoleWriteLine($"Saved 10,000 new people, time: {watch.ElapsedMilliseconds} ms");
                                    watch.Restart();

                                    // Here's an example of using raw SQL: yes, we can do this even in the 0.2 release! Points to the fact we should expect more LINQ to SQL type of enhancements in the future
                                    // The fact we're loading a very lean Person entity set isn't a problem: we're not updating people here, we're just adding phones and it's nice to have people available to identify ownership (just assuming the ID's are sequential isn't a great idea, the database may have had other ideas!)
                                    using (CEF.NewServiceScope())
                                    {
                                        EntitySet<Phone> phones = new EntitySet<Phone>();

                                        foreach (var p in new EntitySet<Person>().DBRetrieveByQuery(CommandType.Text, "SELECT PersonID FROM CEFTest.Person"))
                                        {
                                            phones.Add(new Phone() { Owner = p, PhoneTypeID = PhoneType.Home, Number = $"{random.Next(10)}{random.Next(10)}{random.Next(10)}-{random.Next(10)}{random.Next(10)}{random.Next(10)}{random.Next(10)}" });
                                            phones.Add(new Phone() { Owner = p, PhoneTypeID = PhoneType.Mobile, Number = $"{random.Next(10)}{random.Next(10)}{random.Next(10)}-{random.Next(10)}{random.Next(10)}{random.Next(10)}{random.Next(10)}" });
                                        }

                                        // This method of saving limits to this specific set
                                        phones.DBSave(new DBSaveSettings() { BulkInsertMinimumRows = 10000, RowSavePreview = (row) => { return (true, ObjectState.Added); } });
                                    }

                                    ConsoleWriteLine($"Saved 20,000 new phones, time: {watch.ElapsedMilliseconds} ms");
                                    watch.Restart();

                                    cs.CanCommit();
                                }
                            }

                            // None of the clutter of the above nested service scope applies now, it's gone out of scope! What does apply? The pending changes for greatgrandpa and his descendents.
                            ConsoleWriteLine($"Rows saved: {CEF.DBSave().Count()}");

                            // Changing a child from one parent to another
                            mom.Kids.Remove(me);
                            auntie.Kids.Add(me);

                            // Why do we need to use AsDynamic? We're dealing with our orignal POCO object graph where we added auntie's phone as an array - not something we can remove from. However, in our wrapped world, her Phones collection is an EntitySet which is tracked, so removing from there should cause the phone to become "unowned" in the database on save
                            auntie.AsDynamic().Phones.RemoveAt(0);
                            ConsoleWriteLine($"Rows saved: {CEF.DBSave().Count()}");
                        }
                    }
                    catch (Exception ex)
                    {
                        toReport = ex;
                    }
                });

                backgroundTask.ContinueWith((t) =>
                {
                    try
                    {
                        if (toReport != null)
                        {
                            throw toReport;
                        }

                        // Retrieve a set of all people who have children (excluding those who do not), of an optional minimum age, and counts of both male and female kids
                        // Let's throw in an extra twist: make an update to all parents and save, where the "extra info" of child counts is ignored for updates but useful for example for data binding.

                        var families = new EntitySet<Person>().DBRetrieveSummaryForParents(20);
                        foreach (var parent in families)
                        {
                            parent.Age += 1;
                            ConsoleWriteLine($"{parent.Name}, {parent.Age}, M:{parent.AsDynamic().MaleChildren}, F:{parent.AsDynamic().FemaleChildren}, {parent.AsInfraWrapped().GetRowState()}");
                        }
                        ConsoleWriteLine($"Rows saved: {CEF.DBSave().Count()}");

                        // This is handy for binding to our WPF data grid: we not only get simple live two-way binding but can detect dirty state changes as well from this collection type (GenericBindableSet)
                        // The dirty state change event is useful to enable/disable the save button (only enabled when there is something pending to save)
                        _bindableFamilies = families.AsDynamicBindable();
                        _bindableFamilies.RowPropertyChanged -= BindableFamilies_RowPropertyChanged;
                        _bindableFamilies.RowPropertyChanged += BindableFamilies_RowPropertyChanged;
                        Data1.ItemsSource = _bindableFamilies;

                        watch.Stop();
                        ConsoleWriteLine($"Complex query, updates, binding, time: {watch.ElapsedMilliseconds} ms");
                        ConsoleWriteLine("Finished!");
                        StartTests.IsEnabled = true;
                        RunBenchmark.IsEnabled = true;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.Message, "Exception.", MessageBoxButton.OK, MessageBoxImage.Error);
                        StartTests.IsEnabled = true;
                        RunBenchmark.IsEnabled = true;
                    }
                }, Task.Factory.CancellationToken, TaskContinuationOptions.None, TaskScheduler.FromCurrentSynchronizationContext());

                backgroundTask.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Exception.", MessageBoxButton.OK, MessageBoxImage.Error);
                StartTests.IsEnabled = true;
                RunBenchmark.IsEnabled = true;
            }
        }

        private void BindableFamilies_RowPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            Save.IsEnabled = _bindableFamilies.IsValid;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Notice we can easily save changes made directly in the grid back to the database.
                // The ambient service scope is on the UI thread. A better pattern might be a scope instance per form since multiple open forms would be sharing scope in this approach.
                // Use of MaxDegreeOfParallelism removes the need to use Dispatcher in RowPropertyChanged - DBSave can do work on background threads otherwise
                CEF.DBSave(new DBSaveSettings() { MaxDegreeOfParallelism = 1 });
                Save.IsEnabled = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Exception.", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RunBenchmark_Click(object sender, RoutedEventArgs e)
        {
            const int TESTS_TO_RUN = 3;

            try
            {
                var totalParents = (Rows.SelectedIndex == 0 ? 3000 : 6000);

                ConsoleList.Items.Clear();
                StartTests.IsEnabled = false;
                RunBenchmark.IsEnabled = false;

                ConsoleWriteLine("Please wait, starting background process.");
                Exception toReport = null;
                List<long> testTimes = new List<long>();
                List<long> testTimes2 = new List<long>();
                var testType = Benchmark.SelectedIndex;
                int rowcount = 0;
                int rowcount2 = 0;

                var backgroundTask = new Task(() =>
                {
                    try
                    {
                        for (int testcnt = 1; testcnt <= TESTS_TO_RUN; ++testcnt)
                        {
                            // Execute tear-down/set-up of test SQL objects from script
                            ConsoleWriteLine("Clearing database....");
                            CEF.CurrentDBService().ExecuteRaw("DELETE CEFTest.Phone; UPDATE CEFTest.Person SET IsDeleted=1, LastUpdatedDate=GETUTCDATE(), LastUpdatedBy='Test';");

                            switch (testType)
                            {
                                case 0:
                                    CEFBenchmarks.Benchmark1(totalParents, testTimes, testTimes2, ref rowcount, ref rowcount2);
                                    break;

                                case 1:
                                    CEFBenchmarks.Benchmark1WithBulk(totalParents, testTimes, testTimes2, ref rowcount, ref rowcount2);
                                    break;

                                case 2:
                                    CEFBenchmarks.Benchmark1SavePer(totalParents, testTimes, testTimes2, ref rowcount, ref rowcount2);
                                    break;

                                case 3:
                                    EFBenchmarks.Benchmark1(totalParents, testTimes, testTimes2, ref rowcount, ref rowcount2);
                                    break;

                                case 4:
                                    EFBenchmarks.Benchmark1SavePer(totalParents, testTimes, testTimes2, ref rowcount, ref rowcount2);
                                    break;

                                case 5:
                                    DapperBenchmarks.Benchmark1(totalParents, testTimes, testTimes2, ref rowcount, ref rowcount2);
                                    break;

                                case 6:
                                    NHBenchmarks.Benchmark1(totalParents, testTimes, testTimes2, ref rowcount, ref rowcount2);
                                    break;

                                case 7:
                                    NHBenchmarks.Benchmark1SavePer(totalParents, testTimes, testTimes2, ref rowcount, ref rowcount2);
                                    break;

                                case 8:
                                    CEFBenchmarks.Benchmark2(totalParents, testTimes, testTimes2, ref rowcount, ref rowcount2);
                                    break;

                                case 9:
                                    DapperBenchmarks.Benchmark2(totalParents, testTimes, testTimes2, ref rowcount, ref rowcount2);
                                    break;

                                case 10:
                                    CEFBenchmarks.Benchmark3SavePer(totalParents, testTimes, testTimes2, ref rowcount, ref rowcount2);
                                    break;

                                case 11:
                                    EFBenchmarks.Benchmark3SavePer(totalParents, testTimes, testTimes2, ref rowcount, ref rowcount2);
                                    break;

                                case 12:
                                    DapperBenchmarks.Benchmark3(totalParents, testTimes, testTimes2, ref rowcount, ref rowcount2);
                                    break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        ConsoleWriteLine(ex.Message);
                        ConsoleWriteLine(ex.InnerException?.Message);
                        ConsoleWriteLine(ex.InnerException?.StackTrace);
                        ConsoleWriteLine("Error, waiting...");
                        CEFDebug.WaitAttach();
                        toReport = ex;
                    }
                });

                backgroundTask.ContinueWith((t) =>
                {
                    try
                    {
                        if (toReport != null)
                        {
                            throw toReport;
                        }

                        testTimes.Sort();
                        testTimes2.Sort();

                        ConsoleWriteLine($"Benchmark complete.");
                        ConsoleWriteLine($"Part 1.");
                        ConsoleWriteLine($"Median duration: {testTimes[TESTS_TO_RUN >> 1]} ms");
                        ConsoleWriteLine($"Rows: {rowcount / TESTS_TO_RUN}");
                        ConsoleWriteLine($"Duration per row: {testTimes[TESTS_TO_RUN >> 1] * 1.0 * TESTS_TO_RUN / rowcount:0.00} ms");

                        if (testTimes2.Any())
                        {
                            ConsoleWriteLine($"Part 2.");
                            ConsoleWriteLine($"Median duration: {testTimes2[TESTS_TO_RUN >> 1]} ms");
                            ConsoleWriteLine($"Rows: {rowcount2 / TESTS_TO_RUN}");
                            ConsoleWriteLine($"Duration per row: {testTimes2[TESTS_TO_RUN >> 1] * 1.0 * TESTS_TO_RUN / rowcount2:0.00} ms");
                        }

                        if (testType < 8)
                        {
                            ConsoleWriteLine($"Final Phone row count: {CEF.CurrentDBService().ExecuteScalar<int>("SELECT COUNT(*) as Rows FROM CEFTest.Phone")} (should be 12200/24400)");
                        }

                        StartTests.IsEnabled = true;
                        RunBenchmark.IsEnabled = true;
                        GC.Collect();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.Message, "Exception.", MessageBoxButton.OK, MessageBoxImage.Error);
                        StartTests.IsEnabled = true;
                        RunBenchmark.IsEnabled = true;
                    }
                }, Task.Factory.CancellationToken, TaskContinuationOptions.None, TaskScheduler.FromCurrentSynchronizationContext());

                backgroundTask.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Exception.", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
