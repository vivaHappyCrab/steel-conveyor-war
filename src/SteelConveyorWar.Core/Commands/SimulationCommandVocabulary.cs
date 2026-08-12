namespace SteelConveyorWar.Core.Commands;

/// <summary>
/// M02: capability registry for the command wire — advertised kinds must equal serializable and
/// applicable kinds. Obsolete always-false commands stay in the enum for numeric stability but are
/// filtered from bot-facing vocabulary and rejected on deserialize.
/// </summary>
public static class SimulationCommandVocabulary
{
    /// <summary>
    /// Kinds a host/bot may advertise and submit. Excludes obsolete wire numbers that never apply.
    /// </summary>
    public static IReadOnlyList<SimulationCommandKind> AdvertisedKinds { get; } =
        Enum.GetValues<SimulationCommandKind>()
            .Where(kind => !IsObsolete(kind))
            .OrderBy(kind => (int)kind)
            .ToArray();

    /// <summary>
    /// <see cref="SimulationCommandKind.AssignFactoryBastion"/> is retained for wire-number stability
    /// but is never applicable (handler always returns false) and is rejected on deserialize.
    /// </summary>
    public static bool IsObsolete(SimulationCommandKind kind)
#pragma warning disable CS0618 // intentional: obsolete kind remains in the enum for wire stability
        => kind == SimulationCommandKind.AssignFactoryBastion;
#pragma warning restore CS0618
}
