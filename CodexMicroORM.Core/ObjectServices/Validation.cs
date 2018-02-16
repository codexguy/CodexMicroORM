﻿/***********************************************************************
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
02/2018    0.2.4   Primary implementation (Joel Champagne)
***********************************************************************/
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CodexMicroORM.Core.Services
{
    public class ValidationService : ICEFValidationHost
    {
        private static ConcurrentDictionary<Type, List<(string prop, object defval)>> _typePropRequired = new ConcurrentDictionary<Type, List<(string prop, object defval)>>();
        private static ConcurrentDictionary<Type, List<(string prop, int maxlength)>> _typePropMaxLength = new ConcurrentDictionary<Type, List<(string prop, int maxlength)>>();
        private static ConcurrentDictionary<Type, List<(string prop, double minval, double maxval)>> _typePropRange = new ConcurrentDictionary<Type, List<(string prop, double minval, double maxval)>>();
        private static ConcurrentDictionary<Type, List<(string prop, Func<object, string> fn)>> _typeCustomValidator = new ConcurrentDictionary<Type, List<(string prop, Func<object, string> fn)>>();
        private static ConcurrentDictionary<Type, List<string>> _typeIllegalUpdate = new ConcurrentDictionary<Type, List<string>>();

        /// <summary>
        /// Registers a global validation for a specific type / specific property, indicating it cannot be updated after having been assigned a value.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="propName"></param>
        public static void RegisterIllegalUpdate<T>(string propName)
        {
            CEF.RegisterForType<T>(new ValidationService());

            List<string> vl = null;

            _typeIllegalUpdate.TryGetValue(typeof(T), out vl);

            if (vl == null)
            {
                vl = new List<string>();
            }

            vl.Add(propName);
            _typeIllegalUpdate[typeof(T)] = vl;
        }

        /// <summary>
        /// Registers a global validation for a specific type / specific property, indicating it is a required field.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <typeparam name="V"></typeparam>
        /// <param name="propName"></param>
        public static void RegisterRequired<T, V>(string propName) where T : class
        {
            CEF.RegisterForType<T>(new ValidationService());

            List<(string prop, object defval)> vl = null;

            _typePropRequired.TryGetValue(typeof(T), out vl);

            if (vl == null)
            {
                vl = new List<(string prop, object defval)>();
            }

            vl.Add((propName, default(V)));
            _typePropRequired[typeof(T)] = vl;
        }

        /// <summary>
        /// Registers a global validation for a specific type, allowing running a custom rule over each instance (optionally bound to a specific property). Returning a non-null string error message indicates an error state.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="validator"></param>
        /// <param name="propName"></param>
        public static void RegisterCustomValidation<T>(Func<T, string> validator, string propName = null) where T : class
        {
            CEF.RegisterForType<T>(new ValidationService());

            List<(string prop, Func<object, string> fn)> vl = null;

            _typeCustomValidator.TryGetValue(typeof(T), out vl);

            if (vl == null)
            {
                vl = new List<(string prop, Func<object, string> fn)>();
            }

            vl.Add((propName, (object p) => validator.Invoke((T)p)));
            _typeCustomValidator[typeof(T)] = vl;
        }

        /// <summary>
        /// Registers a global validation for a specific type / specific property, indicating its maximum length when cast to a string.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="propName"></param>
        /// <param name="maxlength"></param>
        public static void RegisterMaxLength<T>(string propName, int maxlength) where T : class
        {
            CEF.RegisterForType<T>(new ValidationService());

            List<(string prop, int maxlength)> vl = null;

            _typePropMaxLength.TryGetValue(typeof(T), out vl);

            if (vl == null)
            {
                vl = new List<(string prop, int maxlength)>();
            }

            vl.Add((propName, maxlength));
            _typePropMaxLength[typeof(T)] = vl;
        }

        /// <summary>
        /// Registers a global vlaidation for a specific type / specific property, indicating a minimum and/or maximum numeric value.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="propName"></param>
        /// <param name="minval"></param>
        /// <param name="maxval"></param>
        public static void RegisterRangeValidation<T>(string propName, double? minval, double? maxval) where T : class
        {
            CEF.RegisterForType<T>(new ValidationService());

            List<(string prop, double minval, double maxval)> vl = null;

            _typePropRange.TryGetValue(typeof(T), out vl);

            if (vl == null)
            {
                vl = new List<(string prop, double minval, double maxval)>();
            }

            vl.Add((propName, minval.GetValueOrDefault(double.MinValue), maxval.GetValueOrDefault(double.MaxValue)));
            _typePropRange[typeof(T)] = vl;
        }

        public static string RequiredFieldMessage
        {
            get;
            set;
        } = "{0} is required.";

        public static string TooLargeFieldMessage
        {
            get;
            set;
        } = "{0} is too long (maximum length is {1}).";

        public static string RangeLessThanFieldMessage
        {
            get;
            set;
        } = "{0} must be less than or equal to {1}";

        public static string RangeGreaterThanFieldMessage
        {
            get;
            set;
        } = "{0} must be greater than or equal to {1}.";

        public static string RangeBetweenFieldMessage
        {
            get;
            set;
        } = "{0} must be between {1} and {2}.";

        public static string IllegalUpdateMessage
        {
            get;
            set;
        } = "Cannot update {0}.";

        public static bool SplitCamelCaseWords
        {
            get;
            set;
        } = true;

        /// <summary>
        /// Return zero, one or many validation messages that pertain to the input object, based on registered validations.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="o"></param>
        /// <returns></returns>
        public IEnumerable<(ValidationErrorCode error, string message)> GetObjectMessage<T>(T o) where T : class
        {
            var uw = o.AsUnwrapped();
            var iw = o.AsInfraWrapped();
            var bt = uw.GetBaseType();

            List<(ValidationErrorCode error, string message)> messages = new List<(ValidationErrorCode error, string message)>();

            if (_typePropRequired.TryGetValue(bt, out List<(string prop, object defval)> vl))
            {
                foreach (var v in vl)
                {
                    if (v.defval.IsSame(iw.GetValue(v.prop)))
                    {
                        messages.Add((ValidationErrorCode.MissingRequired, BuildMessageForProperty(RequiredFieldMessage, v.prop)));
                    }
                }
            }

            if (_typePropMaxLength.TryGetValue(bt, out List<(string prop, int maxlength)> vl2))
            {
                foreach (var v in vl2)
                {
                    if (iw.GetValue(v.prop)?.ToString().Length > v.maxlength)
                    {
                        messages.Add((ValidationErrorCode.TooLarge, BuildMessageForProperty(TooLargeFieldMessage, v.prop, v.maxlength)));
                    }
                }
            }

            if (_typeIllegalUpdate.TryGetValue(bt, out List<string> vl2b))
            {
                iw.UpdateData();

                foreach (var v in vl2b)
                {
                    if (iw.GetRowState() == ObjectState.Modified)
                    {
                        if (!iw.GetOriginalValue(v, false).IsSame(iw.GetValue(v)))
                        {
                            messages.Add((ValidationErrorCode.IllegalUpdate, BuildMessageForProperty(IllegalUpdateMessage, v)));
                        }
                    }
                }
            }

            if (_typePropRange.TryGetValue(bt, out List<(string prop, double minval, double maxval)> vl3))
            {
                foreach (var v in vl3)
                {
                    if (double.TryParse(iw.GetValue(v.prop)?.ToString(), out double val))
                    {
                        if (val < v.minval || val > v.maxval)
                        {
                            if (v.minval != double.MinValue && v.maxval != double.MaxValue)
                            {
                                messages.Add((ValidationErrorCode.NumericRange, BuildMessageForProperty(RangeBetweenFieldMessage, v.prop, v.minval, v.maxval)));
                            }
                            else
                            {
                                if (v.minval == double.MinValue)
                                {
                                    messages.Add((ValidationErrorCode.NumericRange, BuildMessageForProperty(RangeLessThanFieldMessage, v.prop, v.maxval)));
                                }
                                else
                                {
                                    messages.Add((ValidationErrorCode.NumericRange, BuildMessageForProperty(RangeGreaterThanFieldMessage, v.prop, v.minval)));
                                }
                            }
                        }
                    }
                }
            }

            if (_typeCustomValidator.TryGetValue(bt, out List<(string prop, Func<object, string> fn)> vl4))
            {
                foreach (var v in vl4)
                {
                    var msg = v.fn.Invoke(uw);

                    if (!string.IsNullOrEmpty(msg))
                    {
                        messages.Add((ValidationErrorCode.CustomError, msg));
                    }
                }
            }

            return messages;
        }

        /// <summary>
        /// Return zero, one or many validation messages that pertain to a specific property of the input object, based on registered validations.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="o"></param>
        /// <param name="propName"></param>
        /// <returns></returns>
        public IEnumerable<(ValidationErrorCode error, string message)> GetPropertyMessages<T>(T o, string propName) where T : class
        {
            var uw = o.AsUnwrapped();
            var iw = o.AsInfraWrapped();
            var bt = uw.GetBaseType();

            List<(ValidationErrorCode error, string message)> messages = new List<(ValidationErrorCode error, string message)>();

            if (_typePropRequired.TryGetValue(bt, out List<(string prop, object defval)> vl))
            {
                var pmatch = (from a in vl where string.Compare(propName, a.prop, !Globals.CaseSensitiveDictionaries) == 0 select a);

                if (pmatch.Any() && pmatch.First().defval.IsSame(iw.GetValue(propName)))
                {
                    messages.Add((ValidationErrorCode.MissingRequired, BuildMessageForProperty(RequiredFieldMessage, propName)));
                }
            }

            if (_typePropMaxLength.TryGetValue(bt, out List<(string prop, int maxlength)> vl2))
            {
                var pmatch = (from a in vl2 where string.Compare(propName, a.prop, !Globals.CaseSensitiveDictionaries) == 0 select a);

                if (pmatch.Any() && iw.GetValue(propName)?.ToString().Length > pmatch.First().maxlength)
                {
                    messages.Add((ValidationErrorCode.TooLarge, BuildMessageForProperty(TooLargeFieldMessage, propName, pmatch.First().maxlength)));
                }
            }

            if (_typeIllegalUpdate.TryGetValue(bt, out List<string> vl2b))
            {
                iw.UpdateData();

                var pmatch = (from a in vl2b where string.Compare(propName, a, !Globals.CaseSensitiveDictionaries) == 0 select a).FirstOrDefault();

                if (!string.IsNullOrEmpty(pmatch) && iw.GetRowState() == ObjectState.Modified && !iw.GetOriginalValue(pmatch, false).IsSame(iw.GetValue(pmatch)))
                {
                    messages.Add((ValidationErrorCode.IllegalUpdate, BuildMessageForProperty(IllegalUpdateMessage, pmatch)));
                }
            }

            if (_typeCustomValidator.TryGetValue(bt, out List<(string prop, Func<object, string> fn)> vl3))
            {
                var pmatch = (from a in vl3 where string.Compare(propName, a.prop, !Globals.CaseSensitiveDictionaries) == 0 select a);

                if (pmatch.Any())
                {
                    var msg = pmatch.First().fn.Invoke(uw);

                    if (!string.IsNullOrEmpty(msg))
                    {
                        messages.Add((ValidationErrorCode.CustomError, msg));
                    }
                }
            }

            return messages;
        }

        private static string BuildMessageForProperty(string msg, string propName, object opt1 = null, object opt2 = null)
        {
            if (SplitCamelCaseWords)
            {
                foreach (Match m in Regex.Matches(propName, "[a-z][A-Z]").Cast<Match>().ToArray())
                {
                    propName = propName.Replace(m.Value, $"{m.Value[0]} {m.Value[1]}");
                }
            }

            return string.Format(msg, propName, opt1, opt2);
        }

        void ICEFService.Disposing(ServiceScope ss)
        {
        }

        void ICEFService.FinishSetup(ServiceScope.TrackedObject to, ServiceScope ss, bool isNew, IDictionary<string, object> props, ICEFServiceObjState state)
        {
        }

        WrappingSupport ICEFService.IdentifyInfraNeeds(object o, object replaced, ServiceScope ss, bool isNew)
        {
            var bt = o.GetBaseType();

            // We only need to support data errors if we have a registered validation for the type in question!
            if (_typeCustomValidator.ContainsKey(bt) || _typePropMaxLength.ContainsKey(bt) || _typePropRequired.ContainsKey(bt))
            {
                return WrappingSupport.DataErrors;
            }

            return WrappingSupport.None;
        }

        Type ICEFService.IdentifyStateType(object o, ServiceScope ss, bool isNew)
        {
            return null;
        }

        IList<Type> ICEFService.RequiredServices()
        {
            return null;
        }
    }
}
