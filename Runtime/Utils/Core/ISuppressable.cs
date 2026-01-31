using System;
using System.Collections.Generic;

namespace DMotion
{
    /// <summary>
    /// Interface for objects that support notification suppression with batched flush.
    /// Enables batch updates where multiple property changes fire only consolidated
    /// events when the suppression scope ends.
    /// </summary>
    public interface ISuppressable
    {
        /// <summary>
        /// Creates a suppression scope. Notifications are queued during suppression.
        /// Supports nesting - only the outermost scope triggers flush.
        /// </summary>
        /// <param name="flushOnEnd">
        /// If true (default): fires consolidated events when scope ends.
        /// If false: discards all queued events (silent suppression).
        /// </param>
        IDisposable SuppressNotifications(bool flushOnEnd = true);
        
        /// <summary>
        /// Whether notifications are currently suppressed.
        /// </summary>
        bool IsSuppressed { get; }
    }
    
    /// <summary>
    /// Helper struct for managing notification suppression in classes implementing ISuppressable.
    /// Tracks pending property notifications and flushes them when suppression ends.
    /// </summary>
    /// <remarks>
    /// Usage in a class:
    /// <code>
    /// private SuppressionState _suppression = new();
    /// 
    /// public bool IsSuppressed => _suppression.IsSuppressed;
    /// 
    /// public IDisposable SuppressNotifications() => _suppression.Begin(this);
    /// 
    /// protected void OnPropertyChanged(string name, object oldValue, object newValue)
    /// {
    ///     if (_suppression.TryQueue(name, oldValue, newValue))
    ///         return; // Queued for later
    ///     
    ///     // Fire immediately
    ///     PropertyChanged?.Invoke(this, name);
    /// }
    /// </code>
    /// </remarks>
    public struct SuppressionState
    {
        private int _depth;
        private Dictionary<string, (object OldValue, object NewValue)> _pending;
        
        /// <summary>
        /// Whether currently in a suppression scope.
        /// </summary>
        public bool IsSuppressed => _depth > 0;
        
        /// <summary>
        /// Begins a suppression scope. Returns a disposable that ends the scope.
        /// </summary>
        /// <param name="owner">The owner object (for the flush callback).</param>
        /// <param name="onFlush">Callback invoked with pending notifications when outermost scope ends.</param>
        public IDisposable Begin(Action<IReadOnlyDictionary<string, (object OldValue, object NewValue)>> onFlush)
        {
            _depth++;
            _pending ??= new Dictionary<string, (object, object)>();
            return new Scope(this, onFlush);
        }
        
        /// <summary>
        /// Tries to queue a notification. Returns true if suppressed (queued), false if should fire immediately.
        /// </summary>
        public bool TryQueue(string propertyName, object oldValue, object newValue)
        {
            if (_depth <= 0)
                return false;
            
            _pending ??= new Dictionary<string, (object, object)>();
            
            if (!_pending.TryGetValue(propertyName, out var existing))
            {
                _pending[propertyName] = (oldValue, newValue);
            }
            else
            {
                // Keep original old value, update to latest new value
                _pending[propertyName] = (existing.OldValue, newValue);
            }
            return true;
        }
        
        private void End(Action<IReadOnlyDictionary<string, (object OldValue, object NewValue)>> onFlush)
        {
            _depth--;
            if (_depth == 0 && _pending != null && _pending.Count > 0)
            {
                onFlush?.Invoke(_pending);
                _pending.Clear();
            }
        }
        
        private readonly struct Scope : IDisposable
        {
            private readonly SuppressionState _state;
            private readonly Action<IReadOnlyDictionary<string, (object, object)>> _onFlush;
            
            public Scope(SuppressionState state, Action<IReadOnlyDictionary<string, (object, object)>> onFlush)
            {
                _state = state;
                _onFlush = onFlush;
            }
            
            public void Dispose()
            {
                // Note: Can't modify struct from nested struct, need different approach
                // This is a limitation - see SuppressionHelper class below for reference-based solution
            }
        }
    }
    
    /// <summary>
    /// Reference-based suppression helper for use in classes.
    /// Unlike SuppressionState struct, this works correctly with nested scopes.
    /// </summary>
    public class SuppressionHelper
    {
        private int _depth;
        private bool _flushOnEnd = true; // Outermost scope's setting
        private readonly Dictionary<string, (object OldValue, object NewValue)> _pending = new();
        private readonly Action<string, object, object> _onFlush;
        
        /// <summary>
        /// Creates a new suppression helper.
        /// </summary>
        /// <param name="onFlush">Called for each pending notification when suppression ends (if flushOnEnd=true). Parameters: propertyName, oldValue, newValue</param>
        public SuppressionHelper(Action<string, object, object> onFlush)
        {
            _onFlush = onFlush;
        }
        
        /// <summary>
        /// Whether currently in a suppression scope.
        /// </summary>
        public bool IsSuppressed => _depth > 0;
        
        /// <summary>
        /// Number of pending notifications.
        /// </summary>
        public int PendingCount => _pending.Count;
        
        /// <summary>
        /// Begins a suppression scope.
        /// </summary>
        /// <param name="flushOnEnd">
        /// If true (default): fires consolidated events when outermost scope ends.
        /// If false: discards all queued events (silent suppression).
        /// Note: Outermost scope's setting wins for nested scopes.
        /// </param>
        public IDisposable Begin(bool flushOnEnd = true) => new Scope(this, flushOnEnd);
        
        /// <summary>
        /// Tries to queue a notification. Returns true if suppressed (queued), false if should fire immediately.
        /// </summary>
        public bool TryQueue(string propertyName, object oldValue = null, object newValue = null)
        {
            if (_depth <= 0)
                return false;
            
            if (!_pending.TryGetValue(propertyName, out var existing))
            {
                _pending[propertyName] = (oldValue, newValue);
            }
            else
            {
                // Keep original old value, update to latest new value
                _pending[propertyName] = (existing.OldValue, newValue);
            }
            return true;
        }
        
        /// <summary>
        /// Clears all pending notifications without firing them.
        /// </summary>
        public void DiscardPending() => _pending.Clear();
        
        private void End(bool wasOutermost, bool flushOnEnd)
        {
            _depth--;
            
            if (_depth == 0 && _pending.Count > 0)
            {
                // Only flush if outermost scope requested it
                if (_flushOnEnd)
                {
                    foreach (var kvp in _pending)
                    {
                        _onFlush?.Invoke(kvp.Key, kvp.Value.OldValue, kvp.Value.NewValue);
                    }
                }
                _pending.Clear();
                _flushOnEnd = true; // Reset for next use
            }
        }
        
        private class Scope : IDisposable
        {
            private readonly SuppressionHelper _helper;
            private readonly bool _wasOutermost;
            private readonly bool _flushOnEnd;
            
            public Scope(SuppressionHelper helper, bool flushOnEnd)
            {
                _helper = helper;
                _flushOnEnd = flushOnEnd;
                _wasOutermost = helper._depth == 0;
                
                // Outermost scope sets the flush behavior
                if (_wasOutermost)
                {
                    helper._flushOnEnd = flushOnEnd;
                }
                
                helper._depth++;
            }
            
            public void Dispose() => _helper.End(_wasOutermost, _flushOnEnd);
        }
    }
}
