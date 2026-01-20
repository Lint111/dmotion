using DMotion.Authoring;
using Latios;
using Latios.Authoring;
using Latios.Kinemation;
using Latios.Kinemation.Authoring;
using Unity.Entities;

namespace DMotion.Tests
{
    /// <summary>
    /// Baking bootstrap for test environment. Ensures DMotion and Kinemation
    /// SmartBlobbers are registered during subscene baking.
    /// 
    /// Note: If DMotion samples are also imported, there will be a harmless warning
    /// about multiple bootstraps. Both bootstraps are functionally identical.
    /// </summary>
    [UnityEngine.Scripting.Preserve]
    public class TestBakingBootstrap : ICustomBakingBootstrap
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
    // Tests that need ECS preview should create an isolated world on-demand.
    // See Documentation/Features/EcsPreviewAndRigBinding.md Phase 0.

    /// <summary>
    /// Runtime bootstrap for test environment.
    /// </summary>
    [UnityEngine.Scripting.Preserve]
    public class TestBootstrap : ICustomBootstrap
    {
        public bool Initialize(string defaultWorldName)
        {
            var world = new LatiosWorld(defaultWorldName);
            World.DefaultGameObjectInjectionWorld = world;

            var systems = DefaultWorldInitialization.GetAllSystemTypeIndices(WorldSystemFilterFlags.Default);

            BootstrapTools.InjectUnitySystems(systems, world, world.simulationSystemGroup);
            KinemationBootstrap.InstallKinemation(world);
            BootstrapTools.InjectUserSystems(systems, world, world.simulationSystemGroup);

            ScriptBehaviourUpdateOrder.AppendWorldToCurrentPlayerLoop(world);
            return true;
        }
    }
}
