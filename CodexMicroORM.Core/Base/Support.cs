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

namespace CodexMicroORM.Core
{
    /// <summary>
    /// This class affords us a way to do equality comparisons with WeakReference's that are based on the Target, not the WeakReference itself.
    /// This is critical when using it as a dictionary key: I care about finding based on the Target, not the WeakReference itself.
    /// </summary>
    public class CEFWeakReference<T> : WeakReference where T : class
    {
        private int? _hash;

        public CEFWeakReference(T target) : base(target)
        {
            _hash = target?.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if (obj == null)
                return this.Target == null;

            var wr = obj as CEFWeakReference<T>;

            if (wr != null)
            {
                return this.Target.IsSame(wr.Target);
            }

            return obj.Equals(this.Target);
        }

        public override int GetHashCode()
        {
            return _hash.GetValueOrDefault();
        }
    }

}
