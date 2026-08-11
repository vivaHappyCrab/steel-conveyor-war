using SFML.Graphics;
using SFML.System;
using SFML.Window;
using SteelConveyorWar.Core;

namespace SteelConveyorWar.Sfml;

internal sealed class GameCamera
{
    public float X { get; private set; }
    public float Y { get; private set; }
    public GameCamera(float x, float y) { X = x; Y = y; }
    public void Clamp(float worldWidthPx, float worldHeightPx, float playfieldWidth, float playfieldHeight)
    {
        X = Math.Clamp(X, 0f, Math.Max(0f, worldWidthPx - playfieldWidth));
        Y = Math.Clamp(Y, 0f, Math.Max(0f, worldHeightPx - playfieldHeight));
    }
    public void PanBy(float dx, float dy) { X += dx; Y += dy; }
    public void CenterOnWorldPosition(WorldPosition worldPosition, float playfieldWidth, float playfieldHeight)
    {
        X = (float)(worldPosition.ToTileSpaceX() * SfmlUiLayout.TileSize) - playfieldWidth / 2f;
        Y = (float)(worldPosition.ToTileSpaceY() * SfmlUiLayout.TileSize) - playfieldHeight / 2f;
    }
    public void CenterOnTile(TilePosition tile, float playfieldWidth, float playfieldHeight)
        => CenterOnWorldPosition(WorldPosition.FromTileCenter(tile), playfieldWidth, playfieldHeight);
    public View CreateWorldView(float playfieldWidth, float playfieldHeight, uint windowWidth, uint windowHeight)
    {
        var view = new View(new FloatRect(new Vector2f(X, Y), new Vector2f(playfieldWidth, playfieldHeight)));
        view.Viewport = new FloatRect(
            new Vector2f(0f, SfmlUiLayout.TopBarHeight / windowHeight),
            new Vector2f(playfieldWidth / windowWidth, playfieldHeight / windowHeight));
        return view;
    }
}
