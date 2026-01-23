using Latios;
using Latios.Authoring;
using Latios.Kinemation;
using Latios.Kinemation.Authoring;
using Unity.Entities;

namespace DMotion.Authoring
{
    /// <summary>
    /// Singleton baking bootstrap for DMotion. Ensures DMotion and Kinemation
    /// SmartBlobbers are registered during subscene baking.
    /// Uses singleton pattern to prevent multiple bootstrap conflicts.
    /// </summary>
    [UnityEngine.Scripting.Preserve]
    public class DMotionBakingBootstrapSingleton : ICustomBakingBootstrap
    {
        private static bool isInstalled;
        
        public void InitializeBakingForAllWorlds(ref CustomBakingBootstrapContext context)
        {
            if (isInstalled) return;
            isInstalled = true;
            
            KinemationBakingBootstrap.InstallKinemation(ref context);
            DMotionBakingBootstrap.InstallDMotionBakersAndSystems(ref context);
        }
    }

    /// <summary>
    /// Singleton runtime bootstrap for DMotion. Creates a LatiosWorld with
    /// Kinemation installed. Uses singleton pattern to prevent multiple bootstrap conflicts.
    /// </summary>
    [UnityEngine.Scripting.Preserve]
    public class DMotionRuntimeBootstrap : ICustomBootstrap
    {
        private static bool isInitialized;
        
        public bool Initialize(string defaultWorldName)
        {
            // Singleton check - only initialize once
            if (isInitialized)
            {
                UnityEngine.Debug.LogWarning("[DMotionRuntimeBootstrap] Already initialized, skipping duplicate initialization.");
                return true;
            }
            isInitialized = true;
            
            var world = new LatiosWorld(defaultWorldName);
            World.DefaultGameObjectInjectionWorld = world;

            var systems = DefaultWorldInitialization.GetAllSystemTypeIndices(WorldSystemFilterFlags.Default);

            BootstrapTools.InjectUnitySystems(systems, world, world.simulationSystemGroup);
            KinemationBootstrap.InstallKinemation(world);
            BootstrapTools.InjectUserSystems(systems, world, world.simulationSystemGroup);

            ScriptBehaviourUpdateOrder.AppendWorldToCurrentPlayerLoop(world);
            return true;
        }
        
        /// <summary>
        /// Resets the singleton state. Call this when exiting play mode in editor.
        /// </summary>
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState()
        {
            isInitialized = false;
        }
    }
}
