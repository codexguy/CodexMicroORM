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
01/2018    0.2.4   Next set of objects to test with (widgets) (Joel Champagne)
***********************************************************************/
using CodexMicroORM.Core;
using CodexMicroORM.Core.Services;
using System;
using System.Collections.Generic;

namespace CodexMicroORM.DemoObjects
{
    //[EntityRelationships("Person|ParentPersonID")]
    [Serializable]
    public class Person
    {
        public int PersonID { get; set; }
        public string Name { get; set; }
        public int Age { get; set; }
        public string Gender { get; set; }
        public IList<Person> Kids { get; set; }
        public IList<Phone> Phones { get; set; }

        public virtual string Display
        {
            get
            {
                return $"{PersonID} - {Name} - {Age} - {(string.IsNullOrEmpty(Gender) ? "?" : Gender == "M" ? "Male" : "Female")}";
            }
        }

        public override string ToString()
        {
            return Display;
        }
    }

    public enum PhoneType
    {
        Home = 1,
        Work = 2,
        Mobile = 3
    }

    /// <summary>
    /// A phone usually has an owner but can be unowned (a single person).
    /// Look ma! No PhoneID!
    /// </summary>
    public class Phone
    {
        public Person Owner { get; set; }
        public PhoneType PhoneTypeID { get; set; }
        public string Number { get; set; }
        public DateTime? LastUpdatedDate { get; }

        public override string ToString()
        {
            return $"{Number} - {PhoneTypeID} - {(Owner == null ? "null" : Owner.Name)}";
        }
    }

    // Next set of classes are for next set of use cases - field-level mapping demo and more sophisticated tests

    public class Customer
    {
        public Guid CustomerID { get; set; }
        public string Name { get; set; }

        public Address Address { get; set; }
    }

    public class Address
    {
        public string AddressLine { get; set; }

        public string City { get; set; }
    }

    public class WidgetStatus
    {
        public byte WidgetStatusID { get; set; }        // In DB, it's "ID"
        public string StatusDesc { get; set; }
        public string StatusCode { get; set; }
    }

    public class WidgetType
    {
        public string SKU { get; set; }             // PK
        public string Description { get; set; }
    }

    public class Widget
    {
        public int WidgetID { get; set; }
        public string SKU { get; set; }
        public string SerialNumber { get; set; }
        public WidgetStatusValues CurrentStatus { get; set; }         // Use a default value (required field)
        public decimal? Cost { get; set; }
        public decimal? BilledAmount { get; set; }
    }

    public class Receipt : WidgetList
    {
        public string ReceiptNumber                         // Maps to a shared table's GroupNumber (we can leave this unmapped since it's simply a proxy to GroupNumber
        {
            get
            {
                return GroupNumber;
            }
            set
            {
                GroupNumber = value;
            }
        }

        public Customer ReceiptCustomer { get; set; }       // Maps to a shared table's Customer

        public Address FromAddress { get; set; }
        public Address FinalDest { get; set; }
    }

    public class Shipment : WidgetList
    {
        public string ShipmentNumber                        // Maps to a shared table's GroupNumber (we can leave this unmapped since it's simply a proxy to GroupNumber
        {
            get
            {
                return GroupNumber;
            }
            set
            {
                GroupNumber = value;
            }
        }          

        public Customer ShipmentCustomer { get; set; }      // Maps to a shared table's Customer
        public Customer BillingCustomer { get; set; }

        public Address ViaAddress { get; set; }
    }

    public class WidgetList                                 // Effective n:m between widgets and list (e.g. 1 widget can be part of both a receipt and a shipment, and a receipt or shipment can have many widgets)
    {
        public IList<GroupItem> Widgets { get; set; }

        public string GroupNumber { get; set; }
    }

    /// <summary>
    /// Demonstrates a compound primary key, both attributes of which are strings
    /// </summary>
    public class WidgetReview
    {
        public string Username { get; set; }            // PK1
        public WidgetType RatingFor { get; set; }       // PK2 (SKU)
        public int Rating { get; set; }                 // tinyint in DB, range validator
    }

    public class GroupItem                           // In DB, PK is WidgetID + ShipmentID
    {
        public Widget Widget { get; set; }
        public string TrackingNumber { get; set; }
    }

}
