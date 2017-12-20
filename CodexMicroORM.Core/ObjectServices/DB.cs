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
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Text;

namespace CodexMicroORM.Core.Services
{
    public class DBService : ICEFService
    {
        private static IDBProvider _defaultProvider;

        public DBService()
        {
        }

        public DBService(IDBProvider defaultProvider)
        {
            _defaultProvider = defaultProvider;
        }

        public IDBProvider GetProviderForType<T>(T o)
        {
            // todo - of course, need to examine by type
            return _defaultProvider;
        }

        public static IDBProvider DefaultProvider => _defaultProvider;

        IList<Type> ICEFService.RequiredServices()
        {
            return new Type[] { typeof(PCTService), typeof(KeyService) };
        }

        public IList<(object item, string message, int status)> Save(IList<ICEFInfraWrapper> rows, DBSaveSettings settings)
        {
            List<(object item, string message, int status)> results = new List<(object item, string message, int status)>();

            var cs = CEF.CurrentConnectionScope;

            // Ordering of rows supports foreign key dependencies: insert/update top-down, delete bottom-up.
            // Different order #'s require sequential processing, so we group by order # - within an order group/table, we should be able to issue parallel requests
            // We also offer a way to "preview" what will be saved and adjust if needed
            var ordRows = (from a in rows
                           let uw = a.AsUnwrapped()
                           let level = KeyService.GetObjectNestLevel(uw)
                           let sp = (settings.RowSavePreview == null ? (true, null) : settings.RowSavePreview.Invoke(a))
                           where sp.cansave
                           group a by new { Level = level, Type = uw.GetType(), RowState = sp.treatas.GetValueOrDefault(a.GetRowState()) }
                           into g
                           select new { Type = g.Key.Type, Level = g.Key.Level, RowState = g.Key.RowState, Rows = g });

            if ((settings.AllowedOperations & DBSaveSettings.Operations.Delete) != 0)
            {
                results.AddRange(from a in _defaultProvider.DeleteRows(cs, 
                    from a in ordRows orderby a.Level descending, a.Type.Name
                    from b in a.Rows where a.RowState == DataRowState.Deleted
                    select (a.Level, AuditService.SavePreview(b, DataRowState.Deleted)), settings) select (a.row.GetWrappedObject(), a.msg, a.status));
            }
            if ((settings.AllowedOperations & DBSaveSettings.Operations.Insert) != 0)
            {
                results.AddRange(from a in _defaultProvider.InsertRows(cs, 
                    from a in ordRows orderby a.Level, a.Type.Name
                    from b in a.Rows where a.RowState == DataRowState.Added
                    select (a.Level, AuditService.SavePreview(b, DataRowState.Added)), settings) select (a.row.GetWrappedObject(), a.msg, a.status));
            }
            if ((settings.AllowedOperations & DBSaveSettings.Operations.Update) != 0)
            {
                results.AddRange(from a in _defaultProvider.UpdateRows(cs, 
                    from a in ordRows orderby a.Level, a.Type.Name
                    from b in a.Rows where a.RowState == DataRowState.Modified
                    select (a.Level, AuditService.SavePreview(b, DataRowState.Modified)), settings) select (a.row.GetWrappedObject(), a.msg, a.status));
            }

            cs.DoneWork();
            return results;
        }

        public void ExecuteRaw(string command, bool doThrow = true, bool stopOnError = true)
        {
            var cs = CEF.CurrentConnectionScope;
            _defaultProvider.ExecuteRaw(cs, command, doThrow, stopOnError);
            cs.DoneWork();
        }

        public IEnumerable<T> RetrieveByKey<T>(params object[] key) where T : class, new()
        {
            var cs = CEF.CurrentConnectionScope;
            var res = _defaultProvider.RetrieveByKey<T>(cs, true, key);
            cs.DoneWork();
            return res;
        }

        public IEnumerable<T> RetrieveByQuery<T>(CommandType cmdType, string cmdText, params object[] parms) where T : class, new()
        {
            var cs = CEF.CurrentConnectionScope;
            var res = _defaultProvider.RetrieveByQuery<T>(cs, true, cmdType, cmdText, parms);
            cs.DoneWork();
            return res;
        }

        public IEnumerable<T> RetrieveAll<T>() where T: class, new()
        {
            var cs = CEF.CurrentConnectionScope;
            var res = _defaultProvider.RetrieveAll<T>(cs, true);
            cs.DoneWork();
            return res;
        }

        Type ICEFService.IdentifyStateType(object o, ServiceScope ss, bool isNew)
        {
            return null;
        }

        WrappingSupport ICEFService.IdentifyInfraNeeds(object o, object replaced, ServiceScope ss, bool isNew)
        {
            if ((replaced ?? o) is INotifyPropertyChanged)
            {
                return WrappingSupport.OriginalValues | WrappingSupport.PropertyBag;
            }
            else
            {
                return WrappingSupport.Notifications | WrappingSupport.OriginalValues | WrappingSupport.PropertyBag;
            }
        }

        void ICEFService.FinishSetup(ServiceScope.TrackedObject to, ServiceScope ss, bool isNew, IDictionary<string, object> props, ICEFServiceObjState state)
        {
        }
    }
}
