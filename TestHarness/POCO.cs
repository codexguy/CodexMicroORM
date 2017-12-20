using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestHarness
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
