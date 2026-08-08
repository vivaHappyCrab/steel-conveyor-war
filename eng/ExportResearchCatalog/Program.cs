using SteelConveyorWar.Core;

var output = args.Length > 0
    ? args[0]
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "config", "research.json"));

Directory.CreateDirectory(Path.GetDirectoryName(output)!);
var json = ResearchContentLoader.Serialize(MvpResearchCatalog.CreateEmbedded());
File.WriteAllText(output, json);
Console.WriteLine($"Wrote {output}");
