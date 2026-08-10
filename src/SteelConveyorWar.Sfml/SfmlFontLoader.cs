using SFML.Graphics;
using SFML.System;
using SFML.Window;
using SteelConveyorWar.Core;

namespace SteelConveyorWar.Sfml;

internal static class SfmlFontLoader
{
    public static Font? TryLoadFont()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "resources", "fonts", "arial.ttf"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "segoeui.ttf")
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return new Font(candidate);
            }
        }

        return null;
    }

}
