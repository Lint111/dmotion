using System.Collections.Generic;
using System.Linq;
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
    /// </summary>
    [CreateAssetMenu(fileName = "NewLayer", menuName = "DMotion/Animation Layer")]
    public class LayerStateAsset : AnimationStateAsset, INestedStateMachineContainer
    {
        [Tooltip("The state machine for this layer")]
        [SerializeField]
        private StateMachineAsset nestedStateMachine;
        
        [Tooltip("Layer weight (0 = no influence, 1 = full influence)")]
        [Range(0f, 1f)]
        public float Weight = 1f;
        
        [Tooltip("How this layer blends with layers below it")]
        public LayerBlendMode BlendMode = LayerBlendMode.Override;
        
        // Phase 1D: Avatar mask for per-bone filtering
        // public AvatarMask AvatarMask;
        
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
