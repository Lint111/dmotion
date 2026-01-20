namespace DMotion.Authoring
{
    /// <summary>
    /// Indicates the current state of rig binding resolution for a StateMachineAsset.
    /// </summary>
    public enum RigBindingStatus
    {
        /// <summary>
        /// No rig binding has been resolved yet.
        /// The asset needs rig information to be set either manually or through conversion.
        /// </summary>
        Unresolved = 0,

        /// <summary>
        /// A rig has been successfully bound to this asset.
        /// The BoundArmatureData field contains valid armature information.
        /// </summary>
        Resolved = 1,

        /// <summary>
        /// The user explicitly chose not to bind a rig to this asset.
        /// This prevents re-prompting during conversion workflows.
        /// </summary>
        UserOptedOut = 2
    }

    /// <summary>
    /// Indicates how the rig binding was determined for a StateMachineAsset.
    /// Used for diagnostics and to understand the binding's origin.
    /// </summary>
    public enum RigBindingSource
    {
        /// <summary>
        /// No binding source - either unresolved or opted out.
        /// </summary>
        None = 0,

        /// <summary>
        /// Binding came from an Animator component's avatar during Mechination conversion.
        /// This is the highest confidence source.
        /// </summary>
        FromAnimatorAvatar = 1,

        /// <summary>
        /// Binding was inferred from animation clips' source FBX ModelImporter.sourceAvatar.
        /// Used when converting AnimatorController without an Animator context.
        /// </summary>
        FromImporterSourceAvatar = 2,

        /// <summary>
        /// Binding was manually selected by the user in the editor.
        /// </summary>
        UserSelected = 3
    }
}
