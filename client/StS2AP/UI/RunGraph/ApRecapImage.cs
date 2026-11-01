using Godot;
using StS2AP.Utils;
using static StS2AP.UI.RunGraph.RunGraphStyle;

namespace StS2AP.UI.RunGraph;

/// <summary>
/// Saves the recap as one tall PNG: the same page the overlay shows, laid out at a fixed width in
/// an offscreen viewport so the picture does not depend on the window size.
/// </summary>
internal static class ApRecapImage
{
    private const int Width = 1200;
    private const int Padding = 32;
    // Godot's largest texture side; a run with a very long claim list is cut off rather than lost.
    private const int MaxHeight = 16384;

    /// <summary>Returns the saved file's path, or null when the picture could not be made.</summary>
    internal static async Task<string?> SaveAsync(Node host, ApRunRecapData data, string directory, string fileName)
    {
        SubViewport? viewport = null;
        try
        {
            viewport = new SubViewport
            {
                Size = new Vector2I(Width, 800),
                Disable3D = true,
                TransparentBg = false,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            };
            var page = new PanelContainer { CustomMinimumSize = new Vector2(Width, 0f) };
            page.AddThemeStyleboxOverride("panel", Box(Page, Page, 0, Padding));
            page.AddChild(ApRunRecapView.Build(data, interactive: false, out _));
            viewport.AddChild(page);
            host.AddChild(viewport);

            // Containers settle their sizes over a frame; only then is the page's height known.
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            int height = Math.Min(MaxHeight, (int)MathF.Ceiling(page.GetCombinedMinimumSize().Y));
            viewport.Size = new Vector2I(Width, height);
            page.Size = new Vector2(Width, height);

            // Two drawn frames: one for the resized viewport, one for its contents to reach the texture.
            await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

            var image = viewport.GetTexture().GetImage();
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, fileName);
            Error saved = image.SavePng(path);
            if (saved != Error.Ok)
            {
                LogUtility.Warn($"Could not save the recap image to {path}: {saved}");
                return null;
            }
            return path;
        }
        catch (Exception ex)
        {
            LogUtility.Error($"Saving the recap image failed: {ex}");
            return null;
        }
        finally
        {
            if (viewport != null && GodotObject.IsInstanceValid(viewport))
                viewport.QueueFree();
        }
    }
}
