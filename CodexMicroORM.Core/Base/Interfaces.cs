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
using CodexMicroORM.Core.Services;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace CodexMicroORM.Core
{
    /// <summary>
    /// Services can advertise dependencies on other services, their "wrapping requirements", and manage state that might apply (at a scope level).
    /// In theory, third-parties can create their own CEF services to be used by the framework.
    /// </summary>
    public interface ICEFService
    {
        // Identifies services that this service relies on
        IList<Type> RequiredServices();

        // Identifies the type of state object this service needs, if any
        Type IdentifyStateType(object o, ServiceScope ss, bool isNew);

        // Main purpose: will observe replacements having taken place, from these can determine infra wrapping needs
        WrappingSupport IdentifyInfraNeeds(object o, object replaced, ServiceScope ss, bool isNew);

        // Main purposes: all infra wrappers are now in place, can complete init
        void FinishSetup(ServiceScope.TrackedObject to, ServiceScope ss, bool isNew, IDictionary<string, object> props, ICEFServiceObjState state);

        void Disposing(ServiceScope ss);
    }

    public interface ICEFServiceObjState
    {
    }

    public interface IDBProvider
    {
        IDBProviderConnection CreateOpenConnection(string config, bool transactional, string connStringOverride);

        IEnumerable<(ICEFInfraWrapper row, string msg, int status)> DeleteRows(ConnectionScope conn, IEnumerable<(int level, IEnumerable<(string schema, string name, ICEFInfraWrapper row)> rows)> rows, DBSaveSettings settings);
        IEnumerable<(ICEFInfraWrapper row, string msg, int status)> InsertRows(ConnectionScope conn, IEnumerable<(int level, IEnumerable<(string schema, string name, ICEFInfraWrapper row)> rows)> rows, DBSaveSettings settings);
        IEnumerable<(ICEFInfraWrapper row, string msg, int status)> UpdateRows(ConnectionScope conn, IEnumerable<(int level, IEnumerable<(string schema, string name, ICEFInfraWrapper row)> rows)> rows, DBSaveSettings settings);

        IEnumerable<T> RetrieveAll<T>(ConnectionScope conn, bool doWrap) where T : class, new();
        IEnumerable<T> RetrieveByKey<T>(ConnectionScope conn, bool doWrap, object[] key) where T : class, new();
        IEnumerable<T> RetrieveByQuery<T>(ConnectionScope conn, bool doWrap, CommandType cmdType, string cmdText, object[] parms) where T : class, new();

        void ExecuteRaw(ConnectionScope conn, string cmdText, bool doThrow = true, bool stopOnError = true);
        T ExecuteScalar<T>(ConnectionScope conn, string cmdText);
    }

    public interface IDBProviderConnection : IDisposable
    {
        void Commit();

        void Rollback();
    }

    public interface IDBProviderCommand
    {
        IDictionary<string, object> GetParameterValues();

        IEnumerable<Dictionary<string, (object value, Type type)>> ExecuteReadRows();

        IDBProviderCommand ExecuteNoResultSet();

        IEnumerable<(string name, object value)> GetOutputValues();
    }

    public interface ICEFDataHost : ICEFService
    {
        void WaitOnCompletions();

        void AddCompletionTask(Task t);

        void AddCompletionException(Exception ex);

        IList<(object item, string message, int status)> Save(IList<ICEFInfraWrapper> rows, ServiceScope ss, DBSaveSettings settings);

        void ExecuteRaw(string cmdText, bool doThrow = true, bool stopOnError = true);

        T ExecuteScalar<T>(string cmdText);

        IEnumerable<T> RetrieveAll<T>() where T : class, new();

        IEnumerable<T> RetrieveByKey<T>(params object[] key) where T : class, new();

        IEnumerable<T> RetrieveByQuery<T>(CommandType cmdType, string cmdText, params object[] parms) where T : class, new();
    }

    public interface ICEFKeyHost : ICEFService
    {
        void LinkChildInParentContainer(ServiceScope ss, string parentTypeName, string parentFieldName, object parContainer, object child);

        void UnlinkChildFromParentContainer(ServiceScope ss, string parentTypeName, string parentFieldName, object parContainer, object child);

        void UpdateBoundKeys(ServiceScope.TrackedObject to, ServiceScope ss, string fieldName, object oval, object nval);

        void WireDependents(object o, object replaced, ServiceScope ss, ICEFList list, bool? objectModelOnly);

        IEnumerable<object> GetChildObjects(ServiceScope ss, object o, bool all = false);

        IEnumerable<object> GetParentObjects(ServiceScope ss, object o, bool all = false);

        IList<(int ordinal, string name, object value)> GetKeyValues(object o, IList<string> cols = null);

        int GetObjectNestLevel(object o);
    }

    public interface ICEFPersistenceHost : ICEFService
    {
        IEnumerable<T> GetItemsFromSerializationText<T>(string json) where T : class, new();

        bool SaveContents(JsonTextWriter tw, object o, SerializationMode mode, IDictionary<object, bool> visits);
    }

    public interface ICEFCachingHost : ICEFService, IDisposable
    {
        void Shutdown();

        string Start();

        T GetByIdentity<T>(object[] key) where T : class, new();

        IEnumerable<T> GetByQuery<T>(string text, object[] parms) where T : class, new();

        void AddByIdentity<T>(T o, object[] key = null, int? expirySeconds = null) where T : class, new();

        void AddByQuery<T>(IEnumerable<T> list, string text, object[] parms = null, int? expirySeconds = null) where T : class, new();

        void InvalidateForByQuery(Type t, bool typeSpecific);

        void InvalidateIdentityEntry(Type baseType, IDictionary<string, object> props);

        void UpdateByIdentity(Type baseType, IDictionary<string, object> props, object[] key = null, int? expirySeconds = null);

        bool IsCacheBusy();

        int GetActiveCount();

        void DoingWork();

        void DoneWork();
    }

    public interface ICEFSerializable
    {
        string GetSerializationText(SerializationMode? mode = null);
    }

    public interface ICEFAuditHost : ICEFService
    {
        ICEFInfraWrapper SavePreview(ServiceScope ss, ICEFInfraWrapper saving, ObjectState state);

        Func<string> GetLastUpdatedBy
        {
            get;
            set;
        }

        Func<DateTime> GetLastUpdatedDate
        {
            get;
            set;
        }

        string IsDeletedField
        {
            get;
            set;
        }

        string LastUpdatedByField
        {
            get;
            set;
        }

        string LastUpdatedDateField
        {
            get;
            set;
        }

        bool IsLastUpdatedByDBAssigned
        {
            get;
            set;
        }

        bool IsLastUpdatedDateDBAssigned
        {
            get;
            set;
        }
    }

    /// <summary>
    /// Wrapper classes that implement this have the ability to tailor object names at runtime and identify the source objects they might "wrap".
    /// </summary>
    public interface ICEFWrapper
    {
        Type GetBaseType();

        string GetSchemaName();

        void SetCopyTo(object wrapped);

        object GetCopyTo();
    }

    /// <summary>
    /// Infrastructure wrappers are not typically implemented outside of the framework.
    /// </summary>
    public interface ICEFInfraWrapper
    {
        WrappingSupport SupportsWrapping();

        bool HasProperty(string propName);

        void RemoveProperty(string propName);

        object GetWrappedObject();

        ObjectState GetRowState();

        void SetRowState(ObjectState rs);

        IDictionary<string, object> GetAllValues(bool onlyWriteable = false, bool onlySerializable = false);

        bool SetValue(string propName, object value, Type preferredType = null, bool isRequired = false);

        object GetValue(string propName);

        object GetOriginalValue(string propName, bool throwIfNotSet);

        void AcceptChanges();

        bool SaveContents(Newtonsoft.Json.JsonTextWriter tw, SerializationMode mode);

        void RestoreContents(Newtonsoft.Json.JsonTextReader tr);

        void FinalizeObjectContents(Newtonsoft.Json.JsonTextWriter tw, SerializationMode mode);
    }

    /// <summary>
    /// These lists are not typically implemented outside of the framework. Implementation offers special capabilities for lists, with respect to the framework.
    /// </summary>
    public interface ICEFList
    {
        void AddWrappedItem(object o);

        void Initialize(ServiceScope ss, object parentContainer, string parentTypeName, string parentFieldName);

        void SuspendNotifications(bool stop);

        bool ContainsItem(object o);

        void RemoveItem(object o);
    }
}
