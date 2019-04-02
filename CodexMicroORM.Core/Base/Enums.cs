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
***********************************************************************/
using System;

namespace CodexMicroORM.Core
{
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
        SaveFailDefault = (65536 * 4) | (65536 * 2) | (65536 * 8) | (65536 * 16)
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
