using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DMotion.Editor
{
    /// <summary>
    /// Base class for objects that support property change notifications.
    /// Provides automatic PropertyChanged events when properties are set via SetProperty.
    /// </summary>
    /// <remarks>
    /// <para><b>Usage:</b></para>
    /// <code>
    /// public class MyState : ObservableObject
    /// {
    ///     private float _time;
    ///     public float Time
    ///     {
    ///         get => _time;
    ///         set => SetProperty(ref _time, value);
    ///     }
    /// }
    /// 
    /// // Subscribe to changes
    /// myState.PropertyChanged += (sender, e) =>
    /// {
    ///     if (e is ObservablePropertyChangedEventArgs args)
    ///     {
    ///         Debug.Log($"{e.PropertyName}: {args.OldValue} -> {args.NewValue}");
    ///     }
    /// };
    /// 
    /// // Setting property automatically fires event
    /// myState.Time = 0.5f;
    /// </code>
    /// </remarks>
    public abstract class ObservableObject : INotifyPropertyChanged
    {
        /// <summary>
        /// Fired when any property changes.
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;
        
        /// <summary>
        /// Sets a property value and raises PropertyChanged if the value changed.
        /// </summary>
        /// <typeparam name="T">Property type.</typeparam>
        /// <param name="field">Reference to the backing field.</param>
        /// <param name="value">New value.</param>
        /// <param name="propertyName">Property name (auto-filled by compiler).</param>
        /// <returns>True if the value changed, false otherwise.</returns>
        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;
            
            var oldValue = field;
            field = value;
            OnPropertyChanged(propertyName, oldValue, value);
            return true;
        }
        
        /// <summary>
        /// Sets a property value with a custom equality comparer.
        /// </summary>
        protected bool SetProperty<T>(ref T field, T value, IEqualityComparer<T> comparer, [CallerMemberName] string propertyName = null)
        {
            if (comparer.Equals(field, value))
                return false;
            
            var oldValue = field;
            field = value;
            OnPropertyChanged(propertyName, oldValue, value);
            return true;
        }
        
        /// <summary>
        /// Raises PropertyChanged for the specified property.
        /// </summary>
        protected virtual void OnPropertyChanged(string propertyName, object oldValue = null, object newValue = null)
        {
            if (_suppressNotifications)
                return;

            PropertyChanged?.Invoke(this, new ObservablePropertyChangedEventArgs(propertyName, oldValue, newValue));
        }
        
        /// <summary>
        /// Raises PropertyChanged for multiple properties at once.
        /// </summary>
        protected void OnPropertiesChanged(params string[] propertyNames)
        {
            if (_suppressNotifications)
                return;

            foreach (var name in propertyNames)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }
        }
        
        /// <summary>
        /// Suppresses property change notifications during a batch update.
        /// </summary>
        protected IDisposable SuppressNotifications()
        {
            return new NotificationSuppressor(this);
        }
        
        private bool _suppressNotifications;
        
        private class NotificationSuppressor : IDisposable
        {
            private readonly ObservableObject _owner;
            private readonly bool _previousState;
            
            public NotificationSuppressor(ObservableObject owner)
            {
                _owner = owner;
                _previousState = owner._suppressNotifications;
                owner._suppressNotifications = true;
            }
            
            public void Dispose()
            {
                _owner._suppressNotifications = _previousState;
            }
        }
    }
    
    /// <summary>
    /// Extended PropertyChangedEventArgs that includes old and new values.
    /// </summary>
    public class ObservablePropertyChangedEventArgs : PropertyChangedEventArgs
    {
        /// <summary>
        /// The previous value of the property.
        /// </summary>
        public object OldValue { get; }
        
        /// <summary>
        /// The new value of the property.
        /// </summary>
        public object NewValue { get; }
        
        /// <summary>
        /// Timestamp when the change occurred.
        /// </summary>
        public DateTime Timestamp { get; }
        
        public ObservablePropertyChangedEventArgs(string propertyName, object oldValue = null, object newValue = null)
            : base(propertyName)
        {
            OldValue = oldValue;
            NewValue = newValue;
            Timestamp = DateTime.UtcNow;
        }
        
        /// <summary>
        /// Gets the old value as a specific type.
        /// </summary>
        public T GetOldValue<T>() => OldValue is T value ? value : default;
        
        /// <summary>
        /// Gets the new value as a specific type.
        /// </summary>
        public T GetNewValue<T>() => NewValue is T value ? value : default;
    }
}
