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

namespace CodexMicroORM.Core
{
    /// <summary>
    /// Provides appdomain-level default settings that control overall framework behavior. This lets you customize based on certain preferences (naming, etc.).
    /// </summary>
    public static class Globals
    {
        public const int DEFAULT_DICT_CAPACITY = 11;
        public const int DEFAULT_LARGER_DICT_CAPACITY = 31;

        private static WrappingAction _wrappingAction = WrappingAction.Dynamic;
        private static Type _defaultAuditServiceType = typeof(AuditService);
        private static Type _defaultDBServiceType = typeof(DBService);
        private static Type _defaultKeyServiceType = typeof(KeyService);
        private static Type _defaultPCTServiceType = typeof(PCTService);
        private static Type _defaultValidationServiceType = typeof(ValidationService);

        public static Type DefaultPCTServiceType
        {
            get
            {
                return _defaultPCTServiceType;
            }
            set
            {
                if (value.GetInterface("ICEFPersistenceHost") == null)
                {
                    throw new ArgumentException("Type does not implement ICEFPersistenceHost.");
                }

                _defaultPCTServiceType = value;
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
                if (value.GetInterface("ICEFAuditService") == null)
                {
                    throw new ArgumentException("Type does not implement ICEFAuditService.");
                }

                _defaultAuditServiceType = value;
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
                if (value.GetInterface("ICEFDataHost") == null)
                {
                    throw new ArgumentException("Type does not implement ICEFDataHost.");
                }

                _defaultDBServiceType = value;
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
                if (value.GetInterface("ICEFValidationHost") == null)
                {
                    throw new ArgumentException("Type does not implement ICEFValidationHost.");
                }

                _defaultValidationServiceType = value;
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
                if (value.GetInterface("ICEFKeyHost") == null)
                {
                    throw new ArgumentException("Type does not implement ICEFKeyHost.");
                }

                _defaultKeyServiceType = value;
            }
        }

        private static Type _entitySetType = typeof(EntitySet<>);

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
                if (!typeof(ICEFList).IsAssignableFrom(value))
                {
                    throw new ArgumentException("Value does not implement ICEFList.");
                }

                _entitySetType = value;
            }
        }

        public static EntitySet<T> NewEntitySet<T>() where T : class, new()
        {
            return (EntitySet<T>) Activator.CreateInstance(_entitySetType.MakeGenericType(typeof(T)));
        }

        public static EntitySet<T> NewEntitySet<T>(IEnumerable<T> source) where T : class, new()
        {
            return (EntitySet<T>)Activator.CreateInstance(_entitySetType.MakeGenericType(typeof(T)), source);
        }

        public static ServiceScopeSettings GlobalServiceScopeSettings
        {
            get;
            set;
        } = null;

        private static bool _useGlobalServiceScope = false;

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

                    CEF._globalServiceScope = CEF.NewServiceScope(settings);
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
        /// When a global query timeout (milliseconds) is specified, queries are run on threads that can be aborted if the timeout period elapses before completion. By default this is "off" since we assume most DBMS offer their own timeouts.
        /// </summary>
        public static int? GlobalQueryTimeout
        {
            get;
            set;
        } = null;

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
        public static string WrappingClassNamespace
        {
            get;
            set;
        } = "{0}.Wrapper";

        /// <summary>
        /// An optional class name pattern for wrapper classes where {0} can be used to represent the base type's class name.
        /// </summary>
        public static string WrapperClassNamePattern
        {
            get;
            set;
        }

        /// <summary>
        /// Optional name of the assembly the contains the wrapper classes. (If omitted, uses the assembly for the base types.)
        /// </summary>
        public static string WrapperClassAssembly
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

        [ThreadStatic]
        private static string _currentThreadUser = null;

        public static string GetCurrentUser()
        {
            return _currentThreadUser ?? (_currentThreadUser = Environment.UserName);
        }

        /// <summary>
        /// When true, null collections are initialized when found with EntitySet instances; this can impose a performance overhead with the benefit of a simpler way to populate the object graph.
        /// </summary>
        public static bool DefaultInitializeNullCollections
        {
            get;
            set;
        } = false;

        public static bool DefaultTransactionalStandalone
        {
            get;
            set;
        } = false;

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

        public static bool SerializationStateAsInteger
        {
            get;
            set;
        } = true;

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
