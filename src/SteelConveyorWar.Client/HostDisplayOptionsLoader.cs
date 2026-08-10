using System.Text.Json;
using SteelConveyorWar.Sfml;

namespace SteelConveyorWar.Client;

/// <summary>
/// Host-only parser for the presentation <c>window</c> block in <c>game.json</c>.
/// Core <see cref="SteelConveyorWar.Core.GameSettingsLoader"/> ignores this section.
/// </summary>
public static class HostDisplayOptionsLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static SfmlDisplayOptions Parse(string gameJson, int ticksPerSecond)
    {
        var dto = JsonSerializer.Deserialize<GameConfigWindowDto>(gameJson, JsonOptions)
            ?? throw new InvalidOperationException("Game settings JSON deserialized to null.");

        var window = dto.Window;
        var width = window?.Width is > 0 ? (uint)window.Width : SfmlDisplayOptions.Default.Width;
        var height = window?.Height is > 0 ? (uint)window.Height : SfmlDisplayOptions.Default.Height;
        var title = string.IsNullOrWhiteSpace(window?.Title)
            ? (string.IsNullOrWhiteSpace(dto.DisplayName) ? SfmlDisplayOptions.Default.Title : dto.DisplayName)
            : window!.Title;

        return new SfmlDisplayOptions(width, height, title, ticksPerSecond);
    }

    private sealed class GameConfigWindowDto
    {
        public string DisplayName { get; set; } = "";
        public WindowConfigDto? Window { get; set; }
    }

    private sealed class WindowConfigDto
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public string Title { get; set; } = "";
    }
}
