using CodexMicroORM.Core;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestHarness
{
    /// <summary>
    /// Keep in mind: the intent of this class is to be CODE GENERATED - you will not need to write this!!!
    /// Ideally it would be sourced from class metadata (Person) and the underlying database store.
    /// This compliments the startup code which should also be code generated based on DB schema.
    /// </summary>
    public class PersonWrapped : Person, INotifyPropertyChanged, ICEFWrapper
    {
        #region "ICEFWrapper-specific"

        private Person _copyTo = null;

        //public override bool Equals(object obj)
        //{
        //    var compared = obj as Person;

        //    if (compared != null)
        //    {
        //        return compared.PersonID == this.PersonID;
        //    }

        //    return false;
        //}

        //public override int GetHashCode()
        //{
        //    return PersonID.GetHashCode();
        //}

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

        public override string Display
        {
            get
            {
                return $"{PersonID} - {ParentPersonID} - {Name} - {Age} - {(string.IsNullOrEmpty(Gender) ? "?" : Gender == "M" ? "Male" : "Female")}";
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

}
