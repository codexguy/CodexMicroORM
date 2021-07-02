/***********************************************************************
Copyright 2021 CodeX Enterprises LLC

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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using CodexMicroORM.Core.Collections;
using System.Collections.Immutable;
using CodexMicroORM.Core.Helper;
using System.Diagnostics;
using System.Threading;

namespace CodexMicroORM.Core.Services
{
    public class KeyService : ICEFKeyHost
    {
        public const char SHADOW_PROP_PREFIX = '\\';
        public const char SPECIAL_PROP_PREFIX = '~';

        #region "Internal state"

        private static long _lowLongKey = long.MinValue;
        private static int _lowIntKey = int.MinValue;
        private static int _lowShortKey = short.MinValue;

        // Populated during startup, represents all object primary keys we track - should be eastablished prior to relationships!
        private static readonly ConcurrentDictionary<Type, IList<string>> _primaryKeys = new(Globals.DefaultCollectionConcurrencyLevel, Globals.DefaultDictionaryCapacity);
        private static readonly ConcurrentDictionary<Type, Type> _keyKnownTypes = new(Globals.DefaultCollectionConcurrencyLevel, Globals.DefaultDictionaryCapacity);

        // Populated during startup, represents all object relationships we track
        private static readonly ConcurrentIndexedList<TypeChildRelationship> _relations = new(
            nameof(TypeChildRelationship.ParentType),
            nameof(TypeChildRelationship.ChildType),
            nameof(TypeChildRelationship.FullParentChildPropertyName),
            nameof(TypeChildRelationship.FullChildParentPropertyName));

        #endregion

        public KeyService()
        {
        }

        #region "Public state"

        public static bool DefaultPrimaryKeysCanBeDBAssigned
        {
            get;
            set;
        } = true;

        public static MergeBehavior DefaultMergeBehavior
        {
            get;
            set;
        } = Globals.DefaultMergeBehavior;

        #endregion

        #region "Static methods"

        public static TypeChildRelationship? GetMappedPropertyByFieldName(string fn)
        {
            var pcr = _relations.GetFirstByName(nameof(TypeChildRelationship.FullParentChildPropertyName), fn);

            if (pcr != null)
            {
                return pcr;
            }

            return _relations.GetFirstByName(nameof(TypeChildRelationship.FullChildParentPropertyName), fn);
        }

        /// <summary>
        /// For type T, register one or more properties that represent its primary key (unique, non-null).
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="fields"></param>
        public static void RegisterKey<T>(params string[] fields)
        {
            if (fields == null || fields.Length == 0)
                throw new CEFInvalidStateException(InvalidStateType.BadParameterValue, "Fields must be non-blank.");

            _primaryKeys[typeof(T)] = fields;

            CEF.RegisterForType<T>(new KeyService());
        }

        /// <summary>
        /// For type T, register a specific field with a known data type as its primary key.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="field"></param>
        /// <param name="knownType"></param>
        public static void RegisterKeyWithType<T>(string field, Type knownType)
        {
            if (string.IsNullOrEmpty(field))
                throw new CEFInvalidStateException(InvalidStateType.BadParameterValue, "Field must be non-blank.");

            _primaryKeys[typeof(T)] = new string[] { field };
            _keyKnownTypes[typeof(T)] = knownType;

            CEF.RegisterForType<T>(new KeyService());
        }

        /// <summary>
        /// For parent type T, register a relationship to a child entity (optional child role name and property accessor names).
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="relations"></param>
        public static void RegisterRelationship<T>(params TypeChildRelationship[] relations)
        {
            if (relations == null || relations.Length == 0)
                throw new CEFInvalidStateException(InvalidStateType.BadParameterValue, "Relations must be non-blank.");

            foreach (var r in relations)
            {
                if (!_primaryKeys.TryGetValue(typeof(T), out var parentPk))
                {
                    throw new CEFInvalidStateException(InvalidStateType.MissingKey, typeof(T).Name);
                }

                r.ParentKey = parentPk;
                r.ParentType = typeof(T);

                _relations.Add(r);
            }

            CEF.RegisterForType<T>(new KeyService());
        }

        public static IList<string> ResolveKeyDefinitionForType(Type t)
        {
            if (_primaryKeys.TryGetValue(t, out IList<string> pk))
            {
                return pk;
            }
            else
            {
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// Returns all possible child role names associated with this child type.
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        private static IList<string> ResolveChildRolesForType(Type t)
        {
            return (from a in _relations.GetAllByName(nameof(TypeChildRelationship.ChildType), t) select a.ChildRoleName ?? a.ParentKey).SelectMany((p) => p).ToList();
        }

        private static IEnumerable<object> InternalGetParentObjects(KeyServiceState state, ServiceScope ss, object o, RelationTypes types, HashSet<object> visits)
        {
            if (state == null)
                throw new CEFInvalidStateException(InvalidStateType.ArgumentNull, nameof(state));
            if (ss == null)
                throw new CEFInvalidStateException(InvalidStateType.ArgumentNull, nameof(ss));
            if (o == null)
                throw new CEFInvalidStateException(InvalidStateType.ArgumentNull, nameof(o));

            var uw = o.AsUnwrapped();

            if (uw == null || visits.Contains(uw))
            {
                yield break;
            }

            var to = ss.Objects.GetFirstByName(nameof(ServiceScope.TrackedObject.Target), uw);

            if (to != null)
            {
                var tot = to.GetTarget();

                if (tot != null)
                {
                    visits.Add(tot);

                    foreach (var fk in state.AllParents(uw))
                    {
                        var pot = fk.Parent.GetTarget();

                        if (pot != null)
                        {
                            foreach (var n in InternalGetParentObjects(state, ss, pot, types, visits))
                            {
                                yield return n;
                            }
                        }

                        yield return fk.Parent.GetInfraWrapperTarget();
                    }

                    if ((types & RelationTypes.Children) != 0)
                    {
                        foreach (var fk in state.AllChildren(uw))
                        {
                            var ch = fk.Child.GetTarget();

                            if (ch != null)
                            {
                                if (!visits.Contains(ch))
                                {
                                    yield return fk.Child.GetInfraWrapperTarget();

                                    foreach (var nc in InternalGetChildObjects(state, ss, ch, types, visits))
                                    {
                                        yield return nc;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        private static IEnumerable<object> InternalGetChildObjects(KeyServiceState state, ServiceScope ss, object o, RelationTypes types, HashSet<object> visits)
        {
            if (state == null)
                throw new CEFInvalidStateException(InvalidStateType.ArgumentNull, nameof(state));
            if (ss == null)
                throw new CEFInvalidStateException(InvalidStateType.ArgumentNull, nameof(ss));
            if (o == null)
                throw new CEFInvalidStateException(InvalidStateType.ArgumentNull, nameof(o));

            var uw = o.AsUnwrapped();

            if (uw == null || visits.Contains(uw))
            {
                yield break;
            }

            var to = ss.Objects.GetFirstByName(nameof(ServiceScope.TrackedObject.Target), uw);

            if (to != null)
            {
                var cur = to.GetTarget();

                if (cur != null)
                {
                    visits.Add(cur);

                    foreach (var fk in state.AllChildren(uw))
                    {
                        var cot = fk.Child.GetTarget();

                        if (cot != null)
                        {
                            foreach (var n in InternalGetChildObjects(state, ss, cot, types, visits))
                            {
                                yield return n;
                            }
                        }

                        yield return fk.Child.GetInfraWrapperTarget();
                    }

                    if ((types & RelationTypes.Parents) != 0)
                    {
                        foreach (var fk in state.AllParents(uw))
                        {
                            var par = fk.Parent.GetTarget();

                            if (par != null)
                            {
                                if (!visits.Contains(par))
                                {
                                    yield return fk.Parent.GetInfraWrapperTarget();

                                    foreach (var np in InternalGetParentObjects(state, ss, par, types, visits))
                                    {
                                        yield return np;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        private static void InternalGetChildTypes(Type t, IDictionary<Type, IList<string>> visits, bool all)
        {
            if (visits.ContainsKey(t))
            {
                return;
            }

            var children = _relations.GetAllByName(nameof(TypeChildRelationship.ParentType), t);

            foreach (var child in (from a in children select a.ChildType).Distinct())
            {
                if (_primaryKeys.TryGetValue(child, out IList<string> pk))
                {
                    visits[child] = pk;
                }

                if (all)
                {
                    InternalGetChildTypes(child, visits, all);
                }
            }
        }

        private static void InternalGetParentTypes(Type t, IDictionary<Type, IList<string>> visits, bool all)
        {
            if (visits.ContainsKey(t))
            {
                return;
            }

            var parents = _relations.GetAllByName(nameof(TypeChildRelationship.ChildType), t);

            foreach (var parent in (from a in parents select a.ParentType).Distinct())
            {
                if (_primaryKeys.TryGetValue(parent, out IList<string> pk))
                {
                    visits[parent] = pk;
                }

                if (all)
                {
                    InternalGetParentTypes(parent, visits, all);
                }
            }
        }

        #endregion

        public IEnumerable<TypeChildRelationship> GetRelationsForChild(Type childType) => _relations.GetAllByName(nameof(TypeChildRelationship.ChildType), childType);

        IList<Type>? ICEFService.RequiredServices() => null;

        Type ICEFService.IdentifyStateType(object o, ServiceScope ss, bool isNew) => typeof(KeyServiceState);

        WrappingSupport ICEFService.IdentifyInfraNeeds(object o, object? replaced, ServiceScope ss, bool isNew)
        {
            // If you're using shadow properties, you definitely need a property bag and notification to know when a key might be changed/assigned
            if (Globals.UseShadowPropertiesForNew)
            {
                return WrappingSupport.PropertyBag | WrappingSupport.Notifications;
            }

            var need = WrappingSupport.None;

            if (o != null)
            {
                // Need prop bag only if does not contain key fields or child role names, plus require notifications to track changes in these values
                foreach (var field in ResolveKeyDefinitionForType(o.GetBaseType()).Union(ResolveChildRolesForType(o.GetBaseType())))
                {
                    if ((replaced ?? o).GetType().GetProperty(field) == null)
                    {
                        need = WrappingSupport.PropertyBag | WrappingSupport.Notifications;
                        break;
                    }
                }
            }

            return need;
        }

        void ICEFService.FinishSetup(ServiceScope.TrackedObject to, ServiceScope ss, bool isNew, IDictionary<string, object?>? props, ICEFServiceObjState? state, bool initFromTemplate)
        {
            var uw = to.GetTarget();

            if (uw == null)
                return;

            var pkFields = ResolveKeyDefinitionForType(uw.GetBaseType());

            // If we have no key fields, nothing we can do here!
            if (pkFields == null || pkFields.Count() == 0)
            {
                return;
            }

            Type? prefKeyType = null;
            bool needsKeyAssign = false;

            // Case where not "new" row but still missing a key value - still need to generate it as such, for uniqueness!
            if (!isNew)
            {
                if (pkFields.Count == 1)
                {
                    if (props != null && !props.ContainsKey(pkFields[0]))
                    {
                        _keyKnownTypes.TryGetValue(uw.GetBaseType(), out prefKeyType);

                        if (prefKeyType != null)
                        {
                            needsKeyAssign = true;
                        }
                    }
                }
            }

            // Assign keys - new or not, we rely on unqiue values
            if (isNew || needsKeyAssign)
            {
                foreach (var k in pkFields)
                {
                    // Don't overwrite properties that we may have copied!
                    if (props == null || !props.ContainsKey(k))
                    {
                        var (setter, type) = ss.GetSetter(uw, k);

                        if (type == null)
                        {
                            type = prefKeyType;
                        }

                        // Shadow props in use, we rely on the above for the data type, but we'll be updating the shadow prop instead
                        var kssp = (!Globals.UseShadowPropertiesForNew || (type != null && type.Equals(typeof(Guid))) ? (null, null) : ss.GetSetter(uw, SHADOW_PROP_PREFIX + k));

                        var rset = (kssp.setter ?? setter);

                        if (rset != null)
                        {
                            var keyType = (type ?? kssp.type);

                            if (keyType == null)
                            {
                                keyType = Globals.DefaultKeyType;
                            }

                            if (keyType.Equals(typeof(int)))
                            {
                                rset.Invoke(System.Threading.Interlocked.Increment(ref _lowIntKey));
                            }
                            else
                            {
                                if (keyType.Equals(typeof(short)))
                                {
                                    rset.Invoke(Convert.ToInt16(System.Threading.Interlocked.Increment(ref _lowShortKey)));
                                }
                                else
                                {
                                    if (keyType.Equals(typeof(long)))
                                    {
                                        rset.Invoke(System.Threading.Interlocked.Increment(ref _lowLongKey));
                                    }
                                    else
                                    {
                                        // Only do this if no current value assigned
                                        var kg = ss.GetGetter(uw, k);

                                        if (kg.getter != null)
                                        {
                                            var cv = kg.getter();

                                            if (keyType.Equals(typeof(Guid)))
                                            {
                                                if (cv == null || Guid.Empty.Equals(cv))
                                                {
                                                    rset.Invoke(Guid.NewGuid());
                                                }
                                            }
                                            else
                                            {
                                                if (keyType.Equals(typeof(string)))
                                                {
                                                    if (cv == null || string.Empty.Equals(cv))
                                                    {
                                                        rset.Invoke(Guid.NewGuid());
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        if (props != null && Globals.UseShadowPropertiesForNew && props.ContainsKey(k) && props.ContainsKey(SHADOW_PROP_PREFIX + k))
                        {
                            var spval = props[SHADOW_PROP_PREFIX + k];
                            var defVal = WrappingHelper.GetDefaultForType(spval?.GetType() ?? throw new CEFInvalidStateException(InvalidStateType.ObjectTrackingIssue));

                            if (defVal.IsSame(props[k]) && !defVal.IsSame(spval))
                            {
                                var sptype = spval.GetType();

                                if (sptype.Equals(typeof(int)))
                                {
                                    while (Interlocked.Increment(ref _lowIntKey) < (int)spval) ;
                                }
                                else
                                {
                                    if (sptype.Equals(typeof(short)))
                                    {
                                        while (Convert.ToInt16(Interlocked.Increment(ref _lowShortKey)) < (short)spval) ;
                                    }
                                    else
                                    {
                                        if (sptype.Equals(typeof(long)))
                                        {
                                            while (Interlocked.Increment(ref _lowLongKey) < (long)spval) ;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            var keystate = (KeyServiceState?)state ?? throw new CEFInvalidStateException(InvalidStateType.MissingServiceState, "Key");

            keystate.AddPK(ss, to, to.GetNotifyFriendly(), pkFields, (changedPK, oldval, newval) =>
            {
                // Tracking dictionary may no longer correctly reflect composite key - force an update
                keystate.UpdatePK((KeyServiceState.CompositeWrapper)oldval, (KeyServiceState.CompositeWrapper)newval);
            });

            LinkByValuesInScope(to, ss, keystate);
        }

        public object? RemoveFK(ServiceScope ss, TypeChildRelationship key, ServiceScope.TrackedObject parent, ServiceScope.TrackedObject child, INotifyPropertyChanged? parentWrapped, bool nullifyChild)
        {
            return ss.GetServiceState<KeyServiceState>()?.RemoveFK(ss, key, parent, child, parentWrapped, nullifyChild);
        }

        public IEnumerable<object> GetParentObjects(ServiceScope ss, object o, RelationTypes types = RelationTypes.None)
        {
            return InternalGetParentObjects(ss.GetServiceState<KeyServiceState>() ?? throw new CEFInvalidStateException(InvalidStateType.MissingServiceState, "Key"), ss, o, types, new HashSet<object>());
        }

        public IEnumerable<object> GetChildObjects(ServiceScope ss, object o, RelationTypes types = RelationTypes.None)
        {
            return InternalGetChildObjects(ss.GetServiceState<KeyServiceState>() ?? throw new CEFInvalidStateException(InvalidStateType.MissingServiceState, "Key"), ss, o, types, new HashSet<object>());
        }

        public void UpdateBoundKeys(ServiceScope.TrackedObject to, ServiceScope ss, string fieldName, object? oval, object? nval)
        {
            var tot = to.GetTarget() ?? throw new CEFInvalidStateException(InvalidStateType.ObjectTrackingIssue);

            foreach (var rel in _relations.GetAllByName(nameof(TypeChildRelationship.FullChildParentPropertyName), $"{tot.GetBaseType().Name}.{fieldName}"))
            {
                if (nval != null)
                {
                    var toParent = ss.Objects.GetFirstByName(nameof(ServiceScope.TrackedObject.Target), nval);

                    if (toParent != null)
                    {
                        foreach (var (ordinal, name, value) in GetKeyValues(toParent.GetInfraWrapperTarget()))
                        {
                            var (setter, _) = ss.GetSetter(to.GetInfraWrapperTarget(), rel.ChildResolvedKey[ordinal]);
                            setter?.Invoke(value);
                        }
                    }
                }
            }
        }

        private void LinkByValuesInScope(ServiceScope.TrackedObject to, ServiceScope ss, KeyServiceState objstate)
        {
            var iw = to.GetCreateInfra();

            if (iw != null)
            {
                var uw = to.GetTarget();

                if (uw == null)
                    return;

                var w = to.GetWrapper();

                bool didLink = false;
                bool didAdd = false;

                var childRels = _relations.GetAllByName(nameof(TypeChildRelationship.ChildType), uw.GetBaseType()).ToArray();

                Dictionary<TypeChildRelationship, List<(int ordinal, string name, object? value)>> kvCache = new();
                List<(int ordinal, string name, object? value)>? curPK = null;

                foreach (var rel in (from a in childRels where !string.IsNullOrEmpty(a.ParentPropertyName) select a))
                {
                    if (rel.ParentPropertyName != null)
                    {
                        var val = iw.GetValue(rel.ParentPropertyName);

                        if (val != null)
                        {
                            var testParent = ss.GetTrackedByWrapperOrTarget(val);

                            if (testParent != null)
                            {
                                objstate.AddFK(ss, rel, testParent, to, testParent.GetNotifyFriendly(), true, false, false);
                                didAdd = true;
                            }
                            else
                            {
                                objstate.TrackMissingParentByRef(rel, val, to);
                            }
                        }
                        else
                        {
                            var chRoleVals = GetKeyValues(uw, rel.ChildResolvedKey);
                            kvCache[rel] = chRoleVals;

                            if (rel.ParentType != null)
                            {
                                var testParent = objstate.GetTrackedByPKValue(rel.ParentType, (from a in chRoleVals select a.value));

                                if (testParent != null)
                                {
                                    iw.SetValue(rel.ParentPropertyName, testParent.GetWrapperTarget());
                                    objstate.AddFK(ss, rel, testParent, to, testParent.GetNotifyFriendly(), true, false, false);
                                    didAdd = true;
                                }
                                else
                                {
                                    if (Globals.ResolveForArbitraryLoadOrder)
                                    {
                                        // We only track this case when asked to: it's more expensive and usually unnecessary, but during ZDB load phase, things can get loaded out-of-order due to async nature
                                        objstate.TrackMissingParentByValue(rel, to, rel.ParentType, (from a in chRoleVals select a.value));
                                    }
                                }
                            }
                        }
                    }
                }

                foreach (var rel in (from a in childRels where !string.IsNullOrEmpty(a.ChildPropertyName) && a.ParentType != null select a))
                {
                    // Current entity role name used to look up any existing parent and add to their child collection if not already there
                    var chRoleVals = kvCache.TestAssignReturn(rel, () => { return GetKeyValues(uw, rel.ChildResolvedKey); });

                    if ((from a in chRoleVals where a.value != null select a).Any())
                    {
                        var testParent = objstate.GetTrackedByPKValue(rel.ParentType!, (from a in chRoleVals select a.value));

                        if (testParent != null)
                        {
                            var (getter, type) = ss.GetGetter(testParent.GetInfraWrapperTarget(), rel.ChildPropertyName!);

                            if (type != null && getter != null && WrappingHelper.IsWrappableListType(type, null))
                            {
                                var parVal = getter.Invoke();

                                if (parVal == null)
                                {
                                    var tpuw = testParent.AsUnwrapped();

                                    if (tpuw != null)
                                    {
                                        parVal = WrappingHelper.CreateWrappingList(ss, type, tpuw, rel.ChildPropertyName!);

                                        var parChildSet = ss.GetSetter(testParent.GetInfraWrapperTarget(), rel.ChildPropertyName!);
                                        parChildSet.setter?.Invoke(parVal);
                                    }
                                }

                                if (parVal is ICEFList asCefList)
                                {
                                    if (asCefList.AddWrappedItem(w ?? uw, !didAdd))
                                    {
                                        objstate.AddFK(ss, rel, testParent, to, testParent.GetNotifyFriendly(), true, false, true);
                                        didLink = true;
                                    }
                                }
                            }
                        }
                        else
                        {
                            if (Globals.ResolveForArbitraryLoadOrder)
                            {
                                // We only track this case when asked to: it's more expensive and usually unnecessary, but during ZDB load phase, things can get loaded out-of-order due to async nature
                                objstate.TrackMissingParentChildLinkback(rel, to, rel.ParentType!, (from a in chRoleVals select a.value));
                            }
                        }
                    }
                }

                var parentRels = _relations.GetAllByName(nameof(TypeChildRelationship.ParentType), uw.GetBaseType());

                foreach (var rel in (from a in parentRels where !string.IsNullOrEmpty(a.ParentPropertyName) select a))
                {
                    foreach (var testChild in objstate.GetMissingChildrenForParentByRef(rel, w ?? uw))
                    {
                        var (getter, type) = ss.GetGetter(testChild.GetInfraWrapperTarget(), rel.ParentPropertyName!);

                        if (getter != null)
                        {
                            var val = getter.Invoke();

                            if (val != null)
                            {
                                if (val.Equals(uw) || val.Equals(w))
                                {
                                    objstate.AddFK(ss, rel, to, testChild, to.GetNotifyFriendly(), true, true, false);
                                    didLink = true;
                                }
                            }
                        }
                    }

                    // Special case where a child record refers to a parent record by ID - normally would load parent *first*, but if misordered, we go back to the current scope and look for it now
                    if (!didLink && Globals.ResolveForArbitraryLoadOrder)
                    {
                        var parKeyVal = kvCache.TestAssignReturn(rel, () => { return GetKeyValues(uw, rel.ParentKey); });
                        curPK = parKeyVal;

                        // Maintaining an unlinked list for "child with no parent by value", look for cases that meet this condition now (parent appears, need to update child to point at "this" parent)
                        foreach (var testChild in objstate.GetMissingChildrenByValue(rel, (from a in parKeyVal select a.value)))
                        {
                            var (setter, type) = ss.GetSetter(testChild.Child.GetInfraWrapperTarget(), rel.ParentPropertyName!);

                            if (setter != null)
                            {
                                setter.Invoke(to.GetWrapperTarget());
                                objstate.AddFK(ss, rel, to, testChild.Child, to.GetNotifyFriendly(), true, true, false);
                                testChild.Processed = true;
                                didLink = true;
                            }
                        }
                    }
                }

                foreach (var rel in (from a in parentRels where !string.IsNullOrEmpty(a.ChildPropertyName) select a))
                {
                    var (getter, type) = ss.GetGetter(iw, rel.ChildPropertyName!);

                    if (getter != null && type != null)
                    {
                        if (type != null && WrappingHelper.IsWrappableListType(type, null))
                        {
                            var parVal = getter.Invoke();

                            if (parVal != null)
                            {
                                var sValEnum = ((System.Collections.IEnumerable)parVal).GetEnumerator();

                                while (sValEnum.MoveNext())
                                {
                                    var chVal = sValEnum.Current;
                                    var chTo = ss.GetTrackedByWrapperOrTarget(chVal);

                                    if (chTo != null)
                                    {
                                        objstate.AddFK(ss, rel, to, chTo, to.GetNotifyFriendly(), true, false, false);
                                    }
                                }
                            }
                        }
                    }
                }

                if (Globals.ResolveForArbitraryLoadOrder)
                {
                    // In this mode, we try to identify if the current object was previously passed over for linking as a child ref
                    // Normally this should not occur if we populate in a "good order" - but when loading from data store in parallel, could encounter out-of-order
                    if (curPK == null)
                    {
                        curPK = GetKeyValues(uw);
                    }

                    if (curPK?.Count() > 0)
                    {
                        foreach (var pobj in objstate.GetMissingChildLinkbacksForParentByValue(to.BaseType ?? throw new CEFInvalidStateException(InvalidStateType.ObjectTrackingIssue), (from k in curPK select k.value)))
                        {
                            if (!string.IsNullOrEmpty(pobj.Relationship.ChildPropertyName))
                            {
                                var (getter, type) = ss.GetGetter(to.GetInfraWrapperTarget(), pobj.Relationship.ChildPropertyName);

                                if (type != null && getter != null && WrappingHelper.IsWrappableListType(type, null))
                                {
                                    var parVal = getter.Invoke();

                                    if (parVal == null)
                                    {
                                        var wt = to.GetWrapperTarget();

                                        if (wt != null)
                                        {
                                            parVal = WrappingHelper.CreateWrappingList(ss, type, wt, pobj.Relationship.ChildPropertyName);
                                        }

                                        var parChildSet = ss.GetSetter(to.GetInfraWrapperTarget(), pobj.Relationship.ChildPropertyName);
                                        parChildSet.setter?.Invoke(parVal);
                                    }

                                    if (parVal is ICEFList asCefList)
                                    {
                                        var wt = pobj.Child.GetWrapperTarget();

                                        if (wt != null && asCefList.AddWrappedItem(wt))
                                        {
                                            objstate.AddFK(ss, pobj.Relationship, to, pobj.Child, to.GetNotifyFriendly(), false, false, false);
                                            pobj.Processed = true;
                                        }
                                    }

                                    didLink = true;
                                }
                            }
                        }
                    }
                }

                if (didLink)
                {
                    // Short-circuit - we did what's below previously
                    return;
                }

                foreach (var rel in (from a in parentRels where !string.IsNullOrEmpty(a.ChildPropertyName) && a.ChildType == uw.GetBaseType() select a))
                {
                    var chRoleVals = kvCache.TestAssignReturn(rel, () => { return GetKeyValues(uw, rel.ChildResolvedKey); });

                    if (rel.ParentType != null && (from a in chRoleVals where a.value != null select a).Any())
                    {
                        var testParent = objstate.GetTrackedByPKValue(rel.ParentType, (from a in chRoleVals select a.value));

                        if (testParent != null)
                        {
                            var (getter, type) = ss.GetGetter(testParent.GetInfraWrapperTarget(), rel.ChildPropertyName!);

                            if (getter != null)
                            {
                                var parVal = getter.Invoke();
                                var asCefList = parVal as ICEFList;

                                if (parVal == null)
                                {
                                    // If this is a wrappable type, create instance now to host the child value
                                    if (type != null && WrappingHelper.IsWrappableListType(type, parVal))
                                    {
                                        parVal = WrappingHelper.CreateWrappingList(ss, type, testParent.AsUnwrapped(), rel.ChildPropertyName!);
                                        var parSet = ss.GetSetter(testParent.GetInfraWrapperTarget(), rel.ChildPropertyName!);
                                        parSet.setter?.Invoke(parVal);
                                        asCefList = parVal as ICEFList;
                                    }
                                }
                                else
                                {
                                    // Parent value might not be a CEF list type, see if we can convert it to be one now
                                    if (asCefList == null && parVal is System.Collections.IEnumerable enumerable && type != null && WrappingHelper.IsWrappableListType(type, parVal))
                                    {
                                        asCefList = WrappingHelper.CreateWrappingList(ss, type, testParent.AsUnwrapped(), rel.ChildPropertyName!);
                                        var sValEnum = enumerable.GetEnumerator();

                                        while (sValEnum.MoveNext())
                                        {
                                            var toAdd = sValEnum.Current;
                                            var toAddWrapped = ss.GetDynamicWrapperFor(toAdd, false);
                                            var toAddTracked = ss.InternalCreateAddBase(toAdd, toAddWrapped != null && toAddWrapped.GetRowState() == ObjectState.Added, null, null, null, null, true, false) ?? throw new CEFInvalidStateException(InvalidStateType.ObjectTrackingIssue);

                                            if (asCefList != null && !asCefList.ContainsItem(toAddTracked))
                                            {
                                                asCefList.AddWrappedItem(toAddTracked);
                                            }
                                        }

                                        var parSet = ss.GetSetter(testParent.GetInfraWrapperTarget(), rel.ChildPropertyName!);
                                        parSet.setter?.Invoke(asCefList);
                                        didLink = true;
                                    }
                                }

                                if (asCefList != null)
                                {
                                    if (!asCefList.ContainsItem(w ?? uw))
                                    {
                                        if (asCefList.AddWrappedItem(w ?? uw))
                                        {
                                            objstate.AddFK(ss, rel, testParent, to, testParent.GetNotifyFriendly(), true, false, true);
                                            didLink = true;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                if (!didLink)
                {
                    // Check based on values, not properties - only really need to do if nothing else worked above!
                    foreach (var rel in (from a in childRels where a.ChildType == uw.GetBaseType() select a))
                    {
                        var chRoleVals = kvCache.TestAssignReturn(rel, () => { return GetKeyValues(uw, rel.ChildResolvedKey); });

                        if (rel.ParentType != null && (from a in chRoleVals where a.value != null select a).Any())
                        {
                            var testParent = objstate.GetTrackedByPKValue(rel.ParentType, (from a in chRoleVals select a.value));

                            if (testParent != null)
                            {
                                objstate.AddFK(ss, rel, testParent, to, testParent.GetNotifyFriendly(), false, false, true);
                            }
                        }
                    }
                }
            }
        }

        public List<(int ordinal, string name, object? value)> GetKeyValues(object o, IEnumerable<string>? cols = null)
        {
            if (o == null)
                throw new CEFInvalidStateException(InvalidStateType.ArgumentNull, nameof(o));

            if (cols == null)
            {
                cols = ResolveKeyDefinitionForType(o.GetBaseType());

                if (cols == null)
                {
                    throw new CEFInvalidStateException(InvalidStateType.MissingKey, o.GetType().Name);
                }
            }

            List<(int ordinal, string name, object? value)> values = new();
            int ordinal = 0;

            var ss = CEF.CurrentServiceScope;

            foreach (var kf in cols)
            {
                var (getter, _) = ss.GetGetter(o, kf);

                if (getter != null)
                {
                    values.Add((ordinal, kf, getter.Invoke()));
                }

                ++ordinal;
            }

            return values;
        }

        public void UnlinkChildFromParentContainer(ServiceScope ss, string parentTypeName, string parentFieldName, object parContainer, object child)
        {
            string key = $"{parentTypeName}.{parentFieldName}";
            var ki = KeyService.GetMappedPropertyByFieldName(key);

            if (ki != null)
            {
                var top = ss.GetTrackedByWrapperOrTarget(parContainer);

                if (top != null)
                {
                    var toc = ss.GetTrackedByWrapperOrTarget(child);

                    if (toc != null)
                    {
                        var kss = ss.GetServiceState<KeyServiceState>();
                        kss?.RemoveFK(ss, ki, top, toc, top.GetNotifyFriendly(), true);
                    }
                }
            }
        }

        public void LinkChildInParentContainer(ServiceScope ss, string parentTypeName, string parentFieldName, object parContainer, object child)
        {
            string key = $"{parentTypeName}.{parentFieldName}";
            var ki = KeyService.GetMappedPropertyByFieldName(key);

            if (ki != null)
            {
                var top = ss.GetTrackedByWrapperOrTarget(parContainer);

                if (top != null)
                {
                    var toc = ss.GetTrackedByWrapperOrTarget(child);

                    if (toc != null)
                    {
                        var kss = ss.GetServiceState<KeyServiceState>();
                        kss?.AddFK(ss, ki, top, toc, top.GetNotifyFriendly(), true, false, false);
                    }
                }
            }
        }

        public void WireDependents(object o, object replaced, ServiceScope ss, ICEFList list, bool? objectModelOnly)
        {
            var state = ss.GetServiceState<KeyServiceState>();

            if (state == null)
            {
                throw new CEFInvalidStateException(InvalidStateType.LowLevelState, "Could not find key service state.");
            }

            var uw = o.AsUnwrapped();

            if (uw == null)
            {
                // No ability to track this, so no need to wire up anything
                return;
            }

            var tcrs = _relations.GetAllByName(nameof(TypeChildRelationship.ChildType), uw.GetBaseType());

            if (tcrs.Any())
            {
                var child = ss.GetInfraOrWrapperOrTarget(o);

                if (child != null)
                {
                    // If the unwrapped object exists in an EntitySet, replace it with a wrapped version
                    foreach (var tcr in (from a in tcrs where a.ChildPropertyName != null && a.ParentType != null select a))
                    {
                        List<object?> childVals = new();

                        foreach (var crn in tcr.ChildResolvedKey)
                        {
                            var (getter, type) = ss.GetGetter(child, crn);

                            if (getter != null)
                            {
                                childVals.Add(getter.Invoke());
                            }
                        }

                        var testParent = state.GetTrackedByPKValue(tcr.ParentType!, childVals);

                        if (testParent != null)
                        {
                            var wt = testParent.GetWrapperTarget() ?? throw new CEFInvalidStateException(InvalidStateType.ObjectTrackingIssue);

                            var cpi = wt.GetType().GetProperty(tcr.ChildPropertyName);

                            if (cpi != null && cpi.CanRead)
                            {
                                if (wt.FastGetValue(cpi.Name) is ICEFList es)
                                {
                                    var toRemove = (es.ContainsItem(o) && !es.ContainsItem(replaced)) ? o : null;

                                    try
                                    {
                                        list?.SuspendNotifications(true);
                                        es.AddWrappedItem(replaced ?? o);

                                        // Special case - trigger removal of unwrapped and add wrapped
                                        if (toRemove != null)
                                        {
                                            es.RemoveItem(toRemove);
                                        }
                                    }
                                    finally
                                    {
                                        list?.SuspendNotifications(false);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        public static IDictionary<Type, IList<string>> GetChildTypes(object o, bool all = true)
        {
            Dictionary<Type, IList<string>> visits = new();
            InternalGetChildTypes(o.GetBaseType(), visits, all);
            return visits;
        }

        public static IDictionary<Type, IList<string>> GetParentTypes(object o, bool all = true)
        {
            Dictionary<Type, IList<string>> visits = new();
            InternalGetParentTypes(o.GetBaseType(), visits, all);
            return visits;
        }

        public int GetObjectNestLevel(object o)
        {
            return GetParentObjects(CEF.CurrentServiceScope, o, RelationTypes.Parents).Count();
        }

        public virtual void Disposing(ServiceScope ss)
        {
        }

        #region "Key Object State"

        public sealed class KeyServiceState : ICEFServiceObjState
        {
            /// <summary>
            /// This value type is used internally to represent the key value of an object - done in a way to avoid using ref types such as strings (previously used a single stringized version) which can be costly for memory use.
            /// </summary>
            internal struct CompositeItemWrapper
            {
                private readonly long? AsWhole;
                private readonly Guid? AsGuid;
                private readonly string? AsString;
                private readonly bool IsNull;

                public override bool Equals(object obj)
                {
                    if (!(obj is CompositeItemWrapper))
                    {
                        return false;
                    }

                    var other = (CompositeItemWrapper)obj;

                    if (AsWhole.HasValue && other.AsWhole.HasValue)
                        return AsWhole.Value == other.AsWhole.Value;

                    if (AsGuid.HasValue && other.AsGuid.HasValue)
                        return AsGuid.Value == other.AsGuid.Value;

                    if (IsNull)
                        return other.IsNull;

                    return AsString.IsSame(other.AsString);
                }

                public override int GetHashCode()
                {
                    if (AsWhole.HasValue)
                        return AsWhole.Value.GetHashCode();

                    if (AsGuid.HasValue)
                        return AsGuid.Value.GetHashCode();

                    if (IsNull)
                        return -42;

                    if (AsString == null)
                    {
                        throw new CEFInvalidStateException(InvalidStateType.LowLevelState, "Unknown data type.");
                    }

                    return AsString.GetHashCode();
                }

                public CompositeItemWrapper(object? val)
                {
                    AsGuid = null;
                    AsWhole = null;
                    AsString = null;
                    IsNull = false;

                    if (val == null)
                    {
                        IsNull = true;
                    }
                    else
                    {
                        if (val is Enum)
                        {
                            try
                            {
                                val = (int)val;
                            }
                            catch
                            {
                            }
                        }

                        if (val is Guid ag)
                        {
                            AsGuid = ag;
                        }
                        else
                        {
                            if (val is int ai)
                            {
                                AsWhole = ai;
                            }
                            else
                            {
                                if (val is long al)
                                {
                                    AsWhole = al;
                                }
                                else
                                {
                                    if (val is string astr)
                                    {
                                        AsString = astr;
                                    }
                                    else
                                    {
                                        if (val is Int16 ash)
                                        {
                                            AsWhole = ash;
                                        }
                                        else
                                        {
                                            if (val is byte ab)
                                            {
                                                AsWhole = ab;
                                            }
                                            else
                                            {
                                                if (val is DateTime dt)
                                                {
                                                    AsWhole = dt.Ticks;
                                                }
                                                else
                                                {
                                                    if (val is DateOnly dov)
                                                    {
                                                        AsWhole = dov.GetAsInt();
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }

                    // Assertion: must have at least 1 type match or a problem!
                    if (!IsNull && !AsWhole.HasValue && !AsGuid.HasValue && AsString == null)
                    {
                        throw new CEFInvalidStateException(InvalidStateType.BadParameterValue, "Type of value (val) is not supported currently, add to CEF.");
                    }
                }
            }

            internal struct CompositeWrapper
            {
                private ImmutableArray<CompositeItemWrapper> Items;
                private readonly Type BaseType;

                public CompositeWrapper(Type basetype)
                {
                    BaseType = basetype ?? throw new CEFInvalidStateException(InvalidStateType.ArgumentNull, "basetype");
                    Items = ImmutableArray.Create<CompositeItemWrapper>();
                }

                public void Add(CompositeItemWrapper item)
                {
                    Items = Items.Add(item);
                }

                public override bool Equals(object obj)
                {
                    if (!(obj is CompositeWrapper))
                    {
                        return false;
                    }

                    var other = (CompositeWrapper)obj;

                    if (!(BaseType?.Equals(other.BaseType)).GetValueOrDefault())
                    {
                        return false;
                    }

                    var otherItems = other.Items;

                    if (otherItems.Length != Items.Length)
                    {
                        return false;
                    }

                    for (int i = 0; i < Items.Length; ++i)
                    {
                        if (!otherItems[i].Equals(Items[i]))
                        {
                            return false;
                        }
                    }

                    return true;
                }

                public override int GetHashCode()
                {
                    int hc = BaseType.GetHashCode();

                    for (int i = 0; i < Items.Length; ++i)
                    {
                        hc ^= Items[i].GetHashCode();
                    }

                    return hc;
                }
            }

            public class KeyObjectStatePK : IDisposable, ICEFIndexedListItem
            {
                private ServiceScope LinkedScope { get; set; }

                internal CompositeWrapper Composite { get; set; }
                public ServiceScope.TrackedObject Parent { get; set; }
                public IList<string> ParentKeyFields { get; set; }
                public CEFWeakReference<INotifyPropertyChanged> NotifyParent { get; set; }
                private Action<ServiceScope.TrackedObject, object, object> KeyChangeAlert { get; set; }

                public KeyObjectStatePK(ServiceScope ss, ServiceScope.TrackedObject parent, INotifyPropertyChanged notifyParent, IList<string> parentFields, Action<ServiceScope.TrackedObject, object, object> notifyKeyChange)
                {
                    LinkedScope = ss;
                    ParentKeyFields = parentFields;
                    Parent = parent;
                    NotifyParent = new CEFWeakReference<INotifyPropertyChanged>(notifyParent);
                    KeyChangeAlert = notifyKeyChange;

                    // Wire notifications on parent: if key assigned (or changed), need to update dictionary to reflect this and support efficient key lookups
                    notifyParent.PropertyChanged += Parent_PropertyChanged;

                    Composite = BuildComposite(null);
                }

                private void Parent_PropertyChanged(object sender, PropertyChangedEventArgs e)
                {
                    var ordinal = ParentKeyFields.IndexOf(e.PropertyName);

                    // Was it a key change?
                    if (ordinal >= 0 && NotifyParent.IsAlive && NotifyParent.Target != null)
                    {
                        var oldVal = Composite;

                        // If using shadow props and no longer set to default, can remove shadow prop - for now, assume that any assignment will be to a non-null/valid value
                        if (Globals.UseShadowPropertiesForNew)
                        {
                            var iw = Parent.GetInfra();

                            if (iw != null)
                            {
                                if (iw.HasShadowProperty(e.PropertyName))
                                {
                                    iw.RemoveProperty(SHADOW_PROP_PREFIX + e.PropertyName);
                                }
                            }
                        }

                        Composite = BuildComposite(sender as INotifyPropertyChanged);

                        if (!oldVal.Equals(Composite))
                        {
                            // Since composite key has likely changed, need to update this in the dictionary - link no longer correct
                            KeyChangeAlert?.Invoke(Parent, oldVal, Composite);
                        }
                    }
                }

                private CompositeWrapper BuildComposite(INotifyPropertyChanged? sender)
                {
                    var uw = Parent.GetTarget() ?? throw new CEFInvalidStateException(InvalidStateType.ObjectTrackingIssue);
                    var bt = uw.GetBaseType() ?? throw new CEFInvalidStateException(InvalidStateType.ObjectTrackingIssue);

                    CompositeWrapper cw = new(bt);

                    foreach (var f in ParentKeyFields)
                    {
                        Func<object?>? pkGet = null;

                        if (sender != null)
                        {
#pragma warning disable IDE0059 // Unnecessary assignment of a value
                            var (readable, value) = sender.FastPropertyReadableWithValue(f);
#pragma warning restore IDE0059 // Unnecessary assignment of a value

                            if (readable)
                            {
                                pkGet = () =>
                                {
                                    return value;
                                };
                            }
                        }

                        if (pkGet == null)
                        {
                            var o = Parent.GetInfraWrapperTarget();

                            if (Globals.UseShadowPropertiesForNew && o is ICEFInfraWrapper wrapper && wrapper.HasShadowProperty(f))
                            {
                                pkGet = LinkedScope.GetGetter(o, SHADOW_PROP_PREFIX + f).getter;
                            }

                            if (pkGet == null)
                            {
                                pkGet = LinkedScope.GetGetter(o, f).getter;
                            }
                        }

                        if (pkGet != null)
                        {
                            cw.Add(new CompositeItemWrapper(pkGet.Invoke()));
                        }
                    }

                    return cw;
                }

                public override bool Equals(object obj)
                {
                    if (obj is KeyObjectStatePK otherPK)
                    {
                        return otherPK.Parent.Equals(this.Parent);
                    }

                    return false;
                }

                public override int GetHashCode()
                {
                    var h1 = unchecked((uint)Parent.GetHashCode());
                    uint rol = (h1 << 5) | (h1 >> 27);
                    return unchecked((int)(rol + h1));
                }

                public object? GetValue(string propName, bool unwrap)
                {
                    switch (propName)
                    {
                        case nameof(KeyObjectStatePK.Composite):
                            return Composite;

                        case nameof(KeyObjectStateFK.Parent):
                            return Parent.GetTarget();

                        case nameof(KeyObjectStatePK.NotifyParent):
                            if (unwrap)
                                return NotifyParent.IsAlive ? NotifyParent.Target : null;
                            else
                                return NotifyParent;
                        default:
                            throw new NotSupportedException("Unsupported property name.");
                    }
                }

                #region IDisposable Support
                private bool _disposedValue = false; // To detect redundant calls

                protected virtual void Dispose(bool disposing)
                {
                    if (!_disposedValue)
                    {
                        if (disposing && this.GetValue(nameof(NotifyParent), true) != null)
                        {
                            ((INotifyPropertyChanged)NotifyParent.Target).PropertyChanged -= Parent_PropertyChanged;
                        }

                        Parent = null!;
                        NotifyParent = null!;
                        KeyChangeAlert = null!;
                        _disposedValue = true;
                    }
                }

                public void Dispose()
                {
                    Dispose(true);
                }
                #endregion
            }

            public class KeyObjectStateFK : IDisposable, ICEFIndexedListItem
            {
                private ServiceScope LinkedScope { get; set; }
                public TypeChildRelationship Key { get; set; }
                public ServiceScope.TrackedObject Parent { get; set; }
                public ServiceScope.TrackedObject Child { get; set; }
                public CEFWeakReference<INotifyPropertyChanged>? NotifyParent { get; set; }

                public override bool Equals(object obj)
                {
                    if (obj is KeyObjectStateFK otherFK)
                    {
                        return otherFK.Parent.IsSame(this.Parent) && otherFK.Child.IsSame(this.Child);
                    }

                    return false;
                }

                public override int GetHashCode()
                {
                    return (Parent?.GetHashCode()).GetValueOrDefault() ^ (Child?.GetHashCode()).GetValueOrDefault();
                }

                public void NullifyChild(bool silent = true)
                {
                    foreach (var ck in Key.ChildResolvedKey)
                    {
                        var (setter, type) = LinkedScope.GetSetter(Child.GetInfraWrapperTarget(), ck);

                        if (setter == null)
                        {
                            throw new CEFInvalidStateException(InvalidStateType.BadAction, $"Could not find property {ck} on object of type {Child.GetTarget()?.GetType()?.Name}.");
                        }

                        try
                        {
                            setter.Invoke(null);
                        }
                        catch (Exception ex)
                        {
                            if (!silent)
                            {
                                throw new CEFInvalidStateException(InvalidStateType.LowLevelState, $"Failed to remove reference to {Child.GetTarget()?.GetType()?.Name} ({ck}).", ex);
                            }
                        }
                    }
                }

                public KeyObjectStateFK(ServiceScope ss, TypeChildRelationship key, ServiceScope.TrackedObject parent, ServiceScope.TrackedObject child, INotifyPropertyChanged? notifyParent)
                {
                    LinkedScope = ss;
                    Key = key;
                    Parent = parent;
                    Child = child;

                    if (notifyParent != null)
                    {
                        NotifyParent = new CEFWeakReference<INotifyPropertyChanged>(notifyParent);

                        // Wire notifications on parent: if key assigned (or changed), needs to cascade!
                        notifyParent.PropertyChanged += Parent_PropertyChanged;
                    }
                }

                private void Parent_PropertyChanged(object sender, PropertyChangedEventArgs e)
                {
                    if (Key.ParentKey == null)
                    {
                        return;
                    }

                    var ordinal = Key.ParentKey.IndexOf(e.PropertyName);

                    // Was it a key change?
                    if (ordinal >= 0 && NotifyParent != null && NotifyParent.IsAlive && NotifyParent.Target != null)
                    {
                        Func<object?>? forParent = null;

                        if (sender != null)
                        {
                            var (readable, value) = sender.FastPropertyReadableWithValue(e.PropertyName);

                            if (readable)
                            {
                                forParent = () =>
                                {
                                    return value;
                                };
                            }
                        }

                        if (forParent == null)
                        {
                            forParent = LinkedScope.GetGetter(NotifyParent.Target, e.PropertyName).getter;
                        }

                        if (forParent == null)
                        {
                            throw new CEFInvalidStateException(InvalidStateType.BadAction, $"Could not find property {e.PropertyName} on object of type {NotifyParent.Target?.GetType()?.Name}.");
                        }

                        var chCol = Key.ChildResolvedKey[ordinal];
                        var (setter, type) = LinkedScope.GetSetter(Child.GetInfraWrapperTarget(), chCol);

                        // Change - not necessarily an exception to be missing a setter here, related object may not longer be part of same scope (gone out of scope) and let's not throw an error but silently handle if it IS in scope
                        if (setter != null)
                        {
                            setter.Invoke(forParent.Invoke());
                        }
                    }
                }

                internal IList<object?> GetParentKeyValue()
                {
                    List<object?> val = new();

                    if (Key.ParentKey != null && NotifyParent != null)
                    {
                        for (int i = 0; i < Key.ParentKey.Count; ++i)
                        {
                            var (getter, type) = LinkedScope.GetGetter(NotifyParent.Target, Key.ParentKey[i]);

                            if (getter != null)
                            {
                                var parVal = getter.Invoke();
                                val.Add(parVal);
                            }
                        }
                    }

                    return val;
                }

                internal IList<object?> GetChildRoleValue()
                {
                    List<object?> val = new();

                    if (Child.Target != null)
                    {
                        for (int i = 0; i < Key.ChildResolvedKey.Count; ++i)
                        {
                            var (getter, type) = LinkedScope.GetGetter(Child.Target, Key.ChildResolvedKey[i]);

                            if (getter != null)
                            {
                                var chVal = getter.Invoke();
                                val.Add(chVal);
                            }
                        }
                    }

                    return val;
                }

                internal void CopyParentValueToChild()
                {
                    if (Key.ParentKey != null && NotifyParent != null)
                    {
                        for (int i = 0; i < Key.ParentKey.Count && i < Key.ChildResolvedKey.Count; ++i)
                        {
                            var (getter, type) = LinkedScope.GetGetter(NotifyParent.Target, Key.ParentKey[i]);

                            if (getter != null)
                            {
                                var parVal = getter.Invoke();
                                var forChild = LinkedScope.GetSetter(Child.GetInfraWrapperTarget() ?? throw new CEFInvalidStateException(InvalidStateType.ObjectTrackingIssue, "Could not find child data."), Key.ChildResolvedKey[i]);
                                forChild.setter?.Invoke(parVal);
                            }
                        }
                    }
                }

                public object? GetValue(string propName, bool unwrap)
                {
                    switch (propName)
                    {
                        case nameof(KeyObjectStateFK.Key):
                            return Key;

                        case nameof(KeyObjectStateFK.Parent):
                            return Parent.GetTarget();

                        case nameof(KeyObjectStateFK.Child):
                            return Child.GetTarget();

                        case nameof(KeyObjectStateFK.NotifyParent):
                            if (unwrap)
                                return NotifyParent != null && NotifyParent.IsAlive ? NotifyParent.Target : null;
                            else
                                return NotifyParent;
                    }
                    throw new NotSupportedException("Unsupported property name.");
                }

                #region IDisposable Support
                private bool disposedValue = false; // To detect redundant calls

                protected virtual void Dispose(bool disposing)
                {
                    if (!disposedValue)
                    {
                        if (disposing && NotifyParent != null && this.GetValue(nameof(NotifyParent), true) != null)
                        {
                            ((INotifyPropertyChanged)NotifyParent.Target).PropertyChanged -= Parent_PropertyChanged;
                        }

                        Key = null!;
                        Parent = null!;
                        Child = null!;
                        NotifyParent = null!;
                        disposedValue = true;
                    }
                }

                public void Dispose()
                {
                    Dispose(true);
                }
                #endregion
            }

            internal class UnlinkedChildByValueInfo
            {
                public TypeChildRelationship Relationship = new();
                public ServiceScope.TrackedObject Child = new();
                public bool Processed;
            }

            private readonly SlimConcurrentDictionary<TypeChildRelationship, SlimConcurrentDictionary<object, ConcurrentObservableCollection<ServiceScope.TrackedObject>>> _missingParentByRef = new();
            private readonly SlimConcurrentDictionary<TypeChildRelationship, SlimConcurrentDictionary<CompositeWrapper, ConcurrentObservableCollection<UnlinkedChildByValueInfo>>> _missingParentByVal = new();
            private readonly SlimConcurrentDictionary<Type, SlimConcurrentDictionary<CompositeWrapper, ConcurrentObservableCollection<UnlinkedChildByValueInfo>>> _missingParentChildLinkback = new();

            private readonly object _asNull = new();
            private bool _firstInit = true;

            internal ConcurrentIndexedList<KeyObjectStateFK> AllFK { get; private set; } = new(nameof(KeyObjectStateFK.Parent), nameof(KeyObjectStateFK.Child));

            internal ConcurrentIndexedList<KeyObjectStatePK> AllPK { get; private set; } = new ConcurrentIndexedList<KeyObjectStatePK>(nameof(KeyObjectStatePK.Composite)).AddUniqueConstraint(nameof(KeyObjectStatePK.Composite));

            public void Cleanup(ServiceScope ss)
            {
                ProcessUnprocessedParentNullLinks(ss);
                ProcessUnprocessedChildLinkbacks(ss);

                _missingParentByRef.Clear();
                _missingParentByVal.Clear();
                _missingParentChildLinkback.Clear();
            }

            public KeyObjectStateFK? RemoveFK(ServiceScope ss, TypeChildRelationship key, ServiceScope.TrackedObject parent, ServiceScope.TrackedObject child, INotifyPropertyChanged? parentWrapped, bool nullifyChild)
            {
                var findKey = new KeyObjectStateFK(ss, key, parent, child, parentWrapped);
                var existingKey = AllFK.Find(findKey);

                if (existingKey != null)
                {
                    AllFK.Remove(existingKey);

                    if (nullifyChild)
                    {
                        existingKey.NullifyChild();
                    }

                    existingKey.Dispose();
                }

                return existingKey;
            }

            public KeyObjectStateFK? AddFK(ServiceScope ss, TypeChildRelationship key, ServiceScope.TrackedObject parent, ServiceScope.TrackedObject child, INotifyPropertyChanged? parentWrapped, bool copyValues, bool checkUnlinked, bool checkExists)
            {
                if (parentWrapped != null)
                {
                    var nk = new KeyObjectStateFK(ss, key, parent, child, parentWrapped);

                    if (checkExists && AllFK.Contains(nk))
                    {
                        return nk;
                    }

                    AllFK.Add(nk, false);

                    // Remove from unlinked child list, if preset
                    if (checkUnlinked)
                    {
                        if (_missingParentByRef.TryGetValue(key, out var map))
                        {
                            lock (_missingParentByRef)
                            {
                                if (map.TryGetValue(_asNull, out var ul1))
                                {
                                    ul1.Remove(child);
                                }

                                var pwt = parent.GetWrapperTarget();

                                if (pwt != null && map.TryGetValue(pwt, out var ul2))
                                {
                                    var keep = (from a in ul2 where a != child select a);

                                    if (keep.Any())
                                    {
                                        var nlist = new ConcurrentObservableCollection<ServiceScope.TrackedObject>(keep.Count());

                                        foreach (var k in keep)
                                        {
                                            nlist.Add(k);
                                        }

                                        map[pwt] = nlist;
                                    }
                                    else
                                    {
                                        map.Remove(pwt);
                                    }
                                }
                            }
                        }
                    }

                    if (copyValues)
                    {
                        nk.CopyParentValueToChild();
                    }

                    return nk;
                }

                return null;
            }

            internal IEnumerable<(CompositeWrapper pk, UnlinkedChildByValueInfo info)> GetUnprocessedMissingParentLinksForChildren()
            {
                foreach (var kvp1 in _missingParentByVal)
                {
                    foreach (var kvp2 in kvp1.Value)
                    {
                        foreach (var i in kvp2.Value)
                        {
                            if (!i.Processed)
                            {
                                yield return (kvp2.Key, i);
                            }
                        }
                    }
                }
            }

            internal IEnumerable<(CompositeWrapper pk, UnlinkedChildByValueInfo info)> GetUnprocessedMissingChildLinkbacksForParent()
            {
                foreach (var kvp1 in _missingParentChildLinkback)
                {
                    foreach (var kvp2 in kvp1.Value)
                    {
                        foreach (var i in kvp2.Value)
                        {
                            if (!i.Processed)
                            {
                                yield return (kvp2.Key, i);
                            }
                        }
                    }
                }
            }

            internal IEnumerable<UnlinkedChildByValueInfo> GetMissingChildLinkbacksForParentByValue(Type parentType, IEnumerable<object> parentKey)
            {
                CompositeWrapper cw = new(parentType);

                foreach (var k in parentKey)
                {
                    cw.Add(new CompositeItemWrapper(k));
                }

                lock (_missingParentChildLinkback)
                {
                    if (_missingParentChildLinkback.TryGetValue(parentType, out var objs))
                    {
                        if (objs.TryGetValue(cw, out var chinfo))
                        {
                            return chinfo;
                        }
                    }
                }

                return Array.Empty<UnlinkedChildByValueInfo>();
            }

            internal IEnumerable<UnlinkedChildByValueInfo> GetMissingChildrenByValue(TypeChildRelationship key, IEnumerable<object> keyVal)
            {
                if (key.ParentType != null)
                {
                    CompositeWrapper cw = new(key.ParentType);

                    foreach (var f in keyVal)
                    {
                        cw.Add(new CompositeItemWrapper(f));
                    }

                    if (_missingParentByVal.TryGetValue(key, out var cvl))
                    {
                        if (cvl.TryGetValue(cw, out var cl))
                        {
                            return cl;
                        }
                    }
                }

                return Array.Empty<UnlinkedChildByValueInfo>();
            }

            public IEnumerable<ServiceScope.TrackedObject> GetMissingChildrenForParentByRef(TypeChildRelationship key, object parentWoTValue)
            {
                if (_missingParentByRef.TryGetValue(key, out var uk))
                {
                    uk.TryGetValue(parentWoTValue, out var tl1);

                    if (tl1 != null)
                    {
                        return tl1;
                    }
                }

                return Array.Empty<ServiceScope.TrackedObject>();
            }

            public void TrackMissingParentChildLinkback(TypeChildRelationship key, ServiceScope.TrackedObject child, Type parentType, IEnumerable<object> parentKey)
            {
                ConcurrentObservableCollection<UnlinkedChildByValueInfo>? tl = null;
                CompositeWrapper cw = new(parentType);

                foreach (var f in parentKey)
                {
                    cw.Add(new CompositeItemWrapper(f));
                }

                lock (_missingParentChildLinkback)
                {
                    _missingParentChildLinkback.TryGetValue(parentType, out var uk);

                    if (uk == null)
                    {
                        uk = new SlimConcurrentDictionary<CompositeWrapper, ConcurrentObservableCollection<UnlinkedChildByValueInfo>>();
                        _missingParentChildLinkback[parentType] = uk;
                    }

                    uk.TryGetValue(cw, out tl);

                    if (tl == null)
                    {
                        tl = new ConcurrentObservableCollection<UnlinkedChildByValueInfo>();
                        uk[cw] = tl;
                    }
                }

                tl.Add(new UnlinkedChildByValueInfo() { Relationship = key, Child = child });
            }

            public void TrackMissingParentByValue(TypeChildRelationship key, ServiceScope.TrackedObject child, Type parentType, IEnumerable<object> parentKey)
            {
                ConcurrentObservableCollection<UnlinkedChildByValueInfo>? tl = null;
                CompositeWrapper cw = new(parentType);

                foreach (var f in parentKey)
                {
                    cw.Add(new CompositeItemWrapper(f));
                }

                lock (_missingParentByVal)
                {
                    _missingParentByVal.TryGetValue(key, out var uk);

                    if (uk == null)
                    {
                        uk = new SlimConcurrentDictionary<CompositeWrapper, ConcurrentObservableCollection<UnlinkedChildByValueInfo>>();
                        _missingParentByVal[key] = uk;
                    }

                    uk.TryGetValue(cw, out tl);

                    if (tl == null)
                    {
                        tl = new ConcurrentObservableCollection<UnlinkedChildByValueInfo>();
                        uk[cw] = tl;
                    }
                }

                tl.Add(new UnlinkedChildByValueInfo() { Child = child, Relationship = key });
            }

            public void TrackMissingParentByRef(TypeChildRelationship key, object parentWoTValue, ServiceScope.TrackedObject child)
            {
                ConcurrentObservableCollection<ServiceScope.TrackedObject>? tl = null;

                lock (_missingParentByRef)
                {
                    _missingParentByRef.TryGetValue(key, out var uk);

                    if (uk == null)
                    {
                        uk = new SlimConcurrentDictionary<object, ConcurrentObservableCollection<ServiceScope.TrackedObject>>();
                        _missingParentByRef[key] = uk;
                    }

                    uk.TryGetValue(parentWoTValue ?? _asNull, out tl);

                    if (tl == null)
                    {
                        tl = new ConcurrentObservableCollection<ServiceScope.TrackedObject>();
                        uk[parentWoTValue ?? _asNull] = tl;
                    }
                }

                tl.Add(child);
            }

            public KeyObjectStatePK? AddPK(ServiceScope ss, ServiceScope.TrackedObject parent, INotifyPropertyChanged? parentWrapped, IList<string> parentKeyFields, Action<ServiceScope.TrackedObject, object, object> notifyKeyChange)
            {
                lock (_asNull)
                {
                    if (_firstInit)
                    {
                        if (AllPK.InitialCapacity != ss.Settings.EstimatedScopeSize)
                        {
                            AllPK = new ConcurrentIndexedList<KeyObjectStatePK>(ss.Settings.EstimatedScopeSize, nameof(KeyObjectStatePK.Composite)).AddUniqueConstraint(nameof(KeyObjectStatePK.Composite));

                            // FKs are trickier - how many will we have per entity? we'll guess 2:1 but let it be controllable
                            AllFK = new ConcurrentIndexedList<KeyObjectStateFK>(Convert.ToInt32(ss.Settings.EstimatedScopeSize * Globals.EstimatedFKRatio), nameof(KeyObjectStateFK.Parent), nameof(KeyObjectStateFK.Child));
                        }

                        _firstInit = false;
                    }
                }

                if (parentWrapped != null)
                {
                    var nk = new KeyObjectStatePK(ss, parent, parentWrapped, parentKeyFields, notifyKeyChange);
                    return AllPK.Add(nk, true);
                }

                return null;
            }

            internal ServiceScope.TrackedObject? GetTrackedByCompositePK(CompositeWrapper composite)
            {
                return AllPK.GetFirstByName(nameof(KeyObjectStatePK.Composite), composite)?.Parent;
            }

            internal void ProcessUnprocessedParentNullLinks(ServiceScope ss)
            {
                foreach (var (pk, info) in GetUnprocessedMissingParentLinksForChildren())
                {
                    var to = GetTrackedByCompositePK(pk);

                    if (to != null && info.Relationship.ParentPropertyName != null)
                    {
                        var (setter, type) = ss.GetSetter(info.Child.GetInfraWrapperTarget(), info.Relationship.ParentPropertyName);

                        if (setter != null)
                        {
                            setter.Invoke(to.GetWrapperTarget());
                            AddFK(ss, info.Relationship, to, info.Child, to.GetNotifyFriendly(), true, false, false);
                            info.Processed = true;
                        }
                    }
                }
            }

            internal void ProcessUnprocessedChildLinkbacks(ServiceScope ss)
            {
                foreach (var (pk, info) in GetUnprocessedMissingChildLinkbacksForParent())
                {
                    var to = GetTrackedByCompositePK(pk);

                    if (to != null && info.Relationship.ChildPropertyName != null)
                    {
                        var (getter, type) = ss.GetGetter(to.GetInfraWrapperTarget(), info.Relationship.ChildPropertyName);

                        if (type != null && getter != null && WrappingHelper.IsWrappableListType(type, null))
                        {
                            var parVal = getter.Invoke();

                            if (parVal == null)
                            {
                                parVal = WrappingHelper.CreateWrappingList(ss, type, to.GetWrapperTarget(), info.Relationship.ChildPropertyName);

                                var parChildSet = ss.GetSetter(to.GetInfraWrapperTarget(), info.Relationship.ChildPropertyName);
                                parChildSet.setter?.Invoke(parVal);
                            }

                            if (parVal is ICEFList asCefList)
                            {
                                var wt = info.Child.GetWrapperTarget();

                                if (wt != null && asCefList.AddWrappedItem(wt))
                                {
                                    AddFK(ss, info.Relationship, to, info.Child, to.GetNotifyFriendly(), false, false, false);
                                    info.Processed = true;
                                }
                            }
                        }
                    }
                }
            }

            public ServiceScope.TrackedObject? GetTrackedByPKValue(Type parentType, IEnumerable<object?> keyValues)
            {
                CompositeWrapper cw = new(parentType);

                foreach (var f in keyValues)
                {
                    cw.Add(new(f));
                }

                var to = GetTrackedByCompositePK(cw);

                if (to == null || !to.IsAlive)
                {
                    return null;
                }

                return to;
            }

            internal void UpdatePK(CompositeWrapper oldcomp, CompositeWrapper newcomp)
            {
                AllPK.UpdateFieldIndex(nameof(KeyObjectStatePK.Composite), oldcomp, newcomp);
            }

            public IEnumerable<KeyObjectStateFK> AllChildren(object parent)
            {
                return AllFK.GetAllByName(nameof(KeyObjectStateFK.Parent), parent);
            }

            public IEnumerable<KeyObjectStateFK> AllParents(object child)
            {
                return AllFK.GetAllByName(nameof(KeyObjectStateFK.Child), child);
            }
        }

        #endregion
    }
}
