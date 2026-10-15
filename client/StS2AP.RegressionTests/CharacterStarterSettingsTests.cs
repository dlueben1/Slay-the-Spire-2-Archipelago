using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Xunit;

namespace StS2AP.RegressionTests;

public sealed class CharacterStarterSettingsTests
{
    [ArtifactFact("STS2AP_TEST_ASSEMBLY")]
    [Trait("Category", "Manifest")]
    public void CharacterOverridesAndInheritanceSurviveMultiplayerSnapshots()
    {
        string path = Path.GetFullPath(Environment.GetEnvironmentVariable("STS2AP_TEST_ASSEMBLY")!);
        var context = new AssemblyLoadContext("character-starter-settings", isCollectible: true);
        context.Resolving += (_, name) =>
        {
            string dependency = Path.Combine(Path.GetDirectoryName(path)!, name.Name + ".dll");
            return File.Exists(dependency) ? context.LoadFromAssemblyPath(dependency) : null;
        };
        try
        {
            Assembly assembly = context.LoadFromAssemblyPath(path);
            Type type = assembly.GetType("StS2AP.Models.ArchipelagoSettings", true)!;
            const string json = """
                {
                  "Floorsanity": true,
                  "ProgressiveStarterCard": true,
                  "ProgressiveStarterRelic": false,
                  "Characters": {
                    "Ironclad": {"ProgressiveStarterCard": false, "ProgressiveStarterRelic": true},
                    "Silent": {},
                    "Modded-Character": {"ProgressiveStarterCard": true, "ProgressiveStarterRelic": false}
                  }
                }
                """;
            object settings = JsonSerializer.Deserialize(json, type)!;
            // The real settings type crosses the frozen host/run JSON boundary in multiplayer.
            settings = JsonSerializer.Deserialize(JsonSerializer.Serialize(settings, type), type)!;
            MethodInfo card = type.GetMethod("IsProgressiveStarterCardEnabled")!;
            MethodInfo relic = type.GetMethod("IsProgressiveStarterRelicEnabled")!;
            bool Enabled(MethodInfo method, string id) => (bool)method.Invoke(settings, [id])!;

            Assert.False(Enabled(card, "IRONCLAD"));
            Assert.True(Enabled(relic, "ironclad"));
            Assert.True(Enabled(card, "Silent"));
            Assert.False(Enabled(relic, "Silent"));
            Assert.True(Enabled(card, "modded-character"));
            Assert.False(Enabled(relic, "modded-character"));

            // Omitted character fields retain inheritance; explicit overrides remain independent.
            type.GetProperty("ProgressiveStarterCard")!.SetValue(settings, false);
            type.GetProperty("ProgressiveStarterRelic")!.SetValue(settings, true);
            Assert.False(Enabled(card, "Silent"));
            Assert.True(Enabled(relic, "Silent"));
            Assert.True(Enabled(card, "modded-character"));
            Assert.False(Enabled(relic, "modded-character"));

            type.GetProperty("Floorsanity")!.SetValue(settings, false);
            foreach (string id in new[] { "Ironclad", "Silent", "Modded-Character" })
            {
                Assert.False(Enabled(card, id));
                Assert.False(Enabled(relic, id));
            }
        }
        finally
        {
            context.Unload();
        }
    }
}
