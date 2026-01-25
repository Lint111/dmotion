using Latios;
using Latios.Authoring;
using Latios.Kinemation;
using Latios.Kinemation.Authoring;
using Unity.Entities;

namespace DMotion.Authoring
{
    /// <summary>
    /// Baking bootstrap for DMotion. Ensures DMotion and Kinemation
    /// SmartBlobbers are registered during subscene baking.
    /// </summary>
    [UnityEngine.Scripting.Preserve]
    public class DMotionBakingBootstrapUnified : ICustomBakingBootstrap
    {
        public void InitializeBakingForAllWorlds(ref CustomBakingBootstrapContext context)
        {
            // Always install - Unity manages when this is called
            KinemationBakingBootstrap.InstallKinemation(ref context);
            DMotionBakingBootstrap.InstallDMotionBakersAndSystems(ref context);
        }
    }

    /// <summary>
    /// Runtime bootstrap for DMotion. Creates a LatiosWorld with Kinemation installed.
    /// This is the unified bootstrap - no other ICustomBootstrap should exist in the project.
    /// </summary>
    [UnityEngine.Scripting.Preserve]
    public class DMotionRuntimeBootstrap : ICustomBootstrap
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
