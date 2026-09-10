using SteelConveyorWar.Core.Replay;

namespace SteelConveyorWar.Hosting;

/// <summary>
/// Host I/O for durable <see cref="ReplayDocument"/> files. Core stays parse-only.
/// </summary>
public static class ReplayFileStore
{
    public const string FileExtension = ".scwreplay";
    public const string DefaultFolderName = "replays";

    public static string DefaultDirectory(string? baseDirectory = null) =>
        Path.Combine(baseDirectory ?? AppContext.BaseDirectory, DefaultFolderName);

    public static string DefaultFileName(ReplayDocument document, DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(document);
        return $"{timestamp.UtcDateTime:yyyyMMdd-HHmmss}-seed{document.Seed}-t{document.DurationTicks}{FileExtension}";
    }

    public static string DefaultPath(ReplayDocument document, DateTimeOffset timestamp, string? baseDirectory = null) =>
        Path.Combine(DefaultDirectory(baseDirectory), DefaultFileName(document, timestamp));

    public static void Save(string path, ReplayDocument document)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(document);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, ReplayDocumentSerializer.Serialize(document));
    }

    public static ReplayDocument Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Replay file not found.", path);
        }

        return ReplayDocumentSerializer.Deserialize(File.ReadAllText(path));
    }
}
