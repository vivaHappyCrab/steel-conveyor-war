using SteelConveyorWar.Core.Replay;

namespace SteelConveyorWar.Hosting;

/// <summary>
/// Shared Client/Headless flags for durable replay record and playback.
/// </summary>
public sealed record ReplayHostOptions(string? ReplayPath, string? RecordPath, bool NoRecord)
{
    public const string ReplayFlag = "--replay";
    public const string RecordFlag = "--record";
    public const string NoRecordFlag = "--no-record";

    public static ReplayHostOptions None { get; } = new(null, null, false);

    /// <summary>Auto-write a replay when this is not a watch session and recording is not disabled.</summary>
    public bool ShouldRecord => !NoRecord && ReplayPath is null;

    public static ReplayHostOptions Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        string? replayPath = null;
        string? recordPath = null;
        var noRecord = false;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (IsFlag(arg, ReplayFlag))
            {
                replayPath = ReadPath(args, i, ReplayFlag);
                i++;
                continue;
            }

            if (IsFlag(arg, RecordFlag))
            {
                recordPath = ReadPath(args, i, RecordFlag);
                i++;
                continue;
            }

            if (IsFlag(arg, NoRecordFlag))
            {
                noRecord = true;
            }
        }

        return new ReplayHostOptions(replayPath, recordPath, noRecord);
    }

    public string ResolveRecordPath(ReplayDocument document, DateTimeOffset? timestamp = null, string? baseDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!string.IsNullOrWhiteSpace(RecordPath))
        {
            return RecordPath;
        }

        return ReplayFileStore.DefaultPath(document, timestamp ?? DateTimeOffset.UtcNow, baseDirectory);
    }

    private static string ReadPath(IReadOnlyList<string> args, int flagIndex, string flag)
    {
        if (flagIndex >= args.Count - 1 || args[flagIndex + 1].StartsWith('-'))
        {
            throw new ArgumentException($"{flag} requires a file path.");
        }

        var path = args[flagIndex + 1];
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException($"{flag} requires a file path.");
        }

        return path;
    }

    private static bool IsFlag(string arg, string flag) =>
        string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase);
}
