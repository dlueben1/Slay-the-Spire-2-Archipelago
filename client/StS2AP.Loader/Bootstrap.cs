using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Debug;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace StS2AP.Loader;

/// <summary>
/// Stable entry assembly for the multi-version Archipelago bundle.
/// </summary>
[ModInitializer(nameof(Initialize))]
public static class Bootstrap
{
    private const string VariantManifestName = "archipelago-variants.manifest";
    private const string VariantEntryType = "StS2AP.ModEntry";
    private const string VariantEntryMethod = "Initialize";
    private const string RitsuLibAssemblyName = "STS2-RitsuLib";
    // Read by the client to label a build running on a game version line it was not made for.
    private const string UnverifiedBuildKey = "StS2AP.UnverifiedBuildTarget";
    private const int MaxReportedReferenceProblems = 5;
    private static string? _modDirectory;
    private static AssemblyLoadContext? _loadContext;
    private static int _initializationStarted;

    // Every failure below throws: the game reports a mod as failing to initialize only when its
    // initializer throws, and otherwise lists it as loaded with nothing running.
    public static void Initialize()
    {
        // Initializers cannot safely be replayed after partial content registration.
        if (Interlocked.Exchange(ref _initializationStarted, 1) != 0)
        {
            Log.Warn("[Archipelago.Loader] Ignoring repeated initialization.");
            return;
        }

        try
        {
            _modDirectory = Path.GetDirectoryName(typeof(Bootstrap).Assembly.Location);
            if (string.IsNullOrWhiteSpace(_modDirectory))
                throw new InvalidOperationException("Could not resolve the mod directory.");

            _loadContext = AssemblyLoadContext.GetLoadContext(typeof(Bootstrap).Assembly)
                ?? AssemblyLoadContext.Default;
            _loadContext.Resolving += ResolveDependency;

            string? hostVersionText = NormalizeGameVersion(ReleaseInfoManager.Instance.ReleaseInfo?.Version);
            if (hostVersionText is null || !Version.TryParse(hostVersionText, out Version? hostVersion))
                throw new InvalidOperationException("Could not determine the Slay the Spire 2 version.");

            VariantManifest manifest = ReadManifest(_modDirectory);
            VariantSelection? selection = PickVariant(hostVersion, manifest.Variants);
            if (selection is null)
            {
                string supported = string.Join(", ", manifest.Variants.Keys.Order());
                throw new InvalidOperationException(
                    $"STS2 {hostVersionText} has no compatible build: every build is for a newer game " +
                    $"version or another major version. Compiled targets: {supported}."
                );
            }

            bool exactBuild = selection.Version == hostVersion;
            if (selection.CrossesVersionLine)
            {
                Log.Warn(
                    $"[Archipelago.Loader] STS2 {hostVersionText} has no Archipelago build on its version line; " +
                    $"trying the {selection.CompatTarget} build. It has not been verified on this game version."
                );
            }
            else if (!exactBuild)
            {
                Log.Warn(
                    $"[Archipelago.Loader] STS2 {hostVersionText} has no exact Archipelago build; " +
                    $"using the {selection.CompatTarget} build from the same version line."
                );
            }

            string variantPath = ResolveValidatedVariantPath(
                _modDirectory,
                selection.CompatTarget,
                selection.Entry
            );
            Assembly variantAssembly = LoadVariantAssembly(variantPath);
            if (!exactBuild)
                EnsureReferencesResolve(variantAssembly, selection.CompatTarget, hostVersionText);
            if (selection.CrossesVersionLine)
                AppContext.SetData(UnverifiedBuildKey, selection.CompatTarget);
            Patches_VariantTypeDiscovery.Register(variantAssembly);
            InvokeVariantInitializer(variantAssembly);
            Log.Info(
                $"[Archipelago.Loader] Loaded Archipelago {manifest.ModVersion} target " +
                $"{selection.CompatTarget} for STS2 {hostVersionText}."
            );
        }
        catch (Exception ex)
        {
            Log.Error($"[Archipelago.Loader] Archipelago will not start: {ex}");
            throw;
        }
    }

    private static Assembly? ResolveDependency(AssemblyLoadContext context, AssemblyName name)
    {
        if (string.IsNullOrWhiteSpace(_modDirectory) || string.IsNullOrWhiteSpace(name.Name))
            return null;

        // RitsuLib's Workshop package has a stable loader at the mod root and loads the
        // game-compatible implementation from its own lib/<version> directory. Reuse that
        // implementation even when the two mod loaders live in different load contexts.
        // Loading another copy here would split RitsuLib's static registrations and type identity.
        if (string.Equals(name.Name, RitsuLibAssemblyName, StringComparison.OrdinalIgnoreCase))
            return ResolveLoadedRitsuLib(name);

        string candidate = Path.Combine(_modDirectory, name.Name + ".dll");
        if (!File.Exists(candidate))
            return null;

        try
        {
            return context.LoadFromAssemblyPath(candidate);
        }
        catch
        {
            return null;
        }
    }

    private static Assembly LoadVariantAssembly(string variantPath)
    {
        Assembly[] loaded = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => string.Equals(assembly.GetName().Name, "Archipelago", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (loaded.Length == 0)
            return _loadContext!.LoadFromAssemblyPath(variantPath);
        if (loaded.Length == 1 && string.Equals(loaded[0].Location, variantPath, StringComparison.OrdinalIgnoreCase))
            return loaded[0];

        throw new InvalidOperationException(
            "A different or duplicate Archipelago implementation is already loaded; refusing to split model type identity."
        );
    }

    private static Assembly? ResolveLoadedRitsuLib(AssemblyName requestedName)
    {
        Version minimumVersion = requestedName.Version ?? new Version(0, 0);
        Assembly? resolved = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly =>
                string.Equals(
                    assembly.GetName().Name,
                    RitsuLibAssemblyName,
                    StringComparison.OrdinalIgnoreCase
                )
                && (assembly.GetName().Version ?? new Version(0, 0)) >= minimumVersion
            )
            .OrderByDescending(assembly => assembly.GetName().Version)
            .FirstOrDefault();

        if (resolved is null)
        {
            Log.Error(
                $"[Archipelago.Loader] RitsuLib {minimumVersion} or newer is not initialized. " +
                "Install and enable the RitsuLib variant pack so its compatibility loader runs " +
                "before Archipelago."
            );
            return null;
        }

        Version resolvedVersion = resolved.GetName().Version ?? new Version(0, 0);
        Log.Info(
            $"[Archipelago.Loader] Bound {requestedName.Name} {minimumVersion} to loaded " +
            $"RitsuLib {resolvedVersion} from {resolved.Location}."
        );
        return resolved;
    }

    private static VariantManifest ReadManifest(string modDirectory)
    {
        string path = Path.Combine(modDirectory, VariantManifestName);
        if (!File.Exists(path))
            throw new FileNotFoundException("The compatibility manifest is missing.", path);

        VariantManifest? manifest = JsonSerializer.Deserialize<VariantManifest>(File.ReadAllText(path));
        if (manifest is null || manifest.Schema != 1 || manifest.Variants.Count == 0)
            throw new InvalidDataException($"{VariantManifestName} is empty or has an unsupported schema.");
        return manifest;
    }

    /// <summary>
    /// Selects the newest build that does not exceed the host version within the host's
    /// major.minor line. Without one, falls back to the newest older build of the same major
    /// version; that build is unverified there, and the caller checks its references first.
    /// A build for a newer game version or another major version is never used.
    /// </summary>
    private static VariantSelection? PickVariant(
        Version hostVersion,
        IReadOnlyDictionary<string, VariantEntry> variants
    )
    {
        VariantSelection? sameLine = null;
        VariantSelection? olderLine = null;
        foreach ((string compatTarget, VariantEntry? entry) in variants)
        {
            string? normalizedTarget = NormalizeGameVersion(compatTarget);
            if (normalizedTarget is null || !Version.TryParse(normalizedTarget, out Version? targetVersion))
                throw new InvalidDataException($"Variant target '{compatTarget}' is not a numeric three-part version.");
            if (entry is null)
                throw new InvalidDataException($"Variant {compatTarget} has no manifest entry.");
            if (targetVersion.Major != hostVersion.Major || targetVersion.CompareTo(hostVersion) > 0)
                continue;

            if (targetVersion.Minor == hostVersion.Minor)
            {
                if (sameLine is null || targetVersion.CompareTo(sameLine.Version) > 0)
                    sameLine = new VariantSelection(compatTarget, targetVersion, entry, CrossesVersionLine: false);
            }
            else if (olderLine is null || targetVersion.CompareTo(olderLine.Version) > 0)
            {
                olderLine = new VariantSelection(compatTarget, targetVersion, entry, CrossesVersionLine: true);
            }
        }
        return sameLine ?? olderLine;
    }

    /// <summary>
    /// Fails startup when a build made for another game version references game or library
    /// members this game lacks, rather than letting the first use of one fail mid-run.
    /// </summary>
    private static void EnsureReferencesResolve(Assembly variantAssembly, string compatTarget, string hostVersionText)
    {
        IReadOnlyList<string> problems = VariantReferenceCheck.FindMissingReferences(variantAssembly);
        if (problems.Count == 0)
        {
            Log.Info($"[Archipelago.Loader] Every game member the {compatTarget} build uses exists in STS2 {hostVersionText}.");
            return;
        }

        foreach (string problem in problems)
            Log.Error($"[Archipelago.Loader] Missing in STS2 {hostVersionText}: {problem}");
        throw new InvalidOperationException(
            $"The {compatTarget} build uses {problems.Count} game or library members that STS2 {hostVersionText} lacks, " +
            $"so it needs an update for this game version. First: " +
            string.Join("; ", problems.Take(MaxReportedReferenceProblems))
        );
    }

    private static string ResolveValidatedVariantPath(
        string modDirectory,
        string hostVersion,
        VariantEntry entry)
    {
        string expectedRelativePath = Path.Combine("lib", hostVersion, "Archipelago.dll");
        string declaredRelativePath = entry.Assembly.Replace('/', Path.DirectorySeparatorChar);
        if (!string.Equals(declaredRelativePath, expectedRelativePath, StringComparison.Ordinal))
            throw new InvalidDataException($"Variant {hostVersion} must use {expectedRelativePath}.");

        string modRoot = Path.GetFullPath(modDirectory) + Path.DirectorySeparatorChar;
        string assemblyPath = Path.GetFullPath(Path.Combine(modDirectory, declaredRelativePath));
        if (!assemblyPath.StartsWith(modRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Variant path escapes the mod directory: {entry.Assembly}");
        if (!File.Exists(assemblyPath))
            throw new FileNotFoundException($"Variant DLL for STS2 {hostVersion} is missing.", assemblyPath);

        string markerPath = Path.Combine(Path.GetDirectoryName(assemblyPath)!, "compat-target.txt");
        string marker = File.ReadAllText(markerPath).Trim();
        if (!string.Equals(marker, hostVersion, StringComparison.Ordinal))
            throw new InvalidDataException($"Variant marker is '{marker}', expected '{hostVersion}'.");

        string actualHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assemblyPath))).ToLowerInvariant();
        if (!string.Equals(actualHash, entry.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Variant DLL hash mismatch for STS2 {hostVersion}.");
        return assemblyPath;
    }

    private static void InvokeVariantInitializer(Assembly assembly)
    {
        Type entryType = assembly.GetType(VariantEntryType, throwOnError: true)!;
        MethodInfo initialize = entryType.GetMethod(
            VariantEntryMethod,
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null
        ) ?? throw new MissingMethodException(VariantEntryType, VariantEntryMethod);
        initialize.Invoke(null, null);
    }

    private static string? NormalizeGameVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        string numeric = value.Trim().TrimStart('v', 'V').Split('-', '+')[0];
        if (numeric.Count(character => character == '.') != 2)
            return null;
        if (!Version.TryParse(numeric, out Version? version) || version.Build < 0)
            return null;
        return $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private sealed class VariantManifest
    {
        [JsonPropertyName("schema")]
        public int Schema { get; init; }

        [JsonPropertyName("modVersion")]
        public string ModVersion { get; init; } = "unknown";

        [JsonPropertyName("variants")]
        public Dictionary<string, VariantEntry> Variants { get; init; } = [];
    }

    private sealed class VariantEntry
    {
        [JsonPropertyName("assembly")]
        public string Assembly { get; init; } = "";

        [JsonPropertyName("sha256")]
        public string Sha256 { get; init; } = "";
    }

    private sealed record VariantSelection(
        string CompatTarget,
        Version Version,
        VariantEntry Entry,
        bool CrossesVersionLine
    );
}
