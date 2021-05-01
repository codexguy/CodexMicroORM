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
using CodexMicroORM.Core.Services;
using System;
using System.Collections.Generic;
#nullable enable

namespace CodexMicroORM.Core
{
    /// <summary>
    /// Provides appdomain-level default settings that control overall framework behavior. This lets you customize based on certain preferences (naming, etc.).
    /// </summary>
    public static class Globals
    {
        #region "Internal state"

        private static WrappingAction _wrappingAction = WrappingAction.Dynamic;
        private static Type _defaultAuditServiceType = typeof(AuditService);
        private static Type _defaultDBServiceType = typeof(DBService);
        private static Type _defaultKeyServiceType = typeof(KeyService);
        private static Type _defaultPCTServiceType = typeof(PCTService);
        private static Type _defaultValidationServiceType = typeof(ValidationService);
        private static Type _entitySetType = typeof(EntitySet<>);
        private static bool _useGlobalServiceScope = false;
        private static HashSet<string> _globalPropExclDirtyCheck = new();

        [ThreadStatic]
        private static string? _currentThreadUser = null;

        #endregion

        public delegate void GlobalPropertyChangeCallback(object source, string propName, object oldval, object newval);

        /// <summary>
        /// Offers a mechanism to detect changes in infrastructure objects globally. (e.g. used in a syncing framework)
        /// </summary>
        public static GlobalPropertyChangeCallback? GlobalPropertyChangePreview
        {
            get;
            set;
        } = null;

        public static bool CheckDirtyItemsForRealChanges
        {
            get;
            set;
        } = false;

        /// <summary>
        /// Mainly intended for debugging purposes, allows certain very low-level operations to be logged. (Use sparingly!)
        /// </summary>
        public static Action<string>? DeepLogger
        {
            get;
            set;
        } = null;

        /// <summary>
        /// Rather than the default of "3" for standard dictionary initialization, offers a different starting capacity for most dictionaries managed by the framework.
        /// </summary>
        public static int DefaultDictionaryCapacity
        {
            get;
            set;
        } = 11;

        /// <summary>
        /// Similar to DefaultDictionaryCapacity but intended for dictionaries that are known to likely hold more than a trivial number of values.
        /// </summary>
        public static int DefaultLargerDictionaryCapacity
        {
            get;
            set;
        } = 31;

        /// <summary>
        /// Custom collection types that are optimized for parallel usage will leverage this setting to establish "buckets" to which individual threads will map, thus reducing contention over the whole of the collection.
        /// </summary>
        public static int DefaultCollectionConcurrencyLevel
        {
            get;
            set;
        } = Environment.ProcessorCount.MaxOf(4);

        /// <summary>
        /// Custom collection types that are list-based would use this setting to establish their initial capacities unless otherwise specified.
        /// </summary>
        public static int DefaultListCapacity
        {
            get;
            set;
        } = 6;

        public static Type DefaultPCTServiceType
        {
            get
            {
                return _defaultPCTServiceType;
            }
            set
            {
                if (value == null || (value != null && value.GetInterface(typeof(ICEFPersistenceHost).Name) == null))
                {
                    throw new CEFInvalidStateException(InvalidStateType.BadParameterValue, "Type does not implement ICEFPersistenceHost.");
                }

                _defaultPCTServiceType = value!;
            }
        }

        public static Type DefaultAuditServiceType
        {
            get
            {
                return _defaultAuditServiceType;
            }
            set
            {
                if (value == null || (value != null && value.GetInterface(typeof(ICEFAuditHost).Name) == null))
                {
                    throw new CEFInvalidStateException(InvalidStateType.BadParameterValue, "Type does not implement ICEFAuditHost.");
                }

                _defaultAuditServiceType = value!;
            }
        }

        public static Type DefaultDBServiceType
        {
            get
            {
                return _defaultDBServiceType;
            }
            set
            {
                if (value == null || (value != null && value.GetInterface(typeof(ICEFDataHost).Name) == null))
                {
                    throw new CEFInvalidStateException(InvalidStateType.BadParameterValue, "Type does not implement ICEFDataHost.");
                }

                _defaultDBServiceType = value!;
            }
        }

        public static Type DefaultValidationServiceType
        {
            get
            {
                return _defaultValidationServiceType;
            }
            set
            {
                if (value == null || (value != null && value.GetInterface(typeof(ICEFValidationHost).Name) == null))
                {
                    throw new CEFInvalidStateException(InvalidStateType.BadParameterValue, "Type does not implement ICEFValidationHost.");
                }

                _defaultValidationServiceType = value!;
            }
        }

        public static Type DefaultKeyServiceType
        {
            get
            {
                return _defaultKeyServiceType;
            }
            set
            {
                if (value == null || (value != null && value.GetInterface(typeof(ICEFKeyHost).Name) == null))
                {
                    throw new CEFInvalidStateException(InvalidStateType.BadParameterValue, "Type does not implement ICEFKeyHost.");
                }

                _defaultKeyServiceType = value!;
            }
        }

        /// <summary>
        /// When the framework attempts to auto-constuct collections, this is the preferred type to use (must implement ICEFList). Intended to name types with even more functionality than EntitySet, if needed.
        /// </summary>
        public static Type PreferredEntitySetType
        {
            get
            {
                return _entitySetType;
            }
            set
            {
                if (value == null)
                {
                    throw new CEFInvalidStateException(InvalidStateType.ArgumentNull, nameof(PreferredEntitySetType));
                }

                if (!typeof(ICEFList).IsAssignableFrom(value))
                {
                    throw new CEFInvalidStateException(InvalidStateType.BadParameterValue, "Value does not implement ICEFList.");
                }

                _entitySetType = value;
            }
        }

        /// <summary>
        /// Returns an instance of the collection type that CEF uses to track object relationships.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static EntitySet<T> NewEntitySet<T>() where T : class, new()
        {
            return (EntitySet<T>) Activator.CreateInstance(_entitySetType.MakeGenericType(typeof(T)));
        }

        /// <summary>
        /// Returns an instance of the collection type that CEF uses to track object relationships. Initializes based on an enumerable source.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="source"></param>
        /// <returns></returns>
        public static EntitySet<T> NewEntitySet<T>(IEnumerable<T> source) where T : class, new()
        {
            return (EntitySet<T>)Activator.CreateInstance(_entitySetType.MakeGenericType(typeof(T)), source);
        }

        public static ServiceScopeSettings? GlobalServiceScopeSettings
        {
            get;
            set;
        } = null;

        public static PropertyDateStorage DefaultPropertyDateStorage
        {
            get;
            set;
        } = PropertyDateStorage.None;

        public static bool DoCopyParseProperties
        {
            get;
            set;
        } = true;

        public static bool UseGlobalServiceScope
        {
            get
            {
                return _useGlobalServiceScope;
            }
            set
            {
                if (value && !_useGlobalServiceScope)
                {
                    var settings = GlobalServiceScopeSettings ?? new ServiceScopeSettings();
                    settings.CanDispose = false;

                    CEF.InternalGlobalServiceScope = CEF.NewServiceScope(settings);
                    _useGlobalServiceScope = true;
                }
            }
        }

        /// <summary>
        /// If wrapper classes are available/used, this identifies what capabilities they have (notifications? property bags? etc.)
        /// </summary>
        public static WrappingSupport WrapperSupports
        {
            get;
            set;
        } = WrappingSupport.None;

        /// <summary>
        /// When false, every case that might use a wrapper class requires one; otherwise wrapper classes are not required and infrastructure wrappers can be silently used.
        /// </summary>
        public static bool MissingWrapperAllowed
        {
            get;
            set;
        } = true;

        public static ValidationErrorCode ValidationChecksOnSave
        {
            get;
            set;
        } = ValidationErrorCode.SaveFailDefault;

        public static bool ValidationFailureIsException
        {
            get;
            set;
        } = true;

        public static BulkRules DefaultBulkInsertRules
        {
            get;
            set;
        } = BulkRules.Threshold;

        /// <summary>
        /// When a global query timeout (milliseconds) is specified, queries are run up until this maximum timeout, after which co-operative cancellation is attempted. By default this is "off" since we assume most DBMS offer their own timeout mechanism.
        /// </summary>
        public static int? GlobalQueryTimeout
        {
            get;
            set;
        } = null;

        /// <summary>
        /// When set to true, queries use dedicated threads that can be aborted if the global query timeout elapses. This is a higher cost setting so the default is false, with false also being applicable when using RDBMS which can manage their own command timeouts.
        /// </summary>
        public static bool QueriesUseDedicatedThreads
        {
            get;
            set;
        } = false;

        /// <summary>
        /// When set to true, child objects instantiated before their parent references (based on surrogate key values) are linked back to parents when the parents are instantiated. This is not a normal flow, so is considered "false" by default to help improve performance. (ZDB requires it to be true on startup.)
        /// </summary>
        public static bool ResolveForArbitraryLoadOrder
        {
            get;
            set;
        } = false;

        public static WrappingAction DefaultWrappingAction
        {
            get
            {
                if (WrapperSupports != WrappingSupport.None)
                {
                    return WrappingAction.PreCodeGen;
                }
                return _wrappingAction;
            }
            set
            {
                _wrappingAction = value;
            }
        }

        /// <summary>
        /// The name of the wrapper class namespace where {0} can be used to represent the base type's namespace.
        /// </summary>
        public static string? WrappingClassNamespace
        {
            get;
            set;
        } = "{0}.Wrapper";

        /// <summary>
        /// An optional class name pattern for wrapper classes where {0} can be used to represent the base type's class name.
        /// </summary>
        public static string? WrapperClassNamePattern
        {
            get;
            set;
        }

        /// <summary>
        /// Optional name of the assembly the contains the wrapper classes. (If omitted, uses the assembly for the base types.)
        /// </summary>
        public static string? WrapperClassAssembly
        {
            get;
            set;
        }

        /// <summary>
        /// When true (default), object graphs can be traversed using parallel threads.
        /// </summary>
        public static bool EnableParallelPropertyParsing
        {
            get;
            set;
        } = false;

        public static RetrievalPostProcessing DefaultRetrievalPostProcessing
        {
            get;
            set;
        } = RetrievalPostProcessing.Default;

        public static ScopeMode DefaultConnectionScopeMode
        {
            get;
            set;
        } = ScopeMode.CreateNew;

        /// <summary>
        /// When true, new connection scopes automatically create new transactions.
        /// </summary>
        public static bool UseTransactionsForNewScopes
        {
            get;
            set;
        } = true;

        public static string GetCurrentUser() => _currentThreadUser ??= Environment.UserName;

        /// <summary>
        /// When true, null collections are initialized when found with EntitySet instances; this can impose a performance overhead with the benefit of a simpler way to populate the object graph.
        /// </summary>
        public static bool DefaultInitializeNullCollections
        {
            get;
            set;
        } = false;

        /// <summary>
        /// Standalone transactions do not require you to call CanCommit - this is implied if each individual database operation completes without throwing an error. Default of false implies explicit CanCommit should be used.
        /// </summary>
        public static bool DefaultTransactionalStandalone
        {
            get;
            set;
        } = false;

        /// <summary>
        /// The default (global) degree of parallelism used when saving multiple rows of data. (1 implies sequential processing.)
        /// </summary>
        public static int DefaultDBSaveDOP
        {
            get;
            set;
        } = Environment.ProcessorCount;

        public static string SerializationStatePropertyName
        {
            get;
            set;
        } = "_os_";

        public static string SerializationTypePropertyName
        {
            get;
            set;
        } = "_typename_";

        /// <summary>
        /// When true, row state is serialized using a simple integer (when false, uses enumeration text - creates larger serialization output).
        /// </summary>
        public static bool SerializationStateAsInteger
        {
            get;
            set;
        } = true;

        /// <summary>
        /// When true, uncommitted surrogate keys are tracked as "shadow properties" - not normally "visible" to object consumers. (When false, the base properties for the surrogate keys are used to hold uncommitted key values.)
        /// </summary>
        public static bool UseShadowPropertiesForNew
        {
            get;
            set;
        } = true;

        public static bool AsyncCacheUpdates
        {
            get;
            set;
        } = false;

        /// <summary>
        /// When true, saving multiple rows allows out-of-order per-row asynchronous processing.
        /// </summary>
        public static bool UseAsyncSave
        {
            get;
            set;
        } = false;

        public static bool TypeSpecificCacheEvictionsOnUpdates
        {
            get;
            set;
        } = false;

        public static int MaximumCompletionItemsQueued
        {
            get;
            set;
        } = Environment.ProcessorCount * 16;

        /// <summary>
        /// Used when an estimated scope size is provided, identifies the esimtated ratio of scope objects to foreign keys. This helps afford a way to property set the initial dictionary size for these.
        /// </summary>
        public static double EstimatedFKRatio
        {
            get;
            set;
        } = 2.0;

        public static SerializationMode DefaultSerializationMode
        {
            get;
            set;
        } = SerializationMode.Default;

        public static int? CommandTimeoutSeconds
        {
            get;
            set;
        } = null;

        public static int DefaultGlobalCacheIntervalSeconds
        {
            get;
            set;
        } = 600;

        public static CacheBehavior DefaultCacheBehavior
        {
            get;
            set;
        } = CacheBehavior.Default;

        public static MergeBehavior DefaultMergeBehavior
        {
            get;
            set;
        } = MergeBehavior.Default;

        public static Type DefaultKeyType
        {
            get;
            set;
        } = typeof(int);


        public static HashSet<string> GlobalPropertiesExcludedFromDirtyCheck
        {
            get
            {
                return _globalPropExclDirtyCheck;
            }
        }

        public static void AddGlobalPropertyExcludedFromDirtyCheck(string propName)
        {
            _globalPropExclDirtyCheck.Add(propName);
        }

        public static bool? ByKeyRetrievalChecksScopeFirst
        {
            get;
            set;
        } = null;

        public static bool OptimisticConcurrencyWithLastUpdatedDate
        {
            get;
            set;
        } = true;

        public static bool CaseSensitiveDictionaries
        {
            get;
            set;
        } = true;

        public static bool UseReaderWriterLocks
        {
            get;
            set;
        } = true;

        public static bool AssumeSafe
        {
            get;
            set;
        } = false;

        /// <summary>
        /// The reason the default is true is this lets us manage connection scopes that cross multiple sevice scopes. We can override this at a service scope level.
        /// </summary>
        public static bool ConnectionScopePerThread
        {
            get;
            set;
        } = true;

        public static bool AllowDirtyReads
        {
            get;
            set;
        } = true;

        public static bool PortableJSONExcludeAudit
        {
            get;
            set;
        } = false;

        public static SerializationMode? PortableJSONMode
        {
            get;
            set;
        } = SerializationMode.IncludeNull | SerializationMode.IncludeReadOnlyProps | SerializationMode.IncludeType | SerializationMode.SingleLevel;

        public static DateConversionMode PortableJSONConvertDates
        {
            get;
            set;
        } = DateConversionMode.None;

        public static bool PortableJSONIncludeExtended
        {
            get;
            set;
        } = true;

        public static int PortableJSONExtendedPropertySampleSize
        {
            get;
            set;
        } = 5;

        public static StringComparer CurrentStringComparer
        {
            get
            {
                if (CaseSensitiveDictionaries)
                {
                    return StringComparer.CurrentCulture;
                }
                else
                {
                    return StringComparer.CurrentCultureIgnoreCase;
                }
            }
        }
    }
}
