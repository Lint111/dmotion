using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DMotion; // For ISuppressable

namespace DMotion.Editor
{
    /// <summary>
    /// Delegate for property changed events.
    /// </summary>
    public delegate void PropertyChangedEventHandler(object sender, ObservablePropertyChangedEventArgs e);

    /// <summary>
    /// Base class for objects that support property change notifications.
    /// Provides automatic PropertyChanged events when properties are set via SetProperty.
    /// Implements ISuppressable for batched notification support.
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
    ///     Debug.Log($"{e.PropertyName}: {e.OldValue} -> {e.NewValue}");
    /// };
    ///
    /// // Setting property automatically fires event
    /// myState.Time = 0.5f;
    /// 
    /// // Batch multiple changes - only fires consolidated events at end
    /// using (myState.SuppressNotifications())
    /// {
    ///     myState.Time = 0.5f;
    ///     myState.Speed = 1.0f;
    ///     myState.Time = 0.75f; // Same property changed twice - only notified once
    /// }
    /// </code>
    /// </remarks>
    public abstract class ObservableObject : ISuppressable
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
            if (_suppressionDepth > 0)
            {
                // Queue notification - track first old value and latest new value
                if (!_pendingNotifications.TryGetValue(propertyName, out var pending))
                {
                    _pendingNotifications[propertyName] = (oldValue, newValue);
                }
                else
                {
                    // Keep original old value, update to latest new value
                    _pendingNotifications[propertyName] = (pending.OldValue, newValue);
                }
                return;
            }

            PropertyChanged?.Invoke(this, new ObservablePropertyChangedEventArgs(propertyName, oldValue, newValue));
        }
        
        /// <summary>
        /// Raises PropertyChanged for multiple properties at once.
        /// </summary>
        protected void OnPropertiesChanged(params string[] propertyNames)
        {
            foreach (var name in propertyNames)
            {
                OnPropertyChanged(name);
            }
        }
        
        /// <summary>
        /// Whether notifications are currently suppressed.
        /// </summary>
        public bool IsSuppressed => _suppressionDepth > 0;
        
        /// <summary>
        /// Suppresses property change notifications during a batch update.
        /// </summary>
        /// <param name="flushOnEnd">
        /// If true (default): fires consolidated events when outermost scope ends.
        /// If false: discards all queued events (silent suppression).
        /// </param>
        public IDisposable SuppressNotifications(bool flushOnEnd = true)
        {
            return new NotificationSuppressor(this, flushOnEnd);
        }
        
        /// <summary>
        /// Called when suppression ends. Override to customize flush behavior.
        /// </summary>
        protected virtual void OnFlushPendingNotifications()
        {
            // Default: fire all pending notifications
            foreach (var kvp in _pendingNotifications)
            {
                PropertyChanged?.Invoke(this, new ObservablePropertyChangedEventArgs(
                    kvp.Key, kvp.Value.OldValue, kvp.Value.NewValue));
            }
        }
        
        private int _suppressionDepth;
        private bool _flushOnEnd = true;
        private readonly Dictionary<string, (object OldValue, object NewValue)> _pendingNotifications = new();
        
        private class NotificationSuppressor : IDisposable
        {
            private readonly ObservableObject _owner;
            private readonly bool _wasOutermost;
            
            public NotificationSuppressor(ObservableObject owner, bool flushOnEnd)
            {
                _owner = owner;
                _wasOutermost = owner._suppressionDepth == 0;
                
                // Outermost scope sets the flush behavior
                if (_wasOutermost)
                {
                    owner._flushOnEnd = flushOnEnd;
                }
                
                owner._suppressionDepth++;
            }
            
            public void Dispose()
            {
                _owner._suppressionDepth--;
                
                // Only process when we exit the outermost suppression scope
                if (_owner._suppressionDepth == 0 && _owner._pendingNotifications.Count > 0)
                {
                    if (_owner._flushOnEnd)
                    {
                        _owner.OnFlushPendingNotifications();
                    }
                    _owner._pendingNotifications.Clear();
                    _owner._flushOnEnd = true; // Reset for next use
                }
            }
        }
    }
    
    /// <summary>
    /// Event args for property changed events, includes old and new values.
    /// </summary>
    public class ObservablePropertyChangedEventArgs : EventArgs
    {
        /// <summary>
        /// The name of the property that changed.
        /// </summary>
        public string PropertyName { get; }

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
        {
            PropertyName = propertyName;
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
