using System.Text.Json.Nodes;
using StS2AP.Persistence;
using Xunit;

namespace StS2AP.RegressionTests;

public sealed class ApCampaignCompatibilityTests
{
    private static JsonObject Save() => JsonNode.Parse("""
        {"_ritsulib":{"version":1,"run_saved_data":{"Archipelago":{
          "ap_run":{"schema":9,"kind":"run","data":{"SchemaVersion":9}},
          "ap_players":{"schema":9,"kind":"player","players":{
            "1":{"SchemaVersion":9,"Progress":{"card_assignments":{}}},
            "2":{"SchemaVersion":9,"Progress":{"card_assignments":{"42":{
              "materialization_strategy_id":"ap_rng_replicated_card_v1",
              "has_been_revealed":true,
              "serialized_cards":["{\"id\":\"CARD.A\"}"]}}}}
          }}
        }}}}
        """)!.AsObject();

    private static JsonObject Ap(JsonObject save) => save["_ritsulib"]!["run_saved_data"]!["Archipelago"]!.AsObject();
    private static JsonObject Card(JsonObject save) => Ap(save)["ap_players"]!["players"]!["2"]!["Progress"]!["card_assignments"]!["42"]!.AsObject();
    private static string? Error(JsonObject save) =>
        ApCampaignCompatibility.GetError(save.ToJsonString(), "Archipelago", 9, [1UL, 2UL]);

    [Fact]
    public void CurrentCampaignWithAnAssignedCardCanContinueWithoutMutatingItsPayload()
    {
        var save = Save();
        string before = save.ToJsonString();
        Assert.Null(Error(save));
        Assert.Equal(before, save.ToJsonString());
    }

    [Theory]
    [InlineData("run", 8)]
    [InlineData("run", 10)]
    [InlineData("players", 8)]
    [InlineData("players", 10)]
    [InlineData("shared", 8)]
    [InlineData("guest", 8)]
    [InlineData("remote", 8)]
    public void UnsupportedSchemasRefuseTheWholeCampaign(string target, int schema)
    {
        var save = Save();
        var ap = Ap(save);
        switch (target)
        {
            case "run": ap["ap_run"]!["schema"] = schema; break;
            case "players": ap["ap_players"]!["schema"] = schema; break;
            case "shared": ap["ap_run"]!["data"]!["SchemaVersion"] = schema; break;
            case "guest": ap["ap_players"]!["players"]!["1"]!["SchemaVersion"] = schema; break;
            case "remote": ap["ap_players"]!["players"]!["2"]!["SchemaVersion"] = schema; break;
        }
        Assert.Equal(ApCampaignCompatibility.Refusal, Error(save));
    }

    [Theory]
    [InlineData("run")]
    [InlineData("players")]
    [InlineData("shared")]
    [InlineData("remote")]
    public void MissingVersionsCannotDefaultToTheCurrentContract(string target)
    {
        var save = Save();
        var ap = Ap(save);
        switch (target)
        {
            case "run": ap["ap_run"]!.AsObject().Remove("schema"); break;
            case "players": ap["ap_players"]!.AsObject().Remove("schema"); break;
            case "shared": ap["ap_run"]!["data"]!.AsObject().Remove("SchemaVersion"); break;
            case "remote": ap["ap_players"]!["players"]!["2"]!.AsObject().Remove("SchemaVersion"); break;
        }
        Assert.Equal(ApCampaignCompatibility.Refusal, Error(save));
    }

    [Theory]
    [InlineData("ap_rng_owner_final_v1")]
    [InlineData("replica_native_v1")]
    [InlineData("")]
    public void PreviousCardContractsAreRefusedEvenWhenTheirSchemaNumberMatches(string strategy)
    {
        var save = Save();
        Card(save)["materialization_strategy_id"] = strategy;
        Assert.Equal(ApCampaignCompatibility.Refusal, Error(save));
    }

    [Fact]
    public void PreviousEffectReplayContractIsRefusedBeforeOpeningAPicker()
    {
        var save = Save();
        Card(save)["applied_effects"] = new JsonArray(new JsonObject { ["EffectId"] = "silken_tress_used_v1" });
        Assert.Equal(ApCampaignCompatibility.Refusal, Error(save));
    }

    [Fact]
    public void PreviousEmptyEffectFieldDoesNotChangeTheActiveCardContract()
    {
        var save = Save();
        Card(save)["applied_effects"] = new JsonArray();
        Assert.Null(Error(save));
    }

    [Fact]
    public void MissingRosterContributionRefusesTheWholeCampaign()
    {
        var save = Save();
        Ap(save)["ap_players"]!["players"]!.AsObject().Remove("2");
        Assert.Equal(ApCampaignCompatibility.Refusal, Error(save));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("broken")]
    public void MissingOrMalformedAPDataGetsAClearRefusal(string json) =>
        Assert.Equal(ApCampaignCompatibility.Refusal,
            ApCampaignCompatibility.GetError(json, "Archipelago", 9, [1UL]));

    [Fact]
    public void SaveDeletionRecognizesAnArchipelagoMultiplayerSaveAcrossSchemaVersions() =>
        Assert.True(LocalApSaveDeletion.ContainsArchipelagoRunData(
            """{"_ritsulib":{"run_saved_data":{"Archipelago":{"schema":1}}}}""",
            "Archipelago"
        ));

    [Theory]
    [InlineData("{}")]
    [InlineData("broken")]
    [InlineData("{\"_ritsulib\":{\"run_saved_data\":{\"AnotherMod\":{}}}}")]
    public void SaveDeletionPreservesUnknownOrNonArchipelagoMultiplayerSaves(string json) =>
        Assert.False(LocalApSaveDeletion.ContainsArchipelagoRunData(json, "Archipelago"));
}
