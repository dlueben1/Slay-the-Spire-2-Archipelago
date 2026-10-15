using System.Runtime.Loader;
using System.Text.Json;
using StS2AP.Data;
using Xunit;

namespace StS2AP.RegressionTests;

public sealed class CharacterRegistryCompatibilityTests
{
    [ArtifactFact("STS2AP_TEST_ASSEMBLY")]
    [Trait("Category", "Manifest")]
    public void OldAndNamedCharacterConfigurationsKeepTheirNamesAndIndependentIdBlocks()
    {
        string path = Path.GetFullPath(Environment.GetEnvironmentVariable("STS2AP_TEST_ASSEMBLY")!);
        var context = new AssemblyLoadContext("character-registry", isCollectible: true);
        context.Resolving += (_, name) =>
        {
            string dependency = Path.Combine(Path.GetDirectoryName(path)!, name.Name + ".dll");
            return File.Exists(dependency) ? context.LoadFromAssemblyPath(dependency) : null;
        };
        try
        {
            Type type = context.LoadFromAssemblyPath(path).GetType("StS2AP.Models.CharacterConfig", true)!;
            // Each row is an independent slot configuration, not part of one combined roster.
            // Block is the one-based AP ID block supplied as char_offset; it determines item
            // and location IDs. ModNum is the selected modded-roster position (zero for vanilla),
            // not an ID block or the number in "Custom Character X".
            // Blocks must be unique within a roster, but can repeat across these scenarios.
            // Positive ModNum values are unique within a roster; all vanilla entries use zero.
            var configurations = new[]
            {
                // An old generated seed called Watcher Custom Character 1 and assigned block 6.
                // A newer client must retain that seed's supplied values, not remap it to block 51.
                (Name: "Custom Character 1", InternalId: "WATCHER-WATCHER", Block: 6, ModNum: 1),
                // A new seed selects Watcher first: its predefined block is independent of position.
                (Name: "Watcher", InternalId: "WATCHER-WATCHER", Block: 51, ModNum: 1),
                // After a predefined character, the second mod is the first unknown character.
                // It therefore uses generic block 11 and display number 1, while ModNum is 2.
                (Name: "Custom Character 1", InternalId: "UNKNOWN-MOD", Block: 11, ModNum: 2),
                // The expanded generic range reaches block 30 for the twentieth modded character.
                (Name: "Custom Character 20", InternalId: "UNKNOWN-MOD-20", Block: 30, ModNum: 20),
                // A future sixth vanilla character uses reserved vanilla block 6 and ModNum 0.
                // Sharing block 6 with the old-seed scenario is valid: these are different seeds.
                (Name: "Future Vanilla", InternalId: "FUTURE_VANILLA", Block: 6, ModNum: 0),
            };
            foreach (var entry in configurations)
            {
                string json = JsonSerializer.Serialize(new
                {
                    Name = entry.Name, OptionName = entry.InternalId, OfficialName = entry.InternalId,
                    CharOffset = entry.Block, ModNum = entry.ModNum, Seed = "seed", Locked = false,
                    Ascension = Array.Empty<string>(),
                });
                object config = JsonSerializer.Deserialize(json, type)!;
                // The same configuration crosses the multiplayer JSON snapshot boundary.
                config = JsonSerializer.Deserialize(JsonSerializer.Serialize(config, type), type)!;
                Assert.Equal(entry.Name, type.GetProperty("Name")!.GetValue(config));
                Assert.Equal(entry.InternalId, type.GetProperty("OfficialName")!.GetValue(config));
                Assert.Equal(entry.Block, type.GetProperty("CharOffset")!.GetValue(config));
                Assert.Equal(entry.ModNum, type.GetProperty("ModNum")!.GetValue(config));
                long itemId = entry.Block * ArchipelagoIdCodec.BlockSize + 4;
                Assert.Equal(entry.Block, ArchipelagoIdCodec.GetAPCharacterNumberFromItemId(itemId));
                Assert.Equal(4, ArchipelagoIdCodec.GetCharacterItemTypeId(itemId));
                Assert.True(ArchipelagoIdCodec.TryComposeLocationId(79, entry.Block, out long locationId));
                Assert.Equal((entry.Block - 1) * ArchipelagoIdCodec.BlockSize + 79, locationId);
            }
        }
        finally
        {
            context.Unload();
        }
    }
}
