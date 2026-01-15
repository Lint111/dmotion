using NUnit.Framework;
using Unity.Entities;
using AssertionException = UnityEngine.Assertions.AssertionException;

namespace DMotion.Tests
{
    public class ClipSamplerBufferShould : ECSTestBase
    {
        private DynamicBuffer<ClipSampler> CreateSamplerBuffer()
        {
            var newEntity = manager.CreateEntity();
            return manager.AddBuffer<ClipSampler>(newEntity);
        }

        private static byte LastSamplerIndex = ClipSamplerUtils.MaxSamplersCount - 1;

        [Test]
        public void Add_And_Return_Id()
        {
            var samplers = CreateSamplerBuffer();
            Assert.Zero(samplers.Length);
            var id = samplers.AddWithId(new ClipSampler());
            Assert.Zero(id);
            Assert.AreEqual(samplers.Length, 1);
        }


        [Test]
        public void Add_And_Keep_Ids_Sorted()
        {
            var samplers = CreateSamplerBuffer();
            {
                var id = samplers.AddWithId(default);
                Assert.Zero(id);
                var s = samplers[0];
                s.Id = 12;
                samplers[0] = s;
            }
            {
                var id = samplers.AddWithId(default);
                Assert.AreEqual(id, 13);
                var s = samplers[1];
                s.Id = 40;
                samplers[1] = s;
            }
            {
                var id = samplers.AddWithId(default);
                Assert.AreEqual(id, 41);
                var s = samplers[2];
                s.Id = LastSamplerIndex;
                samplers[2] = s;
            }
            {
                var id = samplers.AddWithId(default);
                //we should loop back and return the next smallest id available
                Assert.AreEqual(id, 13);
            }

            for (var i = 1; i < samplers.Length; i++)
            {
                Assert.Greater(samplers[i].Id, samplers[i - 1].Id);
            }
        }

        [Test]
        public void Keep_Ids_Stable_When_Remove()
        {
            var samplers = CreateSamplerBuffer();
            {
                var id = samplers.AddWithId(default);
                Assert.Zero(id);
            }
            {
                var id = samplers.AddWithId(default);
                Assert.AreEqual(id, 1);
            }
            {
                var id = samplers.AddWithId(default);
                Assert.AreEqual(id, 2);
            }
            {
                var id = samplers.AddWithId(default);
                Assert.AreEqual(id, 3);
            }

            samplers.RemoveWithId(2);
            Assert.AreEqual(0, samplers[0].Id);
            Assert.AreEqual(1, samplers[1].Id);
            //We removed sampler with index and Id 2, id 3 should be stable
            Assert.AreEqual(3, samplers[2].Id);
        }

        [Test]
        public void Keep_Ids_Stable_When_RemoveRange()
        {
            var samplers = CreateSamplerBuffer();
            {
                var id = samplers.AddWithId(default);
                Assert.Zero(id);
            }
            {
                var id = samplers.AddWithId(default);
                Assert.AreEqual(id, 1);
            }
            {
                var id = samplers.AddWithId(default);
                Assert.AreEqual(id, 2);
            }
            {
                var id = samplers.AddWithId(default);
                Assert.AreEqual(id, 3);
            }

            samplers.RemoveRangeWithId(1, 2);
            Assert.AreEqual(0, samplers[0].Id);
            //We removed sampler 1 and 2, id 3 should be stable
            Assert.AreEqual(3, samplers[1].Id);
        }

        [Test]
        public void Limit_Reserve_Count()
        {
            var samplers = CreateSamplerBuffer();
            Assert.Throws<AssertionException>(() =>
            {
                samplers.TryFindIdAndInsertIndex(ClipSamplerUtils.MaxReserveCount + 1, out _, out _);
            });
        }

        [Test]
        public void Limit_Clip_SamplerCount()
        {
            var samplers = CreateSamplerBuffer();
            Assert.Throws<AssertionException>(() =>
            {
                samplers.Length += ClipSamplerUtils.MaxSamplersCount;
                samplers.TryFindIdAndInsertIndex(1, out _, out _);
            });
        }

        [Test]
        public void Return_Id_Zero_When_Empty()
        {
            var samplers = CreateSamplerBuffer();
            var success = samplers.TryFindIdAndInsertIndex(1, out var id, out var insertIndex);
            Assert.IsTrue(success);
            Assert.Zero(id);
            Assert.Zero(insertIndex);
        }

        [Test]
        public void IncrementId_When_Add()
        {
            var samplers = CreateSamplerBuffer();
            var id1 = samplers.AddWithId(default);
            var id2 = samplers.AddWithId(default);
            var id3 = samplers.AddWithId(default);
            Assert.AreEqual(id1, 0);
            Assert.AreEqual(id2, 1);
            Assert.AreEqual(id3, 2);
        }

        [Test]
        public void IncrementId_From_LastElement()
        {
            var samplers = CreateSamplerBuffer();
            var id1 = samplers.AddWithId(default);
            Assert.Zero(id1);
            var s = samplers[0];
            s.Id = 37;
            samplers[0] = s;
            var id2 = samplers.AddWithId(default);
            Assert.AreEqual(id2, 38);
        }


        [Test]
        public void LoopBackIndex_Length_One()
        {
            var samplers = CreateSamplerBuffer();
            var id1 = samplers.AddWithId(default);
            Assert.Zero(id1);
            var s = samplers[0];

            //change id to MaxSamplersCount to force loopback
            s.Id = LastSamplerIndex;
            samplers[0] = s;
            var success = samplers.TryFindIdAndInsertIndex(1, out var loopedId, out var insertIndex);
            Assert.IsTrue(success);
            Assert.Zero(loopedId, "Expected looped id to be zero in this case");
            Assert.AreEqual(insertIndex, 1);
        }

        [Test]
        public void LoopBackIndex_Length_GreatherThanOne()
        {
            var samplers = CreateSamplerBuffer();
            {
                var id = samplers.AddWithId(default);
                Assert.Zero(id);
            }
            {
                var id = samplers.AddWithId(default);
                Assert.AreEqual(id, 1);
            }
            {
                var id = samplers.AddWithId(default);
                Assert.AreEqual(id, 2);
            }

            var s = samplers[2];
            s.Id = LastSamplerIndex;
            samplers[2] = s;
            {
                var id = samplers.AddWithId(default);
                //we should loop back to first available id
                Assert.AreEqual(id, 2);
            }
        }

        [Test]
        public void ReserveRange()
        {
            var samplers = CreateSamplerBuffer();
            {
                var id = samplers.AddWithId(default);
                Assert.Zero(id);
            }
            {
                var success = samplers.TryFindIdAndInsertIndex(10, out var id, out var insertIndex);
                Assert.True(success);
                Assert.AreEqual(1, id);
            }
        }
        
        [Test]
        public void ReserveRange_Loopback()
        {
            var reserveCount = 10;
            var samplers = CreateSamplerBuffer();
            {
                var id = samplers.AddWithId(default);
                Assert.Zero(id);
                var s = samplers[0];
                s.Id = (byte) (LastSamplerIndex - reserveCount / 2);
                samplers[0] = s;
            }
            {
                var success = samplers.TryFindIdAndInsertIndex(10, out var id, out var insertIndex);
                Assert.True(success);
                Assert.AreEqual(0, id);
            }
        }
        
        [Test]
        public void ReserveRange_BetweenElements_LoopBack()
        {
            const byte reserveCount = 10;
            var samplers = CreateSamplerBuffer();
            {
                var id = samplers.AddWithId(default);
                Assert.Zero(id);
            }
            {
                var id = samplers.AddWithId(default);
                Assert.AreEqual(1, id);
                var s = samplers[1];
                s.Id = 7;
                samplers[1] = s;
            }
            {
                var id = samplers.AddWithId(default);
                Assert.AreEqual(8, id);
                var s = samplers[2];
                s.Id += reserveCount + 1;
            }
            {
                var id = samplers.AddWithId(default);
                Assert.AreEqual(9, id);
                var s = samplers[3];
                s.Id = (byte) (LastSamplerIndex - reserveCount / 2);
                samplers[3] = s;
            }
            {
                var success = samplers.TryFindIdAndInsertIndex(10, out var id, out var insertIndex);
                Assert.True(success);
                Assert.AreEqual(9, id);
                Assert.AreEqual(insertIndex, 3);
            }
        }

        #region Error Path Tests (M10)

        [Test]
        public void IdToIndex_ReturnsNegativeOne_WhenBufferEmpty()
        {
            var samplers = CreateSamplerBuffer();
            var index = samplers.IdToIndex(0);
            Assert.AreEqual(-1, index, "IdToIndex should return -1 for empty buffer");
        }

        [Test]
        public void IdToIndex_ReturnsNegativeOne_WhenIdNotFound()
        {
            var samplers = CreateSamplerBuffer();
            samplers.AddWithId(default); // ID 0
            samplers.AddWithId(default); // ID 1
            samplers.AddWithId(default); // ID 2

            // Search for non-existent IDs
            Assert.AreEqual(-1, samplers.IdToIndex(99), "Should return -1 for non-existent ID");
            Assert.AreEqual(-1, samplers.IdToIndex(50), "Should return -1 for non-existent ID in middle");
        }

        [Test]
        public void IdToIndex_FindsId_WithBinarySearch_WhenFastPathFails()
        {
            var samplers = CreateSamplerBuffer();

            // Create a fragmented ID space where IDs don't match indices
            samplers.AddWithId(default); // ID 0 at index 0
            var s0 = samplers[0];
            s0.Id = 5;
            samplers[0] = s0;

            samplers.AddWithId(default); // ID 6 at index 1
            var s1 = samplers[1];
            s1.Id = 10;
            samplers[1] = s1;

            samplers.AddWithId(default); // ID 11 at index 2
            var s2 = samplers[2];
            s2.Id = 20;
            samplers[2] = s2;

            // Fast path fails (id != index), binary search kicks in
            Assert.AreEqual(0, samplers.IdToIndex(5), "Should find ID 5 at index 0 via binary search");
            Assert.AreEqual(1, samplers.IdToIndex(10), "Should find ID 10 at index 1 via binary search");
            Assert.AreEqual(2, samplers.IdToIndex(20), "Should find ID 20 at index 2 via binary search");
            Assert.AreEqual(-1, samplers.IdToIndex(15), "Should return -1 for gap in ID space");
        }

        [Test]
        public void RemoveWithId_ReturnsFalse_WhenBufferEmpty()
        {
            var samplers = CreateSamplerBuffer();
            var removed = samplers.RemoveWithId(0);
            Assert.IsFalse(removed, "RemoveWithId should return false for empty buffer");
        }

        [Test]
        public void RemoveWithId_ReturnsFalse_WhenIdNotFound()
        {
            var samplers = CreateSamplerBuffer();
            samplers.AddWithId(default); // ID 0
            samplers.AddWithId(default); // ID 1

            var removed = samplers.RemoveWithId(99);
            Assert.IsFalse(removed, "RemoveWithId should return false for non-existent ID");
            Assert.AreEqual(2, samplers.Length, "Buffer length should be unchanged");
        }

        [Test]
        public void RemoveRangeWithId_ReturnsFalse_WhenIdNotFound()
        {
            var samplers = CreateSamplerBuffer();
            samplers.AddWithId(default); // ID 0

            var removed = samplers.RemoveRangeWithId(50, 3);
            Assert.IsFalse(removed, "RemoveRangeWithId should return false for non-existent ID");
            Assert.AreEqual(1, samplers.Length, "Buffer length should be unchanged");
        }

        [Test]
        public void TryGetWithId_ReturnsFalse_WhenBufferEmpty()
        {
            var samplers = CreateSamplerBuffer();
            var found = samplers.TryGetWithId(0, out var element);
            Assert.IsFalse(found, "TryGetWithId should return false for empty buffer");
            Assert.AreEqual(default(ClipSampler), element, "Element should be default when not found");
        }

        [Test]
        public void TryGetWithId_ReturnsFalse_WhenIdNotFound()
        {
            var samplers = CreateSamplerBuffer();
            samplers.AddWithId(default); // ID 0

            var found = samplers.TryGetWithId(99, out var element);
            Assert.IsFalse(found, "TryGetWithId should return false for non-existent ID");
            Assert.AreEqual(default(ClipSampler), element, "Element should be default when not found");
        }

        [Test]
        public void TryGetWithId_ReturnsTrue_WhenIdExists()
        {
            var samplers = CreateSamplerBuffer();
            var sampler = new ClipSampler { Weight = 0.75f };
            samplers.AddWithId(sampler);

            var found = samplers.TryGetWithId(0, out var element);
            Assert.IsTrue(found, "TryGetWithId should return true for existing ID");
            Assert.AreEqual(0.75f, element.Weight, "Element should have correct data");
        }

        [Test]
        public void ExistsWithId_ReturnsFalse_WhenBufferEmpty()
        {
            var samplers = CreateSamplerBuffer();
            Assert.IsFalse(samplers.ExistsWithId(0), "ExistsWithId should return false for empty buffer");
        }

        [Test]
        public void ExistsWithId_ReturnsFalse_WhenIdNotFound()
        {
            var samplers = CreateSamplerBuffer();
            samplers.AddWithId(default);
            Assert.IsFalse(samplers.ExistsWithId(99), "ExistsWithId should return false for non-existent ID");
        }

        [Test]
        public void ExistsWithId_ReturnsTrue_WhenIdExists()
        {
            var samplers = CreateSamplerBuffer();
            samplers.AddWithId(default);
            Assert.IsTrue(samplers.ExistsWithId(0), "ExistsWithId should return true for existing ID");
        }

        [Test]
        public void GetWithId_ThrowsAssertion_WhenIdNotFound()
        {
            var samplers = CreateSamplerBuffer();
            samplers.AddWithId(default);

            Assert.Throws<AssertionException>(() =>
            {
                samplers.GetWithId(99);
            }, "GetWithId should throw AssertionException for non-existent ID");
        }

        [Test]
        public void TryFindIdAndInsertIndex_ReturnsFalse_WhenIdSpaceFragmented()
        {
            var samplers = CreateSamplerBuffer();

            // Fill the buffer with alternating IDs to fragment the space
            // Each element takes 2 IDs worth of space, leaving no room for contiguous allocation
            for (byte i = 0; i < 60; i++)
            {
                samplers.AddWithId(default);
                var s = samplers[i];
                s.Id = (byte)(i * 2); // IDs: 0, 2, 4, 6, ... 118
                samplers[i] = s;
            }

            // Try to find 3 contiguous IDs - should fail due to fragmentation
            var success = samplers.TryFindIdAndInsertIndex(3, out var id, out var insertIndex);
            Assert.IsFalse(success, "Should return false when ID space is too fragmented");
            Assert.AreEqual(-1, insertIndex, "Insert index should be -1 on failure");
        }

        #endregion
    }
}