using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Xunit;

namespace StS2AP.RegressionTests;

public sealed class PackagingTests
{
    [ArtifactFact("STS2AP_TEST_BUNDLE")]
    [Trait("Category", "Bundle")]
    public void BothPackagedVariantsLoadDependenciesThroughTheModLoader()
    {
        string root = Path.GetFullPath(Environment.GetEnvironmentVariable("STS2AP_TEST_BUNDLE")!);
        foreach (string dependency in new[] { "StS2AP.Domain.dll", "FSharp.Core.dll" })
            Assert.True(File.Exists(Path.Combine(root, dependency)), $"Missing bundle dependency: {dependency}");
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "archipelago-variants.manifest")));
        string[] variants = manifest.RootElement.GetProperty("variants").EnumerateObject()
            .Select(variant => variant.Name).ToArray();
        Assert.Equal(2, variants.Length);
        foreach (string compat in variants)
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
                    {"ApSlotId":2,"ReceivedItemIndex":42,"OwnerNetId":1,
                     "Kind":0,"CardRewardActIndex":1,"CardHasBeenRevealed":true,
                     "MaterializationStrategyId":"ap_rng_replicated_card_v1",
                     "SerializedModels":["{\"id\":\"CARD.A\"}"]}
                    """, specType)!;
                object reward = decode.Invoke(null, [spec, 3])!;
                object configuration = variant.GetType("StS2AP.DomainAdapters.MirroredRewardAdapter", true)!
                    .GetMethod("CardConfiguration", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [spec])!;
                object provenance = configuration.GetType().GetProperty("Policy")!.GetValue(configuration)!;
                Assert.Equal("ap_rng_replicated_card_v1", provenance.GetType().GetProperty("StrategyId")!.GetValue(provenance));
                object origin = reward.GetType().GetProperty("Origin")!.GetValue(reward)!;
                Assert.Equal("2:42", origin.GetType().GetProperty("ReceiptIdentity")!.GetValue(origin));

                // The SDK and client must bind typed JSON APIs to the same assembly.
                // Slot data may still arrive from a foreign copy, so exercise the actual
                // variant's normalization boundary with the test runner's JSON objects.
                Assembly sdk = context.LoadFromAssemblyName(new AssemblyName("Archipelago.MultiClient.Net"));
                Type sdkToken = sdk.GetType("Archipelago.MultiClient.Net.Packets.SetPacket", true)!
                    .GetProperty("DefaultValue")!.PropertyType;
                var normalize = variant.GetType("StS2AP.Utils.ApSlotData", true)!
                    .GetMethod("Normalize", BindingFlags.NonPublic | BindingFlags.Static)!;
                var foreignPlayers = Newtonsoft.Json.Linq.JObject.Parse("""{"1":[{"name":"Ironclad","locked":true}]}""");
                Assert.False(sdkToken.IsInstanceOfType(foreignPlayers));
                var slotData = (Dictionary<string, object>)normalize.Invoke(null,
                    [new Dictionary<string, object> { ["players"] = foreignPlayers }])!;
                Assert.True(sdkToken.IsInstanceOfType(slotData["players"]));
                Assert.True(Newtonsoft.Json.Linq.JToken.DeepEquals(foreignPlayers,
                    Newtonsoft.Json.Linq.JToken.Parse(slotData["players"].ToString()!)));

                foreach (string dependency in new[] { "StS2AP.Domain", "FSharp.Core", "Archipelago.MultiClient.Net", "Newtonsoft.Json" })
                {
                    Assembly loaded = context.Assemblies.Single(a => a.GetName().Name == dependency);
                    if (!string.Equals(loaded.Location, Path.Combine(root, dependency + ".dll"),
                            StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"{dependency} was not loaded from the bundle root.");
                }
                Console.WriteLine($"Packaged {compat} F# and JSON boundaries passed using the actual loader dependency resolver.");
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

    [ArtifactFact("STS2AP_TEST_ASSEMBLY")]
    [Trait("Category", "Manifest")]
    public void BonusDefinitionsSurviveTheMultiplayerJsonBoundary()
    {
        string path = Path.GetFullPath(Environment.GetEnvironmentVariable("STS2AP_TEST_ASSEMBLY")!);
        var context = new AssemblyLoadContext("bonus-settings-check", isCollectible: true);
        context.Resolving += (_, name) =>
        {
            string dependency = Path.Combine(Path.GetDirectoryName(path)!, name.Name + ".dll");
            return File.Exists(dependency) ? context.LoadFromAssemblyPath(dependency) : null;
        };
        try
        {
            var assembly = context.LoadFromAssemblyPath(path);
            var definition = assembly.GetType("StS2AP.Models.BonusItemDefinition", true)!;
            var arrayType = definition.MakeArrayType();
            const string json = """
                [{"Category":"WAX_RELIC","Pools":[],"Value":"THE_BOOT"},
                 {"Category":"WAX_RELIC","Pools":["Common","Fake"],"Value":null}]
                """;
            var restored = (Array)JsonSerializer.Deserialize(json, arrayType)!;
            var roundTrip = (Array)JsonSerializer.Deserialize(JsonSerializer.Serialize(restored, arrayType), arrayType)!;
            Assert.Equal(2, roundTrip.Length);
            Assert.Equal("THE_BOOT", definition.GetProperty("Value")!.GetValue(roundTrip.GetValue(0)));
            Assert.Equal(new[] { "Common", "Fake" },
                (IReadOnlyList<string>)definition.GetProperty("Pools")!.GetValue(roundTrip.GetValue(1))!);
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
