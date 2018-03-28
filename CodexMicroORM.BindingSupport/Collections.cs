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
    public class GenericBindableSet : ObservableCollection<DynamicBindable>, IDisposable
    {
        private bool _isDirty = false;

        public event EventHandler<DirtyStateChangeEventArgs> DirtyStateChange;
        public event EventHandler<PropertyChangedEventArgs> RowPropertyChanged;

        public bool ScanClean
        {
            get;
            set;
        } = false;

        internal GenericBindableSet(IEnumerable<DynamicBindable> source) : base(source)
        {
            AddTracking(source);
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

        protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            base.OnCollectionChanged(e);

            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    AddTracking(from a in e.NewItems.Cast<DynamicBindable>() select a);
                    break;
                case NotifyCollectionChangedAction.Remove:
                    RemoveTracking(from a in e.OldItems.Cast<DynamicBindable>() select a);
                    break;
                case NotifyCollectionChangedAction.Replace:
                    RemoveTracking(from a in e.OldItems.Cast<DynamicBindable>() select a);
                    AddTracking(from a in e.NewItems.Cast<DynamicBindable>() select a);
                    break;
                case NotifyCollectionChangedAction.Reset:
                    RemoveTracking(from a in e.OldItems.Cast<DynamicBindable>() select a);
                    break;
            }

            if (e.Action != NotifyCollectionChangedAction.Move)
            {
                SetDirty();
            }
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
    }
}
