using Unity.Collections;
using Unity.Entities;
using UnityEngine.Assertions;

namespace DMotion
{
    /// <summary>
    /// Context for a single level in the state machine hierarchy.
    /// Each entity has a DynamicBuffer of these, tracking the full navigation stack.
    ///
    /// Design Philosophy: Relationship-based, not depth-limited.
    /// A state machine node connects to another node, which happens to be a sub-machine.
    /// We follow the connections naturally - no artificial depth constraints.
    /// </summary>
    [InternalBufferCapacity(4)] // Typical case: root + 1-2 sub-machines, avoids allocation in most cases
    public struct StateMachineContext : IBufferElementData
    {
        /// <summary>
        /// Current state index at this level of the hierarchy.
        /// Index into the States array of the StateMachineBlob at this level.
        /// </summary>
        internal short CurrentStateIndex;

        /// <summary>
        /// Index of the sub-machine node in the parent level.
        /// -1 if this is the root level.
        /// Used for exit transitions (to know which sub-machine node we're exiting from).
        /// </summary>
        internal short ParentSubMachineIndex;

        /// <summary>
        /// Hierarchy depth (0 = root, 1 = first sub-machine, etc.).
        /// Redundant with buffer position but useful for validation and debugging.
        /// </summary>
        internal byte Level;
    }

    /// <summary>
    /// Extension methods for StateMachineContext buffer (stack operations).
    /// </summary>
    public static class StateMachineContextExtensions
    {
        /// <summary>
        /// Gets the current context (top of stack).
        /// Buffer.Length - 1 is always the current level.
        /// </summary>
        public static ref StateMachineContext GetCurrent(this DynamicBuffer<StateMachineContext> buffer)
        {
            Assert.IsTrue(buffer.Length > 0, "Stack is empty");
            return ref buffer.ElementAt(buffer.Length - 1);
        }

        /// <summary>
        /// Gets the parent context (one level up).
        /// Only valid if buffer.Length > 1.
        /// </summary>
        public static ref StateMachineContext GetParent(this DynamicBuffer<StateMachineContext> buffer)
        {
            Assert.IsTrue(buffer.Length > 1, "Cannot get parent of root context");
            return ref buffer.ElementAt(buffer.Length - 2);
        }

        /// <summary>
        /// Gets the current depth in hierarchy (0 = root).
        /// </summary>
        public static int GetDepth(this DynamicBuffer<StateMachineContext> buffer)
        {
            return buffer.Length - 1;
        }

        /// <summary>
        /// Pushes a new context onto the stack (entering a sub-state machine).
        /// </summary>
        public static void Push(this DynamicBuffer<StateMachineContext> buffer, StateMachineContext context)
        {
            // No depth limit! Just push.
            // DynamicBuffer grows as needed.
            buffer.Add(context);
        }

        /// <summary>
        /// Pops the top context from the stack (exiting a sub-state machine).
        /// Returns the popped context.
        /// </summary>
        public static StateMachineContext Pop(this DynamicBuffer<StateMachineContext> buffer)
        {
            Assert.IsTrue(buffer.Length > 1, "Cannot pop root context");
            var context = buffer[buffer.Length - 1];
            buffer.RemoveAt(buffer.Length - 1);
            return context;
        }

        /// <summary>
        /// Validates the stack integrity (for debugging/testing).
        /// </summary>
        public static bool Validate(this DynamicBuffer<StateMachineContext> buffer)
        {
            if (buffer.Length == 0)
                return false;

            // Validate each context has correct level
            for (int i = 0; i < buffer.Length; i++)
            {
                var context = buffer[i];
                if (context.Level != i)
                    return false;
            }

            // Root must have ParentSubMachineIndex = -1
            if (buffer[0].ParentSubMachineIndex != -1)
                return false;

            return true;
        }

        /// <summary>
        /// Gets the full state path for debugging.
        /// Example: "Root.Combat.LightAttack"
        /// </summary>
        public static FixedString512Bytes GetStatePath(
            this DynamicBuffer<StateMachineContext> buffer,
            ref StateMachineBlob rootBlob)
        {
            var path = new FixedString512Bytes();

            for (int i = 0; i < buffer.Length; i++)
            {
                var context = buffer[i];

                if (i > 0)
                    path.Append('.');

                // Get state name at this level
                // (Implementation note: state name retrieval would need blob to store names)
                path.Append($"State{context.CurrentStateIndex}");
            }

            return path;
        }

        /// <summary>
        /// Gets a compact string representation of the stack (for debugging).
        /// Example: "D2:S0>S1>S3" = Depth 2, states 0, 1, 3
        /// </summary>
        public static FixedString128Bytes GetStackString(this DynamicBuffer<StateMachineContext> buffer)
        {
            var str = new FixedString128Bytes();
            str.Append($"D{buffer.Length - 1}:");

            for (int i = 0; i < buffer.Length; i++)
            {
                if (i > 0)
                    str.Append('>');

                str.Append($"S{buffer[i].CurrentStateIndex}");
            }

            return str;
        }
    }
}
