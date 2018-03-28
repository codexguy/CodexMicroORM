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
01/2018    0.2.2   Primary implementation (Joel Champagne)
***********************************************************************/
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CodexMicroORM.Core.Services
{
    public class PCTService : ICEFPersistenceHost
    {
        Type ICEFService.IdentifyStateType(object o, ServiceScope ss, bool isNew)
        {
            return null;
        }

        /// <summary>
        /// For a given object, persist its JSON representation to stream.
        /// </summary>
        /// <param name="tw"></param>
        /// <param name="o"></param>
        /// <param name="mode"></param>
        /// <param name="visits"></param>
        /// <returns></returns>
        public bool SaveContents(JsonTextWriter tw, object o, SerializationMode mode, IDictionary<object, bool> visits)
        {
            if (visits.ContainsKey(o))
            {
                return visits[o];
            }

            bool include = true;

            if ((mode & SerializationMode.OnlyChanged) != 0)
            {
                // Point of this? if we have object graph a->b->c, if c is modified, both a and b need to be included even if unmodified to support proper hierarchy
                include = RequiresPersistenceForChanges(o, mode, new ConcurrentDictionary<object, bool>());
            }

            if (include)
            {
                WriteSerializationText(tw, o, mode, visits);
            }

            visits[o] = true;

            return include;
        }

        public IEnumerable<T> GetItemsFromSerializationText<T>(string json) where T : class, new()
        {
            var setArray = JArray.Parse(json);

            foreach (var i in setArray.Children())
            {
                if (i.First.Type == JTokenType.Object)
                {
                    yield return CEF.Deserialize<T>(i.ToString());
                }
            }
        }

        private void WriteSerializationText(JsonTextWriter tw, object o, SerializationMode mode, IDictionary<object, bool> visits)
        {
            var iw = o.AsInfraWrapped(false);

            if (iw != null)
            {
                tw.WriteStartObject();

                var wot = iw.GetWrappedObject()?.GetType() ?? iw.GetBaseType();

                // We only really want/need to include type info on outermost objects (session scope level only), so reset this for all nested objects
                var nextmode = (SerializationMode)((int)mode & (-1 ^ (int)SerializationMode.IncludeType));

                if ((mode & SerializationMode.IncludeType) != 0)
                {
                    tw.WritePropertyName(Globals.SerializationTypePropertyName);
                    tw.WriteValue(o.GetType().AssemblyQualifiedName);
                }

                foreach (var kvp in iw.GetAllValues())
                {
                    // If it's enumerable, recurse each item
                    // TODO - better way to detect primitive type like string w/o hardcoding??
                    if (kvp.Value is IEnumerable && kvp.Value.GetType() != typeof(string))
                    {
                        tw.WritePropertyName(kvp.Key);
                        tw.WriteStartArray();

                        var asEnum = (kvp.Value as IEnumerable).GetEnumerator();

                        while (asEnum.MoveNext())
                        {
                            var i = asEnum.Current;

                            // We only need to do this for tracked objects, for now we only do for value types or non-system (TODO)
                            if (i == null || i.GetType().IsValueType || i.GetType().FullName.StartsWith("System."))
                            {
                                tw.WriteValue(i);
                            }
                            else
                            {
                                if ((mode & SerializationMode.SingleLevel) == 0)
                                {
                                    var iw2 = i.AsInfraWrapped();

                                    if (iw2 != null)
                                    {
                                        SaveContents(tw, iw2, nextmode, visits);
                                    }
                                    else
                                    {
                                        if (i.GetType().IsSerializable)
                                        {
                                            tw.WriteValue(i);
                                        }
                                    }
                                }
                            }
                        }

                        tw.WriteEndArray();
                    }
                    else
                    {
                        // If it's a tracked object, recurse
                        if (kvp.Value != null && !kvp.Value.GetType().IsValueType && CEF.CurrentServiceScope.GetTrackedByWrapperOrTarget(kvp.Value) != null)
                        {
                            if ((mode & SerializationMode.SingleLevel) == 0)
                            {
                                var iw2 = kvp.Value.AsInfraWrapped();

                                if (iw2 != null)
                                {
                                    tw.WritePropertyName(kvp.Key);
                                    SaveContents(tw, iw2, nextmode, visits);
                                }
                            }
                        }
                        else
                        {
                            if (kvp.Value != null || (mode & SerializationMode.IncludeNull) != 0)
                            {
                                if (((mode & SerializationMode.IncludeReadOnlyProps) != 0) || (wot.GetProperty(kvp.Key)?.CanWrite).GetValueOrDefault(true))
                                {
                                    if (((mode & SerializationMode.OnlyCLRProperties) == 0) || (wot.GetProperty(kvp.Key)?.CanRead).GetValueOrDefault(false))
                                    {
                                        var aud = CEF.CurrentServiceScope.GetService<ICEFAuditHost>();

                                        if (aud != null)
                                        {
                                            if (((mode & SerializationMode.OriginalForConcurrency) == 0) || (string.Compare(aud.LastUpdatedDateField, kvp.Key, true) != 0 && string.Compare(aud.IsDeletedField, kvp.Key, true) != 0))
                                            {
                                                if (kvp.Value == null || kvp.Value.GetType().IsSerializable)
                                                {
                                                    tw.WritePropertyName(kvp.Key);
                                                    tw.WriteValue(kvp.Value);
                                                }
                                            }
                                            else
                                            {
                                                // Only need to send original date - do not send isdeleted at all
                                                if (string.Compare(aud.IsDeletedField, kvp.Key, true) != 0)
                                                {
                                                    var rs = iw.GetRowState();

                                                    if (rs != ObjectState.Added && rs != ObjectState.Unlinked)
                                                    {
                                                        var val = iw.GetOriginalValue(kvp.Key, false);

                                                        if (val != null)
                                                        {
                                                            tw.WritePropertyName(kvp.Key);
                                                            tw.WriteValue(val);
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                // Allows for inclusion of object state, etc.
                iw.FinalizeObjectContents(tw, mode);

                tw.WriteEndObject();
            }
            else
            {
                tw.WriteValue(o);
            }
        }

        /// <summary>
        /// Only applicable if trying to persist changes. Need to determine if the given object needs persistence because one of its child objects may need it.
        /// </summary>
        /// <param name="o"></param>
        /// <param name="visits"></param>
        /// <returns></returns>
        private static bool RequiresPersistenceForChanges(object o, SerializationMode mode, IDictionary<object, bool> visits)
        {
            if (visits.ContainsKey(o))
            {
                return visits[o];
            }

            var iw = o.AsInfraWrapped(false);

            if (iw != null)
            {
                var rs = iw.GetRowState();

                if (rs != ObjectState.Unchanged && rs != ObjectState.Unlinked)
                {
                    visits[iw] = true;
                    return true;
                }

                if ((mode & SerializationMode.SingleLevel) != 0)
                {
                    visits[iw] = false;
                    return false;
                }

                int requires = 0;
                var av = (from a in iw.GetAllValues() where a.Value != null && !a.Value.GetType().IsValueType && !a.Value.GetType().FullName.StartsWith("System.") select a).ToList();
                var maxdop = Globals.EnableParallelPropertyParsing && Environment.ProcessorCount > 2 && av.Count() > Environment.ProcessorCount >> 1 ? Environment.ProcessorCount >> 1 : 1;

                Parallel.ForEach(av, new ParallelOptions() { MaxDegreeOfParallelism = maxdop }, (kvp, pls) =>
                {
                    // If it's a tracked list, recurse each item
                    var asEnum = kvp.Value as IEnumerable;

                    if (asEnum != null)
                    {
                        var sValEnum = asEnum.GetEnumerator();

                        while (sValEnum.MoveNext())
                        {
                            // Only makes sense if enumerating something wrapped, otherwise assume does NOT require serialization (no changes)
                            var iw2 = CEF.CurrentServiceScope.GetOrCreateInfra(sValEnum.Current, false);

                            if (iw2 != null)
                            {
                                if (RequiresPersistenceForChanges(iw2, mode, visits))
                                {
                                    Interlocked.Increment(ref requires);
                                    pls.Break();
                                }
                            }
                        }
                    }
                    else
                    {
                        // If it's a tracked object, recurse
                        var iw2 = CEF.CurrentServiceScope.GetOrCreateInfra(kvp.Value, false);

                        if (iw2 != null)
                        {
                            if (RequiresPersistenceForChanges(iw2, mode, visits))
                            {
                                Interlocked.Increment(ref requires);
                                pls.Break();
                            }
                        }
                    }
                });

                visits[o] = requires > 0;
                return requires > 0;
            }

            visits[o] = false;
            return false;
        }

        void ICEFService.FinishSetup(ServiceScope.TrackedObject to, ServiceScope ss, bool isNew, IDictionary<string, object> props, ICEFServiceObjState state)
        {
        }

        WrappingSupport ICEFService.IdentifyInfraNeeds(object o, object replaced, ServiceScope ss, bool isNew)
        {
            if ((replaced ?? o) is INotifyPropertyChanged)
            {
                return WrappingSupport.OriginalValues;
            }
            else
            {
                return WrappingSupport.OriginalValues | WrappingSupport.Notifications;
            }
        }

        IList<Type> ICEFService.RequiredServices()
        {
            return new Type[] { typeof(ICEFKeyHost) };
        }

        public void Disposing(ServiceScope ss)
        {
        }
    }
}
