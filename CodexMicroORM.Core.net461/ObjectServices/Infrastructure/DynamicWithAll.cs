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
#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;

namespace CodexMicroORM.Core.Services
{
    /// <summary>
    /// A fully featured wrapper that implements INotifyPropertyChanged, providing change notifications for updates on CLR properties as well as dynamic prop bag properties.
    /// </summary>
    public sealed class DynamicWithAll : DynamicWithValuesBagErrors, INotifyPropertyChanged
    {
        internal DynamicWithAll(object o, ObjectState irs, IDictionary<string, object?>? props, IDictionary<string, Type>? types) : base(o, irs, props, types)
        {            
        }

        protected override void OnPropertyChanged(string propName, object? oldVal, object? newVal, bool isBag)
        {
            base.OnPropertyChanged(propName, oldVal, newVal, isBag);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }

        public override WrappingSupport SupportsWrapping()
        {
            return WrappingSupport.OriginalValues | WrappingSupport.PropertyBag | WrappingSupport.Notifications | WrappingSupport.DataErrors;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

}
