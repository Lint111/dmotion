using Unity.Entities;
using UnityEngine;

namespace DMotion.Samples.BlendTree1D
{
    public class BlendTree1DSampleAuthoring : MonoBehaviour
    {
    }

    public class BlendTree1DSampleBaker : Baker<BlendTree1DSampleAuthoring>
    {
        public override void Bake(BlendTree1DSampleAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<BlendTree1DSample>(entity);
        }
    }
}
