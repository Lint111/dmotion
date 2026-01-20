using DMotion.Authoring;
using Latios;
using Latios.Authoring;
using Latios.Kinemation;
using Latios.Kinemation.Authoring;
using Unity.Entities;

namespace DMotion.Samples.Common
{
    [UnityEngine.Scripting.Preserve]
    public class LatiosBakingBootstrap : ICustomBakingBootstrap
    {
        public void InitializeBakingForAllWorlds(ref CustomBakingBootstrapContext context)
        {
            KinemationBakingBootstrap.InstallKinemation(ref context);
            DMotionBakingBootstrap.InstallDMotionBakersAndSystems(ref context);
        }
    }

    // NOTE: ICustomEditorBootstrap intentionally omitted.
    // Installing Kinemation into the default editor world causes GenerateBrgDrawCommandsSystem
    // to run every frame with no rendering consumer, resulting in JobTempAlloc leak warnings.
    // ECS preview features should create an isolated world on-demand when needed.
    // See DMotion Documentation/Features/EcsPreviewAndRigBinding.md Phase 0.

    [UnityEngine.Scripting.Preserve]
    public class LatiosBootstrap : ICustomBootstrap
    {
        public bool Initialize(string defaultWorldName)
        {
            var world = new LatiosWorld(defaultWorldName);
            World.DefaultGameObjectInjectionWorld = world;

            // Get all systems
            var systems = DefaultWorldInitialization.GetAllSystemTypeIndices(WorldSystemFilterFlags.Default);

            // Inject Unity systems first (required for Kinemation)
            BootstrapTools.InjectUnitySystems(systems, world, world.simulationSystemGroup);

            // Install Kinemation (requires EntitiesGraphicsSystem to exist)
            KinemationBootstrap.InstallKinemation(world);

            // Inject remaining user systems
            BootstrapTools.InjectUserSystems(systems, world, world.simulationSystemGroup);

            ScriptBehaviourUpdateOrder.AppendWorldToCurrentPlayerLoop(world);
            return true;
        }
    }
}
