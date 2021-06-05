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
05/2018    0.6     Moved from key.cs (Joel Champagne)
***********************************************************************/
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CodexMicroORM.Core.Collections;

namespace CodexMicroORM.Core.Services
{
    public sealed class TypeChildRelationship : ICEFIndexedListItem
    {
        private string _identity = "";

        private void SetIdentity()
        {
            StringBuilder sb = new StringBuilder(128);
            sb.Append(ParentType?.Name);
            sb.Append(ChildType?.Name);
            sb.Append(ChildPropertyName);
            sb.Append(ParentPropertyName);
            if (ParentKey != null)
            {
                sb.Append(string.Join("", ParentKey.ToArray()));
            }
            if (ChildRoleName != null)
            {
                sb.Append(string.Join("", ChildRoleName.ToArray()));
            }
            _identity = sb.ToString();
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(_identity);
        }

        public override bool Equals(object obj) => this._identity.IsSame((obj as TypeChildRelationship)?._identity);

        private Type? _childType;
        public Type? ChildType
        {
            get { return _childType; }
            internal set
            {
                _childType = value;
                SetIdentity();
            }
        }

        private Type? _parentType;
        public Type? ParentType
        {
            get { return _parentType; }
            internal set
            {
                _parentType = value;
                SetIdentity();
            }
        }

        private IList<string>? _parentKey;
        public IList<string>? ParentKey
        {
            get { return _parentKey; }
            internal set
            {
                _parentKey = value;
                SetIdentity();
            }
        }

        private string? _childPropertyName;
        public string? ChildPropertyName
        {
            get { return _childPropertyName; }
            internal set
            {
                _childPropertyName = value;
                SetIdentity();
            }
        }

        public string? FullParentChildPropertyName
        {
            get
            {
                if (!string.IsNullOrEmpty(ChildPropertyName))
                {
                    return $"{ParentType?.Name}.{ChildPropertyName}";
                }
                return null;
            }
        }

        public string? FullChildParentPropertyName
        {
            get
            {
                if (!string.IsNullOrEmpty(ParentPropertyName))
                {
                    return $"{ChildType?.Name}.{ParentPropertyName}";
                }
                return null;
            }
        }

        private string? _parentPropertyName;
        public string? ParentPropertyName
        {
            get { return _parentPropertyName; }
            internal set
            {
                _parentPropertyName = value;
                SetIdentity();
            }
        }

        private IList<string>? _childRoleName = null;
        public IList<string>? ChildRoleName
        {
            get
            {
                return _childRoleName;
            }
            internal set
            {
                _childRoleName = value == null ? null : value.Count == 0 ? null : value;
                SetIdentity();
            }
        }

        public IList<string> ChildResolvedKey
        {
            get
            {
                return _childRoleName ?? _parentKey ?? Array.Empty<string>();
            }
        }

        public TypeChildRelationship MapsToChildProperty(string propName)
        {
            ChildPropertyName = propName;
            return this;
        }

        public TypeChildRelationship MapsToParentProperty(string propName)
        {
            ParentPropertyName = propName;
            return this;
        }

        public static TypeChildRelationship Create<TC>(params string[] childRoleName)
        {
            var i = new TypeChildRelationship
            {
                ChildType = typeof(TC),
                ChildRoleName = childRoleName
            };
            return i;
        }

        public object? GetValue(string propName, bool unwrap)
        {
            return propName switch
            {
                nameof(TypeChildRelationship.ChildPropertyName) => ChildPropertyName,
                nameof(TypeChildRelationship.ChildRoleName) => ChildRoleName,
                nameof(TypeChildRelationship.ChildType) => ChildType,
                nameof(TypeChildRelationship.ParentKey) => ParentKey,
                nameof(TypeChildRelationship.ParentPropertyName) => ParentPropertyName,
                nameof(TypeChildRelationship.ParentType) => ParentType,
                nameof(TypeChildRelationship.FullParentChildPropertyName) => FullParentChildPropertyName,
                nameof(TypeChildRelationship.FullChildParentPropertyName) => FullChildParentPropertyName,
                _ => throw new NotSupportedException("Unsupported property name."),
            };
        }
    }
}
