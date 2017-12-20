using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Text;

namespace microCEF.Core.Services
{
    public class ValueTrackingWrapper : ICEFInfraWrapper
    {
        private ConcurrentDictionary<string, object> _originalValues = new ConcurrentDictionary<string, object>();
        private object _source;
        private DataRowState _rowState;

        public void AcceptChanges()
        {
            if (_rowState == DataRowState.Deleted)
            {
                return;
            }

            foreach (var pi in _source.GetType().GetProperties())
            {
                if (pi.CanWrite)
                {
                    _originalValues[pi.Name] = pi.GetValue(_source);
                }
            }

            _rowState = DataRowState.Unchanged;
        }

        public object OriginalValueFor(string propName)
        {
            if (Globals.EnhancedChecking && !_originalValues.ContainsKey(propName))
            {
                ;
            }

            return _originalValues[propName];
        }

        public WrappingNeed SupportsWrapping()
        {
            return WrappingNeed.OriginalValues | WrappingNeed.PropertyBag;
        }

        internal ValueTrackingWrapper(object o, DataRowState irs)
        {
            _source = new WeakReference(o);
            AcceptChanges();
            _rowState = irs;

            if (o is INotifyPropertyChanged)
            {
                ((INotifyPropertyChanged)o).PropertyChanged += CEFValueTrackingWrapper_PropertyChanged;
            }
            else
            {
                // todo
            }
        }

        private bool IsSame(object o1, object o2)
        {
            if (o1 == null && o2 == null)
                return true;

            if (o1 == null && o2 != null)
                return false;

            if (o2 == null && o1 != null)
                return false;

            return o1.Equals(o2);
        }

        private void CEFValueTrackingWrapper_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (!IsSame(_originalValues[e.PropertyName], _source.GetType().GetProperty(e.PropertyName).GetValue(_source)))
            {
                _rowState = DataRowState.Modified;
            }
        }

        #region IDisposable Support
        private bool _disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    ((INotifyPropertyChanged)_source).PropertyChanged -= CEFValueTrackingWrapper_PropertyChanged;
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
