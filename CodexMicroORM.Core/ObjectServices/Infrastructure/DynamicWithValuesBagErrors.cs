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
02/2018    0.2.4   Initial release (Joel Champagne)
***********************************************************************/
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace CodexMicroORM.Core.Services
{
    public class DynamicWithValuesBagErrors : DynamicWithValuesAndBag, IDataErrorInfo
    {
        string IDataErrorInfo.this[string columnName]
        {
            get
            {
                return CEF.CurrentValidationService(this).GetPropertyMessages(this, columnName).AsString().message;
            }
        }

        string IDataErrorInfo.Error
        {
            get
            {
                return CEF.CurrentValidationService(this).GetObjectMessage(this).AsString().message;
            }
        }

        internal DynamicWithValuesBagErrors(object o, ObjectState irs, IDictionary<string, object> props, IDictionary<string, Type> types) : base(o, irs, props, types)
        {
        }

        public override WrappingSupport SupportsWrapping()
        {
            return WrappingSupport.OriginalValues | WrappingSupport.PropertyBag | WrappingSupport.DataErrors;
        }
    }
}
