using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Unity.Mathematics;
using UnityEngine;

namespace DMotion.Authoring
{
    /// <summary>
    /// Represents an animation layer within a multi-layer state machine.
    /// Unlike SubStateMachineStateAsset (visual-only, flattened at bake),
    /// LayerStateAsset creates a separate runtime blob for pose blending.
    ///
    /// Each layer runs its own state machine independently.
    /// Results are blended at the pose level based on weight and blend mode.
    ///
    /// Supports UI binding through Action&lt;string&gt; PropertyChanged event (Unity-native pattern).
    /// - Persistent properties (Weight, BlendMode) trigger SetDirty
    /// - Runtime preview state ([NonSerialized]) for animation playback
    /// </summary>
    [CreateAssetMenu(fileName = "NewLayer", menuName = "DMotion/Animation Layer")]
    public class LayerStateAsset : AnimationStateAsset, INestedStateMachineContainer, ILayerBoneMask
    {
        #region Persistent State (Serialized)

        [Tooltip("The state machine for this layer")]
        [SerializeField]
        private StateMachineAsset nestedStateMachine;

        [Tooltip("Layer weight (0 = no influence, 1 = full influence)")]
        [Range(0f, 1f)]
        [SerializeField]
        private float weight = 1f;

        [Tooltip("How this layer blends with layers below it")]
        [SerializeField]
        private LayerBlendMode blendMode = LayerBlendMode.Override;

        [Tooltip("Avatar mask defining which bones this layer affects. Leave empty for full body.")]
        [SerializeField]
        private AvatarMask avatarMask;

        #endregion

        #region Runtime Preview State (Not Serialized)

        // Layer metadata
        [NonSerialized] private int _layerIndex;
        [NonSerialized] private bool _isEnabled = true;

        // Preview state (composition)
        [NonSerialized] private AnimationStateAsset _selectedState;
        [NonSerialized] private AnimationStateAsset _transitionFrom;
        [NonSerialized] private AnimationStateAsset _transitionTo;
        [NonSerialized] private float _normalizedTime;
        [NonSerialized] private float _transitionProgress;
        [NonSerialized] private float2 _blendPosition;
        [NonSerialized] private bool _isPlaying;

        #endregion

        #region Property Change Notification

        /// <summary>
        /// Fired when any property changes. Parameter is the property name.
        /// Unity-native pattern avoiding System.ComponentModel dependency.
        /// </summary>
        public event Action<LayerStateAsset, string> PropertyChanged;

        /// <summary>
        /// Sets a property value and raises PropertyChanged if the value changed.
        /// </summary>
        protected bool SetProperty<T>(ref T field, T value, bool markDirty = false, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;

            #if UNITY_EDITOR
            if (markDirty)
            {
                UnityEditor.EditorUtility.SetDirty(this);
            }
            #endif

            OnPropertyChanged(propertyName);
            return true;
        }

        /// <summary>
        /// Raises PropertyChanged for the specified property.
        /// </summary>
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, propertyName);
        }

        #endregion

        #region Persistent Properties (with SetDirty)

        /// <summary>
        /// Layer weight (0-1).
        /// Layer 0 is always weight 1.0 (base opaque layer) and cannot be modified.
        /// </summary>
        public float Weight
        {
            get => weight;
            set
            {
                // Base layer weight is locked to 1.0
                if (IsBaseLayer)
                {
                    SetProperty(ref weight, 1f, markDirty: true);
                    return;
                }

                var clampedValue = Mathf.Clamp01(value);
                SetProperty(ref weight, clampedValue, markDirty: true);
            }
        }

        /// <summary>
        /// Layer blend mode.
        /// </summary>
        public LayerBlendMode BlendMode
        {
            get => blendMode;
            set => SetProperty(ref blendMode, value, markDirty: true);
        }

        #endregion

        #region Runtime Preview Properties (no SetDirty)

        /// <summary>
        /// Index of this layer in the state machine.
        /// </summary>
        public int LayerIndex
        {
            get => _layerIndex;
            set => SetProperty(ref _layerIndex, value);
        }

        /// <summary>
        /// Whether this is the base layer (Layer 0).
        /// Base layer is always weight 1.0 and cannot be modified.
        /// </summary>
        public bool IsBaseLayer => _layerIndex == 0;

        /// <summary>
        /// Whether the weight of this layer can be modified.
        /// Base layer (Layer 0) weight is locked to 1.0.
        /// </summary>
        public bool CanModifyWeight => !IsBaseLayer;

        /// <summary>
        /// The effective weight value for this layer.
        /// Base layer always returns 1.0, other layers return their actual weight.
        /// </summary>
        public float EffectiveWeight => IsBaseLayer ? 1f : weight;

        /// <summary>
        /// Whether this layer is enabled in preview.
        /// </summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        /// <summary>
        /// Currently selected state for preview.
        /// </summary>
        public AnimationStateAsset SelectedState
        {
            get => _selectedState;
            set => SetProperty(ref _selectedState, value);
        }

        /// <summary>
        /// Source state for transition preview.
        /// </summary>
        public AnimationStateAsset TransitionFrom
        {
            get => _transitionFrom;
            set => SetProperty(ref _transitionFrom, value);
        }

        /// <summary>
        /// Target state for transition preview.
        /// </summary>
        public AnimationStateAsset TransitionTo
        {
            get => _transitionTo;
            set => SetProperty(ref _transitionTo, value);
        }

        /// <summary>
        /// Normalized time (0-1) for preview playback.
        /// </summary>
        public float NormalizedTime
        {
            get => _normalizedTime;
            set => SetProperty(ref _normalizedTime, Mathf.Clamp01(value));
        }

        /// <summary>
        /// Transition progress (0-1).
        /// </summary>
        public float TransitionProgress
        {
            get => _transitionProgress;
            set => SetProperty(ref _transitionProgress, Mathf.Clamp01(value));
        }

        /// <summary>
        /// Blend position for blend trees.
        /// </summary>
        public float2 BlendPosition
        {
            get => _blendPosition;
            set => SetProperty(ref _blendPosition, value);
        }

        /// <summary>
        /// Whether preview playback is active.
        /// </summary>
        public bool IsPlaying
        {
            get => _isPlaying;
            set => SetProperty(ref _isPlaying, value);
        }

        /// <summary>
        /// Whether in transition mode.
        /// </summary>
        public bool IsTransitionMode => _transitionFrom != null && _transitionTo != null;

        /// <summary>
        /// Whether this layer has an assigned state or transition.
        /// </summary>
        public bool IsAssigned => _selectedState != null || IsTransitionMode;

        #endregion

        #region AnimationStateAsset Implementation
        
        /// <summary>
        /// Layer type - not used in runtime blob (layers have separate blobs).
        /// </summary>
        public override StateType Type => StateType.Layer;
        
        /// <summary>
        /// Total clip count across all states in this layer's state machine.
        /// </summary>
        public override int ClipCount => nestedStateMachine != null ? nestedStateMachine.ClipCount : 0;
        
        /// <summary>
        /// All clips across all states in this layer's state machine.
        /// </summary>
        public override IEnumerable<AnimationClipAsset> Clips => 
            nestedStateMachine != null ? nestedStateMachine.Clips : Enumerable.Empty<AnimationClipAsset>();
        
        #endregion
        
        /// <summary>
        /// The nested state machine containing this layer's states and transitions.
        /// </summary>
        public StateMachineAsset NestedStateMachine
        {
            get => nestedStateMachine;
            set => nestedStateMachine = value;
        }
        
        /// <summary>
        /// Whether this layer has a valid state machine.
        /// </summary>
        public bool HasValidStateMachine => nestedStateMachine != null;
        
        #region ILayerBoneMask Implementation
        
        // Internal wrapper for interface implementation
        private AvatarMaskBoneMask maskWrapper;
        
        private AvatarMaskBoneMask MaskWrapper
        {
            get
            {
                if (maskWrapper == null || maskWrapper.AvatarMask != avatarMask)
                    maskWrapper = new AvatarMaskBoneMask(avatarMask);
                return maskWrapper;
            }
        }
        
        /// <summary>
        /// Whether this layer has a bone mask defined.
        /// </summary>
        public bool HasMask => avatarMask != null;
        
        /// <summary>
        /// Whether this layer affects the full body (no mask).
        /// </summary>
        public bool IsFullBody => avatarMask == null;
        
        /// <summary>
        /// The AvatarMask for this layer, or null for full body.
        /// </summary>
        public AvatarMask AvatarMask
        {
            get => avatarMask;
            set => avatarMask = value;
        }
        
        /// <inheritdoc/>
        public IEnumerable<string> GetIncludedBonePaths(Transform skeletonRoot)
        {
            return MaskWrapper.GetIncludedBonePaths(skeletonRoot);
        }
        
        /// <inheritdoc/>
        public bool IsBoneIncluded(string bonePath)
        {
            return MaskWrapper.IsBoneIncluded(bonePath);
        }
        
        #endregion
        
        /// <summary>
        /// Gets all states in this layer's state machine.
        /// </summary>
        public IEnumerable<AnimationStateAsset> GetLayerStates()
        {
            if (nestedStateMachine == null)
                yield break;
                
            foreach (var state in nestedStateMachine.States)
            {
                yield return state;
            }
        }

        void OnValidate()
        {
            // Ensure name is set
            if (string.IsNullOrEmpty(name))
            {
                name = "New Layer";
            }
            
            // Create nested state machine if missing
            if (nestedStateMachine == null)
            {
                #if UNITY_EDITOR
                CreateNestedStateMachine();
                #endif
            }
        }
        
        #if UNITY_EDITOR
        private void CreateNestedStateMachine()
        {
            nestedStateMachine = CreateInstance<StateMachineAsset>();
            nestedStateMachine.name = $"{name}_StateMachine";
            
            // Add as sub-asset
            var path = UnityEditor.AssetDatabase.GetAssetPath(this);
            if (!string.IsNullOrEmpty(path))
            {
                UnityEditor.AssetDatabase.AddObjectToAsset(nestedStateMachine, this);
                UnityEditor.AssetDatabase.SaveAssets();
            }
        }
        #endif
    }
}
