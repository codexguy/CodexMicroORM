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
***********************************************************************/
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using CodexMicroORM.Core.Collections;
using System.Collections.Immutable;

namespace CodexMicroORM.Core.Services
{
    public class KeyService : ICEFService
    {
        internal const string SHADOW_PROP_PREFIX = "___";

        private static long _lowLongKey = long.MinValue;
        private static int _lowIntKey = int.MinValue;

        // Populated during startup, represents all object primary keys we track - should be eastablished prior to relationships!
        private static ConcurrentDictionary<Type, IList<string>> _primaryKeys = new ConcurrentDictionary<Type, IList<string>>();

        // Populated during startup, represents all object relationships we track
        private static ConcurrentIndexedList<TypeChildRelationship> _relations = new ConcurrentIndexedList<TypeChildRelationship>(
            nameof(TypeChildRelationship.ParentType),
            nameof(TypeChildRelationship.ChildType),
            nameof(TypeChildRelationship.FullParentChildPropertyName),
            nameof(TypeChildRelationship.FullChildParentPropertyName));

        public KeyService()
        {
        }

        #region "Global state"

        public static bool DefaultPrimaryKeysCanBeDBAssigned
        {
            get;
            set;
        } = true;

        public static MergeBehavior DefaultMergeBehavior
        {
            get;
            set;
        } = MergeBehavior.SilentMerge;

        #endregion

        public static TypeChildRelationship GetMappedPropertyByFieldName(string fn)
        {
            var pcr = _relations.GetAllByName(nameof(TypeChildRelationship.FullParentChildPropertyName), fn).FirstOrDefault();

            if (pcr != null)
            {
                return pcr;
            }

            return _relations.GetAllByName(nameof(TypeChildRelationship.FullChildParentPropertyName), fn).FirstOrDefault();
        }

        public static void RegisterKey<T>(params string[] fields)
        {
            if (fields == null || fields.Length == 0)
                throw new ArgumentException("Fields must be non-blank.");

            _primaryKeys[typeof(T)] = fields;

            CEF.Register<T>(new KeyService());
        }

        public static void RegisterRelationship<T>(params TypeChildRelationship[] relations)
        {
            if (relations == null || relations.Length == 0)
                throw new ArgumentException("Relations must be non-blank.");

            foreach (var r in relations)
            {
                IList<string> parentPk = null;

                if (!_primaryKeys.TryGetValue(typeof(T), out parentPk))
                {
                    throw new CEFInvalidOperationException($"You need to define the primary key for type {typeof(T).Name} prior to establishing a relationship using it.");
                }

                r.ParentKey = parentPk;
                r.ParentType = typeof(T);

                _relations.Add(r);
            }

            CEF.Register<T>(new KeyService());
        }

        internal static IList<string> ResolveKeyDefinitionForType(Type t)
        {
            if (_primaryKeys.TryGetValue(t, out IList<string> pk))
            {
                return pk;
            }
            else
            {
                return new string[] { };
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

        IList<Type> ICEFService.RequiredServices()
        {
            return null;
        }

        Type ICEFService.IdentifyStateType(object o, ServiceScope ss, bool isNew)
        {
            return typeof(KeyServiceState);
        }

        WrappingSupport ICEFService.IdentifyInfraNeeds(object o, object replaced, ServiceScope ss, bool isNew)
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
                    if (((object)replaced ?? o).GetType().GetProperty(field) == null)
                    {
                        need = WrappingSupport.PropertyBag | WrappingSupport.Notifications;
                        break;
                    }
                }
            }

            return need;
        }

        void ICEFService.FinishSetup(ServiceScope.TrackedObject to, ServiceScope ss, bool isNew, IDictionary<string, object> props, ICEFServiceObjState state)
        {
            var uw = to.GetTarget();

            if (uw == null)
                return;

            var pkFields = ResolveKeyDefinitionForType(uw.GetBaseType());

            // Assign keys - new or not, we rely on unqiue values
            if (isNew)
            {
                foreach (var k in pkFields)
                {
                    // Don't overwrite properties that we may have copied!
                    if (props == null || !props.ContainsKey(k))
                    {
                        var ks = ss.GetSetter(uw, k);

                        // Shadow props in use, we rely on the above for the data type, but we'll be updating the shadow prop instead
                        var kssp = (!Globals.UseShadowPropertiesForNew ? (null, null) : ss.GetSetter(uw, SHADOW_PROP_PREFIX + k));

                        if ((kssp.setter ?? ks.setter) != null)
                        {
                            var keyType = (ks.type ?? kssp.type);

                            if (keyType == null)
                            {
                                keyType = Globals.DefaultKeyType;
                            }

                            if (keyType.Equals(typeof(int)))
                            {
                                (kssp.setter ?? ks.setter).Invoke(System.Threading.Interlocked.Increment(ref _lowIntKey));
                            }
                            else
                            {
                                if (keyType.Equals(typeof(long)))
                                {
                                    (kssp.setter ?? ks.setter).Invoke(System.Threading.Interlocked.Increment(ref _lowLongKey));
                                }
                                else
                                {
                                    if (keyType.Equals(typeof(Guid)) || keyType.Equals(typeof(string)))
                                    {
                                        (kssp.setter ?? ks.setter).Invoke(Guid.NewGuid());
                                    }
                                }
                            }
                        }
                    }
                }
            }

            var keystate = (KeyServiceState)state;

            keystate.AddPK(ss, to, to.GetNotifyFriendly(), ResolveKeyDefinitionForType(to.BaseType), (changedPK, oldval, newval) =>
            {
                // Tracking dictionary may no longer correctly reflect composite key - force an update
                keystate.UpdatePK((string)oldval, (string)newval);
            });

            LinkByValuesInScope(to, ss, keystate);
        }

        public static IEnumerable<object> GetParentObjects(ServiceScope ss, object o, bool all = false)
        {
            return InternalGetParentObjects(ss.GetServiceState<KeyServiceState>(), ss, o, all, new HashSet<object>());
        }

        private static IEnumerable<object> InternalGetParentObjects(KeyServiceState state, ServiceScope ss, object o, bool all, HashSet<object> visits)
        {
            var uw = o.AsUnwrapped();

            if (visits.Contains(uw))
            {
                yield break;
            }

            var to = ss.Objects.GetFirstByName(nameof(ServiceScope.TrackedObject.Target), uw);

            if (to != null)
            {
                visits.Add(to.GetTarget());

                foreach (var fk in state.AllParents(uw))
                {
                    if (all)
                    {
                        foreach (var n in InternalGetParentObjects(state, ss, fk.Parent.GetTarget(), all, visits))
                        {
                            yield return n;
                        }
                    }

                    yield return fk.Parent.GetInfraWrapperTarget();
                }
            }
        }

        public static IEnumerable<object> GetChildObjects(ServiceScope ss, object o, bool all = false)
        {
            return InternalGetChildObjects(ss.GetServiceState<KeyServiceState>(), ss, o, all, new HashSet<object>());
        }

        private static IEnumerable<object> InternalGetChildObjects(KeyServiceState state, ServiceScope ss, object o, bool all, HashSet<object> visits)
        {
            var uw = o.AsUnwrapped();

            if (visits.Contains(uw))
            {
                yield break;
            }

            var to = ss.Objects.GetFirstByName(nameof(ServiceScope.TrackedObject.Target), uw);

            if (to != null)
            {
                visits.Add(to.GetTarget());

                foreach (var fk in state.AllChildren(uw))
                {
                    if (all)
                    {
                        foreach (var n in InternalGetChildObjects(state, ss, fk.Child.GetTarget(), all, visits))
                        {
                            yield return n;
                        }
                    }

                    yield return fk.Child.GetInfraWrapperTarget();
                }
            }
        }

        public static void UpdateBoundKeys(ServiceScope.TrackedObject to, ServiceScope ss, string fieldName, object oval, object nval)
        {
            foreach (var rel in _relations.GetAllByName(nameof(TypeChildRelationship.FullChildParentPropertyName), $"{to.GetTarget().GetBaseType().Name}.{fieldName}"))
            {
                if (nval != null)
                {
                    var toParent = ss.Objects.GetFirstByName(nameof(ServiceScope.TrackedObject.Target), nval);

                    if (toParent != null)
                    {
                        foreach (var k in GetKeyValues(toParent.GetInfraWrapperTarget()))
                        {
                            var childSet = ss.GetSetter(to.GetInfraWrapperTarget(), rel.ChildResolvedKey[k.ordinal]);
                            childSet.setter.Invoke(k.value);
                        }
                    }
                }
            }
        }

        private static void LinkByValuesInScope(ServiceScope.TrackedObject to, ServiceScope ss, KeyServiceState objstate)
        {
            var iw = to.GetCreateInfra();

            if (iw != null)
            {
                var uw = to.GetTarget();
                var w = to.GetWrapper();

                if (uw == null)
                    return;

                bool linked = false;

                var childRels = _relations.GetAllByName(nameof(TypeChildRelationship.ChildType), uw.GetBaseType());

                foreach (var rel in (from a in childRels where !string.IsNullOrEmpty(a.ParentPropertyName) select a))
                {
                    var val = iw.GetValue(rel.ParentPropertyName);

                    if (val != null)
                    {
                        var testParent = ss.GetTrackedByWrapperOrTarget(val);

                        if (testParent != null)
                        {
                            objstate.AddFK(ss, rel, testParent, to, testParent.GetNotifyFriendly(), true);
                        }
                        else
                        {
                            objstate.TrackUnlinkedParent(rel, val, to);
                        }
                    }
                    else
                    {
                        var chRoleVals = GetKeyValues(uw, rel.ChildResolvedKey);
                        var testParent = objstate.GetTrackedByPKValue(ss, rel.ParentType, (from a in chRoleVals select a.value));

                        if (testParent != null)
                        {
                            iw.SetValue(rel.ParentPropertyName, testParent.GetWrapperTarget());
                            objstate.AddFK(ss, rel, testParent, to, testParent.GetNotifyFriendly(), true);
                            linked = true;
                        }
                    }
                }

                if (linked)
                {
                    foreach (var rel in (from a in childRels where !string.IsNullOrEmpty(a.ChildPropertyName) select a))
                    {
                        // Current entity role name used to look up any existing parent and add to their child collection if not already there
                        var chRoleVals = GetKeyValues(uw, rel.ChildResolvedKey);
                        var testParent = objstate.GetTrackedByPKValue(ss, rel.ParentType, (from a in chRoleVals select a.value));

                        if (testParent != null)
                        {
                            var parChildGet = ss.GetGetter(testParent.GetInfraWrapperTarget(), rel.ChildPropertyName);

                            if (WrappingHelper.IsWrappableListType(parChildGet.type, null))
                            {
                                var parVal = parChildGet.getter.Invoke();

                                if (parVal == null)
                                {
                                    parVal = WrappingHelper.CreateWrappingList(ss, parChildGet.type, testParent.AsUnwrapped(), rel.ChildPropertyName);

                                    var parChildSet = ss.GetSetter(testParent.GetInfraWrapperTarget(), rel.ChildPropertyName);
                                    parChildSet.setter.Invoke(parVal);
                                }

                                var asCefList = parVal as ICEFList;

                                if (asCefList != null)
                                {
                                    asCefList.AddWrappedItem(w ?? uw);
                                }
                            }
                        }
                    }
                }

                var parentRels = _relations.GetAllByName(nameof(TypeChildRelationship.ParentType), uw.GetBaseType());

                foreach (var rel in (from a in parentRels where !string.IsNullOrEmpty(a.ParentPropertyName) select a))
                {
                    foreach (var testChild in objstate.GetUnlinkedChildrenForParent(rel, w ?? uw))
                    {
                        var childGet = ss.GetGetter(testChild.GetInfraWrapperTarget(), rel.ParentPropertyName);

                        if (childGet.getter != null)
                        {
                            var val = childGet.getter.Invoke();

                            if (val != null)
                            {
                                if (val.Equals(uw) || val.Equals(w))
                                {
                                    objstate.AddFK(ss, rel, to, testChild, to.GetNotifyFriendly(), true);
                                }
                            }
                        }
                    }
                }

                foreach (var rel in (from a in parentRels where !string.IsNullOrEmpty(a.ChildPropertyName) select a))
                {
                    var parGet = ss.GetGetter(iw, rel.ChildPropertyName);

                    if (parGet.getter != null)
                    {
                        if (WrappingHelper.IsWrappableListType(parGet.type, null))
                        {
                            var parVal = parGet.getter.Invoke();

                            if (parVal != null)
                            {
                                var sValEnum = ((System.Collections.IEnumerable)parVal).GetEnumerator();

                                while (sValEnum.MoveNext())
                                {
                                    var chVal = sValEnum.Current;
                                    var chTo = ss.GetTrackedByWrapperOrTarget(chVal);

                                    if (chTo != null)
                                    {
                                        objstate.AddFK(ss, rel, to, chTo, to.GetNotifyFriendly(), true);
                                    }
                                }
                            }
                        }
                    }
                }

                foreach (var rel in (from a in parentRels where !string.IsNullOrEmpty(a.ChildPropertyName) && a.ChildType.Equals(uw.GetBaseType()) select a))
                {
                    var chRoleVals = GetKeyValues(uw, rel.ChildResolvedKey);

                    if ((from a in chRoleVals where a.value != null select a).Any())
                    {
                        var testParent = objstate.GetTrackedByPKValue(ss, rel.ParentType, (from a in chRoleVals select a.value));

                        if (testParent != null)
                        {
                            var parGet = ss.GetGetter(testParent.GetInfraWrapperTarget(), rel.ChildPropertyName);
                            var parVal = parGet.getter.Invoke();
                            var asCefList = parVal as ICEFList;

                            if (parVal == null)
                            {
                                // If this is a wrappable type, create instance now to host the child value
                                if (WrappingHelper.IsWrappableListType(parGet.type, parVal))
                                {
                                    parVal = WrappingHelper.CreateWrappingList(ss, parGet.type, testParent.AsUnwrapped(), rel.ChildPropertyName);
                                    var parSet = ss.GetSetter(testParent.GetInfraWrapperTarget(), rel.ChildPropertyName);
                                    parSet.setter.Invoke(parVal);
                                    asCefList = parVal as ICEFList;
                                }
                            }
                            else
                            {
                                // Parent value might not be a CEF list type, see if we can convert it to be one now
                                if (asCefList == null && parVal is System.Collections.IEnumerable && WrappingHelper.IsWrappableListType(parGet.type, parVal))
                                {
                                    asCefList = WrappingHelper.CreateWrappingList(ss, parGet.type, testParent.AsUnwrapped(), rel.ChildPropertyName);

                                    var sValEnum = ((System.Collections.IEnumerable)parVal).GetEnumerator();

                                    while (sValEnum.MoveNext())
                                    {
                                        var toAdd = sValEnum.Current;
                                        var toAddWrapped = ss.GetDynamicWrapperFor(toAdd, false);
                                        var toAddTracked = ss.InternalCreateAddBase(toAdd, toAddWrapped != null && toAddWrapped.GetRowState() == ObjectState.Added, null, null, null, null);

                                        if (!asCefList.ContainsItem(toAddTracked))
                                        {
                                            asCefList.AddWrappedItem(toAddTracked);
                                        }
                                    }

                                    var parSet = ss.GetSetter(testParent.GetInfraWrapperTarget(), rel.ChildPropertyName);
                                    parSet.setter.Invoke(asCefList);
                                }
                            }

                            if (asCefList != null)
                            {
                                if (!asCefList.ContainsItem(w ?? uw))
                                {
                                    asCefList.AddWrappedItem(w ?? uw);
                                    objstate.AddFK(ss, rel, testParent, to, testParent.GetNotifyFriendly(), true);
                                }
                            }
                        }
                    }
                }
            }
        }

        public static IList<(int ordinal, string name, object value)> GetKeyValues(object o, IList<string> cols = null)
        {
            if (o == null)
                throw new ArgumentNullException("o");

            if (cols == null)
            {
                cols = ResolveKeyDefinitionForType(o.GetBaseType());

                if (cols == null)
                {
                    throw new CEFInvalidOperationException($"Failed to identify primary key for {o.GetType().Name}.");
                }
            }

            List<(int ordinal, string name, object value)> values = new List<(int ordinal, string name, object value)>();
            int ordinal = 0;

            foreach (var kf in cols)
            {
                var valGet = CEF.CurrentServiceScope.GetGetter(o, kf);

                if (valGet.getter != null)
                {
                    values.Add((ordinal, kf, valGet.getter.Invoke()));
                }

                ++ordinal;
            }

            return values;
        }

        internal static void UnlinkChildFromParentContainer(ServiceScope ss, string parentTypeName, string parentFieldName, object parContainer, object child)
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
                        kss.RemoveFK(ss, ki, top, toc, top.GetNotifyFriendly(), true);
                    }
                }
            }
        }

        internal static void LinkChildInParentContainer(ServiceScope ss, string parentTypeName, string parentFieldName, object parContainer, object child)
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
                        kss.AddFK(ss, ki, top, toc, top.GetNotifyFriendly(), true);
                    }
                }
            }
        }

        internal static void WireDependents(object o, object replaced, ServiceScope ss, ICEFList list, bool? objectModelOnly, KeyServiceState state)
        {
            if (state == null)
            {
                state = ss.GetServiceState<KeyServiceState>();
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
                    foreach (var tcr in (from a in tcrs where a.ChildPropertyName != null select a))
                    {
                        List<object> childVals = new List<object>();

                        foreach (var crn in tcr.ChildResolvedKey)
                        {
                            var chGet = ss.GetGetter(child, crn);
                            childVals.Add(chGet.getter.Invoke());
                        }

                        var testParent = state.GetTrackedByPKValue(ss, tcr.ParentType, childVals);

                        if (testParent != null)
                        {
                            var wt = testParent.GetWrapperTarget();

                            var cpi = wt.GetType().GetProperty(tcr.ChildPropertyName);

                            if (cpi != null && cpi.CanRead)
                            {
                                var es = wt.FastGetValue(cpi.Name) as ICEFList;

                                if (es != null)
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
            Dictionary<Type, IList<string>> visits = new Dictionary<Type, IList<string>>();
            InternalGetChildTypes(o.GetBaseType(), visits, all);
            return visits;
        }

        public static IDictionary<Type, IList<string>> GetParentTypes(object o, bool all = true)
        {
            Dictionary<Type, IList<string>> visits = new Dictionary<Type, IList<string>>();
            InternalGetParentTypes(o.GetBaseType(), visits, all);
            return visits;
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

        public static int GetObjectNestLevel(object o)
        {
            return GetParentObjects(CEF.CurrentServiceScope, o, true).Count();
        }


        #region "Key Object State"

        internal class KeyServiceState : ICEFServiceObjState
        {
            public class KeyObjectStatePK : IDisposable, ICEFIndexedListItem
            {
                private ServiceScope LinkedScope { get; set; }

                public string Composite { get; set; }
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
                                if (iw.HasProperty(SHADOW_PROP_PREFIX + e.PropertyName))
                                {
                                    iw.RemoveProperty(SHADOW_PROP_PREFIX + e.PropertyName);
                                }
                            }
                        }

                        Composite = BuildComposite(sender as INotifyPropertyChanged);

                        if (oldVal != Composite)
                        {
                            // Since composite key has likely changed, need to update this in the dictionary - link no longer correct
                            KeyChangeAlert?.Invoke(Parent, oldVal, Composite);
                        }
                    }
                }

                public string BuildComposite(INotifyPropertyChanged sender)
                {
                    StringBuilder sb = new StringBuilder(128);

                    var uw = Parent.GetTarget() ?? throw new CEFInvalidOperationException("Cannot determine object identity.");
                    var bt = uw.GetBaseType() ?? throw new CEFInvalidOperationException("Cannot determine object identity.");

                    sb.Append(bt.Name);

                    if (sb.Length > 0)
                    {
                        foreach (var f in ParentKeyFields)
                        {
                            Func<object> pkGet = null;

                            if (sender != null)
                            {
                                var pi = sender.GetType().GetProperty(f);

                                if (pi != null)
                                {
                                    pkGet = () =>
                                    {
                                        return pi.GetValue(sender);
                                    };
                                }
                            }

                            if (pkGet == null)
                            {
                                var iw = Parent.GetInfraWrapperTarget();

                                if (Globals.UseShadowPropertiesForNew && iw.HasProperty(SHADOW_PROP_PREFIX + f))
                                {
                                    pkGet = LinkedScope.GetGetter(iw, SHADOW_PROP_PREFIX + f).getter;
                                }

                                if (pkGet == null)
                                {
                                    pkGet = LinkedScope.GetGetter(iw, f).getter;
                                }
                            }

                            if (pkGet != null)
                            {
                                sb.Append(pkGet.Invoke());
                            }
                        }
                    }

                    return sb.ToString();
                }

                public override bool Equals(object obj)
                {
                    var otherPK = obj as KeyObjectStatePK;

                    if (otherPK != null)
                    {
                        return otherPK.Parent.Equals(this.Parent);
                    }

                    return false;
                }

                public override int GetHashCode()
                {
                    return Parent.GetHashCode();
                }

                public object GetValue(string propName, bool unwrap)
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
                    }
                    throw new NotSupportedException("Unsupported property name.");
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

                        Parent = null;
                        NotifyParent = null;
                        KeyChangeAlert = null;
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
                public CEFWeakReference<INotifyPropertyChanged> NotifyParent { get; set; }

                public override bool Equals(object obj)
                {
                    var otherFK = obj as KeyObjectStateFK;

                    if (otherFK != null)
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
                        var forChild = LinkedScope.GetSetter(Child.GetInfraWrapperTarget(), ck);

                        if (forChild.setter == null)
                        {
                            throw new CEFInvalidOperationException($"Could not find property {ck} on object of type {Child.GetTarget()?.GetType()?.Name}.");
                        }

                        try
                        {
                            forChild.setter.Invoke(null);
                        }
                        catch (Exception ex)
                        {
                            if (!silent)
                            {
                                throw new CEFInvalidOperationException($"Failed to remove reference to {Child.GetTarget()?.GetType()?.Name} ({ck}).", ex);
                            }
                        }
                    }
                }

                public KeyObjectStateFK(ServiceScope ss, TypeChildRelationship key, ServiceScope.TrackedObject parent, ServiceScope.TrackedObject child, INotifyPropertyChanged notifyParent)
                {
                    LinkedScope = ss;
                    Key = key;
                    Parent = parent;
                    Child = child;
                    NotifyParent = new CEFWeakReference<INotifyPropertyChanged>(notifyParent);

                    // Wire notifications on parent: if key assigned (or changed), needs to cascade!
                    notifyParent.PropertyChanged += Parent_PropertyChanged;
                }

                private void Parent_PropertyChanged(object sender, PropertyChangedEventArgs e)
                {
                    var ordinal = Key.ParentKey.IndexOf(e.PropertyName);

                    // Was it a key change?
                    if (ordinal >= 0 && NotifyParent.IsAlive && NotifyParent.Target != null)
                    {
                        Func<object> forParent = null;

                        if (sender != null)
                        {
                            if (sender.FastPropertyReadable(e.PropertyName))
                            {
                                forParent = () =>
                                {
                                    return sender.FastGetValue(e.PropertyName);
                                };
                            }
                        }

                        if (forParent == null)
                        {
                            forParent = LinkedScope.GetGetter(NotifyParent.Target, e.PropertyName).getter;
                        }

                        if (forParent == null)
                        {
                            throw new CEFInvalidOperationException($"Could not find property {e.PropertyName} on object of type {NotifyParent.Target?.GetType()?.Name}.");
                        }

                        var chCol = Key.ChildResolvedKey[ordinal];
                        var forChild = LinkedScope.GetSetter(Child.GetInfraWrapperTarget(), chCol);

                        if (forChild.setter == null)
                        {
                            throw new CEFInvalidOperationException($"Could not find property {chCol} on object of type {Child.GetTarget()?.GetType()?.Name}.");
                        }

                        forChild.setter.Invoke(forParent.Invoke());
                    }
                }

                internal IList<object> GetParentKeyValue()
                {
                    List<object> val = new List<object>();

                    for (int i = 0; i < Key.ParentKey.Count; ++i)
                    {
                        var forParent = LinkedScope.GetGetter(NotifyParent.Target, Key.ParentKey[i]);
                        var parVal = forParent.getter.Invoke();
                        val.Add(parVal);
                    }

                    return val;
                }

                internal IList<object> GetChildRoleValue()
                {
                    List<object> val = new List<object>();

                    for (int i = 0; i < Key.ChildResolvedKey.Count; ++i)
                    {
                        var forChild = LinkedScope.GetGetter(Child.Target, Key.ChildResolvedKey[i]);
                        var chVal = forChild.getter.Invoke();
                        val.Add(chVal);
                    }

                    return val;
                }

                internal void CopyParentValueToChild()
                {
                    for (int i = 0; i < Key.ParentKey.Count && i < Key.ChildResolvedKey.Count; ++i)
                    {
                        var forParent = LinkedScope.GetGetter(NotifyParent.Target, Key.ParentKey[i]);
                        var parVal = forParent.getter.Invoke();
                        var forChild = LinkedScope.GetSetter(Child.GetInfraWrapperTarget(), Key.ChildResolvedKey[i]);
                        forChild.setter.Invoke(parVal);
                    }
                }

                public object GetValue(string propName, bool unwrap)
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
                                return NotifyParent.IsAlive ? NotifyParent.Target : null;
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
                        if (disposing && this.GetValue(nameof(NotifyParent), true) != null)
                        {
                            ((INotifyPropertyChanged)NotifyParent.Target).PropertyChanged -= Parent_PropertyChanged;
                        }

                        Key = null;
                        Parent = null;
                        Child = null;
                        NotifyParent = null;
                        disposedValue = true;
                    }
                }

                public void Dispose()
                {
                    Dispose(true);
                }
                #endregion
            }

            private ConcurrentIndexedList<KeyObjectStateFK> _fks = new ConcurrentIndexedList<KeyObjectStateFK>(nameof(KeyObjectStateFK.Parent), nameof(KeyObjectStateFK.Child));
            private ConcurrentIndexedList<KeyObjectStatePK> _pks = new ConcurrentIndexedList<KeyObjectStatePK>(nameof(KeyObjectStatePK.Composite)).AddUniqueConstraint(nameof(KeyObjectStatePK.Composite));
            private ConcurrentDictionary<TypeChildRelationship, ConcurrentDictionary<object, ImmutableList<ServiceScope.TrackedObject>>> _unlinkedChild = new ConcurrentDictionary<TypeChildRelationship, ConcurrentDictionary<object, ImmutableList<ServiceScope.TrackedObject>>>();
            private object _asNull = new object();

            internal ConcurrentIndexedList<KeyObjectStateFK> AllFK => _fks;
            internal ConcurrentIndexedList<KeyObjectStatePK> AllPK => _pks;

            public KeyObjectStateFK RemoveFK(ServiceScope ss, TypeChildRelationship key, ServiceScope.TrackedObject parent, ServiceScope.TrackedObject child, INotifyPropertyChanged parentWrapped, bool nullifyChild)
            {
                var findKey = new KeyObjectStateFK(ss, key, parent, child, parentWrapped);
                var existingKey = _fks.Find(findKey);

                if (existingKey != null)
                {
                    _fks.Remove(existingKey);

                    if (nullifyChild)
                    {
                        existingKey.NullifyChild();
                    }

                    existingKey.Dispose();
                }

                return existingKey;
            }

            public KeyObjectStateFK AddFK(ServiceScope ss, TypeChildRelationship key, ServiceScope.TrackedObject parent, ServiceScope.TrackedObject child, INotifyPropertyChanged parentWrapped, bool copyValues)
            {
                if (parentWrapped != null)
                {
                    var nk = new KeyObjectStateFK(ss, key, parent, child, parentWrapped);
                    _fks.Add(nk);

                    // Remove from unlinked child list, if preset
                    if (_unlinkedChild.TryGetValue(key, out var map))
                    {
                        if (map.TryGetValue(_asNull, out var ul1))
                        {
                            map[_asNull] = ul1.Remove(child);
                        }

                        var pwt = parent.GetWrapperTarget();
                        if (map.TryGetValue(pwt, out var ul2))
                        {
                            var keep = (from a in ul2 where a != child select a);

                            if (keep.Any())
                            {
                                map[pwt] = ImmutableList.CreateRange(keep);
                            }
                            else
                            {
                                map.TryRemove(pwt, out var tmp);
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

            public IEnumerable<ServiceScope.TrackedObject> GetUnlinkedChildrenForParent(TypeChildRelationship key, object parentWoTValue)
            {
                _unlinkedChild.TryGetValue(key, out var uk);

                if (uk != null)
                {
                    uk.TryGetValue(parentWoTValue, out var tl1);

                    if (tl1 != null)
                    {
                        foreach (var v in tl1)
                        {
                            yield return v;
                        }
                    }

                    uk.TryGetValue(parentWoTValue, out var tl2);

                    if (tl2 != null)
                    {
                        foreach (var v in tl2)
                        {
                            yield return v;
                        }
                    }
                }
            }

            public void TrackUnlinkedParent(TypeChildRelationship key, object parentWoTValue, ServiceScope.TrackedObject child)
            {
                _unlinkedChild.TryGetValue(key, out var uk);

                if (uk == null)
                {
                    uk = new ConcurrentDictionary<object, ImmutableList<ServiceScope.TrackedObject>>();
                    _unlinkedChild[key] = uk;
                }

                uk.TryGetValue(parentWoTValue ?? _asNull, out var tl);

                if (tl == null)
                {
                    tl = ImmutableList<ServiceScope.TrackedObject>.Empty;
                    uk[parentWoTValue ?? _asNull] = tl;
                }

                tl.Add(child);
            }

            public KeyObjectStatePK AddPK(ServiceScope ss, ServiceScope.TrackedObject parent, INotifyPropertyChanged parentWrapped, IList<string> parentKeyFields, Action<ServiceScope.TrackedObject, object, object> notifyKeyChange)
            {
                if (parentWrapped != null)
                {
                    var nk = new KeyObjectStatePK(ss, parent, parentWrapped, parentKeyFields, notifyKeyChange);
                    _pks.Add(nk);
                    return nk;
                }

                return null;
            }

            public ServiceScope.TrackedObject GetTrackedByCompositePK(string composite)
            {
                return _pks.GetFirstByName(nameof(KeyObjectStatePK.Composite), composite)?.Parent;
            }

            public ServiceScope.TrackedObject GetTrackedByPKValue(ServiceScope ss, Type parentType, IEnumerable<object> keyValues)
            {
                StringBuilder sb = new StringBuilder(128);

                sb.Append(parentType.Name);

                foreach (var f in keyValues)
                {
                    sb.Append(f);
                }

                var to = GetTrackedByCompositePK(sb.ToString());

                if (to == null || !to.IsAlive)
                {
                    return null;
                }

                return to;
            }

            public void UpdatePK(string oldcomp, string newcomp)
            {
                _pks.UpdateFieldIndex(nameof(KeyObjectStatePK.Composite), oldcomp, newcomp);
            }

            public IEnumerable<KeyObjectStateFK> AllChildren(object parent)
            {
                return _fks.GetAllByName(nameof(KeyObjectStateFK.Parent), parent);
            }

            public IEnumerable<KeyObjectStateFK> AllParents(object child)
            {
                return _fks.GetAllByName(nameof(KeyObjectStateFK.Child), child);
            }
        }

        #endregion
    }

    public class TypeChildRelationship : ICEFIndexedListItem
    {
        private string _identity;

        private void SetIdentity()
        {
            StringBuilder sb = new StringBuilder(128);
            sb.Append(ParentType?.Name);
            sb.Append(ChildType?.Name);
            sb.Append(ChildPropertyName);
            sb.Append(ParentPropertyName);
            if (ParentKey != null)
            {
                sb.Append(string.Join("", ParentKey.ToArray()));
            }
            if (ChildRoleName != null)
            {
                sb.Append(string.Join("", ChildRoleName.ToArray()));
            }
            _identity = sb.ToString();
        }

        public override int GetHashCode()
        {
            return _identity.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            return this._identity.IsSame((obj as TypeChildRelationship)?._identity);
        }

        private Type _childType;
        public Type ChildType
        {
            get { return _childType; }
            internal set
            {
                _childType = value;
                SetIdentity();
            }
        }

        private Type _parentType;
        public Type ParentType
        {
            get { return _parentType; }
            internal set
            {
                _parentType = value;
                SetIdentity();
            }
        }

        private IList<string> _parentKey;
        public IList<string> ParentKey
        {
            get { return _parentKey; }
            internal set
            {
                _parentKey = value;
                SetIdentity();
            }
        }

        private string _childPropertyName;
        public string ChildPropertyName
        {
            get { return _childPropertyName; }
            internal set
            {
                _childPropertyName = value;
                SetIdentity();
            }
        }

        public string FullParentChildPropertyName
        {
            get
            {
                if (!string.IsNullOrEmpty(ChildPropertyName))
                {
                    return $"{ParentType.Name}.{ChildPropertyName}";
                }
                return null;
            }
        }

        public string FullChildParentPropertyName
        {
            get
            {
                if (!string.IsNullOrEmpty(ParentPropertyName))
                {
                    return $"{ChildType.Name}.{ParentPropertyName}";
                }
                return null;
            }
        }

        private string _parentPropertyName;
        public string ParentPropertyName
        {
            get { return _parentPropertyName; }
            internal set
            {
                _parentPropertyName = value;
                SetIdentity();
            }
        }

        private IList<string> _childRoleName = null;
        public IList<string> ChildRoleName
        {
            get
            {
                return _childRoleName;
            }
            internal set
            {
                _childRoleName = value == null ? null : value.Count == 0 ? null : value;
                SetIdentity();
            }
        }

        public IList<string> ChildResolvedKey
        {
            get
            {
                return _childRoleName ?? _parentKey;
            }
        }

        public TypeChildRelationship MapsToChildProperty(string propName)
        {
            ChildPropertyName = propName;
            return this;
        }

        public TypeChildRelationship MapsToParentProperty(string propName)
        {
            ParentPropertyName = propName;
            return this;
        }

        public static TypeChildRelationship Create<TC>(params string[] childRoleName)
        {
            var i = new TypeChildRelationship();
            i.ChildType = typeof(TC);
            i.ChildRoleName = childRoleName;
            return i;
        }

        public object GetValue(string propName, bool unwrap)
        {
            switch (propName)
            {
                case nameof(TypeChildRelationship.ChildPropertyName):
                    return ChildPropertyName;

                case nameof(TypeChildRelationship.ChildRoleName):
                    return ChildRoleName;

                case nameof(TypeChildRelationship.ChildType):
                    return ChildType;

                case nameof(TypeChildRelationship.ParentKey):
                    return ParentKey;

                case nameof(TypeChildRelationship.ParentPropertyName):
                    return ParentPropertyName;

                case nameof(TypeChildRelationship.ParentType):
                    return ParentType;

                case nameof(TypeChildRelationship.FullParentChildPropertyName):
                    return FullParentChildPropertyName;

                case nameof(TypeChildRelationship.FullChildParentPropertyName):
                    return FullChildParentPropertyName;
            }
            throw new NotSupportedException("Unsupported property name.");
        }
    }
}
