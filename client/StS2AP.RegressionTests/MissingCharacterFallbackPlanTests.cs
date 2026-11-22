using StS2AP.Utils;
using Xunit;
using static StS2AP.Utils.MissingCharacterFallbackPlan;

namespace StS2AP.RegressionTests;

public sealed class MissingCharacterFallbackPlanTests
{
    private const string Defect = "DEFECT";
    private const long BlockSize = 10_000;

    private static readonly HashSet<string> Installed =
        new(["IRONCLAD", "SILENT", "DEFECT", "REGENT", "NECROBINDER"], StringComparer.OrdinalIgnoreCase);

    private static MissingCharacterFallbackPlan PlanFor(params SlotCharacter[] configured) =>
        Create(configured, Installed.Contains, Defect);

    // Character N's locations occupy the zero-based block N - 1.
    private static long Location(long characterNumber, long baseId) =>
        (characterNumber - 1) * BlockSize + baseId;

    [Fact(DisplayName = "A slot whose characters are all installed needs no fallback")]
    public void InstalledRosterNeedsNoFallback()
    {
        var plan = PlanFor(new("IRONCLAD", 1), new("SILENT", 2));

        Assert.False(plan.IsActive);
        Assert.Equal([Location(1, 5)], plan.WithMirroredLocations([Location(1, 5)]));
        Assert.Equal(["IRONCLAD"], plan.GoalCharacterIds("IRONCLAD"));
    }

    [Fact(DisplayName = "Defect borrows the first missing character when the slot has no Defect")]
    public void DefectBorrowsTheLowestNumberedMissingCharacter()
    {
        var plan = PlanFor(new("IRONCLAD", 1), new("WATCHER", 202), new("HERMIT", 201));

        Assert.True(plan.IsActive);
        Assert.Equal([new SlotCharacter("HERMIT", 201), new SlotCharacter("WATCHER", 202)], plan.MissingCharacters);
        Assert.Equal(new SlotCharacter("HERMIT", 201), plan.BorrowedCharacter);
    }

    [Fact(DisplayName = "A configured Defect keeps its own configuration")]
    public void ConfiguredDefectKeepsItsOwnConfiguration()
    {
        var plan = PlanFor(new("defect", 3), new("HERMIT", 201));

        Assert.Null(plan.BorrowedCharacter);
    }

    [Fact(DisplayName = "Blank character IDs count as missing")]
    public void BlankCharacterIdsCountAsMissing()
    {
        var plan = PlanFor(new("IRONCLAD", 1), new("", 11));

        Assert.Equal([new SlotCharacter("", 11)], plan.MissingCharacters);
    }

    [Fact(DisplayName = "Borrowed Defect checks are mirrored to every other missing character")]
    public void BorrowedDefectChecksMirrorToOtherMissingCharacters()
    {
        var plan = PlanFor(new("IRONCLAD", 1), new("HERMIT", 201), new("WATCHER", 202));

        Assert.Equal(
            [Location(201, 42), Location(202, 42)],
            plan.WithMirroredLocations([Location(201, 42)]));
    }

    [Fact(DisplayName = "A configured Defect's checks are mirrored to every missing character")]
    public void ConfiguredDefectChecksMirrorToAllMissingCharacters()
    {
        var plan = PlanFor(new("DEFECT", 3), new("HERMIT", 201), new("WATCHER", 202));

        Assert.Equal(
            [Location(3, 7), Location(201, 7), Location(202, 7)],
            plan.WithMirroredLocations([Location(3, 7)]));
    }

    [Fact(DisplayName = "Every playable character's checks are mirrored to the missing characters")]
    public void OtherCharactersChecksMirrorToMissingCharacters()
    {
        var plan = PlanFor(new("IRONCLAD", 1), new("DEFECT", 3), new("HERMIT", 201));

        Assert.Equal([Location(1, 7), Location(201, 7)], plan.WithMirroredLocations([Location(1, 7)]));
    }

    [Fact(DisplayName = "Reserved IDs are never mirrored")]
    public void ReservedIdsAreNotMirrored()
    {
        var plan = PlanFor(new("IRONCLAD", 1), new("HERMIT", 201));

        Assert.Equal([-1L], plan.WithMirroredLocations([-1L]));
    }

    [Fact(DisplayName = "A borrowed Defect win credits only the missing characters")]
    public void BorrowedDefectWinCreditsMissingCharacters()
    {
        var plan = PlanFor(new("IRONCLAD", 1), new("HERMIT", 201), new("", 11), new("WATCHER", 202));

        Assert.Equal(["HERMIT", "WATCHER"], plan.GoalCharacterIds("DEFECT"));
        Assert.Equal(["HERMIT", "WATCHER"], plan.RepresentedCharacterIds("DEFECT"));
    }

    [Fact(DisplayName = "Any character's win also credits the missing characters")]
    public void OtherCharactersWinCreditsMissingCharacters()
    {
        var plan = PlanFor(new("IRONCLAD", 1), new("HERMIT", 201), new("WATCHER", 202));

        Assert.Equal(["IRONCLAD", "HERMIT", "WATCHER"], plan.GoalCharacterIds("IRONCLAD"));
        Assert.Equal(["IRONCLAD"], plan.RepresentedCharacterIds("IRONCLAD"));
    }

    [Fact(DisplayName = "A configured Defect win credits Defect and every missing character")]
    public void ConfiguredDefectWinCreditsDefectAndMissingCharacters()
    {
        var plan = PlanFor(new("DEFECT", 3), new("HERMIT", 201));

        Assert.Equal(["Defect", "HERMIT"], plan.GoalCharacterIds("Defect"));
        Assert.Equal(["Defect"], plan.RepresentedCharacterIds("Defect"));
    }

    [Fact(DisplayName = "One win with the only playable character satisfies a whole-roster slot goal")]
    public void MirroredGoalsCompleteTheSlotGoal()
    {
        string[] roster = ["IRONCLAD", "HERMIT", "WATCHER"];
        var plan = PlanFor(new("IRONCLAD", 1), new("HERMIT", 201), new("WATCHER", 202));

        Assert.True(SlotGoalPolicy.IsComplete(plan.GoalCharacterIds("IRONCLAD"), roster, 0));
    }
}
