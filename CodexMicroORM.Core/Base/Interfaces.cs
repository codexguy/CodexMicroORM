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
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
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
        void FinishSetup(ServiceScope.TrackedObject to, ServiceScope ss, bool isNew, IDictionary<string, object> props, ICEFServiceObjState state, bool initFromTemplate);

        void Disposing(ServiceScope ss);
    }

    public interface ICEFServiceObjState
    {
        void Cleanup(ServiceScope ss);
    }

    public interface IDBProvider
    {
        IDBProviderConnection CreateOpenConnection(string config, bool transactional, string connStringOverride, int? timeoutOverride);

        IEnumerable<(ICEFInfraWrapper row, string msg, int status)> DeleteRows(ConnectionScope conn, IEnumerable<(int level, IEnumerable<(string schema, string name, Type basetype, ICEFInfraWrapper row)> rows)> rows, DBSaveSettings settings);
        IEnumerable<(ICEFInfraWrapper row, string msg, int status)> InsertRows(ConnectionScope conn, IEnumerable<(int level, IEnumerable<(string schema, string name, Type basetype, ICEFInfraWrapper row)> rows)> rows, DBSaveSettings settings);
        IEnumerable<(ICEFInfraWrapper row, string msg, int status)> UpdateRows(ConnectionScope conn, IEnumerable<(int level, IEnumerable<(string schema, string name, Type basetype, ICEFInfraWrapper row)> rows)> rows, DBSaveSettings settings);

        IEnumerable<T> RetrieveAll<T>(ICEFDataHost db, ConnectionScope conn, bool doWrap) where T : class, new();
        IEnumerable<T> RetrieveByKey<T>(ICEFDataHost db, ConnectionScope conn, bool doWrap, object[] key) where T : class, new();
        IEnumerable<T> RetrieveByQuery<T>(ICEFDataHost db, ConnectionScope conn, bool doWrap, CommandType cmdType, string cmdText, CEF.ColumnDefinitionCallback cc, object[] parms) where T : class, new();

        void ExecuteRaw(ConnectionScope conn, string cmdText, bool doThrow = true, bool stopOnError = true);
        T ExecuteScalar<T>(ConnectionScope conn, string cmdText);

        IEnumerable<(string name, object value)> ExecuteNoResultSet(ConnectionScope conn, System.Data.CommandType cmdType, string cmdText, params object[] parms);
    }

    public interface ICEFStorageNaming
    {
        string EntityPersistedName { get; set; }
    }

    public interface IDBProviderConnection : IDisposable
    {
        void Commit();

        void Rollback();

        string ID();

        bool IsWorking();

        void IncrementWorking();

        void DecrementWorking();
    }

    public interface IDBProviderCommand
    {
        IDictionary<string, object> GetParameterValues();

        IEnumerable<Dictionary<string, (object value, Type type)>> ExecuteReadRows();

        IDBProviderCommand ExecuteNoResultSet();

        IDictionary<string, Type> GetResultSetShape();

        IEnumerable<(string name, object value)> GetOutputValues();
    }

    public interface ICEFDataHost : ICEFService
    {
        void WaitOnCompletions();

        string GetPropertyNameFromStorageName(Type baseType, string storeName);

        void AddCompletionTask(Task t);

        void AddCompletionException(Exception ex);

        IList<(object item, string message, int status)> Save(IList<ICEFInfraWrapper> rows, ServiceScope ss, DBSaveSettings settings);

        void CopyPropertyGroupValues(object o);

        IEnumerable<string> GetPropertyGroupFields(Type t);

        void ExpandPropertyGroupValues(object o);

        void FixupPropertyStorageNames(object o);

        void InitializeObjectsWithoutCorrespondingIDs(object o);

        void ExecuteRaw(string cmdText, bool doThrow = true, bool stopOnError = true);

        T ExecuteScalar<T>(string cmdText);

        void ExecuteNoResultSet(CommandType cmdType, string cmdText, params object[] args);

        IEnumerable<T> RetrieveAll<T>() where T : class, new();

        IEnumerable<T> RetrieveByKey<T>(params object[] key) where T : class, new();

        IEnumerable<T> RetrieveByQuery<T>(CommandType cmdType, string cmdText, CEF.ColumnDefinitionCallback cc, params object[] parms) where T : class, new();

        string GetSchemaNameByType(Type bt);

        string GetEntityNameByType(Type bt, ICEFWrapper w);
    }

    public interface ICEFKeyHost : ICEFService
    {
        void LinkChildInParentContainer(ServiceScope ss, string parentTypeName, string parentFieldName, object parContainer, object child);

        void UnlinkChildFromParentContainer(ServiceScope ss, string parentTypeName, string parentFieldName, object parContainer, object child);

        void UpdateBoundKeys(ServiceScope.TrackedObject to, ServiceScope ss, string fieldName, object oval, object nval);

        void WireDependents(object o, object replaced, ServiceScope ss, ICEFList list, bool? objectModelOnly);

        IEnumerable<object> GetChildObjects(ServiceScope ss, object o, RelationTypes types = RelationTypes.None);

        IEnumerable<object> GetParentObjects(ServiceScope ss, object o, RelationTypes types = RelationTypes.None);

        List<(int ordinal, string name, object value)> GetKeyValues(object o, IEnumerable<string> cols = null);

        int GetObjectNestLevel(object o);

        IEnumerable<TypeChildRelationship> GetRelationsForChild(Type childType);

        object RemoveFK(ServiceScope ss, TypeChildRelationship key, ServiceScope.TrackedObject parent, ServiceScope.TrackedObject child, INotifyPropertyChanged parentWrapped, bool nullifyChild);
    }

    public interface ICEFPersistenceHost : ICEFService
    {
        IEnumerable<T> GetItemsFromSerializationText<T>(string json, JsonSerializationSettings settings) where T : class, new();

        bool SaveContents(JsonTextWriter tw, object o, SerializationMode mode, SerializationVisitTracker visits);
    }

    public interface ICEFCachingHost : ICEFService, IDisposable
    {
        void Shutdown();

        string Start();

        T GetByIdentity<T>(object[] key) where T : class, new();

        IEnumerable<T> GetByQuery<T>(string text, object[] parms) where T : class, new();

        void AddByIdentity<T>(T o, object[] key = null, int? expirySeconds = null) where T : class, new();

        void AddByQuery<T>(IEnumerable<T> list, string text, object[] parms = null, int? expirySeconds = null, CacheBehavior? mode = null) where T : class, new();

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

    public interface ICEFValidationHost : ICEFService
    {
        IEnumerable<(ValidationErrorCode error, string message)> GetObjectMessage<T>(T o) where T : class;

        IEnumerable<(ValidationErrorCode error, string message)> GetPropertyMessages<T>(T o, string propName) where T : class;
    }

    public interface ICEFAuditHost : ICEFService
    {
        ICEFInfraWrapper SavePreview(ServiceScope ss, ICEFInfraWrapper saving, ObjectState state, DBSaveSettings settings);

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

        bool HasShadowProperty(string propName);

        void RemoveProperty(string propName);

        object GetWrappedObject();

        ObjectState GetRowState();

        void SetRowState(ObjectState rs);

        IDictionary<string, object> GetAllValues(bool onlyWriteable = false, bool onlySerializable = false);

        IDictionary<string, Type> GetAllPreferredTypes(bool onlyWriteable = false, bool onlySerializable = false);

        bool SetValue(string propName, object value, Type preferredType = null, bool isRequired = false);

        object GetValue(string propName);

        object GetOriginalValue(string propName, bool throwIfNotSet);

        void SetOriginalValue(string propName, object value);

        void AcceptChanges();

        bool SaveContents(Newtonsoft.Json.JsonTextWriter tw, SerializationMode mode);

        void RestoreContents(Newtonsoft.Json.JsonTextReader tr);

        void FinalizeObjectContents(Newtonsoft.Json.JsonTextWriter tw, SerializationMode mode);

        ValidationWrapper GetValidationState();

        void UpdateData();
    }

    /// <summary>
    /// These lists are not typically implemented outside of the framework. Implementation offers special capabilities for lists, with respect to the framework.
    /// </summary>
    public interface ICEFList
    {
        bool AddWrappedItem(object o, bool allowLinking = true);

        void Initialize(ServiceScope ss, object parentContainer, string parentTypeName, string parentFieldName);

        void SuspendNotifications(bool stop);

        bool ContainsItem(object o);

        bool ContainsItemByKey(object o);

        void RemoveItem(object o);

        string GetSerializationText(SerializationMode? mode = null);
    }
}
