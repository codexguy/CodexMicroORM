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
05/2018    0.6     Split from servicescope.cs (Joel Champagne)
***********************************************************************/
using System;
using System.Collections.Generic;
using CodexMicroORM.Core.Services;
using CodexMicroORM.Core.Collections;
using System.ComponentModel;

namespace CodexMicroORM.Core
{
    public sealed partial class ServiceScope
    {
        public sealed class TrackedObject : ICEFIndexedListItem
        {
            public string BaseName { get; set; }
            public Type BaseType { get; set; }
            public CEFWeakReference<object> Target { get; set; }
            public CEFWeakReference<ICEFWrapper> Wrapper { get; set; }
            public ICEFInfraWrapper Infra { get; set; }
            public List<ICEFService> Services { get; set; }
            
            public bool ValidTarget
            {
                get
                {
                    return (Target?.IsAlive).GetValueOrDefault();
                }
            }

            public object GetTarget()
            {
                if ((Target?.IsAlive).GetValueOrDefault() && Target.Target != null)
                    return Target.Target;

                return null;
            }

            public ICEFInfraWrapper GetInfra()
            {
                if ((Target?.IsAlive).GetValueOrDefault())
                {
                    return Infra;
                }

                return null;
            }

            public INotifyPropertyChanged GetNotifyFriendly()
            {
                var test1 = GetTarget() as INotifyPropertyChanged;
                if (test1 != null)
                    return test1;

                var test2 = GetWrapper() as INotifyPropertyChanged;
                if (test2 != null)
                    return test2;

                return GetInfra() as INotifyPropertyChanged;
            }

            public ICEFInfraWrapper GetCreateInfra()
            {
                var infra = GetInfra();

                if (infra != null)
                    return infra;

                // Must succeed so create an infra wrapper!
                var wt = GetWrapperTarget();

                if (wt == null)
                    throw new CEFInvalidOperationException("Failed to identify target object to create infrastructure wrapper for.");

                Infra = WrappingHelper.CreateInfraWrapper(WrappingSupport.All, WrappingAction.Dynamic, false, wt, null, null, null);
                return Infra;
            }

            public ICEFWrapper GetWrapper()
            {
                if ((Wrapper?.IsAlive).GetValueOrDefault() && Wrapper.Target != null)
                    return Wrapper.Target as ICEFWrapper;

                return null;
            }

            public object GetInfraWrapperTarget()
            {
                return GetInfra() ?? GetWrapper() ?? GetTarget();
            }

            public object GetWrapperTarget()
            {
                return GetWrapper() ?? GetTarget();
            }

            public object GetValue(string propName, bool unwrap)
            {
                switch (propName)
                {
                    case nameof(BaseName):
                        return BaseName;

                    case nameof(BaseType):
                        return BaseType;

                    case nameof(Target):
                        if (unwrap)
                            return Target.IsAlive ? Target.Target : null;
                        else
                            return Target;

                    case nameof(Wrapper):
                        if (unwrap)
                            return Wrapper.IsAlive ? Wrapper.Target : null;
                        else
                            return Wrapper;

                    case nameof(Infra):
                        return Infra;

                    case nameof(Services):
                        return Services;
                }
                throw new NotSupportedException("Unsupported property name.");
            }

            public bool IsAlive
            {
                get
                {
                    return !((!(Target?.IsAlive).GetValueOrDefault() || Target.Target == null)
                        && (!(Wrapper?.IsAlive).GetValueOrDefault() || Wrapper.Target == null));
                }
            }
        }
    }
}
