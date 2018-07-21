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
06/2018    0.7     Initial release (Joel Champagne)
***********************************************************************/
using CodexMicroORM.Core.Services;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace CodexMicroORM.Core
{
    [AttributeUsage(AttributeTargets.Class)]
    public class EntityPrimaryKeyAttribute : Attribute
    {
        public string[] Fields
        {
            get;
            private set;
        }

        public EntityPrimaryKeyAttribute(params string[] fields)
        {
            Fields = fields;
        }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class EntitySchemaNameAttribute : Attribute
    {
        public string Name
        {
            get;
            private set;
        }

        public EntitySchemaNameAttribute(string name)
        {
            Name = name;
        }
    }

    [AttributeUsage(AttributeTargets.Property)]
    public class EntityMaxLengthAttribute : Attribute
    {
        public int Length
        {
            get;
            private set;
        }

        public EntityMaxLengthAttribute(int length)
        {
            if (length < 0)
                length = int.MaxValue;

            Length = length;
        }
    }

    [AttributeUsage(AttributeTargets.Property)]
    public class EntityDefaultValueAttribute : Attribute
    {
        public string Value
        {
            get;
            private set;
        }

        public EntityDefaultValueAttribute(string value)
        {
            Value = value;
        }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class EntityRelationshipsAttribute : Attribute
    {
        public TypeChildRelationship[] Relations
        {
            get;
            private set;
        }

        private static ConcurrentDictionary<string, Type> _typeCache = new ConcurrentDictionary<string, Type>();

        private Type FindTypeByName(string name)
        {
            if (_typeCache.TryGetValue(name, out var t))
            {
                return t;
            }

            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                t = a.GetType(name, false);

                if (t != null)
                {
                    _typeCache[name] = t;
                    return t;
                }
            }

            return null;
        }

        public EntityRelationshipsAttribute(params string[] relations)
        {
            List<TypeChildRelationship> list = new List<TypeChildRelationship>();

            foreach (var rel in relations)
            {
                var typeAndFields = rel.Split('\\');

                if (typeAndFields.Length != 4)
                {
                    throw new ArgumentException($"Invalid relationship spec '{rel}'.");
                }

                var childType = FindTypeByName(typeAndFields[0]);

                if (childType == null)
                {
                    throw new ArgumentException($"Could not find child type '{typeAndFields[0]}'.");
                }

                var fields = typeAndFields[1].Split(',');

                if (fields.Length < 1)
                {
                    throw new ArgumentException($"Invalid relationship spec '{rel}'.");
                }

                var tcr = (TypeChildRelationship) typeof(TypeChildRelationship).GetMethod("Create").MakeGenericMethod(childType).Invoke(null, new object[] { fields });

                if (!string.IsNullOrEmpty(typeAndFields[2]))
                {
                    tcr = tcr.MapsToChildProperty(typeAndFields[2]);
                }

                if (!string.IsNullOrEmpty(typeAndFields[3]))
                {
                    tcr = tcr.MapsToParentProperty(typeAndFields[3]);
                }

                list.Add(tcr);
            }

            Relations = list.ToArray();
        }
    }
}
