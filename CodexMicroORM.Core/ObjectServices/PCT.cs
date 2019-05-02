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
        public bool SaveContents(JsonTextWriter tw, object o, SerializationMode mode, SerializationVisitTracker visits)
        {
            if (visits.Objects.Contains(o))
            {
                return true;
            }

            bool include = true;

            if ((mode & SerializationMode.OnlyChanged) != 0)
            {
                // Point of this? if we have object graph a->b->c, if c is modified, both a and b need to be included even if unmodified to support proper hierarchy
                include = RequiresPersistenceForChanges(o, mode, new ConcurrentDictionary<object, bool>(Globals.DefaultCollectionConcurrencyLevel, Globals.DefaultDictionaryCapacity));
            }

            if (include)
            {
                WriteSerializationText(tw, o, mode, visits);
            }

            visits.Objects.Add(o);

            var wot = o.AsInfraWrapped().GetWrappedObject()?.GetType() ?? o.GetBaseType();

            if (!visits.Types.Contains(wot))
            {
                visits.Types.Add(wot);
            }

            return include;
        }

        public IEnumerable<T> GetItemsFromSerializationText<T>(string json, JsonSerializationSettings settings) where T : class, new()
        {
            if (settings == null)
            {
                settings = new JsonSerializationSettings();
            }

            switch (settings.SerializationType)
            {
                case SerializationType.Array:
                    {
                        var setArray = JArray.Parse(json);

                        foreach (var i in setArray.Children())
                        {
                            if (i.Type == JTokenType.Object)
                            {
                                yield return CEF.Deserialize<T>(i.ToString());
                            }
                        }

                        break;
                    }

                case SerializationType.ObjectWithSchemaType1AndRows:
                    {
                        // Read schema
                        Dictionary<string, Type> schema = new Dictionary<string, Type>();

                        var root = JObject.Parse(json);

                        // Read schema
                        foreach (var propInfo in root.GetValue(settings.SchemaName).ToArray())
                        {
                            if (propInfo is JObject jo)
                            {
                                if (jo.Count < 2 || jo.Count > 3)
                                {
                                    throw new CEFInvalidOperationException("Invalid JSON format.");
                                }

                                JProperty pn = (from a in jo.Children() let b = a as JProperty where b != null && b.Name.Equals(settings.SchemaFieldNameName) select b).FirstOrDefault();
                                JProperty pt = (from a in jo.Children() let b = a as JProperty where b != null && b.Name.Equals(settings.SchemaFieldTypeName) select b).FirstOrDefault();
                                JProperty pr = (from a in jo.Children() let b = a as JProperty where b != null && b.Name.Equals(settings.SchemaFieldRequiredName) select b).FirstOrDefault();

                                if (pn == null || pt == null)
                                {
                                    throw new CEFInvalidOperationException("Invalid JSON format.");
                                }

                                var t = settings.GetDataType(pt.Value.ToString());
                                var torig = t;

                                // Assume that any property might be omitted/missing which means everything should be considered nullable
                                if (t.IsValueType && !(t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Nullable<>)))
                                {
                                    t = typeof(Nullable<>).MakeGenericType(t);
                                }

                                var name = pn.Value.ToString();
                                schema[name] = t;

                                // If is required, we add a validation for this
                                if (pr != null && bool.TryParse(pr.Value.ToString(), out bool prv) && prv)
                                {
                                    ValidationService.RegisterRequired<T>(torig, name);
                                }
                            }
                        }

                        // Read objects, using the schema as the "basis" where missing/omitted properties are still carried through
                        foreach (var itemInfo in root.GetValue(settings.DataRootName).ToArray())
                        {
                            var obj = CEF.Deserialize<T>(itemInfo.ToString());
                            var iw = obj.AsInfraWrapped();

                            // We need to apply property type settings after-the-fact
                            var allProp = iw.GetAllValues();

                            foreach (var propInfo in schema)
                            {
                                var existingInfo = (from a in allProp where a.Key == propInfo.Key select (propInfo.Value, a.Value));

                                if (existingInfo.Any())
                                {
                                    iw.SetValue(propInfo.Key, existingInfo.First().Item2, existingInfo.First().Item1);
                                }
                                else
                                {
                                    iw.SetValue(propInfo.Key, null, propInfo.Value);
                                }
                            }

                            yield return obj;
                        }

                        break;
                    }
            }
        }

        private void WriteSerializationText(JsonTextWriter tw, object o, SerializationMode mode, SerializationVisitTracker visits)
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

                // Attempt to push enumerable types to the "end"
                foreach (var kvp in (from a in iw.GetAllValues() orderby a.Value is IEnumerable ? 1 : 0 select a))
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
                            if ((mode & SerializationMode.ExtendedInfoAsShadowProps) != 0)
                            {
                                var rs = iw.GetRowState();

                                if (rs == ObjectState.Modified || rs == ObjectState.ModifiedPriority)
                                {
                                    var ov = iw.GetOriginalValue(kvp.Key, false);

                                    if (ov != null)
                                    {
                                        tw.WritePropertyName("\\\\" + kvp.Key);
                                        tw.WriteValue(ov);
                                    }
                                }

                                // We write out schema metadata only when see a type for the first time
                                if (!visits.Types.Contains(wot))
                                {
                                    var pb = wot.GetProperty(kvp.Key);

                                    // Preferred data type
                                    var pt = (from a in iw.GetAllPreferredTypes() where a.Key == kvp.Key select a.Value).FirstOrDefault() ?? pb?.PropertyType;

                                    if (pt != null)
                                    {
                                        tw.WritePropertyName("\\+" + kvp.Key);
                                        tw.WriteValue(pt.AssemblyQualifiedName);

                                        // Is writeable
                                        tw.WritePropertyName("\\-" + kvp.Key);
                                        tw.WriteValue(pb?.CanWrite);
                                    }
                                }
                            }

                            if (kvp.Value != null || (mode & SerializationMode.IncludeNull) != 0)
                            {
                                if (((mode & SerializationMode.IncludeReadOnlyProps) != 0) || (wot.GetProperty(kvp.Key)?.CanWrite).GetValueOrDefault(true))
                                {
                                    if (((mode & SerializationMode.OnlyCLRProperties) == 0) || (wot.GetProperty(kvp.Key)?.CanRead).GetValueOrDefault(false))
                                    {
                                        var aud = CEF.CurrentAuditService();

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

                if ((mode & SerializationMode.ExtendedInfoAsShadowProps) != 0)
                {
                    tw.WritePropertyName("_ot_");
                    tw.WriteValue(wot.Name);
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

        void ICEFService.FinishSetup(ServiceScope.TrackedObject to, ServiceScope ss, bool isNew, IDictionary<string, object> props, ICEFServiceObjState state, bool initFromTemplate)
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
            // TODO - verify this is ok! I see no reason to hold this dependency at this point.
            return null;
            //return new Type[] { typeof(ICEFKeyHost) };
        }

        public void Disposing(ServiceScope ss)
        {
        }
    }
}
