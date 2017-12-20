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
12/2017    0.2     Initial release (Joel Champagne)
***********************************************************************/
using System;
using System.Collections.Generic;

namespace CodexMicroORM.DemoObjects
{
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
}
