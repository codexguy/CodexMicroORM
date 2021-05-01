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
#nullable enable
using System.ComponentModel;

namespace CodexMicroORM.Core.Services
{
    public class ValidationWrapper
    {
        private readonly IDataErrorInfo? _source;
        private readonly IDataErrorInfo? _iwsource;

        internal ValidationWrapper(ICEFInfraWrapper iw)
        {
            // If contained object implements IDataErrorInfo, support that "first"
            _source = iw.GetWrappedObject() as IDataErrorInfo;
            _iwsource = iw as IDataErrorInfo;
        }

        public bool IsValid => string.IsNullOrEmpty(_source?.Error) && string.IsNullOrEmpty(_iwsource?.Error);

        public bool IsPropertyValid(string propName) => string.IsNullOrEmpty(_source?[propName]) && string.IsNullOrEmpty(_iwsource?[propName]);

        public string? Error => _source?.Error ?? _iwsource?.Error;

        public string? PropertyError(string propName) => _source?[propName] ?? _iwsource?[propName];
    }
}
