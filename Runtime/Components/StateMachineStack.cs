using Unity.Collections;
using Unity.Entities;
using UnityEngine.Assertions;

namespace DMotion
{
    /// <summary>
    /// Tracks the current position in the state machine hierarchy.
    /// Each entity has its own stack for navigating nested sub-state machines.
    /// Max depth: 8 levels (more than sufficient for typical use cases).
    /// </summary>
    public struct StateMachineStack : IComponentData
    {
        /// <summary>
        /// Stack of state machine contexts (max depth: 8).
        /// Index [0] is root, Index [Depth] is current level.
        /// Using FixedList64Bytes allows up to 8 contexts (8 bytes each).
        /// </summary>
        internal FixedList64Bytes<StateMachineContext> Contexts;

        /// <summary>
        /// Current depth in hierarchy (0 = root, 1 = first sub-machine, etc.).
        /// </summary>
        internal byte Depth;

        /// <summary>
        /// Maximum allowed depth (from blob, for validation).
        /// Set during initialization from StateMachineBlob.MaxNestingDepth.
        /// </summary>
        internal byte MaxDepth;

        /// <summary>
        /// Gets the current context (top of stack).
        /// </summary>
        internal ref StateMachineContext Current
        {
            get
            {
                Assert.IsTrue(Depth < Contexts.Length, "Stack depth out of bounds");
                return ref Contexts.ElementAt(Depth);
            }
        }

        /// <summary>
        /// Gets the parent context (one level up).
        /// Only valid if Depth > 0.
        /// </summary>
        internal ref StateMachineContext Parent
        {
            get
            {
                Assert.IsTrue(Depth > 0, "Cannot get parent of root context");
                return ref Contexts.ElementAt(Depth - 1);
            }
        }

        /// <summary>
        /// Pushes a new context onto the stack (entering a sub-state machine).
        /// </summary>
        internal void Push(StateMachineContext context)
        {
            Assert.IsTrue(Depth + 1 < MaxDepth, $"State machine nesting too deep: {Depth + 1} (max {MaxDepth})");
            Depth++;
            Contexts.Add(context);
        }

        /// <summary>
        /// Pops the top context from the stack (exiting a sub-state machine).
        /// Returns the popped context.
        /// </summary>
        internal StateMachineContext Pop()
        {
            Assert.IsTrue(Depth > 0, "Cannot pop from empty stack (at root level)");
            var context = Contexts[Depth];
            Contexts.RemoveAt(Depth);
            Depth--;
            return context;
        }

        /// <summary>
        /// Validates the stack integrity (for debugging/testing).
        /// </summary>
        internal bool Validate()
        {
            if (Depth > MaxDepth)
                return false;

            if (Depth >= Contexts.Length)
                return false;

            // Validate each context has correct level
            for (int i = 0; i <= Depth; i++)
            {
                var context = Contexts[i];
                if (context.Level != i)
                    return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Context for a single level in the state machine hierarchy.
    /// Tracks which state is active at this level and how we got here.
    /// </summary>
    internal struct StateMachineContext
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
        /// Redundant with stack position but useful for validation and debugging.
        /// </summary>
        internal byte Level;
    }

    /// <summary>
    /// Extension methods for StateMachineStack (debugging utilities).
    /// </summary>
    public static class StateMachineStackExtensions
    {
        /// <summary>
        /// Gets the full state path for debugging.
        /// Example: "Root.Combat.LightAttack"
        /// </summary>
        public static FixedString512Bytes GetStatePath(
            this ref StateMachineStack stack,
            ref StateMachineBlob rootBlob)
        {
            var path = new FixedString512Bytes();

            for (int i = 0; i <= stack.Depth; i++)
            {
                var context = stack.Contexts[i];

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
        public static FixedString128Bytes GetStackString(this ref StateMachineStack stack)
        {
            var str = new FixedString128Bytes();
            str.Append($"D{stack.Depth}:");

            for (int i = 0; i <= stack.Depth; i++)
            {
                if (i > 0)
                    str.Append('>');

                str.Append($"S{stack.Contexts[i].CurrentStateIndex}");
            }

            return str;
        }
    }
}
