using Latios.Authoring;
using Unity.Entities;

namespace DMotion.Authoring
{
    public static class DMotionBakingBootstrap
    {
        /// <summary>
        /// Adds Kinemation bakers and baking systems into baking world and disables the Entities.Graphics's SkinnedMeshRenderer bakers
        /// </summary>
        /// <param name="context">The baking bootstrap context in which to install the DMotion baking systems</param>
        public static void InstallDMotionBakersAndSystems(ref CustomBakingBootstrapContext context)
        {
            context.bakingSystemTypesToInject.Add(TypeManager.GetSystemTypeIndex<AnimationStateMachineSmartBlobberSystem>());
            context.bakingSystemTypesToInject.Add(TypeManager.GetSystemTypeIndex<ClipEventsSmartBlobberSystem>());
        }
    }
}