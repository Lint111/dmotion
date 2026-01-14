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

    /// <summary>
    /// Editor bootstrap for test environment.
    /// </summary>
    [UnityEngine.Scripting.Preserve]
    public class TestEditorBootstrap : ICustomEditorBootstrap
    {
        public World Initialize(string editorWorldName)
        {
            var world = new LatiosWorld(editorWorldName);
            World.DefaultGameObjectInjectionWorld = world;

            var systems = DefaultWorldInitialization.GetAllSystemTypeIndices(WorldSystemFilterFlags.Default, true);
            BootstrapTools.InjectUnitySystems(systems, world, world.simulationSystemGroup);

            KinemationBootstrap.InstallKinemation(world);

            return world;
        }
    }

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
