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
***********************************************************************/
using System;
using System.Collections.Generic;
using System.Linq;
using CodexMicroORM.Core;
using CodexMicroORM.Core.Services;

namespace CodexMicroORM.BindingSupport
{
    public static class Extensions
    {
        public static GenericBindableSet AsDynamicBindable<T>(this IEnumerable<T> list) where T : ICEFInfraWrapper, new()
        {
            return new GenericBindableSet(from a in list let d = a.AsInfraWrapped() as DynamicWithBag where d != null select new DynamicBindable(d));
        }

        public static GenericBindableSet AsDynamicBindable(this System.Collections.IEnumerable list)
        {
            return new GenericBindableSet(from a in list.Cast<object>() let d = a.AsInfraWrapped() as DynamicWithBag where d != null select new DynamicBindable(d));
        }

        public static GenericBindableSet AsDynamicBindable<T>(this EntitySet<T> list) where T : class, new()
        {
            return new GenericBindableSet(from a in list let d = a.AsInfraWrapped() as DynamicWithBag where d != null select new DynamicBindable(d));
        }

        public static DynamicBindable AsDynamicBindable(this object o)
        {
            var d = (o.AsInfraWrapped() as DynamicWithBag) ?? throw new ArgumentException("Type does not have an infrastructure wrapper.");
            return new DynamicBindable(d);
        }
    }
}
