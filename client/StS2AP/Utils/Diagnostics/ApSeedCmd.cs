using System.Globalization;
using Godot;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace StS2AP.Utils.Diagnostics;

/// <summary>
/// Dev-console tools for picking a run's seed: set the seed of the next new run, see which
/// relics a seed gives the AP menu and the first shop, and search for a seed that gives chosen
/// relics. Choosing a seed is choosing the run's luck, so all of it is off in race rooms.
/// </summary>
public sealed class ApSeedCmd : AbstractConsoleCmd
{
    private const string SetArgument = "set";
    private const string ClearArgument = "clear";
    private const string RelicsArgument = "relics";
    private const string FindArgument = "find";
    private const string ShopPrefix = "shop:";
    private const string MenuOption = "menu=";
    private const string SecondsOption = "seconds=";
    private const string ShopsOption = "shops=";
    private const string NormalOption = "normal=";
    private const string ChanceOption = "chance=";

    private const int DefaultMenuCount = 5;
    private const int MaxMenuCount = 20;
    private const int DefaultSearchSeconds = 5;
    private const int MaxSearchSeconds = 60;
    private const int MaxHitsShown = 5;
    private const int MaxShopCount = 6;
    private const int MaxRewardCount = 20;
    private const int DefaultChancePercent = 50;

    private const string Usage =
        "ap_seed: shows the run's seed and the seed set for the next run\n"
        + "ap_seed set <seed>: the next new run uses this seed\n"
        + "ap_seed clear: forgets that seed\n"
        + "ap_seed relics [seed] [menu=N]: the AP menu's first N relics, the first shop's relics and the order "
        + "later shops sell relics in, for a seed\n"
        + "ap_seed find <relic>... [shop:<relic>...] [menu=N] [normal=N] [chance=P] [shops=N] [seconds=S]: "
        + "searches for a seed whose first N AP menu relics hold every <relic> and whose first shop sells every "
        + "shop:<relic>. With normal=N a <relic> may instead come from the next N relic rewards (Elites, or Relic "
        + "items claimed later), kept when the chance of that is at least P percent (default 50). With shops=N a "
        + "shop relic may also be one of the next N-1 of its rarity that shops sell";

    public override string CmdName => "ap_seed";

    public override string Args => "[set <seed> | clear | relics [seed] [menu=N] | find <relic>... [shop:<relic>...] [menu=N] [normal=N] [chance=P] [shops=N] [seconds=S]]";

    public override string Description =>
        "Sets the next run's seed, shows the relics a seed gives, and finds seeds that give chosen relics. Disabled in race rooms.";

    public override bool IsNetworked => false;

    public override CompletionResult GetArgumentCompletions(Player? player, string[] args)
    {
        string partial = args.Length == 0 ? "" : args[^1];
        string[] completed = args.Length == 0 ? [] : args[..^1];
        if (completed.Length == 0)
        {
            return CompleteArgument(
                [SetArgument, ClearArgument, RelicsArgument, FindArgument], [], partial, CompletionType.Subcommand);
        }
        if (!completed[0].Equals(FindArgument, StringComparison.OrdinalIgnoreCase))
            return base.GetArgumentCompletions(player, args);

        bool forShop = partial.StartsWith(ShopPrefix, StringComparison.OrdinalIgnoreCase);
        IEnumerable<string> ids = ModelDb.AllRelics
            .Where(relic => relic.Rarity is RelicRarity.Common or RelicRarity.Uncommon or RelicRarity.Rare
                || (forShop && relic.Rarity is RelicRarity.Shop or RelicRarity.Event))
            .Select(relic => forShop ? ShopPrefix + relic.Id.Entry : relic.Id.Entry);
        return CompleteArgument(ids, completed, partial);
    }

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        if (args.Length == 0)
            return new CmdResult(true, DescribeSeeds());

        if (ArchipelagoClient.IsConnected && ArchipelagoClient.IsRaceMode != false)
        {
            return new CmdResult(false, ArchipelagoClient.IsRaceMode == true
                ? "This command is disabled in race rooms."
                : "The room's race mode is unknown, so this command stays disabled.");
        }

        string[] rest = args[1..];
        return args[0].ToLowerInvariant() switch
        {
            SetArgument => SetSeed(rest),
            ClearArgument => ClearSeed(),
            RelicsArgument => ShowRelics(rest),
            FindArgument => FindSeeds(rest),
            _ => new CmdResult(false, Usage),
        };
    }

    private static string DescribeSeeds()
    {
        string current = RunManager.Instance.State is { } runState
            ? $"This run's seed: {runState.Rng.StringSeed}"
            : "No run in progress.";
        string pending = RunSeedOverride.Pending is { } seed
            ? $"The next new run will use seed {seed}."
            : "The next new run rolls its own seed.";
        return $"{current}\n{pending}\n{Usage}";
    }

    private static CmdResult SetSeed(string[] args)
    {
        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
            return new CmdResult(false, "Usage: ap_seed set <seed>");

        string seed = RunSeedOverride.Set(args[0]);
        LogUtility.Info($"ap_seed: the next new run will use seed {seed}");
        return new CmdResult(true,
            $"The next new run will use seed {seed}, then seeds are random again. "
            + "It replaces a slot's Seeded Run seed. In multiplayer only the host's seed counts.");
    }

    private static CmdResult ClearSeed()
    {
        RunSeedOverride.Clear();
        return new CmdResult(true, "The next new run rolls its own seed.");
    }

    private static CmdResult ShowRelics(string[] args)
    {
        if (!TryCreateForecast(out RelicSeedForecast? forecast, out RunState? runState, out string error))
            return new CmdResult(false, error);

        string? seed = null;
        int menuCount = DefaultMenuCount;
        foreach (string arg in args)
        {
            if (TryReadOption(arg, MenuOption, 1, MaxMenuCount, ref menuCount, out string? optionError))
            {
                if (optionError != null)
                    return new CmdResult(false, optionError);
            }
            else if (seed == null)
            {
                seed = arg;
            }
            else
            {
                return new CmdResult(false, "Usage: ap_seed relics [seed] [menu=N]");
            }
        }
        seed ??= RunSeedOverride.Pending ?? runState.Rng.StringSeed;

        RelicForecast result = forecast.Forecast(seed, menuCount);
        string message = $"{DescribeForecast(result)}\n{DescribeLimits(forecast, runState)}";
        LogUtility.Info($"ap_seed relics:\n{message}");
        return new CmdResult(true, message);
    }

    private static CmdResult FindSeeds(string[] args)
    {
        if (!TryCreateForecast(out RelicSeedForecast? forecast, out RunState? runState, out string error))
            return new CmdResult(false, error);

        var wantedInMenu = new List<RelicModel>();
        var wantedInShop = new List<RelicModel>();
        int menuCount = 0;
        int seconds = DefaultSearchSeconds;
        int shopCount = 1;
        int rewardCount = 0;
        int chancePercent = DefaultChancePercent;
        foreach (string arg in args)
        {
            if (TryReadOption(arg, MenuOption, 1, MaxMenuCount, ref menuCount, out string? optionError)
                || TryReadOption(arg, SecondsOption, 1, MaxSearchSeconds, ref seconds, out optionError)
                || TryReadOption(arg, ShopsOption, 1, MaxShopCount, ref shopCount, out optionError)
                || TryReadOption(arg, NormalOption, 1, MaxRewardCount, ref rewardCount, out optionError)
                || TryReadOption(arg, ChanceOption, 1, 100, ref chancePercent, out optionError))
            {
                if (optionError != null)
                    return new CmdResult(false, optionError);
                continue;
            }

            bool forShop = arg.StartsWith(ShopPrefix, StringComparison.OrdinalIgnoreCase);
            string name = forShop ? arg[ShopPrefix.Length..] : arg;
            List<RelicModel> candidates = (forShop ? forecast.ShopRelics : forecast.MenuRelics).ToList();
            if (!TryFindRelic(name, candidates, out RelicModel? relic, out string? relicError))
            {
                string source = forShop ? "this run's first shop" : "this character's AP menu";
                return new CmdResult(false, $"'{name}' is not a relic {source} can give. {relicError}");
            }
            List<RelicModel> wanted = forShop ? wantedInShop : wantedInMenu;
            if (!wanted.Contains(relic))
                wanted.Add(relic);
        }

        if (wantedInMenu.Count + wantedInShop.Count == 0)
            return new CmdResult(false, "Usage: ap_seed find <relic>... [shop:<relic>...] [menu=N] [normal=N] [chance=P] [shops=N] [seconds=S]");
        if (wantedInShop.Count > 0 && !RelicSeedForecast.ForecastsShops)
            return new CmdResult(false, "Shop relics can only be forecast on game version 0.113 or newer.");
        if (shopCount == 1 && wantedInShop.Count > Patches.Patches_RandomShopStock.RelicSlotCount)
            return new CmdResult(false, "A shop sells three relics. Use shops=N to look past the first shop.");
        if (menuCount == 0)
            menuCount = Math.Max(wantedInMenu.Count, 1);
        if (menuCount + rewardCount < wantedInMenu.Count)
        {
            return new CmdResult(false,
                $"{menuCount + rewardCount} relics cannot hold the {wantedInMenu.Count} asked for. Raise menu= or normal=.");
        }

        if (!forecast.FastSearchMatchesGame(out string mismatch))
        {
            LogUtility.Error($"ap_seed find: the search disagrees with the game's relic bag: {mismatch}");
            return new CmdResult(false,
                "The seed search no longer matches this game version's relic bag, so it would give wrong seeds. "
                + $"'ap_seed relics <seed>' still works. ({mismatch})");
        }

        var query = new RelicSeedQuery(
            wantedInMenu, wantedInShop, menuCount, shopCount, rewardCount, chancePercent / 100.0);
        RelicSeedSearchResult result = forecast.Search(query, TimeSpan.FromSeconds(seconds), MaxHitsShown);
        string message = DescribeSearch(result, query) + "\n" + DescribeLimits(forecast, runState);
        LogUtility.Info($"ap_seed find:\n{message}");
        return new CmdResult(result.Hits.Count > 0, message);
    }

    private static string DescribeSearch(RelicSeedSearchResult result, RelicSeedQuery query)
    {
        var lines = new List<string>
        {
            $"Tried {result.SeedsTried.ToString("N0", CultureInfo.InvariantCulture)} seeds in "
            + $"{result.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)} s.",
        };
        if (result.Hits.Count > 0)
        {
            // The search stops at its first few matches, so this is a rough figure.
            long seedsPerMatch = result.SeedsTried / result.HitCount;
            lines.Add($"{result.HitCount} matched: about 1 seed in {seedsPerMatch.ToString("N0", CultureInfo.InvariantCulture)}.");
            lines.AddRange(result.Hits.Select(DescribeForecast));
            string first = result.Hits[0].Seed;
            DisplayServer.ClipboardSet(first);
            lines.Add($"Use one with: ap_seed set {first} (copied to the clipboard)");
            return string.Join("\n", lines);
        }

        int wantedCount = query.WantedInMenu.Count + query.WantedInShop.Count;
        lines.Add("No seed gave every relic. Search longer with seconds=S, or allow more AP menu relics with menu=N.");
        if (result.Closest != null)
            lines.Add($"Closest ({result.ClosestMatched} of {wantedCount}): {DescribeForecast(result.Closest)}");
        return string.Join("\n", lines);
    }

    private static string DescribeForecast(RelicForecast forecast)
    {
        string menu = string.Join(", ", forecast.Menu.Select(RelicName));
        string shop = forecast.Shop.Length == 0
            ? "not forecast before game version 0.113"
            : string.Join(", ", forecast.Shop.Select(slot => slot.IsAncient ? "an Ancient relic" : RelicName(slot.Relic!)));
        string doubt = forecast.Confirmed ? "" : " (the game's relic bag disagrees with the search; do not trust this seed)";
        string chance = forecast.RewardChance is { } rewardChance
            ? $" ({rewardChance.ToString("P0", CultureInfo.InvariantCulture)} chance of the relics left to later rewards)"
            : "";
        string text = $"{forecast.Seed}{doubt}{chance}\n  AP menu: {menu}";
        string rewards = DescribeQueues(forecast.RewardQueues);
        if (rewards.Length > 0)
            text += $"\n  Relic rewards then give, in order: {rewards}";
        if (forecast.ChestRelics.Length > 0)
            text += $"\n  Chests that hand over a relic give: {string.Join(", ", forecast.ChestRelics.Select(RelicName))}";
        text += $"\n  First shop: {shop}";
        // Without a first shop forecast, the queues start at the first shop itself.
        string which = forecast.Shop.Length == 0 ? "Shops sell" : "Shops then sell";
        string queues = DescribeQueues(forecast.ShopQueues);
        if (queues.Length > 0)
            text += $"\n  {which}, in order: {queues}";
        return text;
    }

    private static string DescribeQueues(IReadOnlyList<RelicQueue> queues) => string.Join("; ", queues
        .Where(queue => queue.Relics.Length > 0)
        .Select(queue => $"{queue.Rarity}: {string.Join(", ", queue.Relics.Select(RelicName))}"));

    /// <summary>What a forecast assumes, so a relic that differs in the run is no surprise.</summary>
    private static string DescribeLimits(RelicSeedForecast forecast, RunState runState)
    {
        string limits =
            "For a new singleplayer run of this character. The AP menu's relics hold when its Relic items are "
            + "opened before any other relic reward; the shop's hold for the first normal shop. Later relic "
            + "rewards (Elites, Relic items claimed later) take the next relic of the rarity they roll, Common "
            + "50%, Uncommon 33%, Rare 17%, in the order listed. A chest that hands over its own relic follows "
            + "the chest list and skips a relic you already hold.";
        limits += forecast.HasRandomShopRelics
            ? " Later shops are not rolled ahead: each slot takes the next relic of the kind it rolls, in the "
                + "order listed. An Ancient shop slot is picked by the shop's floor."
            : " Later shops are not rolled ahead: each shop's last slot takes the next Shop relic, and its other "
                + "slots and any restock take the next relic of the rarity they roll, in the order listed.";
        if (runState.TotalFloor > 1)
            limits += $" Relic bans were read from this run at floor {runState.TotalFloor}; a new run may allow others.";
        return limits;
    }

    private static string RelicName(RelicModel relic) => relic.Title.GetFormattedText();

    /// <summary>
    /// A forecast needs a run in progress: it supplies the character, the unlocks and the AP
    /// settings that decide which relics the bag and the shops hold.
    /// </summary>
    private static bool TryCreateForecast(
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out RelicSeedForecast? forecast,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out RunState? runState,
        out string error)
    {
        forecast = null;
        runState = RunManager.Instance.State;
        Player? player = GameUtility.CurrentPlayer;
        if (runState == null || player == null)
        {
            error = "Start a run as the character first: relics are forecast for a new run like the current one.";
            return false;
        }
        if (MultiplayerSupport.IsRealMultiplayerRun)
        {
            error = "Relics are only forecast for singleplayer runs; a multiplayer AP menu picks its relics another way.";
            return false;
        }

        forecast = new RelicSeedForecast(player, runState);
        error = "";
        return true;
    }

    /// <summary>
    /// Reads <c>name=value</c>. Returns false when the argument is not this option; otherwise
    /// true, with <paramref name="error"/> set when the value is not a whole number in range.
    /// </summary>
    private static bool TryReadOption(string arg, string option, int min, int max, ref int value, out string? error)
    {
        error = null;
        if (!arg.StartsWith(option, StringComparison.OrdinalIgnoreCase))
            return false;
        if (int.TryParse(arg[option.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out int parsed)
            && parsed >= min
            && parsed <= max)
        {
            value = parsed;
        }
        else
        {
            error = $"{option} takes a whole number from {min} to {max}.";
        }
        return true;
    }

    /// <summary>Finds a relic by ID: the exact ID, or the one ID that starts with or holds the text.</summary>
    private static bool TryFindRelic(
        string text,
        IReadOnlyList<RelicModel> candidates,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out RelicModel? relic,
        out string? error)
    {
        string id = text.Trim().ToUpperInvariant().Replace(' ', '_').Replace('-', '_');
        error = null;
        relic = candidates.FirstOrDefault(candidate => candidate.Id.Entry == id);
        if (relic != null || id.Length == 0)
            return relic != null;

        List<RelicModel> matches = candidates.Where(candidate => candidate.Id.Entry.StartsWith(id, StringComparison.Ordinal)).ToList();
        if (matches.Count == 0)
            matches = candidates.Where(candidate => candidate.Id.Entry.Contains(id, StringComparison.Ordinal)).ToList();
        if (matches.Count == 1)
        {
            relic = matches[0];
            return true;
        }
        if (matches.Count > 1)
            error = "It could be: " + string.Join(", ", matches.Take(6).Select(match => match.Id.Entry)) + ".";
        return false;
    }
}
