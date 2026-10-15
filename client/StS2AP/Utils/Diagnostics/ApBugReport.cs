using Godot;

namespace StS2AP.Utils;

/// <summary>Shared Settings/console entry point; compression runs off the Godot thread.</summary>
internal static class ApBugReport
{
    private static bool _exporting;
    public static string Status { get; private set; } = "Export the newest state-divergence report and available game logs.";

    public static bool TryStart(out string message, Action? refresh = null)
    {
        if (_exporting)
        {
            message = "A bug report export is already running.";
            return false;
        }

        string userDirectory = OS.GetUserDataDir();
        string logsDirectory = Path.Combine(string.IsNullOrWhiteSpace(userDirectory)
            ? AppContext.BaseDirectory : userDirectory, "logs");
        _exporting = true;
        message = Status = "Exporting bug report...";
        _ = ExportAsync(logsDirectory, refresh);
        return true;
    }

    private static async Task ExportAsync(string logsDirectory, Action? refresh)
    {
        ApBugReportFiles.ExportResult? result = null;
        string error = "";
        try
        {
            result = await Task.Run(() => ApBugReportFiles.Create(logsDirectory));
        }
        catch (Exception ex)
        {
            error = "Bug report export failed: " + ex.Message;
            LogUtility.Error($"[AP report] Export failed: {ex}");
        }

        Callable.From(() =>
        {
            _exporting = false;
            Status = error;
            if (result is not null)
            {
                Status = $"Bug report ready: {result.Path}\nDivergence file: {result.DivergenceTimeUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}.";
                // Godot uses Explorer on Windows and the platform's file manager elsewhere.
                try
                {
                    Error opened = OS.ShellShowInFileManager(result.Path, openFolder: false);
                    if (opened != Error.Ok)
                        opened = OS.ShellOpen(Path.GetDirectoryName(result.Path)!);
                    if (opened != Error.Ok)
                        Status += "\nCould not open the folder automatically; use the path above.";
                }
                catch (Exception ex)
                {
                    Status += "\nCould not open the folder automatically; use the path above.";
                    LogUtility.Warn("[AP report] Could not open folder: " + ex.Message);
                }
                LogUtility.Info("[AP report] " + Status);
            }
            NotificationUtility.ShowRawText(Status, timeout: 10);
            // The user may have closed Settings while compression was running.
            try { refresh?.Invoke(); }
            catch (Exception ex) { LogUtility.Warn("[AP report] Settings refresh unavailable: " + ex.Message); }
        }).CallDeferred();
    }
}
