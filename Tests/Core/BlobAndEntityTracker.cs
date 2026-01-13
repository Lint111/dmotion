using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

namespace DMotion.Tests
{
    /// <summary>
    /// Centralized tracker for BlobAssetReferences and Entities that need cleanup during test teardown.
    /// Used by all test base classes to prevent memory leaks and ensure proper resource disposal.
    /// </summary>
    public class BlobAndEntityTracker
    {
        private readonly List<Action> blobDisposers = new List<Action>();
        private readonly List<Entity> trackedEntities = new List<Entity>();
        private readonly string ownerName;

        public BlobAndEntityTracker(string ownerName = "Test")
        {
            this.ownerName = ownerName;
        }

        /// <summary>
        /// Tracks a BlobAssetReference for disposal during cleanup.
        /// Use this for any blobs created with Allocator.Persistent in tests.
        /// </summary>
        public void TrackBlob<T>(BlobAssetReference<T> blob) where T : unmanaged
        {
            if (blob.IsCreated)
            {
                blobDisposers.Add(() =>
                {
                    if (blob.IsCreated)
                    {
                        blob.Dispose();
                    }
                });
            }
        }

        /// <summary>
        /// Tracks an entity for cleanup during teardown.
        /// </summary>
        public void TrackEntity(Entity entity)
        {
            if (entity != Entity.Null && !trackedEntities.Contains(entity))
            {
                trackedEntities.Add(entity);
            }
        }

        /// <summary>
        /// Disposes all tracked blobs and destroys all tracked entities.
        /// Call this during test teardown.
        /// </summary>
        /// <param name="manager">EntityManager to use for destroying entities. Can be null if no entities need cleanup.</param>
        public void Cleanup(EntityManager? manager = null)
        {
            // Dispose tracked blobs first
            foreach (var disposer in blobDisposers)
            {
                try
                {
                    disposer();
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[{ownerName}] Failed to dispose blob: {e.Message}");
                }
            }
            blobDisposers.Clear();

            // Clean up tracked entities
            if (manager.HasValue)
            {
                var mgr = manager.Value;
                foreach (var entity in trackedEntities)
                {
                    if (mgr.Exists(entity))
                    {
                        mgr.DestroyEntity(entity);
                    }
                }
            }
            trackedEntities.Clear();
        }

        /// <summary>
        /// Gets the count of tracked blobs.
        /// </summary>
        public int TrackedBlobCount => blobDisposers.Count;

        /// <summary>
        /// Gets the count of tracked entities.
        /// </summary>
        public int TrackedEntityCount => trackedEntities.Count;
    }
}
