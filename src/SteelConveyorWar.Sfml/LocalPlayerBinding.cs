using SteelConveyorWar.Core;

namespace SteelConveyorWar.Sfml;

/// <summary>
/// Resolves which sim seat the SFML host binds as the local player (input/FoW/selection).
/// Default remains P1; pass <c>--local-player 2</c> for P2 hotseat/experiments.
/// </summary>
public static class LocalPlayerBinding
{
    public const string CliFlag = "--local-player";

    public static PlayerId Resolve(IReadOnlyList<string> args, PlayerId? fallback = null)
    {
        var defaultId = fallback ?? SfmlDisplayOptions.DefaultLocalPlayerId;
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (!args[i].Equals(CliFlag, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!int.TryParse(args[i + 1], out var value) || value < 1)
            {
                throw new ArgumentException(
                    $"Invalid {CliFlag} value '{args[i + 1]}'. Expected a positive integer player id (e.g. 1 or 2).");
            }

            return new PlayerId(value);
        }

        return defaultId;
    }
}
