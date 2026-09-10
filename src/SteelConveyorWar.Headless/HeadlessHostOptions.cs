using SteelConveyorWar.Hosting;

namespace SteelConveyorWar.Headless;

/// <summary>
/// CLI options for the Core-only headless host loop.
/// </summary>
public sealed record HeadlessHostOptions(
    int Ticks,
    int PlayerId,
    ReplayHostOptions Replay)
{
    public const int DefaultTicks = 90;

    public static HeadlessHostOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var ticks = DefaultTicks;
        var playerId = 1;
        var replay = ReplayHostOptions.Parse(args);

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (IsFlag(arg, "--ticks") && TryReadInt(args, i + 1, out var parsedTicks))
            {
                ticks = parsedTicks;
                i++;
                continue;
            }

            if (IsFlag(arg, "--player") && TryReadInt(args, i + 1, out var parsedPlayer))
            {
                playerId = parsedPlayer;
                i++;
            }
        }

        if (ticks < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(Ticks), ticks, "Tick count must be at least 1.");
        }

        if (playerId < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(PlayerId), playerId, "Player id must be at least 1.");
        }

        return new HeadlessHostOptions(ticks, playerId, replay);
    }

    private static bool IsFlag(string arg, string flag) =>
        string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase);

    private static bool TryReadInt(string[] args, int index, out int value)
    {
        value = 0;
        return index < args.Length && int.TryParse(args[index], out value);
    }
}
