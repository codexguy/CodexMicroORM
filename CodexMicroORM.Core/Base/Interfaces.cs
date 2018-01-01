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
using System;
using System.Collections.Generic;
using System.Data;

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

        object GetWrappedObject();

        DataRowState GetRowState();

        void SetRowState(DataRowState rs);

        IDictionary<string, object> GetAllValues();

        bool SetValue(string propName, object value, Type preferredType = null, bool isRequired = false);

        object GetValue(string propName);

        void AcceptChanges();
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
