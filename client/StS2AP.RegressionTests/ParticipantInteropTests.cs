using StS2AP.Domain;
using StS2AP.Multiplayer;
using Xunit;

namespace StS2AP.RegressionTests;

public sealed class ParticipantInteropTests
{
    [Theory]
    [InlineData(ApParticipationKind.VanillaGuest)]
    [InlineData(ApParticipationKind.OwnApSlot)]
    public void DomainKindsAgreeWithTheExistingWireEnum(ApParticipationKind wire)
    {
        var decoded = ParticipantKind.Decode((int)wire);
        Assert.True(decoded.IsOk);
        Assert.Equal((int)wire, decoded.ResultValue.WireValue);
    }

    [Fact]
    public void CSharpNullAndMissingNullableIdsAreWaitingInsteadOfImplicitVanilla()
    {
        Assert.Equal("missing-ap-contribution", Blocker(ContributionReadiness.Evaluate(9, null!)));
        var input = new ParticipantContributionInput
        {
            SchemaVersion = 9,
            Participation = new ParticipationInput { Kind = 1, RoomSeed = "seed", ApTeamId = 0 },
            HasSettings = true, ReceiptSourceReady = true,
            RelicReceipts = new Dictionary<long, IReadOnlyList<int>>(),
            ProgressiveAncients = new Dictionary<long, int>(),
        };
        Assert.Equal("incomplete-ap-identity", Blocker(ContributionReadiness.Evaluate(9, input)));
        input.Participation.ApSlotId = 1;
        Assert.Null(Blocker(ContributionReadiness.Evaluate(9, input)));
    }

    [Fact]
    public void ReadyIdentityDoesNotRetainMutableCSharpInputObjects()
    {
        var identity = new ParticipationInput { Kind = 1, RoomSeed = "seed", ApTeamId = 0, ApSlotId = 1 };
        var input = new ParticipantContributionInput
        {
            SchemaVersion = 9, Participation = identity,
            HasSettings = true, ReceiptSourceReady = true,
            RelicReceipts = new Dictionary<long, IReadOnlyList<int>>(),
            ProgressiveAncients = new Dictionary<long, int>(),
        };
        var ready = ContributionReadiness.Evaluate(9, input).Match(
            participant => participant, _ => throw new Exception("Waiting"), _ => throw new Exception("Rejected"));
        identity.Kind = 0;
        identity.RoomSeed = "different";
        input.HasSettings = false;
        input.ReceiptSourceReady = false;

        ready.Match(() => throw new Exception("Became vanilla"), own =>
        {
            Assert.Equal("seed", own.RoomSeed);
            return true;
        });
        identity.Kind = 1;
        Assert.Equal("ap-settings-incomplete", Blocker(ContributionReadiness.Evaluate(9, input)));
    }

    private static string? Blocker(ContributionReadiness readiness) => readiness.Match<string?>(
        _ => null, waiting => waiting.Code, rejected => rejected.Code);
}
