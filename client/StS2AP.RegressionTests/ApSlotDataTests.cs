using System.Reflection;
using System.Runtime.Loader;
using Newtonsoft.Json.Linq;
using StS2AP.Utils;
using Xunit;

namespace StS2AP.RegressionTests;

public sealed class ApSlotDataTests
{
    [Theory]
    [InlineData("1.1.2")]
    [InlineData("2.2.0")]
    [InlineData("3.0.0")]
    public void OrdinarySlotsUsePlayerOneRegardlessOfVersionOrSavedSelection(string version)
    {
        var characters = JArray.Parse("[{\"name\":\"Ironclad\"}]");
        var slot = new Dictionary<string, object>
        {
            ["mod_compat_version"] = version, ["characters"] = characters,
        };
        var player = ApSlotData.ReadPlayer(slot, selectedPlayer: 4);
        Assert.Equal(1, player.Count);
        Assert.Equal(1, player.Number);
        Assert.Same(characters, player.Characters);
    }

    [Fact]
    public void SharedSlotsRetainPlayerSelectionAndRejectIncompleteData()
    {
        var characters = JArray.Parse("[{\"name\":\"Silent\"}]");
        var slot = new Dictionary<string, object>
        {
            ["player_count"] = 2L, ["players"] = new JObject { ["2"] = characters },
        };
        var player = ApSlotData.ReadPlayer(slot, selectedPlayer: 2);
        Assert.Equal(2, player.Count);
        Assert.Equal(2, player.Number);
        Assert.Same(characters, player.Characters);
        Assert.Throws<InvalidDataException>(() => ApSlotData.ReadPlayer(slot, 3));
        Assert.Throws<InvalidDataException>(() => ApSlotData.ReadPlayer(slot, 1));
        slot.Remove("player_count");
        Assert.Throws<InvalidDataException>(() => ApSlotData.ReadPlayer(slot, 2));
        slot.Clear();
        slot["player_count"] = 1;
        Assert.Throws<InvalidDataException>(() => ApSlotData.ReadPlayer(slot, 1));
        slot.Clear();
        Assert.Throws<InvalidDataException>(() => ApSlotData.ReadPlayer(slot, 1));
    }

    [Fact]
    public void SettingsContainersFromAnotherNewtonsoftCopyKeepTheirValues()
    {
        // previous implementations would make it so that JObject's from diffrent assembly load contexts
        // the is JObject check would silently fail which isn't what we want
        var context = new AssemblyLoadContext("foreign-slot-data", isCollectible: true);
        try
        {
            Assembly foreignJson = context.LoadFromAssemblyPath(typeof(JToken).Assembly.Location);
            object Parse(string type, string json) => foreignJson.GetType($"Newtonsoft.Json.Linq.{type}")!
                .GetMethod("Parse", [typeof(string)])!.Invoke(null, [json])!;
            const string characters = """
                [{"name":"Ironclad","option_name":"ironclad","char_offset":1,
                  "official_name":"IRONCLAD","seed":"123","locked":true,"mod_num":0,
                  "ascension":["ToughEnemies"],"optional":null}]
                """;
            object players = Parse("JObject", "{\"1\":" + characters + "}");
            object legacyCharacters = Parse("JArray", characters);
            Assert.False(players is JObject);
            Assert.False(legacyCharacters is JArray);
            Assert.NotSame(typeof(JObject).Assembly, players.GetType().Assembly);

            var input = new Dictionary<string, object>
            {
                ["players"] = players,
                ["characters"] = legacyCharacters,
                ["player_count"] = 1L,
                ["seeded"] = true,
                ["mod_compat_version"] = "2.4.0",
                ["json_string"] = "{\"keep\":\"as text\"}",
                ["null"] = null!,
                ["shop_sanity_options"] = Parse("JObject", """{"card_slots":2,"card_remove":false}"""),
                ["bonus_items"] = Parse("JArray", """[{"WAX_RELIC":{"Pools":["Common","Rare"]}},{"WAX_RELIC":{"Value":"THE_BOOT"}}]"""),
                ["empty"] = Parse("JArray", "[]"),
            };
            var normalized = ApSlotData.Normalize(input);
            var localPlayers = Assert.IsType<JObject>(normalized["players"]);
            Assert.True(JToken.DeepEquals(JArray.Parse(characters), localPlayers["1"]));
            Assert.True(JToken.DeepEquals(localPlayers["1"], Assert.IsType<JArray>(normalized["characters"])));
            var shop = Assert.IsType<JObject>(normalized["shop_sanity_options"]);
            Assert.Equal(2, (int)shop["card_slots"]!);
            Assert.False((bool)shop["card_remove"]!);
            var bonus = Assert.IsType<JArray>(normalized["bonus_items"]);
            Assert.Equal(new[] { "Common", "Rare" }, bonus[0]["WAX_RELIC"]!["Pools"]!.Values<string>());
            Assert.Equal("THE_BOOT", (string?)bonus[1]["WAX_RELIC"]!["Value"]);
            Assert.Empty(Assert.IsType<JArray>(normalized["empty"]));
            foreach (string key in new[] { "player_count", "seeded", "mod_compat_version", "json_string", "null" })
                Assert.Same(input[key], normalized[key]);
            Assert.Same(players, input["players"]);
            Assert.Same(localPlayers, ApSlotData.Normalize(normalized)["players"]);
        }
        finally
        {
            context.Unload();
        }
    }
}
