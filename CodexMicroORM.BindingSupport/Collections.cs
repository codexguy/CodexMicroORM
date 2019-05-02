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
using CodexMicroORM.Core;
using CodexMicroORM.Core.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace CodexMicroORM.BindingSupport
{
    /// <summary>
    /// A GenericBindableSet is an ObservableCollection of DynamicBindable. It resembles a DataTable, as such, since it offers no strong-typed CLR properties to access data.
    /// It's flexibility is in that it can be bound to WPF lists easily, as DynamicBindable's implement ICustomTypeProvider.
    /// </summary>
    public class GenericBindableSet : BindingList<DynamicBindable>, IDisposable, ITypedList
    {
        private bool _isDirty = false;

        public event EventHandler<DirtyStateChangeEventArgs> DirtyStateChange;
        public event EventHandler<PropertyChangedEventArgs> RowPropertyChanged;

        public Dictionary<string, Type> ExternalSchema
        {
            get;
            set;
        } = new Dictionary<string, Type>();

        public ServiceScope OwningScope
        {
            get;
            set;
        } = CEF.CurrentServiceScope;

        public Type BaseItemType
        {
            get;
            set;
        }

        public bool ScanClean
        {
            get;
            set;
        } = false;

        internal GenericBindableSet(IEnumerable<DynamicBindable> source) : base(source.ToList())
        {
            AddTracking(source);
            this.AllowNew = true;
            this.AllowEdit = true;
            this.AllowRemove = true;
        }

        private void ItemPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            SetDirty();
            RowPropertyChanged?.Invoke(sender, e);
        }

        public bool IsValid
        {
            get
            {
                foreach (var r in this)
                {
                    var ide = r.Wrapped as IDataErrorInfo;

                    if (ide != null && !string.IsNullOrEmpty(ide.Error))
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        public bool IsDirty => _isDirty;

        protected override object AddNewCore()
        {
            using (CEF.UseServiceScope(OwningScope))
            {
                // We rely on construction of a new DynamicBindable that has the same shape as the first item in the collection, if any exist
                if (this.Any())
                {
                    var f = this.First();
                    var wot = f.Wrapped?.GetWrappedObject()?.GetType();

                    if (wot != null)
                    {
                        var no = Activator.CreateInstance(wot);
                        var wno = CEF.IncludeObject(no, ObjectState.Added);
                        var nod = wno.AsDynamicBindable();
                        base.Add(nod);
                        return nod;
                    }
                }

                // If none exist, need to rely on the "default schema" provided
                if (BaseItemType != null)
                {
                    var no = Activator.CreateInstance(BaseItemType);
                    var wno = CEF.IncludeObject(no, ObjectState.Added);
                    var iw = wno.AsInfraWrapped();
                    var nod = wno.AsDynamicBindable();

                    if (ExternalSchema != null)
                    {
                        foreach (var e in ExternalSchema)
                        {
                            if (!iw.HasProperty(e.Key))
                            {
                                iw.SetValue(e.Key, null, e.Value);
                            }
                        }
                    }

                    base.Add(nod);
                    return nod;
                }

                // No default schema? It's an error situation.
                throw new InvalidOperationException("Cannot add a new item to the GenericBindableSet collection since there's no object definition available.");
            }
        }

        public void ResetClean()
        {
            if (_isDirty)
            {
                DirtyStateChange?.Invoke(this, new DirtyStateChangeEventArgs(false));
                _isDirty = false;
            }
        }

        internal void SetDirty()
        {
            if (!_isDirty)
            {
                DirtyStateChange?.Invoke(this, new DirtyStateChangeEventArgs(true));
                _isDirty = true;
            }
        }

        private void AddTracking(IEnumerable<DynamicBindable> toAdd)
        {
            foreach (var s in toAdd)
            {
                s.PropertyChanged += ItemPropertyChanged;
                s.DirtyStateChange += S_DirtyStateChange;
            }
        }

        private void S_DirtyStateChange(object sender, Core.Services.DirtyStateChangeEventArgs e)
        {
            if (ScanClean && e.NewState == Core.ObjectState.Unchanged)
            {
                if (!(from a in this where a.State != Core.ObjectState.Unchanged select a).Any())
                {
                    ResetClean();
                }
            }
        }

        private void RemoveTracking(IEnumerable<DynamicBindable> toRemove)
        {
            foreach (var s in toRemove)
            {
                s.PropertyChanged -= ItemPropertyChanged;
                s.DirtyStateChange -= S_DirtyStateChange;
            }
        }

        protected override void InsertItem(int index, DynamicBindable item)
        {
            base.InsertItem(index, item);
            AddTracking(new DynamicBindable[] { item });
            SetDirty();
        }

        protected override void RemoveItem(int index)
        {
            var i = this[index];
            RemoveTracking(new DynamicBindable[] { this[index] });
            base.RemoveItem(index);

            using (CEF.UseServiceScope(OwningScope))
            {
                var uw = i.Wrapped;

                if (uw != null)
                {
                    CEF.DeleteObject(uw);
                }
            }

            SetDirty();
        }

        protected override void SetItem(int index, DynamicBindable item)
        {
            bool change = (item != this[index]);
            RemoveTracking(new DynamicBindable[] { this[index] });
            base.SetItem(index, item);
            AddTracking(new DynamicBindable[] { item });

            if (change)
            {
                SetDirty();
            }
        }

        protected override void ClearItems()
        {
            RemoveTracking(this);
            base.ClearItems();
            SetDirty();
        }

        public class DirtyStateChangeEventArgs : EventArgs
        {
            private bool _newState;

            internal DirtyStateChangeEventArgs(bool newState)
            {
                _newState = newState;
            }

            public bool IsDirty => _newState;
        }

        #region IDisposable Support
        private bool _disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    RemoveTracking(this);
                }

                _disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
        #endregion

        public string GetListName(PropertyDescriptor[] listAccessors)
        {
            return typeof(GenericBindableSet).Name;
        }

        public PropertyDescriptorCollection GetItemProperties(PropertyDescriptor[] listAccessors)
        {
            PropertyDescriptorCollection pdc;

            if (this.Count > 0)
            {
                // If we have data, use the underlying data
                var i = this[0];
                pdc = ((ICustomTypeDescriptor)i).GetProperties();
            }
            else
            {
                pdc = new PropertyDescriptorCollection(null);
            }

            // No data, rely on external schema if available
            foreach (var s in ExternalSchema)
            {
                pdc.Add(DynamicBindable.GetNewPropertyDescriptor(s.Key, s.Value));
            }

            return pdc;
        }
    }
}
