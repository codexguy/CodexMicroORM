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
#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using CodexMicroORM.Core.Services;

namespace CodexMicroORM.Core
{
    /// <summary>
    /// Settings applicable to service scopes.
    /// </summary>
    [Serializable]
    public sealed class ServiceScopeSettings
    {
        public bool EntitySetUsesUnwrapped
        {
            get;
            set;
        } = false;

        public bool InitializeNullCollections
        {
            get;
            set;
        } = Globals.DefaultInitializeNullCollections;

        public SerializationMode SerializationMode
        {
            get;
            set;
        } = Globals.DefaultSerializationMode;

        public bool? UseAsyncSave
        {
            get;
            set;
        } = null;

        public bool? AsyncCacheUpdates
        {
            get;
            set;
        } = null;

        public CacheBehavior? CacheBehavior
        {
            get;
            set;
        } = null;

        public int? GlobalCacheDuration
        {
            get;
            set;
        } = null;

        public RetrievalPostProcessing RetrievalPostProcessing
        {
            get;
            set;
        } = Globals.DefaultRetrievalPostProcessing;

        public MergeBehavior MergeBehavior
        {
            get;
            set;
        } = Globals.DefaultMergeBehavior;

        public Func<string> GetLastUpdatedBy
        {
            get;
            set;
        } = () => { return Globals.GetCurrentUser(); };

        public int EstimatedScopeSize
        {
            get;
            set;
        } = Environment.ProcessorCount * 7;

        public bool? ConnectionScopePerThread
        {
            get;
            set;
        } = null;

        [ThreadStatic]
        public bool CanDispose = true;
    }

    public sealed class JsonSerializationSettings
    {
        public SerializationType SerializationType
        {
            get;
            set;
        } = SerializationType.Array;

        public string SchemaName
        {
            get;
            set;
        } = "Schema";

        public string SchemaFieldNameName
        {
            get;
            set;
        } = "Name";

        public string SchemaFieldTypeName
        {
            get;
            set;
        } = "Type";

        public string SchemaFieldRequiredName
        {
            get;
            set;
        } = "Required";

        public string? DataRootName
        {
            get;
            set;
        }

        public Func<string, Type> GetDataType
        {
            get;
            set;
        } = (tn) =>
        {
            return Type.GetType($"System.{tn}");
        };
    }

    /// <summary>
    /// Settings applicable to connection scopes.
    /// </summary>
    [Serializable]
    public sealed class ConnectionScopeSettings
    {
        public ScopeMode? ScopeMode
        {
            get;
            set;
        } = null;

        public string? ConnectionStringOverride
        {
            get;
            set;
        }

        public bool? IsTransactional
        {
            get;
            set;
        } = null;

        public int? CommandTimeoutOverride
        {
            get;
            set;
        } = null;
    }

    /// <summary>
    /// Settings applicable to the request to save to the database.
    /// </summary>
    public sealed class DBSaveSettings
    {
        public IEnumerable<Type>? TypesWithChanges
        {
            get;
            set;
        }

        public int MaxDegreeOfParallelism
        {
            get;
            set;
        } = Globals.DefaultDBSaveDOP;

        public enum Operations
        {
            Insert = 1,
            Update = 2,
            Delete = 3,
            All = 7
        }

        public object? UserPayload
        {
            get;
            set;
        } = null;

        public ValidationErrorCode? ValidationChecksOnSave
        {
            get;
            set;
        } = Globals.ValidationChecksOnSave;

        public bool? ValidationFailureIsException
        {
            get;
            set;
        } = Globals.ValidationFailureIsException;

        public bool? UseAsyncSave
        {
            get;
            set;
        } = null;

        public bool? AsyncCacheUpdates
        {
            get;
            set;
        } = null;

        public object? RootObject
        {
            get;
            set;
        } = null;

        public Type? LimitToSingleType
        {
            get;
            set;
        } = null;

        public bool IncludeRootChildren
        {
            get;
            set;
        } = true;

        public string? EntityPersistName
        {
            get;
            set;
        } = null;

        public Type? EntityPersistType
        {
            get;
            set;
        } = null;

        public string? LastUpdatedBy
        {
            get;
            set;
        } = null;

        public bool IncludeRootParents
        {
            get;
            set;
        } = true;

        public Operations AllowedOperations
        {
            get;
            set;
        } = Operations.All;

        public BulkRules BulkInsertRules
        {
            get;
            set;
        } = Globals.DefaultBulkInsertRules;

        private IList<Type> _bulkInsertTypes = new List<Type>();

        public IList<Type> BulkInsertTypes
        {
            get
            {
                return _bulkInsertTypes;
            }
            set
            {
                if (value.Count > 0)
                {
                    BulkInsertRules = BulkRules.ByType;
                }

                _bulkInsertTypes = value;
            }
        }

        public int BulkInsertMinimumRows
        {
            get;
            set;
        } = 100000;

        public DBSaveSettings UseBulkInsertTypes(params Type[] types)
        {
            BulkInsertTypes = types;
            return this;
        }

        public Type? IgnoreObjectType
        {
            get;
            set;
        } = null;

        public IEnumerable<object>? SourceList
        {
            get;
            set;
        } = null;

        public Func<ICEFInfraWrapper, (bool cansave, ObjectState? treatas)>? RowSavePreview
        {
            get;
            set;
        } = null;

        public bool? DeferAcceptChanges
        {
            get;
            set;
        } = null;

        internal bool NoAcceptChanges
        {
            get;
            set;
        } = false;
    }

    public sealed class PortableSerializationOptions
    {
        public PortableSerializationOptions()
        {
        }

        public PortableSerializationOptions(params string[] cols)
        {
            ExcludeColumns = cols;
        }

        public SerializationMode? Mode
        {
            get;
            set;
        } = null;

        public IEnumerable<string>? IncludeColumns
        {
            get;
            set;
        } = null;

        public IEnumerable<string>? ExcludeColumns
        {
            get;
            set;
        } = null;

        public bool? AreColumnsExcluded
        {
            get;
            set;
        } = null;

        public bool? ExcludeAudit
        {
            get;
            set;
        } = null;

        public bool? OnlyWriteable
        {
            get;
            set;
        } = null;

        public bool? IncludeExtended
        {
            get;
            set;
        } = null;

        public int? ExtendedPropertySampleSize
        {
            get;
            set;
        } = null;

        public DateConversionMode? ConvertDates
        {
            get;
            set;
        } = null;

        public Func<ICEFInfraWrapper, object>? SortSpec
        {
            get;
            set;
        }

        public Func<ICEFInfraWrapper, bool>? FilterSpec
        {
            get;
            set;
        }
    }
}
