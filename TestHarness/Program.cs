using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CodexMicroORM.Core;
using CodexMicroORM.Core.Services;
using CodexMicroORM.Providers;
using System.ComponentModel;
using System.Dynamic;
using System.Data;
using System.IO;

namespace TestHarness
{
    class Program
    {
        static void Main(string[] args)
        {
            var random = new Random();
            var watch = new System.Diagnostics.Stopwatch();

            // Tells CEF that we do have code gen available that supports notifications
            // The {0}Wrapped pattern tells CEF that "Person" as a POCO class is wrapped by the "PersonWrapped" class - and/or you can use namespace differences
            Globals.WrapperSupports = WrappingSupport.Notifications;
            Globals.WrappingClassNamespace = null;
            Globals.WrapperClassNamePattern = "{0}Wrapped";

            // Default audit service would use Environment's username but doing this to illustrate; plus for last updated date, default is to use UTC date/time but we've changed here to use local time
            // DBservice in this case initialized based on a single connection string to use throughout, future examples will demo multiple databases; default schema is "dbo", we'll override for demo purposes
            CEF.AddGlobalService(new AuditService(() => { return "Me: " + Environment.UserName; }, () => { return DateTime.Now; }));
            CEF.AddGlobalService(new DBService(new MSSQLProcBasedProvider(@"Data Source=(local)\sql2016;Database=xskrapetest;Integrated Security=SSPI;MultipleActiveResultSets=true", defaultSchema: "CEFTest")));

            // Establish primary keys based on types (notice for Phone we don't care about the PhoneID in the object model - do in the database but mCEF handles that!)
            KeyService.RegisterKey<Person>(nameof(Person.PersonID));
            KeyService.RegisterKey<Phone>("PhoneID");

            // Establish all the relationships we know about via database and/or object model
            KeyService.RegisterRelationship<Person>(TypeChildRelationship.Create<Person>("ParentPersonID").MapsToChildProperty(nameof(Person.Kids)));
            KeyService.RegisterRelationship<Person>(TypeChildRelationship.Create<Phone>().MapsToParentProperty(nameof(Phone.Owner)).MapsToChildProperty(nameof(Person.Phones)));

            // Execute tear-down/set-up of test SQL objects from script
            CEF.CurrentDBService().ExecuteRaw("DELETE CEFTest.Phone; UPDATE CEFTest.Person SET IsDeleted=1, LastUpdatedDate=GETUTCDATE(), LastUpdatedBy='Test';");

            //*************** Above this line constitutes app init time (ideally 99% code generated in a perfect world), after this line could be anywhere in your biz logic...

            watch.Start();

            // Creates and saves a person in 2 lines of code!
            // Of note: no need for a context, we're using the implicit one created in TLS (great for a simple console app, recommended is to use explicit scopes)
            var tristan = CEF.NewObject(new Person() { Name = "Tristan", Age = 4, Gender = "M" });
            Console.WriteLine($"Rows saved: {CEF.DBSave().Count()}");
            Console.WriteLine($"A PersonID key as assigned by the database has been round-tripped back to us: {tristan.PersonID}");
            Console.WriteLine($"And LastUpdatedDate as assigned by the database too, despite not being in our base POCO: {((PersonWrapped)tristan).LastUpdatedDate}");

            // Creates and saves a person similar to above, but using wrapper object directly is fine too - and we've got an extension method that lets us save in 1 line of code!
            var zella = CEF.NewObject(new PersonWrapped() { Name = "Zella", Age = 7, Gender = "F" }).DBSave();
            Console.WriteLine($"Similar to above, but already working with genned wrapper so no need to cast it: {zella.LastUpdatedDate}");

            // We have the option to indicate whether the object should be considered new or not with respect to db, when adding to scope (CreateObject on the other hand is always "new") - in reality we could have used CreateObject here too
            var sally = new Person() { Name = "Sally", Age = 34, Gender = "F" };
            sally = CEF.IncludeObject(sally, DataRowState.Added);
            Console.WriteLine($"Should be 1: {CEF.DBSave().Count()}");

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

            Console.WriteLine($"Row state for Tristan: {tristan.AsInfraWrapped().GetRowState()}");
            billy.Phones = new Phone[] { new Phone() { Number = "707-555-1236", PhoneTypeID = PhoneType.Mobile, Owner = billy } };
            sally.Kids.Add(CEF.IncludeObject(billy, DataRowState.Added));
            sally.Age += 1;
            billy.Age += 1;

            // On saving here, of note we're inserting a new person, getting their id back, carrying this down to the child (phone as the owner), saving the child - all this despite the Phone class not even *having* a PersonID on it (it's just the Owner property which assumes this relationship exists, and we established that in the setup)
            Console.WriteLine($"Row state for Billy's phone: {billy.Phones.First().AsDynamic().GetRowState()}");
            CEF.DBSave();
            Console.WriteLine($"Pick first kid - should now have a parent assigned! ParentPersonID is not part of base POCO but is in wrapper/DB model: {((PersonWrapped)sally.Kids.Last()).ParentPersonID}");
            Console.WriteLine($"Billy's phone has a PhoneID tracked despite the POCO object model not containing this field: {billy.Phones.First().AsDynamic().PhoneID}");

            // Remove Zella from Sally's kids - should just nullify the ParentPersonID
            sally.Kids.Remove(zella);
            CEF.DBSave();

            // Put it back now
            sally.Kids.Add(zella);
            CEF.DBSave();

            // Swap ownership of Billy's phone to Zella - saving should reflect Zella's new ownership (and Billy's non-ownership)
            // Note: our POCO here does not implement INotifyPropertyChanged, so this change in row state is not reflected until we do something meaningful (e.g. save, or ask for row state like being done here)
            var phone = billy.Phones.First();
            phone.Owner = zella;
            Console.WriteLine($"Row state for phone in question: {phone.AsInfraWrapped().GetRowState()}");
            CEF.DBSave();

            // Ok, we're done..? If not, the local scope will be rebuilt next time it's used. In this case, all our prior work is wiped out, we're "starting fresh"
            CEF.CurrentServiceScope.Dispose();

            // One way to repopulate our service scope is to load people with 2 retrievals: start with the parent (by key), then a second call (by parent id) for children - merge into the same set (extension method names help make that clearer)
            // The "by parent" retrieval is done as an extension method that we propose can and should be code generated based on the db object - forms a stronger contract with the db so if say a parameter changes, we could get a compile-time error to track down and fix
            // (We could also have created a procedure to "load family" in one call - a union for parent and children. In many cases, reducing DB round-trips helps performance at cost of slightly more complex data layer.)
            // Next, delete parent (with cascade option), save (notice it properly deletes children first, then parent)
            // Framework has automtically wired up the relationships between parent and child such that marking the parent for deletion has automatically marked children for deletion as well
            // Note that removal from collection is not considered deletion - there could be other reasons you're removing from a collection, but might offer a way to interpret this as deletion on a one-off basis in future
            // Also note that we use Tristan's PersonID not "tristan" itself - scope was disposed above, no longer has wrappers, etc.
            // And what about Billy's phone? If it had audit history, we'd prefer to have the framework manage/delete it too (versus say leaving it to cascaded deletes in the database) - and we do achieve that because of an extra call to load Phones for a parent and all their kids (TODO - vnext might support auto-loading of children into scope to let this happen without needing to explicitly load)
            // Important question: does a phone *require* an owner? This will be left to key service in vnext as it has an important implication on deletion here: cascade deletes or set to null where able to (for this example, cascades the deletion to the Phone)

            var sallysFamily = new EntitySet<Person>().DBRetrieveByKey(sally.PersonID).DBAppendByParentID(sally.PersonID);
            var sallysFamilyPhones = new EntitySet<Phone>().DBRetrieveAllForFamily(sally.PersonID);
            var newSally = (from a in sallysFamily where a.Kids != null && a.Kids.Any() select a).First();
            var newTristan = (from a in sallysFamily where a.PersonID == tristan.PersonID select a).First();
            Console.WriteLine($"Row state for Tristan: {newTristan.AsInfraWrapped().GetRowState()}");
            CEF.DeleteObject(newSally);
            Console.WriteLine($"Row state for Tristan: {newTristan.AsInfraWrapped().GetRowState()}");
            Console.WriteLine($"Saved rows: {CEF.DBSave().Count()}");

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
                greatgrandpa.Kids = new Person[] { grandpa };
                grandpa.Kids = new Person[] { auntie, mom };
                CEF.IncludeObject(greatgrandpa, DataRowState.Added);
                myphone.PhoneTypeID = PhoneType.Mobile;

                // But wait, we didn't initialize mom.Kids with a collection instance! - some of the details of the current scope can be adjusted, such as above where we use InitializeNullCollections=true
                mom.Kids.Add(me);

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
                        ss.DBSave();

                        // This should do nothing - nothing is dirty!
                        Console.WriteLine($"Should be 0: {CEF.DBSave().Count()}");

                        // Updates 2 records (parent, child), delete other record, saves
                        // Of note: Phone class is NOT wrapped by a code genned object - but it still gets saved properly (we miss out on notifications, etc. unless we explicitly ask for its infra wrapper which has these)
                        // Also, we've updated the POCO for Joel - which has no notifications - this might be "lost" during save, but we do check for updates done in this manner and catch it
                        joel.Age += 1;
                        cellnum.Number = "707-555-7777";
                        CEF.DeleteObject(worknum);
                        CEF.DBSave();

                        // This *does* reflect a change in the row state prior to saving since the wrapper class implements INotifyPropertyChanged
                        ((PersonWrapped)joel).Age += 1;
                        Console.WriteLine($"Row state for Joel: {joel.AsInfraWrapped().GetRowState()}");
                        CEF.DBSave();

                        // A catch handler not calling this allows the transaction to naturally roll back
                        cs.CanCommit();
                    }

                    using (var cs = CEF.NewConnectionScope())
                    {
                        // Finally, we can use this as a dynamic object as well and expect the same results...
                        joel.AsDynamic().Age += 1;
                        Console.WriteLine($"Row state for Joel: {joel.AsInfraWrapped().GetRowState()}");

                        Console.WriteLine($"Time: {watch.ElapsedMilliseconds}");
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

                        Console.WriteLine($"Time: {watch.ElapsedMilliseconds}");
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
                            phones.DBSave(new DBSaveSettings() { BulkInsertMinimumRows = 10000, RowSavePreview = (row) => { return (true, DataRowState.Added); } });
                        }

                        Console.WriteLine($"Time: {watch.ElapsedMilliseconds}");
                        watch.Restart();

                        cs.CanCommit();
                    }
                }

                // None of the clutter of the above nested service scope applies now, it's gone out of scope! What does apply? The pending changes for greatgrandpa and his descendents.
                Console.WriteLine($"Rows saved: {CEF.DBSave().Count()}");

                // Changing a child from one parent to another
                mom.Kids.Remove(me);
                auntie.Kids.Add(me);

                // Why do we need to use AsDynamic? We're dealing with our orignal POCO object graph where we added auntie's phone as an array - not something we can remove from. However, in our wrapped world, her Phones collection is an EntitySet which is tracked, so removing from there should cause the phone to become "unowned" in the database on save
                auntie.AsDynamic().Phones.RemoveAt(0);
                Console.WriteLine($"Rows saved: {CEF.DBSave().Count()}");

                // Retrieve a set of all people who have children (excluding those who do not), of an optional minimum age, and counts of both male and female kids
                // How does this perform compared to other framework solutions that are based on LINQ? Frameworks that use procedures too? Benchmarks to come.
                // Let's throw in an extra twist: make an update to all parents and save, where the "extra info" of child counts is ignored for updates but useful for example for data binding.

                var familes = new EntitySet<Person>().DBRetrieveSummaryForParents(20);
                foreach (var parent in familes)
                {
                    parent.Age += 1;
                    Console.WriteLine($"{parent.Name}, {parent.Age}, M:{parent.AsDynamic().MaleChildren}, F:{parent.AsDynamic().FemaleChildren}, {parent.AsInfraWrapped().GetRowState()}");
                }
                Console.WriteLine($"Rows saved: {CEF.DBSave().Count()}");

                watch.Stop();
                Console.WriteLine($"Time: {watch.ElapsedMilliseconds}");
            }

            // TODO - next release, will include more tests...

            // Build a complete object graph (mix of objs and collections), add to a new scope, save

            // Make some changes (add, update, delete) - get changes (theoretically serialize to biz layer)

            // In a different scope, reconstitute object with changes and save the changes

            // Test WPF data binding - different project

            // Test name mapping (vnext feature) - e.g. if Phone.PersonID were changed to Phone.OwnerID
        }
    }


}
