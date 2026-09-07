using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Xunit;

namespace StS2AP.RegressionTests;

public sealed class PackagingTests
{
    [ArtifactFact("STS2AP_TEST_BUNDLE")]
    [Trait("Category", "Bundle")]
    public void BothPackagedVariantsLoadFSharpThroughTheModLoader()
    {
        string root = Path.GetFullPath(Environment.GetEnvironmentVariable("STS2AP_TEST_BUNDLE")!);
        foreach (string dependency in new[] { "StS2AP.Domain.dll", "FSharp.Core.dll" })
            Assert.True(File.Exists(Path.Combine(root, dependency)), $"Missing bundle dependency: {dependency}");
        foreach (string compat in new[] { "0.107.1", "0.111.0" })
        {
            var context = new BundleContext(compat);
            try
            {
                // Use the shipped loader without starting the game.
                Assembly loader = context.LoadFromAssemblyPath(Path.Combine(root, "Archipelago.dll"));
                Type bootstrap = loader.GetType("StS2AP.Loader.Bootstrap", throwOnError: true)!;
                bootstrap.GetField("_modDirectory", BindingFlags.NonPublic | BindingFlags.Static)!
                    .SetValue(null, root);
                var resolve = bootstrap.GetMethod("ResolveDependency", BindingFlags.NonPublic | BindingFlags.Static)!
                    .CreateDelegate<Func<AssemblyLoadContext, AssemblyName, Assembly?>>();
                context.ResolveFromBundle = resolve;
                Assembly variant = context.LoadFromAssemblyPath(Path.Combine(root, "lib", compat, "Archipelago.dll"));
                ValidateEmbeddedManifests(variant, root);
                MethodInfo decode = variant.GetType("StS2AP.DomainAdapters.MirroredRewardAdapter", true)!
                    .GetMethod("Decode", BindingFlags.Public | BindingFlags.Static)!;
                Type specType = variant.GetType("StS2AP.Models.ApMirroredRewardSpec", true)!;
                object spec = JsonSerializer.Deserialize("""
                    {"SchemaVersion":5,"ApSlotId":2,"ReceivedItemIndex":42,"OwnerNetId":1,
                     "Kind":0,"CardRewardActIndex":1,"CardHasBeenRevealed":true,
                     "MaterializationStrategyId":"replica_native_v1","RequiresNativeMaterialization":false,
                     "SerializedModels":["{\"id\":\"CARD.A\"}"]}
                    """, specType)!;
                object reward = decode.Invoke(null, [spec, 3])!;
                object configuration = variant.GetType("StS2AP.DomainAdapters.MirroredRewardAdapter", true)!
                    .GetMethod("CardConfiguration", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [spec])!;
                object provenance = configuration.GetType().GetProperty("Policy")!.GetValue(configuration)!;
                Assert.Equal("replica_native_v1", provenance.GetType().GetProperty("StrategyId")!.GetValue(provenance));
                object origin = reward.GetType().GetProperty("Origin")!.GetValue(reward)!;
                Assert.Equal("2:42", origin.GetType().GetProperty("ReceiptIdentity")!.GetValue(origin));

                foreach (string dependency in new[] { "StS2AP.Domain", "FSharp.Core" })
                {
                    Assembly loaded = context.Assemblies.Single(a => a.GetName().Name == dependency);
                    if (!string.Equals(loaded.Location, Path.Combine(root, dependency + ".dll"),
                            StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"{dependency} was not loaded from the bundle root.");
                }
                Console.WriteLine($"Packaged {compat} C# -> F# call passed using the actual loader dependency resolver.");
            }
            finally
            {
                context.Unload();
            }
        }
    }

    [ArtifactFact("STS2AP_TEST_ASSEMBLY")]
    [Trait("Category", "Manifest")]
    public void BuiltAssemblyContainsBothVersionManifests()
    {
        string path = Environment.GetEnvironmentVariable("STS2AP_TEST_ASSEMBLY")!;
        var context = new AssemblyLoadContext("manifest-check", isCollectible: true);
        try
        {
            ValidateEmbeddedManifests(context.LoadFromAssemblyPath(Path.GetFullPath(path)), null);
        }
        finally
        {
            context.Unload();
        }
    }

    private static void ValidateEmbeddedManifests(Assembly variant, string? bundleRoot)
    {
        foreach (var (resource, property) in new[]
                 {
                     ("StS2AP.Archipelago.json", "version"),
                     ("StS2AP.Spire2Archipelago.json", "world_version"),
                 })
        {
            using Stream stream = variant.GetManifestResourceStream(resource)
                ?? throw new InvalidDataException(
                    $"{variant.Location} is missing {resource}; present resources: "
                    + string.Join(", ", variant.GetManifestResourceNames()));
            using JsonDocument manifest = JsonDocument.Parse(stream);
            string? version = manifest.RootElement.GetProperty(property).GetString();
            if (!Version.TryParse(version?.Split('-', '+')[0], out Version? parsed) || parsed.Build < 0)
                throw new InvalidDataException($"{resource} has an invalid {property}.");
            if (property == "version" && bundleRoot != null)
            {
                using JsonDocument external = JsonDocument.Parse(
                    File.ReadAllText(Path.Combine(bundleRoot, "Archipelago.json")));
                if (version != external.RootElement.GetProperty(property).GetString())
                    throw new InvalidDataException("Embedded and deployed mod versions do not match.");
            }
        }
        Console.WriteLine($"Embedded manifests passed: {variant.Location}");
    }

    private sealed class BundleContext(string compat)
        : AssemblyLoadContext($"fsharp-bundle-{compat}", isCollectible: true)
    {
        public Func<AssemblyLoadContext, AssemblyName, Assembly?>? ResolveFromBundle { get; set; }

        // Probe the bundle before the test runner's default context: its own FSharp.Core
        // would otherwise hide missing packaged dependencies. Resolution uses production code.
        protected override Assembly? Load(AssemblyName assemblyName) =>
            ResolveFromBundle?.Invoke(this, assemblyName);
    }
}
