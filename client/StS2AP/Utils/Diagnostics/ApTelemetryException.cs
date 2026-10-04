using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace StS2AP.Utils;

/// <summary>Builds symbol-only diagnostics before RitsuLib persists or transmits them.</summary>
internal static class ApTelemetryException
{
    internal static string Symbol(string? value, int maximum = 256)
    {
        if (string.IsNullOrWhiteSpace(value)) return "unknown";
        // Identifiers are useful; paths, URLs, messages and argument values are not.
        return new string(value.Take(maximum)
            .Select(c => char.IsAsciiLetterOrDigit(c) || "._+<>`[],():-".Contains(c) ? c : '_').ToArray());
    }

    internal static JsonObject Create(Exception exception, string source)
    {
        var exceptions = new JsonArray();
        var visited = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        for (Exception? current = exception; current != null && exceptions.Count < 4 && visited.Add(current);
             current = current.InnerException)
        {
            var frames = new JsonArray();
            // StackTrace's method metadata avoids parsing raw StackTrace strings containing paths.
            foreach (StackFrame frame in (new StackTrace(current, false).GetFrames() ?? []).Take(32).Reverse())
            {
                MethodBase? method = frame.GetMethod();
                if (method is null) continue;
                frames.Add(new JsonObject
                {
                    ["function"] = Symbol($"{method.DeclaringType?.FullName}.{method.Name}"),
                    ["module"] = Symbol(method.DeclaringType?.Assembly.GetName().Name),
                });
            }
            exceptions.Add(new JsonObject
            {
                ["type"] = Symbol(current.GetType().FullName),
                ["value"] = "Exception message omitted",
                ["stacktrace"] = new JsonObject { ["type"] = "raw", ["frames"] = frames },
            });
        }
        return Report(exceptions, source);
    }

    internal static JsonObject CreateEngine(JsonObject error)
    {
        // Godot text/code/backtraces may contain player data. Use only its function symbol and type.
        var exceptions = new JsonArray(new JsonObject
        {
            ["type"] = "GodotEngineError",
            ["value"] = "Engine error message omitted",
            ["stacktrace"] = new JsonObject
            {
                ["type"] = "raw",
                ["frames"] = new JsonArray(new JsonObject
                {
                    ["function"] = Symbol(error["function"]?.GetValue<string>()),
                    ["module"] = "Godot",
                }),
            },
        });
        JsonObject report = Report(exceptions, "godot_logger");
        report["engine_error_type"] = error["type"]?.GetValue<int>() ?? 0;
        return report;
    }

    private static JsonObject Report(JsonArray exceptions, string source) => new()
    {
        ["exception_list"] = exceptions,
        ["fingerprint"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(exceptions.ToJsonString()))),
        ["source"] = Symbol(source, 128),
    };
}

/// <summary>Bounds work before disk writes; counts affected sessions, not every repeated occurrence.</summary>
internal sealed class ApTelemetryErrorBudget
{
    private readonly object _sync = new();
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private DateTimeOffset _window;
    private int _windowCount;

    internal bool TryAccept(string fingerprint, DateTimeOffset now)
    {
        lock (_sync)
        {
            if (now - _window >= TimeSpan.FromMinutes(1))
            {
                _window = now;
                _windowCount = 0;
            }
            if (_seen.Count >= 200 || _windowCount >= 20 || !_seen.Add(fingerprint)) return false;
            _windowCount++;
            return true;
        }
    }
}
