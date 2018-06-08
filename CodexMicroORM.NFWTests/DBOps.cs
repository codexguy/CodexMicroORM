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
02/2018    0.2.4   Addition of Widget-based tests, new features (Joel Champagne)
***********************************************************************/
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodexMicroORM.Core;
using CodexMicroORM.Core.Services;
using CodexMicroORM.Providers;
using CodexMicroORM.DemoObjects;
using CodexMicroORM.BindingSupport;
using System.Linq;
using System.Data;
using System;
using System.Text.RegularExpressions;
using System.IO;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Collections.Generic;
using CodexMicroORM.Core.Collections;
using System.Windows;
using Newtonsoft.Json.Linq;

namespace CodexMicroORM.NFWTests
{
    [TestClass]
    public class SelfContainedDBOps
    {
        private const string DB_SERVER = @"(local)\sql2016";

        /// <summary>
        /// All tests here are self-contained and only rely on the shared demo classes.
        /// It's true: these are larger tests than we might ordinarily try for, but we're testing deep into multiple operations against our object models (I encourage the community to develop better tests and tests for more edge cases!)
        /// Many of these initial tests are here to ensure the demo is in working order - many more to follow!
        /// Also - much of this can ultimate be code-generated based on a source model (be it actual database, ERD, etc.) - rather than use attributes on your POCO, though, we support the possibility that your POCO are in an assembly you cannot change (or choose not to, to keep them free of ORM-specific attributes).
        /// </summary>
        public SelfContainedDBOps()
        {
            // One-time set-up
            Globals.WrapperSupports = WrappingSupport.Notifications;
            Globals.WrappingClassNamespace = null;
            Globals.WrapperClassNamePattern = "{0}Wrapped";
            CEF.AddGlobalService(new DBService(new MSSQLProcBasedProvider($@"Data Source={DB_SERVER};Database=CodexMicroORMTest;Integrated Security=SSPI;MultipleActiveResultSets=true", defaultSchema: "CEFTest")));
            CEF.AddGlobalService(new AuditService());

            // Primary keys
            KeyService.RegisterKey<Person>(nameof(Person.PersonID));
            KeyService.RegisterKey<Phone>("PhoneID");
            KeyService.RegisterKey<Widget>(nameof(Widget.WidgetID));
            KeyService.RegisterKey<WidgetType>(nameof(WidgetType.SKU));
            KeyService.RegisterKey<Customer>(nameof(Customer.CustomerID));
            KeyService.RegisterKey<Receipt>("WidgetGroupID");
            KeyService.RegisterKey<GroupItem>("WidgetGroupID", "WidgetID");
            KeyService.RegisterKey<Shipment>("WidgetGroupID");
            KeyService.RegisterKey<WidgetType>(nameof(WidgetType.SKU));
            KeyService.RegisterKey<WidgetReview>("SKU", nameof(WidgetReview.Username));

            // Schemas that differ from global default
            DBService.RegisterSchema<Widget>("WTest");
            DBService.RegisterSchema<Receipt>("WTest");
            DBService.RegisterSchema<Shipment>("WTest");
            DBService.RegisterSchema<Customer>("WTest");
            DBService.RegisterSchema<GroupItem>("WTest");
            DBService.RegisterSchema<WidgetType>("WTest");
            DBService.RegisterSchema<WidgetReview>("WTest");

            // Default values
            DBService.RegisterDefault<Widget, WidgetStatusValues>(nameof(Widget.CurrentStatus), WidgetStatusValues.PEND);

            // Cases where OM property names differ from storage field names
            DBService.RegisterStorageFieldName<Widget>(nameof(Widget.CurrentStatus), "CurrentStatusID");

            // Cases where DB entity name differs from object names
            DBService.RegisterStorageEntityName<GroupItem>("WidgetGroupItem");

            // Relationships between objects
            KeyService.RegisterRelationship<Person>(TypeChildRelationship.Create<Person>("ParentPersonID").MapsToChildProperty(nameof(Person.Kids)));
            KeyService.RegisterRelationship<Person>(TypeChildRelationship.Create<Phone>().MapsToParentProperty(nameof(Phone.Owner)).MapsToChildProperty(nameof(Person.Phones)));
            KeyService.RegisterRelationship<Customer>(TypeChildRelationship.Create<Receipt>().MapsToParentProperty(nameof(Receipt.ReceiptCustomer)));
            KeyService.RegisterRelationship<Customer>(TypeChildRelationship.Create<Shipment>().MapsToParentProperty(nameof(Shipment.ShipmentCustomer)));
            KeyService.RegisterRelationship<Customer>(TypeChildRelationship.Create<Shipment>("BillingCustomerID").MapsToParentProperty(nameof(Shipment.BillingCustomer)));
            KeyService.RegisterRelationship<Receipt>(TypeChildRelationship.Create<GroupItem>().MapsToChildProperty(nameof(Receipt.Widgets)));
            KeyService.RegisterRelationship<Shipment>(TypeChildRelationship.Create<GroupItem>().MapsToChildProperty(nameof(Receipt.Widgets)));
            KeyService.RegisterRelationship<Widget>(TypeChildRelationship.Create<GroupItem>().MapsToParentProperty(nameof(GroupItem.Widget)));
            KeyService.RegisterRelationship<WidgetType>(TypeChildRelationship.Create<WidgetReview>().MapsToParentProperty(nameof(WidgetReview.RatingFor)));

            // Required field validation
            ValidationService.RegisterRequired<Widget, string>(nameof(Widget.SerialNumber));
            ValidationService.RegisterRequired<Receipt, string>("FromAddressLine");
            ValidationService.RegisterRequired<Receipt, string>("FromCity");

            // Max length validation
            ValidationService.RegisterMaxLength<Widget>(nameof(Widget.SerialNumber), 20);

            // Standard numeric range validation
            ValidationService.RegisterRangeValidation<WidgetReview>(nameof(WidgetReview.Rating), 0, 10);

            // Custom domain validation - error
            ValidationService.RegisterCustomValidation((Person p) =>
            {
                if (p.Age < 0 || p.Age > 120)
                {
                    return "Age must be between 0 and 120.";
                }
                return null;
            }, nameof(Person.Age));

            // Illegal update validation
            ValidationService.RegisterIllegalUpdate<WidgetReview>(nameof(WidgetReview.Username));

            // Property is object that contains 1:1 properties
            DBService.RegisterPropertyGroup<Receipt, Address>(nameof(Receipt.FinalDest), "FinalDest");
            DBService.RegisterPropertyGroup<Receipt, Address>(nameof(Receipt.FromAddress), "From");
            DBService.RegisterPropertyGroup<Customer, Address>(nameof(Customer.Address));
            DBService.RegisterPropertyGroup<Shipment, Address>(nameof(Shipment.ViaAddress), "Via");

            // Optionally call CRUD for 1:1/0 parent when saving these types (insert/update before, deletes after)
            DBService.RegisterOnSaveParentSave<Receipt>("WidgetGroup");
            DBService.RegisterOnSaveParentSave<Shipment>("WidgetGroup");

            // This will construct a new test database, if needed - if the script changes, you'll need to drop the CodexMicroORMTest database before running
            using (CEF.NewConnectionScope(new ConnectionScopeSettings() { IsTransactional = false, ConnectionStringOverride = $@"Data Source={DB_SERVER};Database=master;Integrated Security=SSPI;MultipleActiveResultSets=true" }))
            {
                CEF.CurrentDBService().ExecuteRaw(File.ReadAllText("setup.sql"), false);
            }

            // Perform specialized clean-up for tests
            using (CEF.NewConnectionScope(new ConnectionScopeSettings() { IsTransactional = false, ConnectionStringOverride = $@"Data Source={DB_SERVER};Database=CodexMicroORMTest;Integrated Security=SSPI;MultipleActiveResultSets=true" }))
            {
                CEF.CurrentDBService().ExecuteRaw("EXEC WTest.[up_Widget_TestCleanup]");
            }
        }

        #region "Widget"

        [TestMethod]
        public void WidgetMultiLevelSaving()
        {
            Receipt r1 = null;

            using (var ss = CEF.NewServiceScope(new ServiceScopeSettings() { EstimatedScopeSize = 2500000, InitializeNullCollections = true }))
            {
                List<string> city = new List<string>();
                city.Add("Martinez");
                city.Add("Santa Rosa");
                city.Add("Napa");
                city.Add("San Pablo");
                city.Add("Oakland");
                city.Add("Albany");
                city.Add("Larkspur");
                city.Add("Belmont");
                city.Add("Regina");
                city.Add("Edmonton");
                city.Add("Calgary");
                city.Add("Kyburz");
                city.Add("Fremont");
                city.Add("Groveland");
                city.Add("Williston");

                List<WidgetType> alltype = new List<WidgetType>();

                // Create 10 Widget types
                for (int i = 1; i <= 10; ++i)
                {
                    var wtl = new EntitySet<WidgetType>().DBRetrieveByKeyOrInsert(new WidgetType() { SKU = $"WX{i}", Description = $"Widget {i}" });
                    alltype.Add(wtl.First());
                }

                CEF.DBSave();

                List<Widget> allwid = new List<Widget>();

                // Create 5 widgets
                Parallel.For(1, 6, (i) =>
                {
                    using (CEF.UseServiceScope(ss))
                    {
                        var o = CEF.NewObject(new Widget() { SKU = $"WX{(i % 10) + 1}", CurrentStatus = (WidgetStatusValues)((i % 4) + 1), Cost = (i % 100000) / 100m, SerialNumber = $"SN{i.ToString("000000")}" });

                        lock (allwid)
                            allwid.Add(o);
                    }
                });

                CEF.DBSave();

                // Create 7 widget reviews (70,000 users x 10 types)
                Parallel.For(1, 8, (i) =>
                {
                    using (CEF.UseServiceScope(ss))
                    {
                        CEF.NewObject(new WidgetReview() { RatingFor = alltype[i], Rating = Convert.ToInt32(Math.Round((Math.Pow((i % 10), 1.3) / Math.Pow(9, 1.3) * 10), 0) / 10.0), Username = $"User{((i - 1) % 70000)}" });
                    }
                });

                CEF.DBSave();

                List<Customer> allcust = new List<Customer>();

                // Create 2 customers
                Parallel.For(1, 3, (i) =>
                {
                    using (CEF.UseServiceScope(ss))
                    {
                        var o = CEF.NewObject(new Customer { Name = $"CustX{i}", Address = new Address() { City = city[i % city.Count], AddressLine = "123 1st St." } });

                        lock (allcust)
                            allcust.Add(o);
                    }
                });

                CEF.DBSave();

                // Create 2 receipts, 2 widgets each
                for (int i = 1; i < 3; ++i)
                {
                    using (CEF.UseServiceScope(ss))
                    {
                        var r = CEF.NewObject(new Receipt() { ReceiptCustomer = allcust[i - 1], ReceiptNumber = $"G{i}", FinalDest = new Address() { City = city[(i ^ 6) % city.Count] }, FromAddress = new Address() { City = city[(i ^ 4) % city.Count], AddressLine = "456 Marble Lane" } });
                        r.Widgets.Add(new GroupItem() { Widget = allwid[((i - 1) * 2)], TrackingNumber = $"N{(i * 2)}" });
                        r.Widgets.Add(new GroupItem() { Widget = allwid[((i - 1) * 2) + 1], TrackingNumber = $"N{(i * 2) + 1}" });

                        if (r1 == null)
                        {
                            r1 = r;
                        }
                    }
                }

                CEF.DBSave();
            }

            // Verify what's been saved!
        }

        [TestMethod]
        public void NewWidgetNewReceiptNewShipment()
        {
            string rcpNumber;
            string shipNumber;
            DateTime start = DateTime.UtcNow;

            using (CEF.NewServiceScope(new ServiceScopeSettings() { InitializeNullCollections = true }))
            {
                var w1 = CEF.NewObject(new Widget() { SKU = "A001", Cost = 100 });

                // Verify used default for status
                Assert.AreEqual(WidgetStatusValues.PEND, w1.CurrentStatus);

                // Identify that the serial number is missing (required field)
                var valstate = w1.AsInfraWrapped().GetValidationState();
                Assert.IsFalse(valstate.IsValid);
                Assert.IsFalse(valstate.IsPropertyValid(nameof(Widget.SerialNumber)));
                Assert.IsTrue(valstate.IsPropertyValid(nameof(Widget.Cost)));

                // Set the serial number and save should work
                w1.SerialNumber = "ABCDEF";
                Assert.IsTrue(valstate.IsValid);
                Assert.IsTrue(valstate.IsPropertyValid(nameof(Widget.SerialNumber)));
                Assert.IsTrue(w1.DBSave().WidgetID > 0);

                // Create another widget, also without serial number and try to save it - should get a validation message before it hits the database
                var w2 = CEF.NewObject(new Widget() { SKU = "A002", Cost = 50 });

                try
                {
                    w2.DBSave(new DBSaveSettings() { ValidationChecksOnSave = ValidationErrorCode.MissingRequired });
                    Assert.Fail("Previous save request should have failed.");
                }
                catch (CEFValidationException)
                {
                    // Expected exception type (only)
                }

                var errors = w2.DBSaveWithMessage(new DBSaveSettings() { ValidationChecksOnSave = ValidationErrorCode.MissingRequired, ValidationFailureIsException = false });
                Assert.AreEqual(ValidationErrorCode.MissingRequired, errors.error);
                Assert.IsTrue(errors.message.Contains("Serial Number"));

                // With this, too large instead
                w2.SerialNumber = "2312319238190378687526386347236481638714672658273468265872468237462834682734618461874";
                errors = w2.DBSaveWithMessage(new DBSaveSettings() { ValidationChecksOnSave = ValidationErrorCode.MissingRequired | ValidationErrorCode.TooLarge, ValidationFailureIsException = false });
                Assert.AreEqual(ValidationErrorCode.TooLarge, errors.error);
                Assert.IsTrue(errors.message.Contains("Serial Number"));

                // Should resolve error condition
                w2.SerialNumber = "123123223";
                valstate = w2.AsInfraWrapped().GetValidationState();
                Assert.IsTrue(valstate.IsValid);
                Assert.IsTrue(string.IsNullOrEmpty(valstate.Error));

                // Create a customer
                var cus = CEF.NewObject(new Customer() { Name = "BobTest", Address = new Address() { AddressLine = "222 Main St.", City = "Toledo" } });
                cus.DBSave();
                Assert.IsFalse(cus.CustomerID.Equals(Guid.Empty));

                // Create a widget receipt and save it - this spans two different tables (Receipt, WidgetGroup) despite using 1 main class and Address which maps to Receipt (twice, actually - "From" and "FinalDest" info)
                rcpNumber = "R" + Environment.TickCount.ToString();
                var r = CEF.NewObject(new Receipt() { FromAddress = new Address() { AddressLine = "123 Cherry Lane", City = "Martinez" }, ReceiptNumber = rcpNumber, ReceiptCustomer = cus });
                r.DBSave();

                // Removes values from FromAddress - which are required, so this should be a problem!
                // Our object does not implement INotifyPropertyChanged - we call UpdateData to forcively push current state to infra wrapper (this is a good reason to use a generated wrapper that *does* implement INotifyPropertyChanged!)
                r.FromAddress = null;
                var riw = r.AsInfraWrapped();
                riw.UpdateData();
                valstate = riw.GetValidationState();
                Assert.IsFalse(valstate.IsValid);
                Assert.AreEqual("From Address Line is required. From City is required.", valstate.Error);

                r.FromAddress = new Address() { AddressLine = "124 Cherry Lane", City = "Vallejo" };
                riw.UpdateData();
                Assert.IsTrue(valstate.IsValid);
                Assert.IsTrue(riw.GetRowState() == ObjectState.Modified);

                // Create a third widget which is not rooted to the receipt (should not save)
                var w3 = CEF.NewObject(new Widget() { SKU = "A001", Cost = 99, SerialNumber = "G232AS23" });

                // Add the 2 widgets created above to the Receipt and save - also tests compound keys
                // We leverage the previously retrieved WidgetGroupID, next test should try to do it when everything is unsaved
                // The default behavior saving against an object is to save all related objects, so saving this unsaved widget should include the receipt, etc. - in the proper order, etc. Only thing left unsaved is w3 which is not directly related
                r.Widgets.Add(new GroupItem() { Widget = w1 });
                r.Widgets.Add(new GroupItem() { Widget = w2 });
                Assert.AreEqual(5, (from a in CEF.CurrentServiceScope.GetAllTracked() where a.GetRowState() != ObjectState.Unchanged select a).Count());
                w2.DBSave();
                Assert.AreEqual(1, (from a in CEF.CurrentServiceScope.GetAllTracked() where a.GetRowState() != ObjectState.Unchanged select a).Count());

                // Add a widget to a new Shipment, change its type, and save entire new graph for shipment, items, etc.
                shipNumber = "S" + Environment.TickCount.ToString();
                var s = CEF.NewObject(new Shipment() { BillingCustomer = cus, ShipmentCustomer = cus, ShipmentNumber = shipNumber, ViaAddress = new Address() { City = "Portland" } });
                s.Widgets.Add(new GroupItem() { Widget = w2, TrackingNumber = Guid.NewGuid().ToString() });
                s.Widgets.Add(new GroupItem() { Widget = w3, TrackingNumber = Guid.NewGuid().ToString() });
                s.DBSave();
                Assert.AreEqual(0, (from a in CEF.CurrentServiceScope.GetAllTracked() where a.GetRowState() != ObjectState.Unchanged select a).Count());

                // Change billing customer
                s.BillingCustomer = CEF.NewObject(new Customer() { Name = "Bob", Address = new Address() { AddressLine = "272 Circle Drive", City = "Kyburz" } });
                Assert.AreEqual(2, CEF.DBSave().Count());

                // Create 2 reviews (compound key)
                var w1type = new EntitySet<WidgetType>().DBRetrieveByKey(w1.SKU);
                Assert.AreEqual(1, w1type.Count());
                var wr1 = CEF.NewObject(new WidgetReview() { Rating = 12, RatingFor = w1type.First(), Username = $"steph{Environment.TickCount}" });
                valstate = wr1.AsInfraWrapped().GetValidationState();
                Assert.IsFalse(valstate.IsValid);
                wr1.Rating = 10;

                CEF.NewObject(new WidgetReview() { Rating = 6, RatingFor = w1type.First(), Username = $"lebron{Environment.TickCount}" });
                CEF.DBSave();

                // Update of username on a review is a bit tricky (part of key) - should really be delete+insert, so make in-place updates illegal
                wr1.Username = $"kd{Environment.TickCount}";
                valstate = wr1.AsInfraWrapped().GetValidationState();
                Assert.IsFalse(valstate.IsValid);
                Assert.IsTrue(valstate.Error.Contains("Cannot update"));
            }

            using (CEF.NewServiceScope(new ServiceScopeSettings() { InitializeNullCollections = true }))
            {
                // Retrieval of a receipt needs to pull from 2 tables, plus populate address prop group
                var rcp = new EntitySet<Receipt>().DBRetrieveByReceiptNumber(rcpNumber).First();
                Assert.AreEqual("Toledo", rcp.ReceiptCustomer.Address.City);

                // Retrieval of widgets for a receipt, including extra info (avg rating)
                rcp.Widgets = new EntitySet<GroupItem>().DBRetrieveByGroupNumber(rcpNumber, start);
                Assert.AreEqual(150, (from a in rcp.Widgets select a.Widget.Cost).Sum());
                Assert.AreEqual(WidgetStatusValues.PEND, rcp.Widgets.First().Widget.CurrentStatus);
                Assert.AreEqual(8, (from a in rcp.Widgets where a.Widget.SKU == "A001" select a.AsDynamic().AvgRating).First());

                // Update/save
                rcp.Widgets.First().Widget.CurrentStatus = WidgetStatusValues.PROC;
                rcp.FinalDest.AddressLine = "200 Windy Lane";
                rcp.FinalDest.City = "Groveland";
                Assert.AreEqual(2, CEF.DBSave().Count());

                rcp.Widgets.First().Widget.CurrentStatus = WidgetStatusValues.SHIP;
                rcp.Widgets.First().TrackingNumber = "121312321";
                Assert.AreEqual(2, CEF.DBSave().Count());
            }

            using (CEF.NewServiceScope(new ServiceScopeSettings() { InitializeNullCollections = true }))
            {
                // Retrieval of a receipt needs to pull from 2 tables, plus populate address prop group
                var rcp = new EntitySet<Receipt>().DBRetrieveByReceiptNumber(rcpNumber).First();
                Assert.AreEqual("Toledo", rcp.ReceiptCustomer.Address.City);

                // Retrieving widgets could also be done outside of collection on Receipt - should get wired up properly, though
                var wl = new EntitySet<GroupItem>().DBRetrieveByGroupNumber(rcpNumber, start);
                Assert.AreEqual(2, rcp.Widgets.Count());
            }
        }

        #endregion

        /// <summary>
        /// Allows us to run some tests that could manipulate global static state as part of testing. Tests run in sandbox use the settings defined in constructor for SandboxTests class, may individually customize/change per test.
        /// </summary>
        /// <param name="test"></param>
        private void RunSandboxTest(Func<SandboxTests, string> test)
        {
            var ads = new AppDomainSetup();
            ads.ApplicationBase = AppDomain.CurrentDomain.BaseDirectory;
            var ad = AppDomain.CreateDomain($"test{Environment.TickCount}", AppDomain.CurrentDomain.Evidence, ads);
            ad.Load(File.ReadAllBytes(System.Reflection.Assembly.GetExecutingAssembly().Location));
            var ti = (SandboxTests)ad.CreateInstanceAndUnwrap(typeof(SandboxTests).Assembly.FullName, typeof(SandboxTests).FullName);

            var msg = test.Invoke(ti);

            if (!string.IsNullOrEmpty(msg))
            {
                Assert.Fail(msg);
            }
        }

        [TestMethod]
        public void CaseInsensitivePropAccessAndTLSGlobals()
        {
            try
            {
                using (CEF.NewServiceScope())
                {
                    var p = CEF.NewObject(new Person() { Name = "Test1", Age = 11, Gender = "M" });
                    Assert.AreEqual(CEF.DBSave().Count(), 1);
                    var ps = new EntitySet<Person>().DBRetrieveByKey(p.PersonID);
                    var p2 = ps.First().AsInfraWrapped();

                    // This should fail! - default is case sensitive
                    var n = p2.AsDynamic().name;
                    Assert.Fail();
                }
            }
            catch (Exception)
            {
            }

            RunSandboxTest((proxy) =>
            {
                return proxy.CaseInsensitivePropAccessAndTLSGlobals();
            });

            try
            {
                using (CEF.NewServiceScope())
                {
                    var p = CEF.NewObject(new Person() { Name = "Test1", Age = 11, Gender = "M" });
                    Assert.AreEqual(CEF.DBSave().Count(), 1);
                    var ps = new EntitySet<Person>().DBRetrieveByKey(p.PersonID);
                    var p2 = ps.First().AsInfraWrapped();

                    // This should fail! - change in setting in sandbox should not be seen outside of sandbox (testing the test!)
                    var n = p2.AsDynamic().name;
                    Assert.Fail();
                }
            }
            catch (Exception)
            {
            }
        }

        [TestMethod]
        public void SingleItemCreateSave()
        {
            using (CEF.NewServiceScope())
            {
                var p = CEF.NewObject(new Person() { Name = "Test1", Age = 11, Gender = "M" });
                Assert.AreEqual(CEF.DBSave().Count(), 1);
                var ps = new EntitySet<Person>().DBRetrieveByKey(p.PersonID);
                Assert.AreEqual(1, ps.Count);
                Assert.AreEqual(ps.First().PersonID, p.PersonID);
            }
        }

        [TestMethod]
        public void UseInitNullCollectionsAddAndSave()
        {
            using (CEF.NewServiceScope(new ServiceScopeSettings() { InitializeNullCollections = true }))
            {
                var p1 = CEF.NewObject<PersonWrapped>();
                p1.Name = "Joe";
                p1.Age = 22;
                p1.Gender = "M";
                p1.Phones.Add(new Phone() { Number = "111-2222", PhoneTypeID = PhoneType.Home, Owner = p1 });
                p1.Phones.Add(new Phone() { Number = "222-3333", PhoneTypeID = PhoneType.Mobile, Owner = p1 });
                var p2 = CEF.NewObject<PersonWrapped>();
                p2.Name = "Mary";
                p2.Age = 2;
                p2.Gender = "F";
                p1.Kids.Add(p2);
                Assert.AreEqual(4, CEF.DBSave().Count());
                var ph1 = (from a in p1.Phones where a.PhoneTypeID == PhoneType.Home select a).First();
                ph1.Owner = null;
                p1.Phones.Remove(ph1);
                p2.Age = 1;
                Assert.AreEqual(2, CEF.DBSave().Count());
            }
        }

        [TestMethod]
        public void NoInitNullCollectionsAddAndSave()
        {
            using (CEF.NewServiceScope())
            {
                var p1 = CEF.NewObject<PersonWrapped>();
                p1.Name = "Joe";
                p1.Age = 22;
                p1.Gender = "M";
                p1.Phones = CEF.CreateList(p1, nameof(Person.Phones), ObjectState.Added, new Phone() { Number = "111-2222", PhoneTypeID = PhoneType.Home, Owner = p1 }, new Phone() { Number = "222-3333", PhoneTypeID = PhoneType.Mobile, Owner = p1 });
                var p2 = CEF.NewObject<PersonWrapped>();
                p2.Name = "Mary";
                p2.Age = 2;
                p2.Gender = "F";
                p1.Kids = CEF.CreateList<Person>(p1, nameof(Person.Kids), ObjectState.Added, p2);
                Assert.AreEqual(4, CEF.DBSave().Count());
                var ph1 = (from a in p1.Phones where a.PhoneTypeID == PhoneType.Home select a).First();
                ph1.Owner = null;
                p1.Phones.Remove(ph1);
                p2.Age = 1;
                Assert.AreEqual(2, CEF.DBSave().Count());
                p2.Phones = CEF.CreateList(p2, nameof(Person.Phones), ObjectState.Modified, ph1);
                Assert.AreEqual(1, CEF.DBSave().Count());
                Assert.IsTrue(ph1.AsDynamic().PhoneID > 0);
                CEF.DeleteObject(p2);
                Assert.AreEqual(2, CEF.DBSave().Count());
            }
        }

        [TestMethod]
        public void LoadIncrementalByIDs()
        {
            int p1ID;

            using (CEF.NewServiceScope())
            {
                var p1 = new Person() { Name = "Fred", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3333", PhoneTypeID = PhoneType.Mobile } } };
                var p2 = new Person() { Name = "Sam", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3334", PhoneTypeID = PhoneType.Mobile } } };
                var p3 = new Person() { Name = "Carol", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3335", PhoneTypeID = PhoneType.Mobile } } };
                var p4 = new Person() { Name = "Kylo", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3336", PhoneTypeID = PhoneType.Mobile } } };
                var p5 = new Person() { Name = "Perry", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3337", PhoneTypeID = PhoneType.Mobile } } };
                var p6 = new Person() { Name = "William", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3338", PhoneTypeID = PhoneType.Mobile } } };
                p1.Kids = new Person[] { p2, p3 };
                p2.Kids = new Person[] { p4 };
                p3.Kids = new Person[] { p5, p6 };
                CEF.NewObject(p1);
                CEF.DBSave();
                p1ID = p1.PersonID;
            }

            using (CEF.NewServiceScope(new ServiceScopeSettings() { InitializeNullCollections = true }))
            {
                var people = new EntitySet<Person>().DBRetrieveByKey(p1ID).DBAppendByParentID(p1ID);

                // Here's where we do *not* offer additional services (yet)
                foreach (var p in people)
                {
                    p.Phones = new EntitySet<Phone>().DBRetrieveByOwner(p.PersonID, null);
                }

                var parent = (from a in people where a.Kids?.Count > 0 select a).FirstOrDefault();
                Assert.AreEqual(2, parent.Kids.Count());
                Assert.AreEqual(2, (from a in parent.Kids select a.Phones.Count()).Sum());
            }
        }

        [TestMethod]
        public void TestLastUpdatedByDateAssignment()
        {
            var p1 = new Person() { Name = "Fred", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3333", PhoneTypeID = PhoneType.Mobile } } };

            using (CEF.NewServiceScope(new ServiceScopeSettings() { GetLastUpdatedBy = () => { return "XYZ"; } }))
            {
                CEF.NewObject(p1);
                Assert.AreEqual(2, CEF.DBSave().Count());
            }

            using (CEF.NewServiceScope())
            {
                var es1 = new EntitySet<Person>().DBRetrieveByKey(p1.PersonID);
                Assert.AreEqual("XYZ", es1.First().AsDynamic().LastUpdatedBy);
                var lud = (DateTime)(es1.First().AsDynamic().LastUpdatedDate);
                var timeDiff = DateTime.UtcNow.Subtract(lud).TotalSeconds;
                Assert.IsTrue(timeDiff >= 0 && timeDiff < 5);
            }
        }

        [TestMethod]
        public void AsyncSaveCommitted()
        {
            Person p1;
            Person p2;
            Person p3;
            Person p4;
            Person p5;
            Person p6;

            using (CEF.NewServiceScope(new ServiceScopeSettings() { UseAsyncSave = true, InitializeNullCollections = true }))
            {
                using (var cs = CEF.NewConnectionScope())
                {
                    p1 = CEF.NewObject(new Person() { Name = "STFred", Age = 44 });
                    p1.Phones.Add(new Phone() { Number = "222-3333", PhoneTypeID = PhoneType.Mobile });
                    p2 = CEF.NewObject(new Person() { Name = "STSam", Age = 44 });
                    p2.Phones.Add(new Phone() { Number = "222-8172", PhoneTypeID = PhoneType.Mobile });
                    p3 = new Person() { Name = "STCarol", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3335", PhoneTypeID = PhoneType.Mobile } } };
                    p4 = new Person() { Name = "STKylo", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3336", PhoneTypeID = PhoneType.Mobile } } };
                    p5 = new Person() { Name = "STPerry", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3337", PhoneTypeID = PhoneType.Mobile } } };
                    p6 = new Person() { Name = "STWilliam", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3338", PhoneTypeID = PhoneType.Mobile } } };
                    p1.Kids.Add(p2);
                    p1.Kids.Add(p3);
                    p2.Kids.Add(p4);
                    p3.Kids.Add(p5);
                    CEF.DBSave();
                    cs.CanCommit();
                }

                Assert.IsTrue(p1.PersonID > 0);
                Assert.IsTrue(p2.PersonID > 0);
                Assert.IsTrue(p3.PersonID > 0);
                Assert.IsTrue(p4.PersonID > 0);
                Assert.IsTrue(p5.PersonID > 0);
                Assert.IsTrue(p6.PersonID == 0);

                p3.Kids.Add(p6);
                CEF.DBSave();
            }

            Assert.IsTrue(p6.PersonID > 0);
        }

        [TestMethod]
        public void FailedTxDoesNotAcceptChangesCanRetry()
        {
            // There are some cases where it might make sense to preserve the entire service scope state since a rollback could imply more than CEF actually does - however, we leave this to the f/w user to decide how/when to use (should be more an edge case)

            using (CEF.NewServiceScope(new ServiceScopeSettings() { InitializeNullCollections = true }))
            {
                EntitySet<Person> vals = new EntitySet<Person>();
                vals.Add(new Person() { Name = "Joe Jr.", Age = 20, Gender = "Z" });
                vals.Add(new Person() { Name = "Unrelated", Age = 25, Gender = "F" });
                vals.Add(new Person() { Name = "Joe Sr.", Age = 40, Gender = "M" });
                vals.Last().Kids.Add(vals.First());

                // This fails since it violates a CHECK constraint we have in the database, on Gender - being in a transaction, it should not "accept changes" to support an effective "retry"
                try
                {
                    using (var tx = CEF.NewTransactionScope())
                    {
                        vals.DBSave();
                        tx.CanCommit();
                        Assert.Fail("Should not get here, save request should have failed due to CHECK constraint violation.");
                    }
                }
                catch
                {
                }

                Assert.IsTrue(vals.First().AsInfraWrapped().IsDirty());
                Assert.IsTrue(vals.Last().AsInfraWrapped().IsDirty());
                vals.First().Gender = "M";

                using (var tx = CEF.NewTransactionScope())
                {
                    Assert.AreEqual(3, vals.DBSave().Count());
                    tx.CanCommit();
                }

                Assert.IsTrue(vals[0].PersonID > 0);
                Assert.IsTrue(vals[1].PersonID > 0);
                Assert.IsTrue(vals[2].PersonID > 0);
                Assert.IsTrue(vals[0].AsDynamic().ParentPersonID == vals[2].PersonID);
                Assert.IsFalse(vals.IsDirty());
            }
        }

        [TestMethod]
        public void DeserializeFromFileAndAdd()
        {
            using (CEF.NewServiceScope(new ServiceScopeSettings() { InitializeNullCollections = true }))
            {
                var ws = CEF.CurrentServiceScope.DeserializeSet<Widget>(File.ReadAllText("testdata1.json"));
                Assert.AreEqual(999, ws.Count);

                CEF.NewObject(new Widget() { SKU = "A002", BilledAmount = 20, CurrentStatus = WidgetStatusValues.PROC, SerialNumber = "27812323" });
                CEF.NewObject(new Widget() { SKU = "A002", BilledAmount = 20, CurrentStatus = WidgetStatusValues.PROC, SerialNumber = "27812324" });
                CEF.NewObject(new Widget() { SKU = "A002", BilledAmount = 20, CurrentStatus = WidgetStatusValues.PROC, SerialNumber = "27812325" });
                CEF.NewObject(new Widget() { SKU = "A002", BilledAmount = 20, CurrentStatus = WidgetStatusValues.PROC, SerialNumber = "27812326" });

                // This should work - but if we'd added something prior to deserialization, it might not and that's ok - we consider it an edge case currently if you have preexisting added objects in scope and try to deserialize more added objects
                Assert.AreEqual(1003, CEF.DBSave().Count());
            }
        }

        [TestMethod]
        public void SerializeDeserializeSets()
        {
            string text;

            using (CEF.NewServiceScope(new ServiceScopeSettings() { InitializeNullCollections = true }))
            {
                EntitySet<Person> vals = new EntitySet<Person>();
                vals.Add(new Person() { Name = "Joe Jr.", Age = 20, Gender = "M" });
                vals.Add(new Person() { Name = "Joe Sr.", Age = 40, Gender = "M" });
                vals.Last().Kids.Add(vals.First());
                Assert.AreEqual(2, CEF.GetAllTracked().Count());
                text = vals.AsJSON();
            }

            using (CEF.NewServiceScope())
            {
                var vals = CEF.DeserializeSet<Person>(text);
                Assert.AreEqual(2, vals.Count);
                Assert.AreEqual(2, CEF.GetAllTracked().Count());
                var sr = (from a in vals where a.Age == 40 select a).FirstOrDefault();
                var jr = (from a in vals where a.Age == 20 select a).FirstOrDefault();
                Assert.IsTrue(sr.Kids.First().PersonID == jr.PersonID);
            }
        }

        [TestMethod]
        public void SerializeDeserializeSave()
        {
            string serTextPerson;
            string serTextScope;

            using (CEF.NewServiceScope(new ServiceScopeSettings() { SerializationMode = SerializationMode.OverWireOnlyChanges, InitializeNullCollections = true }))
            {
                var p1 = CEF.NewObject(new Person() { Name = "STFred", Age = 44 });
                p1.Phones.Add(new Phone() { Number = "222-3333", PhoneTypeID = PhoneType.Mobile });
                var p2 = CEF.NewObject(new Person() { Name = "STSam", Age = 44 });
                p2.Phones.Add(new Phone() { Number = "222-8172", PhoneTypeID = PhoneType.Mobile });
                var p3 = new Person() { Name = "STCarol", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3335", PhoneTypeID = PhoneType.Mobile } } };
                var p4 = new Person() { Name = "STKylo", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3336", PhoneTypeID = PhoneType.Mobile } } };
                var p5 = new Person() { Name = "STPerry", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3337", PhoneTypeID = PhoneType.Mobile } } };
                var p6 = new Person() { Name = "STWilliam", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3338", PhoneTypeID = PhoneType.Mobile } } };
                p1.Kids.Add(p2);
                p1.Kids.Add(p3);
                p2.Kids.Add(p4);
                p3.Kids.Add(p5);
                Assert.AreEqual(10, CEF.DBSave().Count());

                // Previous op should have assigned keys from DB, but now let's make some changes
                p2.Age += 1;
                p2.Phones.RemoveAt(0);
                p3.Kids.Add(p6);

                // Serializing a single object (and all its child info)
                serTextPerson = p1.AsJSON();

                // Serialize every tracked object in scope (with serialization mode caveats)
                serTextScope = CEF.CurrentServiceScope.AsJSON();
            }

            using (CEF.NewServiceScope())
            {
                // Should reconstitute only items needed to support saving the person change (adding of child and their phone, and increment of age) - notably, does not include the disassociated phone object since we're using a serialized *person* object and all their directly related data
                var start = DateTime.UtcNow;
                var p1b = CEF.Deserialize<Person>(serTextPerson);
                Assert.AreEqual(3, CEF.DBSave().Count());
                Assert.AreEqual("222-3338", (from a in p1b.Kids where a.Name == "STCarol" from b in a.Kids where b.Name == "STWilliam" select b.Phones.First()).First().Number);

                // There were 3 changed items, yes, but had to have 5 in total to properly "root" all objects in the hierarchy
                Assert.AreEqual(5, CEF.GetAllTracked().Count());

                var end1 = (from a in p1b.Kids where a.Age == 45 select a).First().AsDynamic().LastUpdatedDate;
                var end2 = (from a in p1b.Kids where a.Name == "STCarol" from b in a.Kids where b.Name == "STWilliam" select b).First().AsDynamic().LastUpdatedDate;
                Assert.IsTrue(end1 > start);
                Assert.IsTrue(end2 > start);
            }

            using (CEF.NewServiceScope())
            {
                // Reconstituting for everything that was in scope includes the disassociated phone object - let's mark everything as clean then dirty up that one up again so it should cause a single update when we save
                var start = DateTime.UtcNow;
                CEF.DeserializeScope(serTextScope);
                CEF.AcceptAllChanges();
                var phone = (from a in CEF.GetAllTracked() where a.HasProperty(nameof(Phone.Number)) && string.Compare(a.GetValue(nameof(Phone.Number))?.ToString(), "222-8172") == 0 select a).First().AsDynamic();
                phone.Number = "222-8173";
                Assert.AreEqual(1, CEF.DBSave().Count());
                var end = phone.LastUpdatedDate;
                Assert.IsTrue(end >= start);

                // Service scope serialization is a bit different than it was for the person: we're only expecting 4 objects in the scope (not doing as an object graph, doing everything as a list, effectively)
                Assert.AreEqual(4, CEF.GetAllTracked().Count());
            }
        }

        [TestMethod]
        public void MemFileCacheRetrieves()
        {
            int p1id;
            int ph2id;

            // Create some data specific to this test
            using (CEF.NewServiceScope())
            {
                var p1 = new Person() { Name = "Freddie", Age = 48, Phones = new Phone[] { new Phone() { Number = "678-3333", PhoneTypeID = PhoneType.Mobile } } };
                var p2 = new Person() { Name = "Sam", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3334", PhoneTypeID = PhoneType.Mobile } } };
                var p3 = new Person() { Name = "Carol", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3335", PhoneTypeID = PhoneType.Mobile } } };
                var p4 = new Person() { Name = "Kylo", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3336", PhoneTypeID = PhoneType.Mobile } } };
                var p5 = new Person() { Name = "Perry", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3337", PhoneTypeID = PhoneType.Mobile } } };
                var p6 = new Person() { Name = "William", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3338", PhoneTypeID = PhoneType.Mobile } } };
                p1.Kids = new Person[] { p2, p3 };
                p2.Kids = new Person[] { p4 };
                p3.Kids = new Person[] { p5, p6 };
                CEF.NewObject(p1);
                Assert.AreEqual(12, CEF.DBSave().Count());
                p1id = p1.PersonID;
                ph2id = p2.Phones.First().AsDynamic().PhoneID;
            }

            // DB speed (still in db cache, though)
            long dbspeed = 0;

            using (CEF.NewServiceScope())
            {
                new EntitySet<Phone>().DBRetrieveByKey(ph2id);
                var sw = new Stopwatch();
                sw.Start();
                var t1 = new EntitySet<Phone>().DBRetrieveByKey(ph2id).First().Number == "222-3334";
                dbspeed = sw.ElapsedTicks;
                Assert.IsTrue(t1);
            }

            MemoryFileSystemBacked.FlushAll("CEF_Testing");

            using (CEF.NewServiceScope(new ServiceScopeSettings() { CacheBehavior = CacheBehavior.MaximumDefault }, new MemoryFileSystemBacked("CEF_Testing", MemoryFileSystemBacked.CacheStorageStrategy.SingleDirectory)))
            {
                // By retrieving from db, we're caching it by identity
                var p1b = new EntitySet<Person>().DBRetrieveByKey(p1id).First();
                Assert.AreEqual(48, p1b.Age);

                // Make a change in the object and "recache" its changed form
                p1b.Age = 49;
                CEF.CurrentCacheService().AddByIdentity(p1b);
            }

            using (CEF.NewServiceScope(new ServiceScopeSettings() { RetrievalPostProcessing = RetrievalPostProcessing.None, CacheBehavior = CacheBehavior.MaximumDefault }, new MemoryFileSystemBacked("CEF_Testing", MemoryFileSystemBacked.CacheStorageStrategy.SingleDirectory)))
            {
                // Having gone out of scope, the previous object no longer exists, right? Wrong - it does in the file-backed cache. Verify it exists by attempting to retrieve the object as if from the database - instead, we get it from the cache since we've initialized it at the scope level.
                // How can I be sure it came from the cache and not DB? I cached something that should be mismatched from DB (the age)
                var p1bset = new EntitySet<Person>().DBRetrieveByKey(p1id);
                Assert.AreEqual(49, p1bset.First().Age);

                // Now let's try to load phones - using a custom query to load all phones for a given family
                var sw2 = new Stopwatch();
                sw2.Start();
                var phones = new EntitySet<Phone>().DBRetrieveAllForFamily(p1id);
                var elapsed2 = sw2.ElapsedTicks;

                // We do some of the important work in the background so be sure that's done before continue test. In a real-world setting, it would be fine to continue without the work complete - we might end up reading from the DB when could have gotten from cache but having the main work be background is likely to give better overall results.
                Task.Delay(7000).Wait();

                // Doing a retrieve now for a specific phone should be based on cache
                var sw = new Stopwatch();
                sw.Start();
                var ph2 = new EntitySet<Phone>().DBRetrieveByKey(ph2id).First();
                var elapsed = sw.ElapsedTicks;
                Assert.AreEqual("222-3334", ph2.Number);
            }

            using (CEF.NewServiceScope(new ServiceScopeSettings() { RetrievalPostProcessing = RetrievalPostProcessing.None, CacheBehavior = CacheBehavior.MaximumDefault }, new MemoryFileSystemBacked("CEF_Testing", MemoryFileSystemBacked.CacheStorageStrategy.SingleDirectory)))
            {
                // Doing a retrieve now for a specific phone should be fast (direct from disk - slower than memory but faster than a database where the call has to go through a bunch of sql parsing/processing)
                var sw = new Stopwatch();
                sw.Start();
                var ph2 = new EntitySet<Phone>().DBRetrieveByKey(ph2id).First().Number;
                var elapsed = sw.ElapsedTicks;
                Assert.AreEqual("222-3334", ph2);

                // Should be memory-cached at this point... better be fast!
                var sw2 = new Stopwatch();
                sw2.Start();
                var ph2a = new EntitySet<Phone>().DBRetrieveByKey(ph2id).First();
                var elapsed2 = sw2.ElapsedTicks;

                // From disk cache for a set
                var sw3 = new Stopwatch();
                sw3.Start();
                var phCnt = new EntitySet<Phone>().DBRetrieveAllForFamily(p1id).Count;
                var elapsed3 = sw3.ElapsedTicks;
                Assert.AreEqual(3, phCnt);

                // Do an update to a single phone and save it - should update the cache, invalidate any phone queries
                Assert.AreEqual(5, CEF.CurrentCacheService().GetActiveCount());
                ph2a.Number = "223-3334";
                CEF.DBSave();
                Assert.AreEqual(4, CEF.CurrentCacheService().GetActiveCount());

                // Delete/save/retrieve - should not come back from cache
                var ph2aid = (int)ph2a.AsDynamic().PhoneID;
                CEF.DeleteObject(ph2a);
                CEF.DBSave();
                Assert.AreEqual(3, CEF.CurrentCacheService().GetActiveCount());

                var delCnt = new EntitySet<Phone>().DBRetrieveByKey(ph2aid).Count;
                Assert.AreEqual(0, delCnt);

                Task.Delay(5000).Wait();
            }
        }

        //[TestMethod]
        //public void TestLightweightLongList()
        //{
        //    LightweightLongList ll = new LightweightLongList(31);

        //    var sw = new Stopwatch();
        //    sw.Start();

        //    for (int i = 1; i < 10000000; ++i)
        //    {
        //        ll.Add(i);
        //    }

        //    var t1 = sw.ElapsedMilliseconds;
        //    sw.Restart();

        //    for (int i = 100000; i < 200000; ++i)
        //    {
        //        ll.Remove(i);
        //    }

        //    var t2 = sw.ElapsedMilliseconds;
        //    sw.Restart();

        //    long acc = 0;
        //    for (int i = 1; i < 10; ++i)
        //    {
        //        acc += (from a in ll.All().Take(300000) select a).Sum();
        //    }

        //    var t3 = sw.ElapsedMilliseconds;
        //    sw.Restart();
        //}

        [TestMethod]
        public void PopulateFromInitialPocoVariousRetrievalsSaves()
        {
            using (CEF.NewServiceScope())
            {
                var p1 = new Person() { Name = "Fred", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3333", PhoneTypeID = PhoneType.Mobile } } };
                var p2 = new Person() { Name = "Sam", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3334", PhoneTypeID = PhoneType.Mobile } } };
                var p3 = new Person() { Name = "Carol", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3335", PhoneTypeID = PhoneType.Mobile } } };
                var p4 = new Person() { Name = "Kylo", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3336", PhoneTypeID = PhoneType.Mobile } } };
                var p5 = new Person() { Name = "Perry", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3337", PhoneTypeID = PhoneType.Mobile } } };
                var p6 = new Person() { Name = "William", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3338", PhoneTypeID = PhoneType.Mobile } } };
                p1.Kids = new Person[] { p2, p3 };
                p2.Kids = new Person[] { p4 };
                p3.Kids = new Person[] { p5, p6 };
                CEF.NewObject(p1);
                Assert.AreEqual(12, CEF.DBSave().Count());
                Assert.AreEqual(2, CEF.CurrentKeyService().GetObjectNestLevel(p6));
                Assert.AreEqual(3, CEF.CurrentKeyService().GetObjectNestLevel(p6.Phones.First()));
                var es1 = new EntitySet<Person>().DBRetrieveByKey(p5.PersonID).DBAppendByKey(p6.PersonID);
                Assert.AreEqual(2, es1.Count);
                var es2 = new EntitySet<PersonWrapped>().DBRetrieveByKey(p3.PersonID).DBAppendByParentID(p3.PersonID);
                (from a in es2 where a.Name == "Perry" select a).FirstOrDefault().Age = 45;
                Assert.AreEqual(2, (from a in es2 where a.AsInfraWrapped().GetRowState() == ObjectState.Unchanged select a).Count());
                Assert.IsTrue((from a in es2 where a.Name == "Carol" select a).FirstOrDefault().PersonID == p3.PersonID);
                var es3 = new EntitySet<Phone>();
                es3.Add(p2.Phones.First());
                es3.Add(p4.Phones.First());                
                p2.AsWrapped<PersonWrapped>().Phones.RemoveAt(0);
                p4.AsWrapped<PersonWrapped>().Phones.RemoveAt(0);
                es3.ForAll((ph) => ph.PhoneTypeID = PhoneType.Home);
                Assert.IsTrue(es3.All((p) => p.PhoneTypeID == PhoneType.Home && p.AsDynamic().PersonID == null));
                Assert.AreEqual(2, es3.DBSave().Count());
                CEF.DeleteObject(p1);
                Assert.AreEqual(10, CEF.DBSave(new DBSaveSettings() { RootObject = p1 }).Count());
                es3.DeleteAll();
                Assert.AreEqual(2, CEF.DBSave().Count());
            }
        }

        [TestMethod]
        public void MultipleNestedServiceScopesAndCanCommit()
        {
            using (CEF.NewServiceScope())
            {
                var p1 = new Person() { Name = "Fred", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3333", PhoneTypeID = PhoneType.Mobile } } };
                var p2 = new Person() { Name = "Sam", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3334", PhoneTypeID = PhoneType.Mobile } } };
                var p3 = new Person() { Name = "Carol", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3335", PhoneTypeID = PhoneType.Mobile } } };
                var p4 = new Person() { Name = "Kylo", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3336", PhoneTypeID = PhoneType.Mobile } } };
                var p5 = new Person() { Name = "Perry", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3337", PhoneTypeID = PhoneType.Mobile } } };
                var p6 = new Person() { Name = "William", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3338", PhoneTypeID = PhoneType.Mobile } } };
                p1.Kids = new Person[] { p2, p3 };
                p2.Kids = new Person[] { p4 };
                p3.Kids = new Person[] { p5, p6 };
                CEF.NewObject(p1);

                using (CEF.NewServiceScope())
                {
                    // Nested service scope contains no objects!
                    Assert.AreEqual(0, CEF.DBSave().Count());
                }

                // Global UseTransactionsForNewScopes default is true, so this would initiate a new tx
                using (var cs = CEF.NewConnectionScope())
                {
                    // Do not call CanCommit - should not have saved anything!
                    Assert.AreEqual(12, CEF.DBSave().Count());
                }

                var es1 = new EntitySet<Person>().DBRetrieveByKey(p1.PersonID);
                Assert.AreEqual(0, es1.Count);
            }

            using (CEF.NewServiceScope())
            {
                var p1 = new Person() { Name = "Fred", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3333", PhoneTypeID = PhoneType.Mobile } } };
                var p2 = new Person() { Name = "Sam", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3334", PhoneTypeID = PhoneType.Mobile } } };
                var p3 = new Person() { Name = "Carol", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3335", PhoneTypeID = PhoneType.Mobile } } };
                var p4 = new Person() { Name = "Kylo", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3336", PhoneTypeID = PhoneType.Mobile } } };
                var p5 = new Person() { Name = "Perry", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3337", PhoneTypeID = PhoneType.Mobile } } };
                var p6 = new Person() { Name = "William", Age = 44, Phones = new Phone[] { new Phone() { Number = "222-3338", PhoneTypeID = PhoneType.Mobile } } };
                p1.Kids = new Person[] { p2, p3 };
                p2.Kids = new Person[] { p4 };
                p3.Kids = new Person[] { p5, p6 };
                CEF.NewObject(p1);

                using (var cs = CEF.NewConnectionScope())
                {
                    Assert.AreEqual(12, CEF.DBSave().Count());
                    cs.CanCommit();
                }

                var es1 = new EntitySet<Person>().DBRetrieveByKey(p1.PersonID);
                Assert.AreEqual(1, es1.Count);
            }
        }

        [TestMethod]
        public void SavingWithBulkInsert()
        {
            var tag = Regex.Replace(Guid.NewGuid().ToString(), @"\W", "").Substring(0, 8);

            using (CEF.NewServiceScope())
            {
                EntitySet<Person> people = new EntitySet<Person>();

                for (int i = 1; i <= 10000; ++i)
                {
                    people.Add(new Person() { Name = $"{tag}{i}", Age = (i % 50) + 10 });
                }

                CEF.DBSave(new DBSaveSettings() { BulkInsertMinimumRows = 10000 });
            }

            using (CEF.NewServiceScope())
            {
                EntitySet<Phone> phones = new EntitySet<Phone>();
                EntitySet<Person> people = new EntitySet<Person>();
                people.DBRetrieveByQuery(CommandType.Text, $"SELECT PersonID FROM CEFTest.Person WHERE Name LIKE '{tag}%'");

                foreach (var p in people)
                {
                    if ((p.PersonID % 4) == 0)
                    {
                        phones.Add(new Phone() { Number = tag, PhoneTypeID = PhoneType.Mobile, Owner = p });
                    }
                }

                Assert.AreEqual(2500, CEF.DBSave().Count());

                CEF.CurrentDBService().ExecuteRaw($@"
DELETE CEFTest.Phone WHERE Number='{tag}';
GO
UPDATE CEFTest.Person SET IsDeleted=1, LastUpdatedDate=GETUTCDATE(), LastUpdatedBy='Test' WHERE Name LIKE '{tag}%';
GO
");
            }
        }

        [TestMethod]
        public void GenericBindableSetLoading()
        {
            var tag = Regex.Replace(Guid.NewGuid().ToString(), @"\W", "").Substring(0, 8);

            using (CEF.NewServiceScope(new ServiceScopeSettings() { InitializeNullCollections = true }))
            {
                var p1 = CEF.NewObject<Person>();
                p1.Name = "Joe";
                p1.Age = 22;
                p1.Gender = "M";
                var ph1 = CEF.NewObject(new Phone() { Number = "111-2222", PhoneTypeID = PhoneType.Home, Owner = p1 });
                p1.Phones.Add(new Phone() { Number = "222-3333", PhoneTypeID = PhoneType.Mobile });
                var p2 = CEF.NewObject<PersonWrapped>();
                p2.Name = "Mary";
                p2.Age = 2;
                p2.Gender = "F";
                p1.Kids.Add(p2);
                Assert.AreEqual(4, CEF.DBSave().Count());
                GenericBindableSet gbs = new EntitySet<Person>().DBRetrieveByKey(p1.PersonID).AsDynamicBindable();
                Assert.AreEqual(1, gbs.Count);
                Assert.AreEqual("Joe", gbs.First().GetPropertyValue(nameof(Person.Name)));
                string newName = $"Joel{tag}";
                gbs.First().SetPropertyValue(nameof(Person.Name), newName);
                Assert.AreEqual(1, CEF.DBSave().Count());
                var es1 = new EntitySet<Person>().DBRetrieveSummaryForParents(20);
                Assert.AreEqual(2, (from a in es1 where a.Name == newName select a).First().AsDynamic().FamilyPhones);
                Assert.AreEqual(p1.PersonID, (from a in es1 where a.Name == newName select a).First().PersonID);

                var dv = es1.DeepCopyDataView(filter: $@"Name='{newName}'");
                Assert.AreEqual(1, dv.Count);
                Assert.AreEqual(1, dv[0]["FemaleChildren"]);
            }
        }
    }
}
