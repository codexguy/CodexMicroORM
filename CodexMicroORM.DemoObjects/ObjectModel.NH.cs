using FluentNHibernate.Mapping;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CodexMicroORM.NHObjectModel
{
    class PersonMap : ClassMap<Person>
    {
        public PersonMap()
        {
            Schema("CEFTest");
            Id(x => x.PersonID).GeneratedBy.Identity();
            Map(x => x.Name);
            Map(x => x.Age);
            Map(x => x.Gender);
            Map(x => x.LastUpdatedBy);
            Map(x => x.LastUpdatedDate);
            Map(x => x.ParentPersonID);
            HasMany<Phone>(x => x.Phones).KeyColumn(nameof(Phone.PersonID)).Cascade.SaveUpdate();
            HasMany<Person>(x => x.Kids).KeyColumn(nameof(Person.ParentPersonID)).Inverse().Cascade.SaveUpdate();
        }
    }

    class PhoneMap : ClassMap<Phone>
    {
        public PhoneMap()
        {
            Schema("CEFTest");
            Id(x => x.PhoneID).GeneratedBy.Identity();
            Map(x => x.Number);
            Map(x => x.PhoneTypeID).CustomType<PhoneTypeEnum>();
            Map(x => x.LastUpdatedBy);
            Map(x => x.LastUpdatedDate);
            References<Person>(x => x.Person, nameof(Person.PersonID)).Nullable();
        }
    }

    class up_Person_SummaryForParents_ResultMap : ClassMap<up_Person_SummaryForParents_Result>
    {
        public up_Person_SummaryForParents_ResultMap()
        {
            Id(x => x.PersonID);
            Map(x => x.Age);
            Map(x => x.FamilyPhones);
            Map(x => x.FemaleChildren);
            Map(x => x.Gender);
            Map(x => x.LastUpdatedBy);
            Map(x => x.LastUpdatedDate);
            Map(x => x.MaleChildren);
            Map(x => x.Name);
            Map(x => x.ParentPersonID);
        }
    }


    public partial class Phone
    {
        public virtual int PhoneID { get; set; }
        public virtual PhoneTypeEnum PhoneTypeID { get; set; }
        public virtual string Number { get; set; }
        public virtual string LastUpdatedBy { get; set; }
        public virtual System.DateTime LastUpdatedDate { get; set; }
        public virtual Nullable<int> PersonID { get; set; }

        public virtual Person Person { get; set; }
        public virtual PhoneType PhoneType { get; set; }
    }

    public partial class Person
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2214:DoNotCallOverridableMethodsInConstructors")]
        public Person()
        {
            this.Kids = new HashSet<Person>();
            this.Phones = new HashSet<Phone>();
        }

        public virtual int PersonID { get; set; }
        public virtual string Name { get; set; }
        public virtual int Age { get; set; }
        public virtual Nullable<int> ParentPersonID { get; set; }
        public virtual string LastUpdatedBy { get; set; }
        public virtual System.DateTime LastUpdatedDate { get; set; }
        public virtual string Gender { get; set; }
        public virtual bool IsDeleted { get; set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public virtual ICollection<Person> Kids { get; set; }
        public virtual Person Parent { get; set; }
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public virtual ICollection<Phone> Phones { get; set; }
    }

    public partial class PhoneType
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2214:DoNotCallOverridableMethodsInConstructors")]
        public PhoneType()
        {
            this.Phones = new HashSet<Phone>();
        }

        public virtual PhoneTypeEnum PhoneTypeID { get; set; }
        public virtual string PhoneTypeDesc { get; set; }
        public virtual string LastUpdatedBy { get; set; }
        public virtual System.DateTime LastUpdatedDate { get; set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public virtual ICollection<Phone> Phones { get; set; }
    }

    public enum PhoneTypeEnum : int
    {
        Home = 1,
        Work = 2,
        Mobile = 3
    }

    public partial class up_Person_SummaryForParents_Result
    {
        public virtual int PersonID { get; set; }
        public virtual string Name { get; set; }
        public virtual int Age { get; set; }
        public virtual Nullable<int> ParentPersonID { get; set; }
        public virtual string Gender { get; set; }
        public virtual string LastUpdatedBy { get; set; }
        public virtual System.DateTime LastUpdatedDate { get; set; }
        public virtual Nullable<int> MaleChildren { get; set; }
        public virtual Nullable<int> FemaleChildren { get; set; }
        public virtual Nullable<int> FamilyPhones { get; set; }
    }

}
