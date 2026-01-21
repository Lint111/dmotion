using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Serialization;

namespace DMotion.Authoring
{
    [Serializable]
    public class StateOutTransition
    {
        public AnimationStateAsset ToState;
        public bool HasEndTime;
        [Min(0)]
        public float EndTime;
        [Min(0), FormerlySerializedAs("NormalizedTransitionDuration")]
        public float TransitionDuration;
        public List<TransitionCondition> Conditions;
        
        /// <summary>
        /// Whether this transition can target the current state (self-transition).
        /// Only relevant for Any State transitions. If false, the transition won't
        /// fire when already in the destination state.
        /// </summary>
        [Tooltip("Allow this transition to fire when already in the target state")]
        public bool CanTransitionToSelf;
        
        /// <summary>
        /// Custom blend curve for the transition. X axis is normalized transition time (0-1),
        /// Y axis is the weight of the "From" state (1 at start, 0 at end).
        /// The "To" state weight is (1 - curve value).
        /// </summary>
        [Tooltip("Custom blend curve. X = transition time (0-1), Y = From state weight (1 to 0)")]
        public AnimationCurve BlendCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);

        public IEnumerable<BoolTransitionCondition> BoolTransitions =>
            Conditions.Where(c => c.Parameter is BoolParameterAsset).Select(c => c.AsBoolCondition);
        public IEnumerable<IntegerTransitionCondition> IntTransitions =>
            Conditions.Where(c => c.Parameter is IntParameterAsset).Select(c => c.AsIntegerCondition);

        public StateOutTransition(AnimationStateAsset to,
            float transitionDuration = 0.15f,
            List<BoolTransitionCondition> boolTransitions = null,
            List<IntegerTransitionCondition> intTransitions = null,
            bool canTransitionToSelf = false)
        {
            ToState = to;
            TransitionDuration = transitionDuration;
            CanTransitionToSelf = canTransitionToSelf;
            Conditions = new List<TransitionCondition>();
            if (boolTransitions != null)
            {
                Conditions.AddRange(boolTransitions.Select(b => b.ToGenericCondition()));
            }

            if (intTransitions != null)
            {
                Conditions.AddRange(intTransitions.Select(i => i.ToGenericCondition()));
            }
        }
    }
}