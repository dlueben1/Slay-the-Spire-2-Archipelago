using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Gold;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Rooms;

namespace StS2AP.Bingo.Detectors.Economy;

/// <summary>
/// Gold held and gold spent. Gold claimed from Archipelago goes through the game's
/// PlayerCmd.GainGold, so it counts like any other gold (user ruling).
/// </summary>
internal static class GoldDetectors
{
    // E14, M11, H09 "Have N gold at once": the player's gold after each gain, and at checkpoints.
    private static readonly (string Key, int Gold)[] GoldHeldSquares =
    {
        ("E14", 200),
        ("M11", 400),
        ("H09", 600),
    };

    // M12, H10 "Spend N gold in one run": the run record's GoldSpent summed over the run. The game
    // records shop purchases, card removals and the gold some events charge as spent; gold lost
    // or stolen is not. AP shop checks are charged as spent too.
    private static readonly (string Key, int Gold)[] GoldSpentInRunSquares =
    {
        ("M12", 600),
        ("H10", 1000),
    };

    // R-M05 "Spend 300 gold in one shop visit": GoldSpent of the shop's own map point in the run
    // record, so leaving and reloading inside the shop keeps the count the save has.
    private const string SpendInOneShopSquare = "R-M05";
    private const int SpendInOneShopGold = 300;

    // R-M03 "Reach the Act 2 boss without spending gold": nothing recorded as spent in the run
    // when the Act 2 boss room is entered.
    private const string NoSpendingSquare = "R-M03";
    private const int Act2Index = 1;

    public static void CheckGoldHeld(Player player)
    {
        foreach ((string key, int gold) in GoldHeldSquares)
            BingoSquares.Reach(key, player, player.Gold, gold);
    }

    public static void CheckGoldSpentInRun(Player player)
    {
        if (!EconomyDetector.AnyOpen(GoldSpentInRunSquares.Select(square => square.Key)))
            return;
        int spent = GoldSpentInRun(player);
        foreach ((string key, int gold) in GoldSpentInRunSquares)
            BingoSquares.Reach(key, player, spent, gold);
    }

    private static void CheckGoldSpentInShop(Player player)
    {
        if (!BingoSquares.IsOpen(SpendInOneShopSquare) || player.RunState.CurrentRoom is not MerchantRoom)
            return;
        int spentHere = EconomyDetector.CurrentPointRecord(player)?.GoldSpent ?? 0;
        BingoSquares.Reach(SpendInOneShopSquare, player, spentHere, SpendInOneShopGold);
    }

    public static void CheckReachedAct2BossWithoutSpending(Player player, AbstractRoom room)
    {
        if (!BingoSquares.IsOpen(NoSpendingSquare)
            || room.RoomType != RoomType.Boss
            || player.RunState.CurrentActIndex != Act2Index)
        {
            return;
        }
        EconomyDetector.CompleteIf(NoSpendingSquare, GoldSpentInRun(player) == 0, player);
    }

    private static int GoldSpentInRun(Player player) =>
        EconomyDetector.RunRecord(player).Sum(stats => stats.GoldSpent);

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterGoldGained))]
    private static class AfterGoldGained
    {
        [HarmonyPrefix]
        private static void Prefix(Player player) =>
            EconomyDetector.Run(nameof(AfterGoldGained), player, static player =>
            {
                if (LocalContext.IsMe(player))
                    CheckGoldHeld(player);
            });
    }

    /// <summary>
    /// LoseGold is synchronous: it adds the amount to the run record's GoldSpent and then lowers
    /// the gold, so a postfix sees the updated totals.
    /// </summary>
    [HarmonyPatch(typeof(PlayerCmd), nameof(PlayerCmd.LoseGold))]
    private static class AfterGoldLost
    {
        [HarmonyPostfix]
        private static void Postfix(Player player, GoldLossType goldLossType) =>
            EconomyDetector.Run(nameof(AfterGoldLost), (player, goldLossType), static args =>
            {
                (Player player, GoldLossType goldLossType) = args;
                if (goldLossType != GoldLossType.Spent || !LocalContext.IsMe(player))
                    return;
                CheckGoldSpentInRun(player);
                CheckGoldSpentInShop(player);
            });
    }
}
