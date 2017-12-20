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

namespace CodexMicroORM.Core
{
    /// <summary>
    /// Provides appdomain-level default settings that control overall framework behavior. This lets you customize based on certain preferences (naming, etc.).
    /// </summary>
    public static class Globals
    {
        private static WrappingAction _wrappingAction = WrappingAction.Dynamic;

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

        public static string KeyNamingPattern
        {
            get;
            set;
        } = "{0}ID";

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

        public static int? CommandTimeoutSeconds
        {
            get;
            set;
        } = null;

        private static Func<string> defaultGetLastUpdatedByFunc = () =>
        {
            return Environment.UserName;
        };

        public static MergeBehavior DefaultMergeBehavior
        {
            get;
            set;
        } = MergeBehavior.SilentMerge;

        public static Type DefaultKeyType
        {
            get;
            set;
        } = typeof(int);

        public static bool OptimisticConcurrencyWithLastUpdatedDate
        {
            get;
            set;
        } = true;

        public static string DefaultLastUpdatedByField
        {
            get;
            set;
        } = "LastUpdatedBy";

        public static string DefaultLastUpdatedDateField
        {
            get;
            set;
        } = "LastUpdatedDate";

        public static string DefaultIsDeletedField
        {
            get;
            set;
        } = "IsDeleted";

        public static Func<string> DefaultGetLastUpdatedByFunc
        {
            get
            {
                return defaultGetLastUpdatedByFunc;
            }
            set
            {
                defaultGetLastUpdatedByFunc = value;
            }
        }
    }
}
