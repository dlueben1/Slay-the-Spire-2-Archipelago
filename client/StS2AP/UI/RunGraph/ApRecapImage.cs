using Godot;
using StS2AP.Utils;
using static StS2AP.UI.RunGraph.RunGraphStyle;

namespace StS2AP.UI.RunGraph;

/// <summary>
/// Saves a recap page as a PNG, laid out at a fixed width in an offscreen viewport so the picture
/// does not depend on the window size.
/// </summary>
internal static class ApRecapImage
{
    private const int Padding = 32;
    // Godot's largest texture side; a run with a very long claim list is cut off rather than lost.
    private const int MaxHeight = 16384;

    /// <summary>
    /// The page's height once its layout has settled. Wrapped labels first measure themselves at
    /// a width of nothing, one word per line, and a control never shrinks back to its minimum by
    /// itself, so the page is shrunk each frame until its minimum height stops changing.
    /// </summary>
    private static async Task<int> SettledHeight(Node host, Control page, int width)
    {
        float last = -1f;
        for (int frame = 0; frame < 10; frame++)
        {
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            float height = page.GetCombinedMinimumSize().Y;
            page.Size = new Vector2(width, height);
            if (Math.Abs(height - last) < 0.5f)
                break;
            last = height;
        }
        return Math.Min(MaxHeight, (int)MathF.Ceiling(page.GetCombinedMinimumSize().Y));
    }

    /// <summary>
    /// Draws the page <paramref name="build"/> makes at <paramref name="width"/> pixels wide and
    /// saves it. Returns the saved file's path, or null when the picture could not be made.
    /// </summary>
    internal static async Task<string?> SaveAsync(Node host, Func<Control> build, int width, string directory, string fileName)
    {
        SubViewport? viewport = null;
        try
        {
            viewport = new SubViewport
            {
                Size = new Vector2I(width, 800),
                Disable3D = true,
                TransparentBg = false,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            };
            var page = new PanelContainer { CustomMinimumSize = new Vector2(width, 0f) };
            page.AddThemeStyleboxOverride("panel", Box(Page, Page, 0, Padding));
            page.AddChild(build());
            viewport.AddChild(page);
            host.AddChild(viewport);

            int height = await SettledHeight(host, page, width);
            viewport.Size = new Vector2I(width, height);
            page.Size = new Vector2(width, height);

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
