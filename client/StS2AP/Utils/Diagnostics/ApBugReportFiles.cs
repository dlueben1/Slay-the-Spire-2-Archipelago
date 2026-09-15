using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace StS2AP.Utils;

/// <summary>Packages existing diagnostics without changing RitsuLib's original report.</summary>
internal static class ApBugReportFiles
{
    internal sealed record ExportResult(string Path, DateTime DivergenceTimeUtc);

    public static ExportResult Create(string logsDirectory)
    {
        // Matches RitsuLib's StateDivergenceLogBundleWriter publication directory/prefix.
        FileInfo? source = Directory.Exists(logsDirectory)
            ? new DirectoryInfo(logsDirectory).EnumerateFiles("ritsulib_state_divergence_*.zip")
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ThenByDescending(file => file.Name, StringComparer.Ordinal).FirstOrDefault()
            : null;
        if (source is null)
            throw new FileNotFoundException("No RitsuLib state-divergence report found in " + logsDirectory);

        string outputDirectory = Path.Combine(logsDirectory, "ArchipelagoReports");
        Directory.CreateDirectory(outputDirectory);
        string output = Path.Combine(outputDirectory, $"ap-report_{DateTime.Now:yyyyMMdd_HHmmss_fffffff}.zip");
        string pending = output + ".partial";
        var notes = new StringBuilder()
            .AppendLine("Archipelago bug report")
            .AppendLine($"Exported (UTC): {DateTime.UtcNow:O}")
            .AppendLine($"Divergence source: {source.Name}")
            .AppendLine($"Divergence file timestamp (UTC): {source.LastWriteTimeUtc:O}")
            .AppendLine("divergence/ preserves all original ZIP entries, including capture metadata and available replica logs.")
            .AppendLine("warnings-errors.txt is extracted from those replica logs, including multiline messages/stack traces.")
            .AppendLine("game-logs/ contains up to five newest available Godot logs. These may be from later sessions.")
            .AppendLine("The original divergence ZIP is unchanged. No live viewer or log-buffer capture is used.");
        var warnings = new StringBuilder();

        try
        {
            using (ZipArchive original = ZipFile.OpenRead(source.FullName))
            using (ZipArchive report = ZipFile.Open(pending, ZipArchiveMode.Create))
            {
                foreach (ZipArchiveEntry entry in original.Entries)
                {
                    string name = entry.FullName.Replace('\\', '/');
                    if (name.StartsWith('/') || name.Contains(':') || name.Split('/').Contains(".."))
                        throw new InvalidDataException("Invalid divergence ZIP entry: " + name);
                    using (Stream input = entry.Open())
                    using (Stream target = report.CreateEntry("divergence/" + name).Open())
                        input.CopyTo(target);

                    if (!name.EndsWith("-debug-log.records.json", StringComparison.Ordinal))
                        continue;
                    try
                    {
                        using Stream input = entry.Open();
                        AppendWarnings(input, name, warnings);
                    }
                    catch (Exception ex) when (ex is JsonException or InvalidOperationException)
                    {
                        notes.AppendLine($"Warning extraction unavailable for {name}: {ex.Message}");
                    }
                }

                foreach (FileInfo log in new DirectoryInfo(logsDirectory).EnumerateFiles("godot*.log")
                    .OrderByDescending(file => file.LastWriteTimeUtc).Take(5))
                {
                    try
                    {
                        // Godot can still be writing this file while the user exports.
                        using var input = new FileStream(log.FullName, FileMode.Open, FileAccess.Read,
                            FileShare.ReadWrite | FileShare.Delete);
                        using Stream target = report.CreateEntry("game-logs/" + log.Name).Open();
                        input.CopyTo(target);
                        notes.AppendLine($"Game log: {log.Name}; last written (UTC): {log.LastWriteTimeUtc:O}");
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        notes.AppendLine($"Game log missing or incomplete: {log.Name}: {ex.Message}");
                    }
                }

                WriteText(report, "warnings-errors.txt", warnings.Length == 0
                    ? "No warning/error records extracted. See README.txt and the original replica logs.\n"
                    : warnings.ToString());
                WriteText(report, "README.txt", notes.ToString());
            }
            File.Move(pending, output);
            return new ExportResult(output, source.LastWriteTimeUtc);
        }
        finally
        {
            if (File.Exists(pending))
                File.Delete(pending);
        }
    }

    private static void AppendWarnings(Stream input, string name, StringBuilder output)
    {
        using JsonDocument records = JsonDocument.Parse(input);
        foreach (JsonElement record in records.RootElement.EnumerateArray())
        {
            if (!record.TryGetProperty("severityNumber", out JsonElement severity)
                || !severity.TryGetInt32(out int level) || level < 13)
                continue;
            output.AppendLine($"[{name}] {Field(record, "timestamp")} [{Field(record, "severityText")}] {Field(record, "source")}")
                .AppendLine(Field(record, "body")).AppendLine();
        }
    }

    private static string Field(JsonElement record, string key) =>
        record.TryGetProperty(key, out JsonElement value) ? value.ToString() : "";

    private static void WriteText(ZipArchive archive, string name, string text)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(text);
    }
}
