/***********************************************************************
Copyright 2021 CodeX Enterprises LLC

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
using CodexMicroORM.Core;
using CodexMicroORM.Core.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace CodexMicroORM.DemoObjects
{
    public enum WidgetStatusValues
    {
        PEND = 1,
        RECV = 2,
        PROC = 3,
        SHIP = 4
    }

    /// <summary>
    /// Keep in mind: the intent of this class is to be CODE GENERATED - you will not need to write this!!!
    /// Ideally it would be sourced from class metadata (Person) and the underlying database store.
    /// This compliments the startup code which should also be code generated based on DB schema and/or logical model definition.
    /// </summary>
    [Serializable]
    public class PersonWrapped : Person, INotifyPropertyChanged, ICEFWrapper
    {
        #region "ICEFWrapper-specific"

        private Person _copyTo = null;

        Type ICEFWrapper.GetBaseType()
        {
            return typeof(Person);
        }

        string ICEFWrapper.GetSchemaName()
        {
            return "CEFTest";
        }

        void ICEFWrapper.SetCopyTo(object wrapped)
        {
            _copyTo = wrapped as Person;
        }

        object ICEFWrapper.GetCopyTo()
        {
            return _copyTo;
        }

        #endregion

        // Generated - initialize known collections
        // Note: changed in 1.2 from List to EntitySet for Kids to acknowledge need for change tracking, null initialization, etc.
        public PersonWrapped()
        {
            Kids = new EntitySet<Person>();
            Phones = new EntitySet<Phone>();
        }

        public new IList<Person> Kids
        {
            get
            {
                return base.Kids;
            }
            set
            {
                bool changed = (Kids != value);
                base.Kids = value;
                if (_copyTo != null)
                {
                    _copyTo.Kids = value;
                }
                if (changed)
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Kids)));
                }
            }
        }

        public new int PersonID
        {
            get
            {
                return base.PersonID;
            }
            set
            {
                bool changed = (PersonID != value);
                base.PersonID = value;
                if (_copyTo != null)
                {
                    _copyTo.PersonID = value;
                }
                if (changed)
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PersonID)));
                }
            }
        }

        public new string Name
        {
            get
            {
                return base.Name;
            }
            set
            {
                bool changed = (base.Name != value);
                base.Name = value;
                if (_copyTo != null)
                {
                    _copyTo.Name = value;
                }
                if (changed)
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
                }
            }
        }

        public new int Age
        {
            get
            {
                return base.Age;
            }
            set
            {
                bool changed = (base.Age != value);
                base.Age = value;
                if (_copyTo != null)
                {
                    _copyTo.Age = value;
                }
                if (changed)
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Age)));
                }
            }
        }

        private int? _ParentPersonID;
        public int? ParentPersonID
        {
            get
            {
                return _ParentPersonID;
            }
            set
            {
                bool changed = (_ParentPersonID != value);
                _ParentPersonID = value;
                if (changed)
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ParentPersonID)));
                }
            }
        }

        private string _LastUpdatedBy;
        public string LastUpdatedBy
        {
            get
            {
                return _LastUpdatedBy;
            }
            set
            {
                bool changed = (_LastUpdatedBy != value);
                _LastUpdatedBy = value;
                if (changed)
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LastUpdatedBy)));
                }
            }
        }

        private DateTime _LastUpdatedDate;
        public DateTime LastUpdatedDate
        {
            get
            {
                return _LastUpdatedDate;
            }
            set
            {
                bool changed = (_LastUpdatedDate != value);
                _LastUpdatedDate = value;
                if (changed)
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LastUpdatedDate)));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

}
