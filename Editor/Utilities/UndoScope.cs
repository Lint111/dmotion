using System;
using UnityEditor;
using UnityEngine;

namespace DMotion.Editor
{
    /// <summary>
    /// Provides a disposable scope for grouping Undo operations.
    /// Usage:
    /// <code>
    /// using (UndoScope.Begin("Operation Name", targetObject))
    /// {
    ///     // Modify targetObject...
    /// } // Auto-collapses undo operations
    /// </code>
    /// </summary>
    internal readonly struct UndoScope : IDisposable
    {
        private readonly int _undoGroup;
        
        private UndoScope(int undoGroup)
        {
            _undoGroup = undoGroup;
        }
        
        /// <summary>
        /// Begins an undo scope with the given operation name.
        /// Records the target object for undo.
        /// </summary>
        public static UndoScope Begin(string operationName, UnityEngine.Object target)
        {
            Undo.SetCurrentGroupName(operationName);
            var group = Undo.GetCurrentGroup();
            Undo.RecordObject(target, operationName);
            return new UndoScope(group);
        }
        
        /// <summary>
        /// Begins an undo scope with the given operation name.
        /// Records multiple target objects for undo.
        /// </summary>
        public static UndoScope Begin(string operationName, params UnityEngine.Object[] targets)
        {
            Undo.SetCurrentGroupName(operationName);
            var group = Undo.GetCurrentGroup();
            foreach (var target in targets)
            {
                if (target != null)
                    Undo.RecordObject(target, operationName);
            }
            return new UndoScope(group);
        }
        
        /// <summary>
        /// Begins an undo scope without recording any objects initially.
        /// Use Undo.RecordObject manually within the scope.
        /// </summary>
        public static UndoScope Begin(string operationName)
        {
            Undo.SetCurrentGroupName(operationName);
            var group = Undo.GetCurrentGroup();
            return new UndoScope(group);
        }
        
        public void Dispose()
        {
            Undo.CollapseUndoOperations(_undoGroup);
        }
    }
}
