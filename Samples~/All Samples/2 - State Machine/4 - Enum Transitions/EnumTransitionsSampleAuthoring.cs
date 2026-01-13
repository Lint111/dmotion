using Unity.Entities;
using UnityEngine;

namespace DMotion.Samples.EnumTransitions
{
    public class EnumTransitionsSampleAuthoring : MonoBehaviour
    {
    }

    public class EnumTransitionsSampleBaker : Baker<EnumTransitionsSampleAuthoring>
    {
        public override void Bake(EnumTransitionsSampleAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<EnumTransitionsSample>(entity);
        }
    }
}
