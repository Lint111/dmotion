using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace DMotion.Authoring
{
    /// <summary>
    /// Represents a leaf state with its hierarchical path.
    /// Used for querying the visual grouping structure.
    /// </summary>
    public readonly struct StateWithPath
    {
        /// <summary>The leaf state asset (Single or LinearBlend)</summary>
        public readonly AnimationStateAsset State;

        /// <summary>Hierarchical path (e.g., "Combat/Attack/Slash")</summary>
        public readonly string Path;

        /// <summary>The immediate parent group, or null if at root level</summary>
        public readonly SubStateMachineStateAsset ParentGroup;

        public StateWithPath(AnimationStateAsset state, string path, SubStateMachineStateAsset parentGroup)
        {
            State = state;
            Path = path;
            ParentGroup = parentGroup;
        }
    }

    [CreateAssetMenu(menuName = StateMachineEditorConstants.DMotionPath + "/State Machine")]
    public class StateMachineAsset : ScriptableObject
    {
        public AnimationStateAsset DefaultState;
        public List<AnimationStateAsset> States = new();
        public List<AnimationParameterAsset> Parameters = new();

        [Header("Any State Transitions")]
        [Tooltip("Global transitions that can be taken from any state. Evaluated before regular state transitions.")]
        public List<StateOutTransition> AnyStateTransitions = new();

        public IEnumerable<AnimationClipAsset> Clips => States.SelectMany(s => s.Clips);
        public int ClipCount => States.Sum(s => s.ClipCount);

        #region Hierarchy Query APIs

        /// <summary>
        /// Gets all leaf states (Single/LinearBlend) with their hierarchical paths.
        /// SubStateMachines are traversed recursively - their nested states are included with full paths.
        /// </summary>
        public IEnumerable<StateWithPath> GetAllLeafStates()
        {
            return CollectLeafStatesRecursive(this, "", null);
        }

        /// <summary>
        /// Gets all leaf states belonging to a specific SubStateMachine group.
        /// Includes states in nested SubStateMachines within the group.
        /// </summary>
        public IEnumerable<AnimationStateAsset> GetStatesInGroup(SubStateMachineStateAsset group)
        {
            if (group == null || group.NestedStateMachine == null)
                yield break;

            foreach (var state in group.NestedStateMachine.States)
            {
                if (state is SubStateMachineStateAsset nestedGroup)
                {
                    foreach (var nestedState in GetStatesInGroup(nestedGroup))
                        yield return nestedState;
                }
                else
                {
                    yield return state;
                }
            }
        }

        /// <summary>
        /// Gets the hierarchical path for a specific state (e.g., "Combat/Attack/Slash").
        /// Returns null if the state is not found in this machine.
        /// </summary>
        public string GetStatePath(AnimationStateAsset state)
        {
            return FindStatePathRecursive(this, state, "");
        }

        /// <summary>
        /// Gets the parent SubStateMachine group for a state.
        /// Returns null if the state is at the root level or not found.
        /// </summary>
        public SubStateMachineStateAsset GetParentGroup(AnimationStateAsset state)
        {
            return FindParentGroupRecursive(this, state, null);
        }

        /// <summary>
        /// Finds states matching a path pattern.
        /// Supports wildcards: * matches any single segment, ** matches any number of segments.
        /// Examples: "Combat/*", "*/Attack", "**/Slash", "Combat/**"
        /// </summary>
        public IEnumerable<AnimationStateAsset> FindStatesByPath(string pattern)
        {
            var allStates = GetAllLeafStates().ToList();
            var regex = PatternToRegex(pattern);

            foreach (var stateWithPath in allStates)
            {
                if (regex.IsMatch(stateWithPath.Path))
                    yield return stateWithPath.State;
            }
        }

        /// <summary>
        /// Gets all SubStateMachine groups in the hierarchy (depth-first order).
        /// </summary>
        public IEnumerable<SubStateMachineStateAsset> GetAllGroups()
        {
            return CollectGroupsRecursive(this);
        }

        /// <summary>
        /// Gets direct child groups (SubStateMachines) at the root level.
        /// </summary>
        public IEnumerable<SubStateMachineStateAsset> GetRootGroups()
        {
            return States.OfType<SubStateMachineStateAsset>();
        }

        /// <summary>
        /// Gets the group hierarchy as a tree structure.
        /// Returns tuples of (group, depth) for tree visualization.
        /// </summary>
        public IEnumerable<(SubStateMachineStateAsset group, int depth)> GetGroupHierarchy()
        {
            return CollectGroupHierarchyRecursive(this, 0);
        }

        #endregion

        #region Private Helpers

        private static IEnumerable<StateWithPath> CollectLeafStatesRecursive(
            StateMachineAsset machine,
            string pathPrefix,
            SubStateMachineStateAsset currentParent)
        {
            foreach (var state in machine.States)
            {
                var statePath = string.IsNullOrEmpty(pathPrefix)
                    ? state.name
                    : $"{pathPrefix}/{state.name}";

                if (state is SubStateMachineStateAsset subMachine)
                {
                    if (subMachine.NestedStateMachine != null)
                    {
                        foreach (var nested in CollectLeafStatesRecursive(
                            subMachine.NestedStateMachine, statePath, subMachine))
                        {
                            yield return nested;
                        }
                    }
                }
                else
                {
                    yield return new StateWithPath(state, statePath, currentParent);
                }
            }
        }

        private static string FindStatePathRecursive(StateMachineAsset machine, AnimationStateAsset target, string pathPrefix)
        {
            foreach (var state in machine.States)
            {
                var statePath = string.IsNullOrEmpty(pathPrefix)
                    ? state.name
                    : $"{pathPrefix}/{state.name}";

                if (state == target)
                    return statePath;

                if (state is SubStateMachineStateAsset subMachine && subMachine.NestedStateMachine != null)
                {
                    var found = FindStatePathRecursive(subMachine.NestedStateMachine, target, statePath);
                    if (found != null)
                        return found;
                }
            }

            return null;
        }

        private static SubStateMachineStateAsset FindParentGroupRecursive(
            StateMachineAsset machine,
            AnimationStateAsset target,
            SubStateMachineStateAsset currentParent)
        {
            foreach (var state in machine.States)
            {
                if (state == target)
                    return currentParent;

                if (state is SubStateMachineStateAsset subMachine && subMachine.NestedStateMachine != null)
                {
                    var found = FindParentGroupRecursive(subMachine.NestedStateMachine, target, subMachine);
                    if (found != null || subMachine.NestedStateMachine.States.Contains(target))
                        return found ?? subMachine;
                }
            }

            return null;
        }

        private static IEnumerable<SubStateMachineStateAsset> CollectGroupsRecursive(StateMachineAsset machine)
        {
            foreach (var state in machine.States)
            {
                if (state is SubStateMachineStateAsset subMachine)
                {
                    yield return subMachine;

                    if (subMachine.NestedStateMachine != null)
                    {
                        foreach (var nested in CollectGroupsRecursive(subMachine.NestedStateMachine))
                            yield return nested;
                    }
                }
            }
        }

        private static IEnumerable<(SubStateMachineStateAsset, int)> CollectGroupHierarchyRecursive(
            StateMachineAsset machine, int depth)
        {
            foreach (var state in machine.States)
            {
                if (state is SubStateMachineStateAsset subMachine)
                {
                    yield return (subMachine, depth);

                    if (subMachine.NestedStateMachine != null)
                    {
                        foreach (var nested in CollectGroupHierarchyRecursive(subMachine.NestedStateMachine, depth + 1))
                            yield return nested;
                    }
                }
            }
        }

        private static Regex PatternToRegex(string pattern)
        {
            // Escape regex special chars except our wildcards
            var escaped = Regex.Escape(pattern);

            // Replace escaped wildcards with regex patterns
            // ** matches any path (including slashes)
            escaped = escaped.Replace(@"\*\*", ".*");
            // * matches single segment (no slashes)
            escaped = escaped.Replace(@"\*", @"[^/]*");

            return new Regex($"^{escaped}$", RegexOptions.IgnoreCase);
        }

        #endregion
    }
}