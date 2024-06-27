/***********************************************************************
Copyright 2024 CodeX Enterprises LLC

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
#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace CodexMicroORM.Core
{
    public enum InvalidStateType
    {
        Undefined = 0,
        ArgumentNull = 1,
        LowLevelState = 2,
        ObjectTrackingIssue = 3,
        MissingService = 4,
        MissingKey = 5,
        BadParameterValue = 6,
        Serialization = 7,
        BadAction = 8,
        SQLLayer = 9,
        DataTypeIssue = 10,
        MissingInit = 11,
        MissingServiceState = 12
    }

    public class CEFInvalidStateException : InvalidOperationException
    {
        private readonly string? _message;
        private readonly string? _method;

        public CEFInvalidStateException(InvalidStateType failtype, [CallerMemberName] string? caller = null) : base()
        {
            FailType = failtype;
            _method = caller;
        }

        public CEFInvalidStateException(InvalidStateType failtype, string message, [CallerMemberName] string? caller = null) : base()
        {
            FailType = failtype;
            _message = message;
            _method = caller;
        }

        public InvalidStateType FailType { get; } = InvalidStateType.Undefined;

        public override string Message
        {
            get
            {
                string s = FailType switch
                {
                    InvalidStateType.ArgumentNull => "Argument value missing.",
                    InvalidStateType.LowLevelState => "Invalid or corrupted state (low-level).",
                    InvalidStateType.ObjectTrackingIssue => "Object tracking data is missing or corrupt.",
                    InvalidStateType.MissingService => "Missing service.",
                    InvalidStateType.MissingKey => "Missing key definition.",
                    InvalidStateType.BadParameterValue => "Bad parameter value.",
                    InvalidStateType.Serialization => "Serialization error.",
                    InvalidStateType.BadAction => "Bad attempted action.",
                    InvalidStateType.SQLLayer => "SQL-related error.",
                    InvalidStateType.DataTypeIssue => "Data type / conversion issue.",
                    InvalidStateType.MissingInit => "Missing initialization (framework settings).",
                    InvalidStateType.MissingServiceState => "Missing service state data.",
                    _ => "Invalid operation.",
                };

                StringBuilder sb = new();
                sb.Append(s);
                sb.Append(" This indicates a possible framework or framework usage issue.");

                if (!string.IsNullOrEmpty(_message))
                {
                    sb.Append(" ");

                    switch (FailType)
                    {
                        case InvalidStateType.ArgumentNull:
                            sb.Append($"Argument: {_message}.");
                            break;

                        case InvalidStateType.MissingKey:
                            sb.Append($"Type: {_message}.");
                            break;

                        case InvalidStateType.MissingServiceState:
                            sb.Append($"Service: {_message}");
                            break;

                        default:
                            sb.Append(_message);
                            break;
                    }
                }

                if (!string.IsNullOrEmpty(_method))
                {
                    sb.Append(" (");
                    sb.Append("In: ");
                    sb.Append(_method);
                    sb.Append(".)");
                }

                return sb.ToString();
            }
        }

        public CEFInvalidStateException()
        {
        }

        public CEFInvalidStateException(string message, [CallerMemberName] string? caller = null) : base(message)
        {
            _message = message;
            _method = caller;
        }

        public CEFInvalidStateException(string message, Exception innerException) : base(message, innerException)
        {
            _message = message;
        }

        public CEFInvalidStateException(InvalidStateType failtype, string message, Exception innerException, [CallerMemberName] string? caller = null) : base(message, innerException)
        {
            FailType = failtype;
            _message = message;
            _method = caller;
        }

        public CEFInvalidStateException(string message) : base(message)
        {
        }
    }

    public class CEFConstraintException : Exception
    {
        public CEFConstraintException(string msg) : base(msg)
        {
        }

        public CEFConstraintException(string msg, Exception inner) : base(msg, inner)
        {
        }

        public CEFConstraintException()
        {
        }
    }

    public class CEFTimeoutException : TimeoutException
    {
        public CEFTimeoutException(string msg) : base(msg)
        {
        }

        public CEFTimeoutException(string msg, Exception inner) : base(msg, inner)
        {
        }

        public CEFTimeoutException()
        {
        }
    }

    public class CEFInvalidDataException : InvalidOperationException
    {
        public CEFInvalidDataException(string msg) : base(msg)
        {
        }
    }

    public class CEFValidationException : ApplicationException
    {
        private readonly IEnumerable<(ValidationErrorCode error, string message)>? _messages = null;

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

        public IEnumerable<(ValidationErrorCode error, string message)>? Messages => _messages;

        public CEFValidationException()
        {
        }
    }
}
