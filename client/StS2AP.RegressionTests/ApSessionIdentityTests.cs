using System.Text.Json;
using System.Text.Json.Nodes;
using StS2AP.Utils;
using Xunit;

namespace StS2AP.RegressionTests;

public sealed class ApSessionIdentityTests
{
    private const string PersistedIdentity =
        """{"server_authority":"ap.example:38281","room_seed":"seed","ap_team_id":0,"ap_slot_id":1}""";

    [Theory]
    [InlineData("other.example:38281", "seed", 0, 1)]
    [InlineData("ap.example:38282", "seed", 0, 1)]
    [InlineData("ap.example:38281", "other-seed", 0, 1)]
    [InlineData("ap.example:38281", "seed", 1, 1)]
    [InlineData("ap.example:38281", "seed", 0, 2)]
    public void DifferentDestinationsCannotShareReconnectOrOutboxIdentity(
        string server, string seed, int team, int slot)
    {
        var expected = ApSessionIdentity.Create("ap.example:38281", "seed", 0, 1);
        var candidate = ApSessionIdentity.Create(server, seed, team, slot);

        Assert.NotEqual(expected, candidate);
        Assert.NotEqual(expected.GetFileKey(), candidate.GetFileKey());
    }

    [Fact]
    public void EquivalentAddressAndPersistedIdentityRemainEqual()
    {
        var expected = ApSessionIdentity.Create("ap.example:38281", "seed", 0, 1);
        var candidate = ApSessionIdentity.Create(" AP.EXAMPLE:38281/ ", "seed", 0, 1);
        var restored = JsonSerializer.Deserialize<ApSessionIdentity>(
            JsonSerializer.Serialize(candidate));

        Assert.Equal(expected, candidate);
        Assert.Equal(expected, restored);
        Assert.Equal(expected.GetFileKey(), candidate.GetFileKey());
    }

    [Fact]
    public void RunSlotEqualityExcludesServerButSessionEqualityIncludesIt()
    {
        var first = ApSessionIdentity.Create("first.example", "seed", 0, 1);
        var second = ApSessionIdentity.Create("second.example", "seed", 0, 1);

        Assert.NotEqual(first, second);
        Assert.Equal(first.Slot, second.Slot);
        Assert.Equal(ApSlotIdentity.Create("seed", 0, 1), first.Slot);
        Assert.NotEqual(ApSlotIdentity.Create("SEED", 0, 1), first.Slot);
        Assert.NotEqual(ApSlotIdentity.Create("seed", 1, 1), first.Slot);
        Assert.NotEqual(ApSlotIdentity.Create("seed", 0, 2), first.Slot);
    }

    [Fact]
    public void ExistingOutboxJsonAndFileKeyRemainCompatible()
    {
        string json = """{"schema_version":1,"identity":IDENTITY,"location_ids":[123,456]}"""
            .Replace("IDENTITY", PersistedIdentity);
        var outbox = JsonSerializer.Deserialize<PendingCheckOutbox>(json)!;

        Assert.Equal(PendingCheckOutbox.CurrentSchemaVersion, outbox.SchemaVersion);
        Assert.Equal(new long[] { 123, 456 }, outbox.LocationIds);
        Assert.Equal(PersistedIdentity, JsonSerializer.Serialize(outbox.Identity));
        // Digest of the previous length-prefixed canonical representation, before this refactor.
        Assert.Equal("04BC8208AA72E7AE5741A8523683805EB11D6808828EAE1882F5228CF290016E",
            outbox.Identity.GetFileKey());
        Assert.Equal("seed/ap-team-0/ap-slot-1@ap.example:38281", outbox.Identity.ToString());
    }

    [Theory]
    [InlineData("server_authority")]
    [InlineData("room_seed")]
    [InlineData("ap_team_id")]
    [InlineData("ap_slot_id")]
    public void MissingOrNullPersistedFieldsCannotConstructAnIdentity(string field)
    {
        var data = JsonNode.Parse(PersistedIdentity)!;
        data.AsObject().Remove(field);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ApSessionIdentity>(data.ToJsonString()));
        data[field] = null;
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ApSessionIdentity>(data.ToJsonString()));
    }

    [Theory]
    [InlineData("server_authority", "")]
    [InlineData("server_authority", "   ")]
    [InlineData("server_authority", "///")]
    [InlineData("room_seed", "")]
    [InlineData("room_seed", "   ")]
    public void DeserializationCannotBypassStringValidation(string field, string value)
    {
        var data = JsonNode.Parse(PersistedIdentity)!;
        data[field] = value;
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ApSessionIdentity>(data.ToJsonString()));
    }

    [Theory]
    [InlineData("ap_team_id")]
    [InlineData("ap_slot_id")]
    public void DeserializationCannotBypassNumericValidation(string field)
    {
        var data = JsonNode.Parse(PersistedIdentity)!;
        data[field] = -1;
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ApSessionIdentity>(data.ToJsonString()));
    }

    [Fact]
    public void PersistedAddressesUseTheSameNormalizationAsLiveConnections()
    {
        var data = JsonNode.Parse(PersistedIdentity)!;
        data["server_authority"] = " AP.EXAMPLE:38281/ ";
        var restored = JsonSerializer.Deserialize<ApSessionIdentity>(data.ToJsonString());
        Assert.Equal(ApSessionIdentity.Create("ap.example:38281", "seed", 0, 1), restored);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PendingCheckOutbox>(
            """{"schema_version":1,"identity":null,"location_ids":[]}"""));
    }

    [Theory]
    [InlineData(null, 0, 1)]
    [InlineData(" ", 0, 1)]
    [InlineData("seed", null, 1)]
    [InlineData("seed", -1, 1)]
    [InlineData("seed", 0, null)]
    [InlineData("seed", 0, -1)]
    public void IncompleteOrInvalidRunFieldsDoNotProduceASlotIdentity(string? seed, int? team, int? slot)
    {
        Assert.False(ApSlotIdentity.TryCreate(seed, team, slot, out var identity));
        Assert.Null(identity);
    }

    [Fact]
    public void CompleteRunFieldsProduceTheSameValidatedSlotIdentity()
    {
        Assert.True(ApSlotIdentity.TryCreate("seed", 0, 1, out var identity));
        Assert.Equal(ApSessionIdentity.Create("ap.example:38281", "seed", 0, 1).Slot, identity);
    }
}
