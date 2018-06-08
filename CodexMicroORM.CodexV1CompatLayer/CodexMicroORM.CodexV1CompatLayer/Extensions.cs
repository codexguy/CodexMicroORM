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
06/2018    0.7     Initial release (Joel Champagne)
***********************************************************************/
using CodexMicroORM.Core;
using CodexMicroORM.Core.Services;
using System;

namespace CodeXFramework.BaseEntity
{
    public static class Extensions
    {
        public static void Save<T>(this EntitySet<T> target) where T : class, new()
        {
            target.DBSave();
        }
        public static void RetrieveByKey<T>(this EntitySet<T> target, params object[] args) where T : class, new()
        {
            target.DBRetrieveByKey(args);
        }

    }
}
