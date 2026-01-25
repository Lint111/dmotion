namespace DMotion.Authoring
{
    /// <summary>
    /// Controls validation for layer count.
    /// Inline buffer capacity is 4 at compile time.
    /// Exceeding the mode's limit triggers a warning but still works (uses heap).
    /// </summary>
    public enum LayerCapacityMode
    {
        /// <summary>Optimized for typical use (base, upper body, additive, face).</summary>
        Compact = 4,
        /// <summary>For complex characters with many layers.</summary>
        Extended = 8,
        /// <summary>No validation - accept any layer count.</summary>
        Unlimited = 0
    }
}
