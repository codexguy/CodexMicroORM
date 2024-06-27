using System;
using System.ComponentModel;
using CodexMicroORM.Core;
using CodexMicroORM.Core.Services;
#nullable enable

// Using attributes implies you will need to call AttributeInitializer.Apply() in your startup code.
// Note: this is a code generated file; you would not typically change this by hand.

namespace CodexMicroORM.DemoObjects2
{


    [EntitySchemaName("CEFTest")]
    [EntityRelationships("CodexMicroORM.DemoObjects2.Phone\\PhoneTypeID\\\\")]
    [EntityPrimaryKey(nameof(PhoneTypeID))]
    [Serializable()]
    public partial class PhoneType : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private int _PhoneTypeID = default;
        public int PhoneTypeID
        {
            get { return _PhoneTypeID; }
            set
            {
                bool changed = (_PhoneTypeID != value);
                _PhoneTypeID = value;
                if (changed)
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PhoneTypeID)));
                }
            }
        }

        private string _PhoneTypeDesc = string.Empty;
        [EntityMaxLength(100)]
        [EntityRequired]
        public string PhoneTypeDesc
        {
            get { return _PhoneTypeDesc; }
            set
            {
                bool changed = (_PhoneTypeDesc != value);
                _PhoneTypeDesc = value;
                if (changed)
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PhoneTypeDesc)));
                }
            }
        }

        private string _LastUpdatedBy = string.Empty;
        [EntityMaxLength(50)]
        public string LastUpdatedBy
        {
            get { return _LastUpdatedBy; }
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

        private DateTime _LastUpdatedDate = default;
        [EntityDateHandling(PropertyDateStorage.TwoWayConvertUtc)]
        public DateTime LastUpdatedDate
        {
            get { return _LastUpdatedDate; }
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



    }

    public partial class PhoneTypeSet : EntitySet<PhoneType>, ICEFStorageNaming
    {
        public string? EntityPersistedName { get; set; }

        public PhoneType Add(string _PhoneTypeDesc)
        {
            var t = CEF.NewObject(new PhoneType()
            {
                PhoneTypeDesc = _PhoneTypeDesc
            });
            Add(t);
            return t;
        }



    }


    [EntitySchemaName("CEFTest")]
    [EntityPrimaryKey(nameof(PhoneID))]
    [Serializable()]
    public partial class Phone : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private int _PhoneID = default;
        public int PhoneID
        {
            get { return _PhoneID; }
            set
            {
                bool changed = (_PhoneID != value);
                _PhoneID = value;
                if (changed)
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PhoneID)));
                }
            }
        }

        private int _PhoneTypeID = default;
        public int PhoneTypeID
        {
            get { return _PhoneTypeID; }
            set
            {
                bool changed = (_PhoneTypeID != value);
                _PhoneTypeID = value;
                if (changed)
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PhoneTypeID)));
                }
            }
        }

        private string _Number = string.Empty;
        [EntityMaxLength(20)]
        [EntityRequired]
        public string Number
        {
            get { return _Number; }
            set
            {
                bool changed = (_Number != value);
                _Number = value;
                if (changed)
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Number)));
                }
            }
        }

        private string _LastUpdatedBy = string.Empty;
        [EntityMaxLength(50)]
        public string LastUpdatedBy
        {
            get { return _LastUpdatedBy; }
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

        private DateTime _LastUpdatedDate = default;
        [EntityDateHandling(PropertyDateStorage.TwoWayConvertUtc)]
        public DateTime LastUpdatedDate
        {
            get { return _LastUpdatedDate; }
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

        private int? _PersonID;
        public int? PersonID
        {
            get { return _PersonID; }
            set
            {
                bool changed = (_PersonID != value);
                _PersonID = value;
                if (changed)
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PersonID)));
                }
            }
        }



    }

    public partial class PhoneSet : EntitySet<Phone>, ICEFStorageNaming
    {
        public string? EntityPersistedName { get; set; }

        public Phone Add(int _PhoneTypeID, string _Number, int? _PersonID)
        {
            var t = CEF.NewObject(new Phone()
            {
                PhoneTypeID = _PhoneTypeID,
                Number = _Number,
                PersonID = _PersonID
            });
            Add(t);
            return t;
        }



    }


    [EntitySchemaName("CEFTest")]
    [EntityRelationships("CodexMicroORM.DemoObjects2.Person\\ParentPersonID\\\\", "CodexMicroORM.DemoObjects2.Phone\\PersonID\\\\")]
    [EntityPrimaryKey(nameof(PersonID))]
    [Serializable()]
    public partial class Person : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private int _PersonID = default;
        public int PersonID
        {
            get { return _PersonID; }
            set
            {
                bool changed = (_PersonID != value);
                _PersonID = value;
                if (changed)
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PersonID)));
                }
            }
        }

        private string _Name = string.Empty;
        [EntityMaxLength(100)]
        [EntityRequired]
        public string Name
        {
            get { return _Name; }
            set
            {
                bool changed = (_Name != value);
                _Name = value;
                if (changed)
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
                }
            }
        }

        private int _Age = default;
        public int Age
        {
            get { return _Age; }
            set
            {
                bool changed = (_Age != value);
                _Age = value;
                if (changed)
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Age)));
                }
            }
        }

        private int? _ParentPersonID;
        public int? ParentPersonID
        {
            get { return _ParentPersonID; }
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

        private string _LastUpdatedBy = string.Empty;
        [EntityMaxLength(50)]
        public string LastUpdatedBy
        {
            get { return _LastUpdatedBy; }
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

        private DateTime _LastUpdatedDate = default;
        [EntityDateHandling(PropertyDateStorage.TwoWayConvertUtc)]
        public DateTime LastUpdatedDate
        {
            get { return _LastUpdatedDate; }
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

        private string? _Gender;
        [EntityMaxLength(1)]
        public string? Gender
        {
            get { return _Gender; }
            set
            {
                bool changed = (_Gender != value);
                _Gender = value;
                if (changed)
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Gender)));
                }
            }
        }



    }

    public partial class PersonSet : EntitySet<Person>, ICEFStorageNaming
    {
        public string? EntityPersistedName { get; set; }

        public Person Add(string _Name, int _Age, int? _ParentPersonID, string? _Gender)
        {
            var t = CEF.NewObject(new Person()
            {
                Name = _Name,
                Age = _Age,
                ParentPersonID = _ParentPersonID,
                Gender = _Gender
            });
            Add(t);
            return t;
        }



    }

}
