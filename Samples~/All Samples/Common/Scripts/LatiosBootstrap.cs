using DMotion.Authoring;
using Latios;
using Latios.Authoring;
using Latios.Kinemation;
using Latios.Kinemation.Authoring;
using Unity.Collections;
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

    [UnityEngine.Scripting.Preserve]
    public class LatiosEditorBootstrap : ICustomEditorBootstrap
    {
        public World Initialize(string editorWorldName)
        {
            var world = new LatiosWorld(editorWorldName);
            World.DefaultGameObjectInjectionWorld = world;

            // Get all systems and inject Unity systems first (required for Kinemation)
            var systems = DefaultWorldInitialization.GetAllSystemTypeIndices(WorldSystemFilterFlags.Default, true);
            BootstrapTools.InjectUnitySystems(systems, world, world.simulationSystemGroup);

            // Now install Kinemation (requires EntitiesGraphicsSystem to exist)
            KinemationBootstrap.InstallKinemation(world);

            return world;
        }
    }

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
