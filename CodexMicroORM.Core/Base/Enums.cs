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
***********************************************************************/
using System;

namespace CodexMicroORM.Core
{
    [Flags]
    public enum DBSaveTriggerFlags
    {
        Insert = 1,
        Update = 2,
        Delete = 4,
        Before = 32,
        After = 64
    }

    public enum PropertyDateStorage
    {
        None = 0,
        TwoWayConvertUtc = 1,
        TwoWayConvertUtcOnlyWithTime = 2
    }

    public enum SerializationType
    {
        Array = 0,
        ObjectWithSchemaType1AndRows = 1
    }

    [Flags]
    public enum SerializationMode
    {
        ObjectState = 1,
        OnlyChanged = 2,
        IncludeReadOnlyProps = 4,
        IncludeNull = 8,
        OriginalForConcurrency = 16,
        OnlyCLRProperties = 32,
        IncludeType = 64,
        SingleLevel = 128,
        ExtendedInfoAsShadowProps = 256,
        Default = 9,
        OverWire = 25,
        OverWireOnlyChanges = 27
    }

    [Flags]
    public enum CacheBehavior
    {
        Off = 0,
        IdentityBased = 1,
        QueryBased = 2,
        ConvertQueryToIdentity = 4,
        OnlyForAllQuery = 8,
        ForAllDoesntConvertToIdentity = 16,
        Default = 5,
        ListCentricDefault = 29,
        MaximumDefault = 23
    }

    public enum ObjectState
    {
        Unchanged = 0,
        Modified = 1,
        Added = 2,
        Deleted = 3,
        Unlinked = 4,
        ModifiedPriority = 5
    }

    [Flags]
    public enum BulkRules
    {
        Never = 0,
        Always = 1,
        Threshold = 2,
        LeafOnly = 4,
        ByType = 8,
        Default = 6
    }

    public enum WrappingAction
    {
        NoneOrProvisionedAlready = 0,
        PreCodeGen = 1,
        Dynamic = 2,
        DataTable = 3
    }

    public enum ScopeMode
    {
        UseAmbient = 0,
        CreateNew = 1
    }

    public enum FrameworkWarningType
    {
        RetrievalIdentity = 1
    }

    /// <summary>
    /// Added: 1.2
    /// When retrieving possibly multiple records into an Entity Set, if the underlying type has a key defined and if the incoming data has duplicate keys, this setting determines how to handle the situation.
    /// None is is close to the existing behavior, pre 1.2 - zero overhead added to retrieval process, behaves like MaintainIdentityAndWarn with no warnings issued.
    /// MaintainIdentityAndWarn is close to the existing behavior, pre 1.2. In this case, the first instance is kept/returned, a warning is issued for each attempt to return a new row, values are copied to the first instance, such that there will only be as many instances tracked as there are unique values.
    /// WireFirst will keep a list of all instances that were attempted to be returned but only the first per key value will be "linkable" within the object graph.
    /// WithShadowProp will effectively act as a key-override such that the type or instance will be treated similar to a non-keyed type, but with a shadow property to hold a system-generated key value (_ID). This makes it possible to have multiple instances of the same key value, but they will not be linkable within the object graph.
    /// </summary>
    public enum RetrievalIdentityMode
    {
        MaintainIdentityAndWarn = 0,
        ThrowErrorOnDuplicate = 1,
        AllowMultipleWireFirst = 2,
        AllowMultipleWithShadowProp = 3
    }

    [Flags]
    public enum RetrievalPostProcessing
    {
        None = 0,
        PropertyGroups = 1,
        ParentInstancesWithoutCLRProperties = 2,
        PropertyNameFixups = 4,
        Default = 7
    }

    /// <summary>
    /// Identifies the type of validation failure presented by the validation engine. Can also be used as a filter to represent what types of validations are of interest. As such, is a flag that can be combined as a filter, or discrete values to indicate specific failures.
    /// Why are some types such as required field validation not included in default save validation? The database is also effectively doing this too, and we assume an app might have their own method of validation, so picking the least-overhead approach as the default: but easy to override at a Global level.
    /// </summary>
    [Flags]
    public enum ValidationErrorCode
    {
        None = 0,
        MissingRequired = 65536,
        TooLarge = (65536 * 2),
        CustomError = (65536 * 4),
        NumericRange = (65536 * 8),
        IllegalUpdate = (65536 * 16),
        SaveFailDefault = MissingRequired | TooLarge | CustomError | NumericRange | IllegalUpdate
    }

    [Flags]
    public enum RelationTypes
    {
        None = 0,
        Parents = 1,
        Children = 2,
        Both = 3
    }

    [Flags]
    public enum WrappingSupport
    {
        None = 0,
        Notifications = 1,
        OriginalValues = 2,
        PropertyBag = 4,
        DataErrors = 8,
        All = 15
    }

    [Flags]
    public enum MergeBehavior
    {
        SilentMerge = 1,
        FailIfDifferent = 2,
        CheckScopeIfPending = 4,
        Default = 5
    }

    public enum DeleteCascadeAction
    {
        None = 0,
        Cascade = 1,
        SetNull = 2,
        Fail = 3
    }

    public enum DateConversionMode
    {
        None = 0,
        ToGMTAlways = 1,
        ToGMTWhenHasTime = 2
    }
}
