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
02/2018    0.2.4   CEFValidationException (Joel Champagne)
***********************************************************************/
using System;
using System.Collections.Generic;

namespace CodexMicroORM.Core
{
    public class CEFInvalidOperationException : InvalidOperationException
    {
        public CEFInvalidOperationException(string msg) : base(msg)
        {
        }

        public CEFInvalidOperationException(string msg, Exception inner) : base(msg, inner)
        {
        }
    }

    public class CEFValidationException : ApplicationException
    {
        private IEnumerable<(ValidationErrorCode error, string message)> _messages = null;

        public CEFValidationException(string msg) : base(msg)
        {
        }

        public CEFValidationException(string msg, Exception inner) : base(msg, inner)
        {
        }

        public CEFValidationException(string msg, IEnumerable<(ValidationErrorCode error, string message)> messages) : base(msg)
        {
            _messages = messages;
        }

        public IEnumerable<(ValidationErrorCode error, string message)> Messages => _messages;
    }
}
