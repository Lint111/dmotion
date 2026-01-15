using System;
using System.Text;
using UnityEngine;

namespace DMotion.Authoring.UnityControllerBridge
{
    /// <summary>
    /// Project-level asset that converts a Unity AnimatorController to DMotion StateMachineAsset.
    /// One bridge asset per controller - can be referenced by many entities.
    /// Automatically detects controller changes and triggers re-conversion.
    /// </summary>
    [CreateAssetMenu(fileName = "ControllerBridge", menuName = "DMotion/Unity Controller Bridge")]
    public class UnityControllerBridgeAsset : ScriptableObject
    {
        [Header("Source")]
        [Tooltip("The Unity AnimatorController to convert")]
        [SerializeField] private UnityEditor.Animations.AnimatorController _sourceController;

        [Header("Output (Generated - Read Only)")]
        [Tooltip("The generated DMotion StateMachineAsset")]
        [SerializeField] private StateMachineAsset _generatedStateMachine;

        [Header("Status")]
        [SerializeField] private bool _isDirty = true;
        [SerializeField] private string _lastConversionTime = "Never";

        [SerializeField, HideInInspector]
        private string _cachedControllerHash;

        #region Events

        /// <summary>
        /// Event fired when a bridge asset is registered (OnEnable).
        /// </summary>
        public static event Action<UnityControllerBridgeAsset> OnBridgeRegistered;

        /// <summary>
        /// Event fired when a bridge asset is unregistered (OnDisable).
        /// </summary>
        public static event Action<UnityControllerBridgeAsset> OnBridgeUnregistered;

        /// <summary>
        /// Event fired when a bridge detects it's dirty (controller changed).
        /// </summary>
        public static event Action<UnityControllerBridgeAsset> OnBridgeDirty;

        #endregion

        #region Public Properties

        /// <summary>
        /// The source Unity AnimatorController.
        /// </summary>
        public UnityEditor.Animations.AnimatorController SourceController => _sourceController;

        /// <summary>
        /// The generated DMotion StateMachineAsset (read-only, regenerated on changes).
        /// </summary>
        public StateMachineAsset GeneratedStateMachine => _generatedStateMachine;

        /// <summary>
        /// Whether this bridge needs re-conversion.
        /// </summary>
        public bool IsDirty => _isDirty;

        /// <summary>
        /// Time of last successful conversion.
        /// </summary>
        public string LastConversionTime => _lastConversionTime;

        /// <summary>
        /// Unique identifier for this bridge (based on asset GUID).
        /// </summary>
        public string BridgeId
        {
            get
            {
#if UNITY_EDITOR
                string assetPath = UnityEditor.AssetDatabase.GetAssetPath(this);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    return UnityEditor.AssetDatabase.AssetPathToGUID(assetPath);
                }
#endif
                return GetInstanceID().ToString();
            }
        }

        #endregion

        #region Lifecycle

        private void OnEnable()
        {
#if UNITY_EDITOR
            // Register with the bridge registry
            OnBridgeRegistered?.Invoke(this);

            // Calculate initial hash if not set
            if (string.IsNullOrEmpty(_cachedControllerHash) && _sourceController != null)
            {
                _cachedControllerHash = CalculateControllerHash();
            }
#endif
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            // Unregister from the bridge registry
            OnBridgeUnregistered?.Invoke(this);
#endif
        }

        private void OnValidate()
        {
#if UNITY_EDITOR
            // Check if source controller changed
            if (_sourceController != null)
            {
                CheckForChanges();
            }
#endif
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Checks if the source controller has changed since last conversion.
        /// If changes detected, marks bridge as dirty and fires OnBridgeDirty event.
        /// </summary>
        /// <returns>True if changes detected.</returns>
        public bool CheckForChanges()
        {
            if (_sourceController == null)
            {
                return false;
            }

            string currentHash = CalculateControllerHash();
            bool changed = currentHash != _cachedControllerHash;

            if (changed && !_isDirty)
            {
                _isDirty = true;
                OnBridgeDirty?.Invoke(this);
#if UNITY_EDITOR
                UnityEditor.EditorUtility.SetDirty(this);
#endif
            }

            return changed;
        }

        /// <summary>
        /// Marks the bridge as clean (after successful conversion).
        /// Updates cached hash and last conversion time.
        /// </summary>
        public void MarkClean()
        {
            _isDirty = false;
            _cachedControllerHash = CalculateControllerHash();
            _lastConversionTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        /// <summary>
        /// Forces the bridge to be marked dirty.
        /// </summary>
        public void MarkDirty()
        {
            if (!_isDirty)
            {
                _isDirty = true;
                OnBridgeDirty?.Invoke(this);

#if UNITY_EDITOR
                UnityEditor.EditorUtility.SetDirty(this);
#endif
            }
        }

        /// <summary>
        /// Sets the generated StateMachineAsset (called by conversion system).
        /// </summary>
        internal void SetGeneratedStateMachine(StateMachineAsset stateMachine)
        {
            _generatedStateMachine = stateMachine;

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Calculates a hash of the controller's structure for dirty detection.
        /// Hashes: parameters, states, transitions, blend trees.
        /// </summary>
        private string CalculateControllerHash()
        {
            if (_sourceController == null)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();

            try
            {
                // Hash parameters
                foreach (var param in _sourceController.parameters)
                {
                    sb.Append($"P:{param.name}:{param.type}:{param.defaultFloat}:{param.defaultInt}:{param.defaultBool};");
                }

                // Hash layers (we only support base layer for now, but include count)
                sb.Append($"Layers:{_sourceController.layers.Length};");

                if (_sourceController.layers.Length > 0)
                {
                    var baseLayer = _sourceController.layers[0];
                    var stateMachine = baseLayer.stateMachine;

                    // Hash default state
                    if (stateMachine.defaultState != null)
                    {
                        sb.Append($"Default:{stateMachine.defaultState.name};");
                    }

                    // Hash states
                    foreach (var childState in stateMachine.states)
                    {
                        var state = childState.state;
                        sb.Append($"S:{state.name}:{state.speed}:{state.cycleOffset}:");
                        sb.Append($"{state.speedParameterActive}:{state.speedParameter}:");

                        // Hash motion
                        if (state.motion != null)
                        {
                            sb.Append($"M:{state.motion.GetType().Name}:{state.motion.name}:");

                            // If blend tree, hash structure
                            if (state.motion is UnityEditor.Animations.BlendTree blendTree)
                            {
                                HashBlendTree(blendTree, sb);
                            }
                        }

                        // Hash transitions
                        sb.Append($"T:{state.transitions.Length}:");
                        foreach (var transition in state.transitions)
                        {
                            HashTransition(transition, sb);
                        }

                        sb.Append(";");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UnityControllerBridge] Error calculating hash for {name}: {ex.Message}");
                return Guid.NewGuid().ToString(); // Return unique hash to trigger rebuild
            }

            return Hash128.Compute(sb.ToString()).ToString();
        }

        private void HashBlendTree(UnityEditor.Animations.BlendTree blendTree, StringBuilder sb)
        {
            sb.Append($"BT:{blendTree.blendType}:{blendTree.blendParameter}:");
            sb.Append($"{blendTree.children.Length}:");

            foreach (var child in blendTree.children)
            {
                sb.Append($"C:{child.threshold}:{child.timeScale}:");
                if (child.motion != null)
                {
                    sb.Append($"{child.motion.name}:");
                }
            }
        }

        private void HashTransition(UnityEditor.Animations.AnimatorStateTransition transition, StringBuilder sb)
        {
            sb.Append($"TR:");
            if (transition.destinationState != null)
            {
                sb.Append($"{transition.destinationState.name}:");
            }
            sb.Append($"{transition.duration}:{transition.offset}:");
            sb.Append($"{transition.hasExitTime}:{transition.exitTime}:");
            sb.Append($"{transition.conditions.Length}:");

            foreach (var condition in transition.conditions)
            {
                sb.Append($"{condition.parameter}:{condition.mode}:{condition.threshold}:");
            }
        }

        #endregion
    }
}
