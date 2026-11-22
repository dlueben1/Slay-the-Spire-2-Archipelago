using Godot;
using StS2AP.Utils;

namespace StS2AP.UI.RunGraph;

/// <summary>Opens the run-graph folders and files the overlays write.</summary>
internal static class ApRunGraphFiles
{
    /// <summary>Shows a file in the platform's file manager, or opens a folder.</summary>
    internal static void ShowInFileManager(string path)
    {
        try
        {
            bool isFile = File.Exists(path);
            if (!isFile)
                Directory.CreateDirectory(path);
            // Godot uses Explorer on Windows and the platform's file manager elsewhere.
            Error opened = isFile
                ? OS.ShellShowInFileManager(path, openFolder: false)
                : OS.ShellOpen(path);
            if (opened != Error.Ok && isFile)
                opened = OS.ShellOpen(Path.GetDirectoryName(path)!);
            if (opened != Error.Ok)
                NotificationUtility.ShowRawText($"Couldn't open the folder automatically. It is at {path}", timeout: 8);
        }
        catch (Exception ex)
        {
            LogUtility.Warn("Could not open run graph folder: " + ex.Message);
            NotificationUtility.ShowRawText($"Couldn't open the folder automatically. It is at {path}", timeout: 8);
        }
    }
}
