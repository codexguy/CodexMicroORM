/***********************************************************************
Copyright 2021 CodeX Enterprises LLC

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.

Example usage:
In your app init code, include:

CEF.AddGlobalService(new DBService(new MSSQLProcBasedProvider(myConnectionString)));
AttributeInitializer.Apply(typeof(SystemParameterSet).Assembly, ...);

The Apply here will inspect keys and such that are described here via attributes.

Major Changes:
06/2018    0.7     Initial release (Joel Champagne)
***********************************************************************/
#nullable enable
using CodexMicroORM.Core;
using CodexMicroORM.Core.Services;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;

namespace CodeXFramework.StandardEntities
{
    public static partial class SystemParameterMethods
    {

        public static void RetrieveAll(this EntitySet<SystemParameter> target)
        {
            target.DBRetrieveByQuery<SystemParameter>(CommandType.StoredProcedure, "[CodeXSpecific].[up_SystemParameter_ForList]");
        }
        public static void RetrieveAllWithAppend(this EntitySet<SystemParameter> target)
        {
            target.DBAppendByQuery<SystemParameter>(CommandType.StoredProcedure, "[CodeXSpecific].[up_SystemParameter_ForList]");
        }

    }



    public static partial class SystemParameterMethods
    {

        public static void RetrieveByKeyName(this EntitySet<SystemParameter> target, string KeyName)
        {
            target.DBRetrieveByQuery<SystemParameter>(CommandType.StoredProcedure, "[CodeXSpecific].[up_SystemParameter_ByKeyName]", KeyName);
        }
        public static void RetrieveByKeyNameWithAppend(this EntitySet<SystemParameter> target, string KeyName)
        {
            target.DBAppendByQuery<SystemParameter>(CommandType.StoredProcedure, "[CodeXSpecific].[up_SystemParameter_ByKeyName]", KeyName);
        }

    }



    [EntityPrimaryKey(nameof(UserParameterID))]
    [Serializable()]
    public partial class UserParameter
    {

        public long UserParameterID
        {
            get;
            set;
        }

        public string? KeyName
        {
            get;
            set;
        }

        public string? Value
        {
            get;
            set;
        }

        public string? LastUpdatedBy
        {
            get;
            set;
        }

        public DateTime? LastUpdatedDate
        {
            get;
            set;
        }

        public string? UserName
        {
            get;
            set;
        }



    }

    public partial class UserParameterSet : EntitySet<UserParameter>
    {
        public UserParameter Add(string _KeyName, string _Value, string _UserName)
        {
            var t = CEF.NewObject(new UserParameter()
            {
                KeyName = _KeyName,
                Value = _Value,
                UserName = _UserName
            });
            Add(t);
            return t;
        }


        public UserParameter First => this.FirstOrDefault();
        public void Save()
        {
            this.DBSave();
        }
        public void RetrieveByKey(params object[] args)
        {
            this.DBRetrieveByKey(args);
        }

    }

    [EntitySchemaName("CodeXSpecific")]
    [EntityPrimaryKey(nameof(SystemParameterID))]
    [Serializable()]
    public partial class SystemParameter
    {

        public int SystemParameterID
        {
            get;
            set;
        }

        public string? KeyName
        {
            get;
            set;
        }

        public string? Value
        {
            get;
            set;
        }

        public string? LastUpdatedBy
        {
            get;
            set;
        }

        public DateTime? LastUpdatedDate
        {
            get;
            set;
        }



    }

    public partial class SystemParameterSet : EntitySet<SystemParameter>
    {
        public SystemParameter Add(string _KeyName, string _Value)
        {
            var t = CEF.NewObject(new SystemParameter()
            {
                KeyName = _KeyName,
                Value = _Value
            });
            Add(t);
            return t;
        }


        public SystemParameter First => this.FirstOrDefault();
        public void Save()
        {
            this.DBSave();
        }
        public void RetrieveByKey(params object[] args)
        {
            this.DBRetrieveByKey(args);
        }

    }

    [EntitySchemaName("CodeXSpecific")]
    [EntityPrimaryKey(nameof(PrivAssignID))]
    [Serializable()]
    public partial class PrivilegeAssignment
    {

        public int PrivAssignID
        {
            get;
            set;
        }

        public int PrivID
        {
            get;
            set;
        }

        public string? UserName
        {
            get;
            set;
        }

        public string? RoleName
        {
            get;
            set;
        }

        public string? LastUpdatedBy
        {
            get;
            set;
        }

        public DateTime? LastUpdatedDate
        {
            get;
            set;
        }

        public int? PrivIntValue
        {
            get;
            set;
        }

        public string? PrivStringValue
        {
            get;
            set;
        }



    }

    public partial class PrivilegeAssignmentSet : EntitySet<PrivilegeAssignment>
    {
        public PrivilegeAssignment Add(int _PrivID, string _UserName, string _RoleName, int? _PrivIntValue, string _PrivStringValue)
        {
            var t = CEF.NewObject(new PrivilegeAssignment()
            {
                PrivID = _PrivID,
                UserName = _UserName,
                RoleName = _RoleName,
                PrivIntValue = _PrivIntValue,
                PrivStringValue = _PrivStringValue
            });
            Add(t);
            return t;
        }


        public PrivilegeAssignment First => this.FirstOrDefault();
        public void Save()
        {
            this.DBSave();
        }
        public void RetrieveByKey(params object[] args)
        {
            this.DBRetrieveByKey(args);
        }

    }

    [EntitySchemaName("CodeXSpecific")]
    [EntityPrimaryKey(nameof(PrivID))]
    [Serializable()]
    public partial class Privilege
    {

        public int PrivID
        {
            get;
            set;
        }

        public string? PrivCode
        {
            get;
            set;
        }

        public string? PrivSubCode
        {
            get;
            set;
        }

        public string? PrivDesc
        {
            get;
            set;
        }

        public string? PrivType
        {
            get;
            set;
        }

        public string? LastUpdatedBy
        {
            get;
            set;
        }

        public DateTime LastUpdatedDate
        {
            get;
            set;
        }

        public int? DefaultIntValue
        {
            get;
            set;
        }

        public string? DefaultStringValue
        {
            get;
            set;
        }

        public int? DefaultIntNoRecord
        {
            get;
            set;
        }

        public string? DefaultStringNoRecord
        {
            get;
            set;
        }



    }

    public partial class PrivilegeSet : EntitySet<Privilege>
    {
        public Privilege Add(string _PrivCode, string _PrivSubCode, string _PrivDesc, string _PrivType, int? _DefaultIntValue, string _DefaultStringValue, int? _DefaultIntNoRecord, string _DefaultStringNoRecord)
        {
            var t = CEF.NewObject(new Privilege()
            {
                PrivCode = _PrivCode,
                PrivSubCode = _PrivSubCode,
                PrivDesc = _PrivDesc,
                PrivType = _PrivType,
                DefaultIntValue = _DefaultIntValue,
                DefaultStringValue = _DefaultStringValue,
                DefaultIntNoRecord = _DefaultIntNoRecord,
                DefaultStringNoRecord = _DefaultStringNoRecord
            });
            Add(t);
            return t;
        }


        public Privilege First => this.FirstOrDefault();
        public void Save()
        {
            this.DBSave();
        }
        public void RetrieveByKey(params object[] args)
        {
            this.DBRetrieveByKey(args);
        }

    }

    [EntitySchemaName("CodeXSpecific")]
    [EntityPrimaryKey(nameof(UID))]
    [Serializable()]
    public partial class ObjCacheBacking
    {

        public Guid UID
        {
            get;
            set;
        }

        public DateTime LeaseExpires
        {
            get;
            set;
        }

        public string? CompressedObject
        {
            get;
            set;
        }

        public string? TypeName
        {
            get;
            set;
        }



    }

    public partial class ObjCacheBackingSet : EntitySet<ObjCacheBacking>
    {
        public ObjCacheBacking Add(DateTime _LeaseExpires, string _CompressedObject, string _TypeName)
        {
            var t = CEF.NewObject(new ObjCacheBacking()
            {
                LeaseExpires = _LeaseExpires,
                CompressedObject = _CompressedObject,
                TypeName = _TypeName
            });
            Add(t);
            return t;
        }
        public ObjCacheBacking Add(Guid _UID, DateTime _LeaseExpires, string _CompressedObject, string _TypeName)
        {
            var t = CEF.NewObject(new ObjCacheBacking()
            {
                UID = _UID,
                LeaseExpires = _LeaseExpires,
                CompressedObject = _CompressedObject,
                TypeName = _TypeName
            });
            Add(t);
            return t;
        }

        public ObjCacheBacking First => this.FirstOrDefault();
        public void Save()
        {
            this.DBSave();
        }
        public void RetrieveByKey(params object[] args)
        {
            this.DBRetrieveByKey(args);
        }

    }

    [EntitySchemaName("CodeXSpecific")]
    [EntityPrimaryKey(nameof(MonitoredHistoryMetadataID))]
    [Serializable()]
    public partial class MonitoredHistoryMetadata
    {

        public int MonitoredHistoryMetadataID
        {
            get;
            set;
        }

        public string? TableName
        {
            get;
            set;
        }

        public string? ColumnName
        {
            get;
            set;
        }

        public string? TableKeyName
        {
            get;
            set;
        }

        public string? SelectTableName
        {
            get;
            set;
        }

        public string? SelectColumnName
        {
            get;
            set;
        }

        public string? DisplayTableName
        {
            get;
            set;
        }

        public string? DisplayColumnName
        {
            get;
            set;
        }

        public string? SelectOldJoinClause
        {
            get;
            set;
        }

        public string? SelectNewJoinClause
        {
            get;
            set;
        }

        public string? FullSelect
        {
            get;
            set;
        }



    }

    public partial class MonitoredHistoryMetadataSet : EntitySet<MonitoredHistoryMetadata>
    {
        public MonitoredHistoryMetadata Add(string _TableName, string _ColumnName, string _TableKeyName, string _SelectTableName, string _SelectColumnName, string _DisplayTableName, string _DisplayColumnName, string _SelectOldJoinClause, string _SelectNewJoinClause, string _FullSelect)
        {
            var t = CEF.NewObject(new MonitoredHistoryMetadata()
            {
                TableName = _TableName,
                ColumnName = _ColumnName,
                TableKeyName = _TableKeyName,
                SelectTableName = _SelectTableName,
                SelectColumnName = _SelectColumnName,
                DisplayTableName = _DisplayTableName,
                DisplayColumnName = _DisplayColumnName,
                SelectOldJoinClause = _SelectOldJoinClause,
                SelectNewJoinClause = _SelectNewJoinClause,
                FullSelect = _FullSelect
            });
            Add(t);
            return t;
        }


        public MonitoredHistoryMetadata First => this.FirstOrDefault();
        public void Save()
        {
            this.DBSave();
        }
        public void RetrieveByKey(params object[] args)
        {
            this.DBRetrieveByKey(args);
        }

    }

    [EntitySchemaName("CodeXSpecific")]
    [EntityPrimaryKey(nameof(MonitoredHistoryLogID))]
    [Serializable()]
    public partial class MonitoredHistoryLog
    {

        public long MonitoredHistoryLogID
        {
            get;
            set;
        }

        public int MonitoredHistoryMetadataID
        {
            get;
            set;
        }

        public int KeyValue
        {
            get;
            set;
        }

        public string? LastUpdatedBy
        {
            get;
            set;
        }

        public DateTime LastUpdatedDate
        {
            get;
            set;
        }

        public string? OldValue
        {
            get;
            set;
        }

        public string? NewValue
        {
            get;
            set;
        }



    }

    public partial class MonitoredHistoryLogSet : EntitySet<MonitoredHistoryLog>
    {
        public MonitoredHistoryLog Add(int _MonitoredHistoryMetadataID, int _KeyValue, string _OldValue, string _NewValue)
        {
            var t = CEF.NewObject(new MonitoredHistoryLog()
            {
                MonitoredHistoryMetadataID = _MonitoredHistoryMetadataID,
                KeyValue = _KeyValue,
                OldValue = _OldValue,
                NewValue = _NewValue
            });
            Add(t);
            return t;
        }


        public MonitoredHistoryLog First => this.FirstOrDefault();
        public void Save()
        {
            this.DBSave();
        }
        public void RetrieveByKey(params object[] args)
        {
            this.DBRetrieveByKey(args);
        }

    }

    [EntitySchemaName("CodeXSpecific")]
    [EntityPrimaryKey(nameof(MachineParameterID))]
    [Serializable()]
    public partial class MachineParameter
    {

        public int MachineParameterID
        {
            get;
            set;
        }

        public string? KeyName
        {
            get;
            set;
        }

        public string? Value
        {
            get;
            set;
        }

        public string? LastUpdatedBy
        {
            get;
            set;
        }

        public DateTime LastUpdatedDate
        {
            get;
            set;
        }

        public string? MachineName
        {
            get;
            set;
        }



    }

    public partial class MachineParameterSet : EntitySet<MachineParameter>
    {
        public MachineParameter Add(string _KeyName, string _Value, string _MachineName)
        {
            var t = CEF.NewObject(new MachineParameter()
            {
                KeyName = _KeyName,
                Value = _Value,
                MachineName = _MachineName
            });
            Add(t);
            return t;
        }


        public MachineParameter First => this.FirstOrDefault();
        public void Save()
        {
            this.DBSave();
        }
        public void RetrieveByKey(params object[] args)
        {
            this.DBRetrieveByKey(args);
        }

    }
}
