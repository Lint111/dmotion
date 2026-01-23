// NOTE: Bootstrap classes have been moved to DMotion.Runtime.
// See DMotion.Authoring.DMotionBakingBootstrapSingleton and DMotionRuntimeBootstrap.
// These singleton bootstraps prevent multiple bootstrap conflicts when both
// DMotion tests and samples are present in the same project.
//
// The bootstraps use singleton pattern to ensure only one initialization occurs,
// even if multiple ICustomBootstrap implementations exist.

namespace DMotion.Tests
{
    // This file is kept for backwards compatibility and documentation.
    // All bootstrap functionality is now in Runtime/Authoring/DMotionBootstrap.cs
}
