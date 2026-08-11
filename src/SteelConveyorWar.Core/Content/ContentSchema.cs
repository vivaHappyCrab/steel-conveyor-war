namespace SteelConveyorWar.Core;

/// <summary>
/// R34: single schema-version policy shared by every content loader. Previously each loader
/// hard-coded its own <c>schemaVersion &lt; 1</c> guard, so there was no upper bound and no
/// consistent message. Centralizing it means an unknown/incompatible format fails fast the same
/// way everywhere instead of being silently (partially) applied.
/// </summary>
public static class ContentSchema
{
    /// <summary>Highest schema version this build understands. Bump when a breaking format change ships.</summary>
    public const int CurrentVersion = 1;

    /// <summary>Lowest schema version this build still accepts.</summary>
    public const int MinimumSupportedVersion = 1;

    /// <summary>
    /// Throws when <paramref name="schemaVersion"/> is outside the supported range. Callers pass a
    /// human-readable <paramref name="catalogName"/> (e.g. "game", "build-costs") for the message.
    /// </summary>
    public static void RequireSupportedVersion(string catalogName, int schemaVersion)
    {
        if (schemaVersion < MinimumSupportedVersion || schemaVersion > CurrentVersion)
        {
            throw new InvalidOperationException(
                $"Unsupported {catalogName} schemaVersion '{schemaVersion}'. This build supports " +
                $"versions [{MinimumSupportedVersion}..{CurrentVersion}]. Update the content or the game.");
        }
    }
}
