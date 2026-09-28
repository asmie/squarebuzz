namespace Squarebuzz.Core.Generation;

/// <summary>Identifies the algorithm used to rebuild generated saves.</summary>
/// <remarks>
/// Increment Current whenever a change alters the board produced by any seed, including
/// random-call ordering. Incompatible generated saves are discarded at startup.
/// Authored saves use their own puzzle revisions.
/// </remarks>
public static class GeneratorVersion
{
    /// <summary>Version 4 adds asymmetric details after mirroring. Earlier versions changed density and empty-line repair.</summary>
    public const int Current = 4;

    /// <summary>Unversioned generated saves cannot be reproduced reliably and are treated as incompatible.</summary>
    public const int Unknown = 0;
}
