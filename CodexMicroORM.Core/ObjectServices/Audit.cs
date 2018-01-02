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
using System.Data;

namespace CodexMicroORM.Core.Services
{
    public class AuditService : ICEFService
    {
        public static Func<string> GetLastUpdatedBy
        {
            get;
            set;
        } = () => { return Environment.UserName; };

        public static Func<DateTime> GetLastUpdatedDate
        {
            get;
            set;
        } = () => { return DateTime.UtcNow; };

        public static string IsDeletedField
        {
            get;
            set;
        } = "IsDeleted";

        public static string LastUpdatedByField
        {
            get;
            set;
        } = "LastUpdatedBy";

        public static string LastUpdatedDateField
        {
            get;
            set;
        } = "LastUpdatedDate";

        public static bool IsLastUpdatedByDBAssigned
        {
            get;
            set;
        } = false;

        public static bool IsLastUpdatedDateDBAssigned
        {
            get;
            set;
        } = true;

        public AuditService()
        {
        }

        public AuditService(Func<string> getLastUpdatedBy)
        {
            GetLastUpdatedBy = getLastUpdatedBy;
        }

        public AuditService(Func<DateTime> getLastUpdatedDate)
        {
            GetLastUpdatedDate = getLastUpdatedDate;
        }

        public AuditService(Func<string> getLastUpdatedBy, Func<DateTime> getLastUpdatedDate)
        {
            GetLastUpdatedBy = getLastUpdatedBy;
            GetLastUpdatedDate = getLastUpdatedDate;
        }

        public static ICEFInfraWrapper SavePreview(ServiceScope ss, ICEFInfraWrapper saving, ObjectState state)
        {
            if (!IsLastUpdatedByDBAssigned && !string.IsNullOrEmpty(LastUpdatedByField))
            {
                saving.SetValue(LastUpdatedByField, (ss.Settings.GetLastUpdatedBy ?? GetLastUpdatedBy).Invoke());
            }

            if (!IsLastUpdatedDateDBAssigned && !string.IsNullOrEmpty(LastUpdatedDateField))
            {
                saving.SetValue(LastUpdatedDateField, GetLastUpdatedDate.Invoke());
            }

            if (state == ObjectState.Added)
            {
                if (!string.IsNullOrEmpty(IsDeletedField))
                {
                    saving.SetValue(IsDeletedField, false);
                }
            }

            return saving;
        }

        IList<Type> ICEFService.RequiredServices()
        {
            return null;
        }

        Type ICEFService.IdentifyStateType(object o, ServiceScope ss, bool isNew)
        {
            return null;
        }

        WrappingSupport ICEFService.IdentifyInfraNeeds(object o, object replaced, ServiceScope ss, bool isNew)
        {
            if ((!string.IsNullOrEmpty(LastUpdatedByField) && !(replaced ?? o).HasProperty(LastUpdatedByField)) ||
                (!string.IsNullOrEmpty(LastUpdatedDateField) && !(replaced ?? o).HasProperty(LastUpdatedDateField)) ||
                (!string.IsNullOrEmpty(IsDeletedField) && !(replaced ?? o).HasProperty(IsDeletedField)))
            {
                return WrappingSupport.PropertyBag;
            }

            return WrappingSupport.None;
        }

        void ICEFService.FinishSetup(ServiceScope.TrackedObject to, ServiceScope ss, bool isNew, IDictionary<string, object> props, ICEFServiceObjState state)
        {
        }
    }
}
