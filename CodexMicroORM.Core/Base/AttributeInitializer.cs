/***********************************************************************
Copyright 2022 CodeX Enterprises LLC

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
06/2018    0.7     Initial release (Joel Champagne)
***********************************************************************/
using CodexMicroORM.Core.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
#nullable enable

namespace CodexMicroORM.Core
{
    public static class AttributeInitializer
    {
        public static bool SilentlyContinue
        {
            get;
            set;
        } = false;

        public static Action<(Type type, string? prop, Type attr)>? PreviewHandler
        {
            get;
            set;
        } = null;

        public static void Apply(params Assembly[] args)
        {
            if (args?.Length == 0)
            {
                args = AppDomain.CurrentDomain.GetAssemblies();
            }

            // Traverse provided assemblies, looking for classes that implement attributes of interest
            Parallel.ForEach(args, (a) =>
            {
                Parallel.ForEach(a.GetTypes(), (t) =>
                {
                    try
                    {
                        var pkAttr = t.GetCustomAttribute<EntityPrimaryKeyAttribute>();

                        if (pkAttr != null)
                        {
                            PreviewHandler?.Invoke((t, null, typeof(EntityPrimaryKeyAttribute)));
                            if (pkAttr.ShadowType != null)
                            {
                                typeof(KeyService).GetMethod("RegisterKeyWithType").MakeGenericMethod(t).Invoke(null, new object[] { pkAttr.Fields.First(), pkAttr.ShadowType });
                            }
                            else
                            {
                                typeof(KeyService).GetMethod("RegisterKey").MakeGenericMethod(t).Invoke(null, new object[] { pkAttr.Fields });
                            }

                            foreach (var prop in t.GetProperties())
                            {
                                var maxLenAttr = prop.GetCustomAttribute<EntityMaxLengthAttribute>();

                                if (maxLenAttr != null)
                                {
                                    PreviewHandler?.Invoke((t, prop.Name, typeof(EntityMaxLengthAttribute)));
                                    typeof(ValidationService).GetMethod("RegisterMaxLength").MakeGenericMethod(t).Invoke(null, new object[] { prop.Name, maxLenAttr.Length });
                                }

                                var defValAttr = prop.GetCustomAttribute<EntityDefaultValueAttribute>();

                                if (defValAttr != null)
                                {
                                    PreviewHandler?.Invoke((t, prop.Name, typeof(EntityDefaultValueAttribute)));
                                    typeof(DBService).GetMethod("RegisterDefault").MakeGenericMethod(t, prop.PropertyType).Invoke(null, new object[] { prop.Name, defValAttr.Value.CoerceType(prop.PropertyType)! });
                                }

                                var reqValAttr = prop.GetCustomAttribute<EntityRequiredAttribute>();

                                if (reqValAttr != null)
                                {
                                    PreviewHandler?.Invoke((t, prop.Name, typeof(EntityRequiredAttribute)));
                                    typeof(ValidationService).GetMethod("RegisterRequired", new Type[] { typeof(string) }).MakeGenericMethod(t, prop.PropertyType).Invoke(null, new object[] { prop.Name });
                                }

                                var ignBindAttr = prop.GetCustomAttribute<EntityIgnoreBindingAttribute>();

                                if (ignBindAttr != null)
                                {
                                    PreviewHandler?.Invoke((t, prop.Name, typeof(EntityIgnoreBindingAttribute)));
                                }

                                var treatROAttr = prop.GetCustomAttribute<PropertyTreatAsIfReadOnlyAttribute>();

                                if (treatROAttr != null)
                                {
                                    CEF.RegisterPropertyNameTreatReadOnly(prop.Name);
                                }
                            }
                        }

                        var dnsAttr = t.GetCustomAttribute<EntityDoNotSaveAttribute>();

                        if (dnsAttr != null)
                        {
                            PreviewHandler?.Invoke((t, null, typeof(EntityDoNotSaveAttribute)));
                            typeof(ServiceScope).GetMethod("RegisterDoNotSave").MakeGenericMethod(t).Invoke(null, Array.Empty<object>());
                        }

                        foreach (EntityAdditionalPropertiesAttribute addPropAttr in t.GetCustomAttributes<EntityAdditionalPropertiesAttribute>())
                        {
                            PreviewHandler?.Invoke((t, null, typeof(EntityAdditionalPropertiesAttribute)));
                            typeof(ServiceScope).GetMethod("AddAdditionalPropertyHost").MakeGenericMethod(t).Invoke(null, new object[] { addPropAttr.PropertyName });
                        }
                    }
                    catch
                    {
                        if (!SilentlyContinue)
                        {
                            throw;
                        }
                    }

                    try
                    {
                        foreach (var prop in t.GetProperties())
                        {
                            var dateStoreAttr = prop.GetCustomAttribute<EntityDateHandlingAttribute>();

                            if (dateStoreAttr != null && dateStoreAttr.StorageMode != PropertyDateStorage.None)
                            {
                                PreviewHandler?.Invoke((t, prop.Name, typeof(EntityDateHandlingAttribute)));
                                typeof(ServiceScope).GetMethod("SetDateStorageMode").MakeGenericMethod(t).Invoke(null, new object[] { prop.Name, dateStoreAttr.StorageMode });
                            }
                        }
                    }
                    catch
                    {
                        if (!SilentlyContinue)
                        {
                            throw;
                        }
                    }

                    try
                    {
                        var cacheAttr = t.GetCustomAttribute<EntityCacheRecommendAttribute>();

                        if (cacheAttr != null)
                        {
                            PreviewHandler?.Invoke((t, null, typeof(EntityCacheRecommendAttribute)));

                            typeof(ServiceScope).GetMethod("SetCacheBehavior").MakeGenericMethod(t).Invoke(null, new object[] { CacheBehavior.MaximumDefault });

                            if (cacheAttr.OnlyMemory.HasValue)
                            {
                                typeof(ServiceScope).GetMethod("SetCacheOnlyMemory").MakeGenericMethod(t).Invoke(null, new object[] { cacheAttr.OnlyMemory.Value });
                            }

                            if (cacheAttr.IntervalMinutes.HasValue)
                            {
                                typeof(ServiceScope).GetMethod("SetCacheSeconds").MakeGenericMethod(t).Invoke(null, new object[] { cacheAttr.IntervalMinutes.Value * 60 });
                            }
                        }
                    }
                    catch
                    {
                        if (!SilentlyContinue)
                        {
                            throw;
                        }
                    }

                    try
                    {
                        var schemaAttr = t.GetCustomAttribute<EntitySchemaNameAttribute>();

                        if (schemaAttr != null)
                        {
                            PreviewHandler?.Invoke((t, null, typeof(EntitySchemaNameAttribute)));
                            typeof(DBService).GetMethod("RegisterSchema").MakeGenericMethod(t).Invoke(null, new object[] { schemaAttr.Name });
                        }
                    }
                    catch
                    {
                        if (!SilentlyContinue)
                        {
                            throw;
                        }
                    }

                    try
                    {
                        var relAttr = t.GetCustomAttribute<EntityRelationshipsAttribute>();

                        if (relAttr != null)
                        {
                            PreviewHandler?.Invoke((t, null, typeof(EntityRelationshipsAttribute)));
                            typeof(KeyService).GetMethod("RegisterRelationship").MakeGenericMethod(t).Invoke(null, new object[] { relAttr.Relations });
                        }
                    }
                    catch
                    {
                        if (!SilentlyContinue)
                        {
                            throw;
                        }
                    }
                });
            });
        }
    }
}
