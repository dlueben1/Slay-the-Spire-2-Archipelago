using System.Diagnostics;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Entities.Rngs;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Modifiers;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Models;
using StS2AP.Patches;
using ShopRelicKind = StS2AP.Patches.Patches_RandomShopStock.ShopRelicKind;

namespace StS2AP.Utils;

/// <summary>One relic slot of a forecast shop.</summary>
/// <param name="Relic">The relic on sale, or null for an Ancient slot.</param>
/// <param name="IsAncient">
/// A Random Shop Relics slot that rolled Ancient. Its relic is picked from the shop's floor as
/// well as the seed, so it is not known before the shop is reached.
/// </param>
internal readonly record struct ForecastShopSlot(RelicModel? Relic, bool IsAncient);

/// <summary>What a seed gives a singleplayer run of the current character.</summary>
/// <param name="Menu">The relics the AP menu's first Relic items turn into, in order.</param>
/// <param name="Shop">The first normal shop's relic slots; empty where the game version has no forecast.</param>
internal sealed record RelicForecast(string Seed, RelicModel[] Menu, ForecastShopSlot[] Shop)
{
    /// <summary>False when the game's own relic bag code disagreed with the fast search.</summary>
    public bool Confirmed { get; init; } = true;

    /// <summary>
    /// For each rarity, the relics shops sell after the ones in <see cref="Shop"/>, in order.
    /// Later shops cannot be rolled ahead of time, because what the player buys moves the shop
    /// RNG, but whichever rarity a slot or a restock rolls, it takes that rarity's next relic.
    /// </summary>
    public IReadOnlyList<ShopQueue> ShopQueues { get; init; } = [];
}

/// <summary>The next relics of one rarity that shops sell, soonest first.</summary>
internal sealed record ShopQueue(RelicRarity Rarity, RelicModel[] Relics);

/// <summary>The relics a seed search looks for.</summary>
/// <param name="MenuCount">How many of the AP menu's first relics may hold the wanted ones.</param>
/// <param name="ShopCount">
/// How many shops a wanted shop relic may take to turn up: it is in the first shop, or among the
/// next <c>ShopCount - 1</c> relics of its rarity that shops sell.
/// </param>
internal sealed record RelicSeedQuery(
    IReadOnlyList<RelicModel> WantedInMenu,
    IReadOnlyList<RelicModel> WantedInShop,
    int MenuCount,
    int ShopCount = 1)
{
    /// <summary>How far down a rarity's shop queue a wanted shop relic may sit.</summary>
    public int ShopQueueDepth => ShopCount - 1;
}

/// <param name="Hits">The matching seeds shown, at most the number asked for.</param>
/// <param name="HitCount">Every matching seed among those tried, shown or not.</param>
internal sealed record RelicSeedSearchResult(
    IReadOnlyList<RelicForecast> Hits,
    int HitCount,
    RelicForecast? Closest,
    int ClosestMatched,
    long SeedsTried,
    TimeSpan Elapsed);

/// <summary>
/// Works out which relics a seed hands a singleplayer run, without starting the run.
/// </summary>
/// <remarks>
/// <para>
/// A new run shuffles every relic into the player's grab bag once, on the run's UpFront RNG.
/// After that a relic reward rolls a rarity on the player's Rewards RNG and takes that rarity's
/// first relic, and a shop rolls on the player's Shops RNG and takes relics from the back. The AP
/// menu's Relic items are ordinary relic rewards in singleplayer, so the menu's first relics and
/// the first shop's relics follow from the seed alone, as long as nothing else has drawn on those
/// two RNGs or emptied the bag first.
/// </para>
/// <para>
/// Shops after the first cannot be rolled ahead, since every purchase and restock draws on the
/// Shops RNG. The bag's order still holds: a slot that rolls a rarity sells that rarity's next
/// relic from the back, and a normal shop's last slot is always the next Shop relic. A forecast
/// lists that order per rarity.
/// </para>
/// <para>
/// A forecast is made twice over. <see cref="GameBag"/> runs the game's own
/// <see cref="RelicGrabBag"/>, and is what the console shows. <see cref="FastBag"/> repeats the
/// same shuffle on plain arrays so a search can try millions of seeds on every core. A search
/// compares the two before it starts and again for every seed it reports, so a game update that
/// changes the bag shows up as a refusal instead of as wrong seeds.
/// </para>
/// <para>
/// The run in progress supplies the character, the unlocks, the relics the run bans and the AP
/// shop settings, so a forecast is for a new run like the current one. Multiplayer is left out:
/// there the AP menu picks relics by hash, not from the bag's order.
/// </para>
/// </remarks>
internal sealed class RelicSeedForecast
{
    private const int SelfCheckSeeds = 12;
    private const int SelfCheckMenuCount = 8;
    private const int SeedsPerBatch = 16384;

    /// <summary>How many relics of each rarity a forecast lists beyond the first shop.</summary>
    public const int DefaultShopQueueDepth = 4;

    // A bag out of one rarity passes a pull to the next rarity up, Shop to Common to Uncommon to
    // Rare, so the queues are read from the top down and no reading disturbs one still to come.
    private static readonly RelicRarity[] ShopQueueReadOrder =
        [RelicRarity.Rare, RelicRarity.Uncommon, RelicRarity.Common, RelicRarity.Shop];

    private readonly Player _player;
    private readonly RunState _runState;
    private readonly int _playerSlotIndex;
    private readonly RelicModel _fallbackRelic;

    // The relics in the bag, and for each bag rarity the positions of its relics before the shuffle.
    private readonly RelicModel[] _relics;
    private readonly int[][] _dequeTemplates;
    private readonly RelicRarity[] _dequeRarities;
    // The shared bag is shuffled first, on the same RNG, one draw per rarity it holds.
    private readonly int _sharedDequeCount;
    // Relics the bag drops before any pull: not allowed in this run, or removed when it was created.
    private readonly bool[] _blocked;
    private readonly bool[] _allowedInShops;
    private readonly HashSet<RelicModel> _removedAtRunStart;

    // Random Shop Relics, when the slot has it on.
    private readonly Dictionary<ShopRelicKind, int>? _shopWeights;
    private readonly List<RelicModel> _eventRelicCandidates;

    private readonly ulong _upFrontNameHash = RngNameHash(RunRngType.UpFront.ToString());
    private readonly ulong _rewardsNameHash = RngNameHash(PlayerRngType.Rewards.ToString());
    private readonly ulong _shopsNameHash = RngNameHash(PlayerRngType.Shops.ToString());

    /// <summary>Whether this game version rolls a shop's relics before anything else in the shop.</summary>
    public static bool ForecastsShops =>
#if STS2_AT_LEAST_0_113_0
        true;
#else
        // Before 0.113 a shop rolls its cards first and its relic rarities on the Rewards RNG.
        false;
#endif

    public bool HasRandomShopRelics => _shopWeights != null;

    /// <summary>The relics the AP menu can hand out in a run like this one.</summary>
    public IEnumerable<RelicModel> MenuRelics => _relics.Where((relic, index) =>
        !_blocked[index] && relic.Rarity != RelicRarity.Shop);

    /// <summary>The relics a first shop can sell in a run like this one.</summary>
    public IEnumerable<RelicModel> ShopRelics
    {
        get
        {
            IEnumerable<RelicModel> fromBag = _relics.Where((_, index) => !_blocked[index] && _allowedInShops[index]);
            return _shopWeights?.ContainsKey(ShopRelicKind.Event) == true
                ? fromBag.Concat(_eventRelicCandidates)
                : fromBag;
        }
    }

    /// <summary>Reads everything a forecast needs from the run in progress. Main thread only.</summary>
    public RelicSeedForecast(Player player, RunState runState)
    {
        _player = player;
        _runState = runState;
        _playerSlotIndex = runState.GetPlayerSlotIndex(player);
        _fallbackRelic = RelicFactory.FallbackRelic;

        // As RelicGrabBag.Populate(Player, Rng) fills the bag.
        _relics = ModelDb.RelicPool<SharedRelicPool>().GetUnlockedRelics(player.UnlockState)
            .Concat(player.Character.RelicPool.GetUnlockedRelics(player.UnlockState))
            .Where(relic => RelicGrabBag._rarities.Contains(relic.Rarity))
            .ToArray();
        // The bag keeps one list per rarity, in the order it first meets each rarity.
        _dequeRarities = _relics.Select(relic => relic.Rarity).Distinct().ToArray();
        _dequeTemplates = _dequeRarities
            .Select(rarity => Enumerable.Range(0, _relics.Length).Where(index => _relics[index].Rarity == rarity).ToArray())
            .ToArray();
        _sharedDequeCount = SharedBagRelics().Select(relic => relic.Rarity).Distinct().Count();

        _removedAtRunStart = RelicsRemovedAtRunStart(runState);
        _blocked = _relics.Select(relic => !relic.IsAllowed(runState) || _removedAtRunStart.Contains(relic)).ToArray();
        _allowedInShops = _relics.Select(relic => relic.IsAllowedInShops).ToArray();

        _shopWeights = Patches_RandomShopStock.TryGetRelicWeights(player, out var weights, out _) ? weights : null;
        _eventRelicCandidates = Patches_RandomShopStock.EventRelicCandidates(player, new HashSet<ModelId>());
    }

    // As RunManager.InitializeNewRun fills the shared bag.
    private IEnumerable<RelicModel> SharedBagRelics() =>
        ModelDb.RelicPool<SharedRelicPool>().GetUnlockedRelics(_runState.UnlockState);

    /// <summary>
    /// Relics a modifier takes out of the bags when the run is created. The removal happens
    /// after the shuffle, so these keep their place in it and are then skipped.
    /// </summary>
    private static HashSet<RelicModel> RelicsRemovedAtRunStart(RunState runState)
    {
        var removed = new HashSet<RelicModel>();
        if (runState.Modifiers.Any(modifier => modifier is DeadlyEvents or ApDangerousUnknowns))
            removed.Add(ModelDb.Relic<JuzuBracelet>());
        return removed;
    }

    private static ulong RngNameHash(string rngTypeName) =>
        StringHelper.GetDeterministicHashCode(StringHelper.SnakeCase(rngTypeName));

    /// <summary>The forecast for one seed, from the game's own relic bag code. Main thread only.</summary>
    public RelicForecast Forecast(string seed, int menuCount, int shopQueueDepth = DefaultShopQueueDepth)
    {
        seed = SeedHelper.CanonicalizeSeed(seed);
        // The run and the player hash the seed themselves; they differ for old-format seeds.
        ulong runSeed = new RunRngSet(seed).Seed;
        ulong playerSeed = StringHelper.GetDeterministicHashCode(seed) + (ulong)_playerSlotIndex;
        return ForecastWith(new GameBag(this, runSeed), seed, playerSeed, menuCount, shopQueueDepth);
    }

    private RelicForecast ForecastWith(IBag bag, string seed, ulong playerSeed, int menuCount, int shopQueueDepth)
    {
        var menu = new RelicModel[menuCount];
        var shop = new ForecastShopSlot[ForecastsShops ? Patches_RandomShopStock.RelicSlotCount : 0];
        Roll(bag, playerSeed, menu, shop);
        List<ShopQueue> queues = ShopQueueReadOrder
            .Select(rarity => new ShopQueue(rarity, bag.TakeShopQueue(rarity, shopQueueDepth)))
            .Reverse()
            .ToList();
        return new RelicForecast(seed, menu, shop) { ShopQueues = queues };
    }

    /// <summary>
    /// Rolls the AP menu's first relics and the first shop's relics from a freshly shuffled bag.
    /// The menu comes first, as it does for a player who opens it at the start of the run.
    /// </summary>
    private void Roll(IBag bag, ulong playerSeed, RelicModel[] menu, ForecastShopSlot[] shop)
    {
        var rewards = new Rng(playerSeed + _rewardsNameHash);
        for (int index = 0; index < menu.Length; index++)
            menu[index] = bag.PullFromFront(RelicFactory.RollRarity(rewards)) ?? _fallbackRelic;

        if (shop.Length == 0)
            return;
        var shops = new Rng(playerSeed + _shopsNameHash);
        if (_shopWeights == null)
            RollVanillaShop(bag, shops, shop);
        else
            RollRandomShop(bag, shops, shop, _shopWeights);
    }

    // As MerchantInventory.GetDefaultRelicEntries: both rarities are rolled before either slot is
    // filled, and every slot rolls its price as it is filled.
    private void RollVanillaShop(IBag bag, Rng shops, ForecastShopSlot[] shop)
    {
        RelicRarity first = RelicFactory.RollRarity(shops);
        RelicRarity second = RelicFactory.RollRarity(shops);
        FillFromBag(bag, shops, shop, 0, first);
        FillFromBag(bag, shops, shop, 1, second);
        FillFromBag(bag, shops, shop, 2, RelicRarity.Shop);
    }

    // As Patches_RandomShopStock.PopulateRelics.
    private void RollRandomShop(IBag bag, Rng shops, ForecastShopSlot[] shop, Dictionary<ShopRelicKind, int> weights)
    {
        for (int slot = 0; slot < shop.Length; slot++)
        {
            ShopRelicKind kind = Patches_RandomShopStock.RollKind(shops, weights);
            if (kind == ShopRelicKind.Ancient)
            {
                // Picked by hash from the shop's floor, with no draw on this RNG but the price.
                shop[slot] = new ForecastShopSlot(null, IsAncient: true);
                RollPrice(shops);
                continue;
            }

            RelicModel? eventRelic = null;
            if (kind == ShopRelicKind.Event)
            {
                var onSale = shop.Take(slot).Select(sold => sold.Relic).ToHashSet();
                eventRelic = shops.NextItem(_eventRelicCandidates.Where(relic => !onSale.Contains(relic)));
            }
            if (eventRelic != null)
            {
                shop[slot] = new ForecastShopSlot(eventRelic, IsAncient: false);
                RollPrice(shops);
            }
            else
            {
                FillFromBag(bag, shops, shop, slot, Patches_RandomShopStock.GrabBagRarity(kind));
            }
        }
    }

    private void FillFromBag(IBag bag, Rng shops, ForecastShopSlot[] shop, int slot, RelicRarity rarity)
    {
        shop[slot] = new ForecastShopSlot(bag.PullFromBackForShop(rarity) ?? _fallbackRelic, IsAncient: false);
        RollPrice(shops);
    }

    // As MerchantRelicEntry.CalcCost; the price itself is not forecast, only its draw.
    private static void RollPrice(Rng shops) => shops.NextFloat(0.85f, 1.15f);

    /// <summary>
    /// Whether the fast search agrees with the game's own relic bag code on a sample of seeds.
    /// Main thread only.
    /// </summary>
    public bool FastSearchMatchesGame(out string mismatch)
    {
        var random = new System.Random(12345);
        for (int attempt = 0; attempt < SelfCheckSeeds; attempt++)
        {
            string seed = RandomSeed(random);
            RelicForecast game = Forecast(seed, SelfCheckMenuCount);
            RelicForecast fast = FastForecast(seed, SelfCheckMenuCount, DefaultShopQueueDepth);
            if (!SameRelics(game, fast))
            {
                mismatch = $"seed {seed}: game [{Describe(game)}], search [{Describe(fast)}]";
                return false;
            }
        }
        mismatch = "";
        return true;
    }

    private RelicForecast FastForecast(string seed, int menuCount, int shopQueueDepth)
    {
        ulong hash = StringHelper.GetDeterministicHashCode(seed);
        var bag = new FastBag(this);
        bag.Shuffle(hash);
        return ForecastWith(bag, seed, hash + (ulong)_playerSlotIndex, menuCount, shopQueueDepth);
    }

    private static bool SameRelics(RelicForecast left, RelicForecast right) =>
        left.Menu.SequenceEqual(right.Menu)
        && left.Shop.SequenceEqual(right.Shop)
        && left.ShopQueues.Count == right.ShopQueues.Count
        && left.ShopQueues.Zip(right.ShopQueues).All(pair =>
            pair.First.Rarity == pair.Second.Rarity && pair.First.Relics.SequenceEqual(pair.Second.Relics));

    /// <summary>A forecast as relic IDs, for logs and mismatch reports.</summary>
    public static string Describe(RelicForecast forecast) =>
        string.Join(", ", forecast.Menu.Select(relic => relic.Id.Entry))
        + " | "
        + string.Join(", ", forecast.Shop.Select(slot => slot.IsAncient ? "(ancient)" : slot.Relic!.Id.Entry))
        + string.Concat(forecast.ShopQueues.Select(queue =>
            $" | {queue.Rarity}: {string.Join(", ", queue.Relics.Select(relic => relic.Id.Entry))}"));

    /// <summary>
    /// Tries random seeds until enough of them give every wanted relic, or the time runs out.
    /// Both are checked between batches, so a search can run a little past either.
    /// Blocks the calling (main) thread while worker threads roll the seeds.
    /// </summary>
    public RelicSeedSearchResult Search(RelicSeedQuery query, TimeSpan budget, int maxHits)
    {
        var stopwatch = Stopwatch.StartNew();
        var random = new System.Random();
        var seeds = new string[SeedsPerBatch];
        var hashes = new ulong[SeedsPerBatch];
        var hitSeeds = new List<string>();
        var gate = new object();
        string? closestSeed = null;
        int closestMatched = -1;
        int wantedCount = query.WantedInMenu.Count + query.WantedInShop.Count;
        long tried = 0;

        while (stopwatch.Elapsed < budget && hitSeeds.Count < maxHits)
        {
            // The game's seed hash keeps a shared buffer, so every seed is hashed here, not on the workers.
            for (int index = 0; index < SeedsPerBatch; index++)
            {
                seeds[index] = RandomSeed(random);
                hashes[index] = StringHelper.GetDeterministicHashCode(seeds[index]);
            }

            Parallel.For(
                0,
                SeedsPerBatch,
                () => new SearchWorker(this, query),
                (index, _, worker) =>
                {
                    int matched = worker.CountMatches(hashes[index]);
                    if (matched == wantedCount || matched > Volatile.Read(ref closestMatched))
                    {
                        lock (gate)
                        {
                            if (matched == wantedCount)
                                hitSeeds.Add(seeds[index]);
                            if (matched > closestMatched)
                            {
                                closestMatched = matched;
                                closestSeed = seeds[index];
                            }
                        }
                    }
                    return worker;
                },
                _ => { });
            tried += SeedsPerBatch;
        }

        // Every seed shown goes back through the game's own code.
        List<RelicForecast> hits = hitSeeds.Take(maxHits).Select(seed => Confirm(seed, query)).ToList();
        RelicForecast? closest = hits.Count == 0 && closestSeed != null ? Confirm(closestSeed, query) : null;
        return new RelicSeedSearchResult(hits, hitSeeds.Count, closest, closestMatched, tried, stopwatch.Elapsed);
    }

    private RelicForecast Confirm(string seed, RelicSeedQuery query)
    {
        int depth = Math.Max(query.ShopQueueDepth, DefaultShopQueueDepth);
        RelicForecast game = Forecast(seed, query.MenuCount, depth);
        return game with { Confirmed = SameRelics(game, FastForecast(seed, query.MenuCount, depth)) };
    }

    /// <summary>A seed of the length and letters the game gives a new run.</summary>
    private static string RandomSeed(System.Random random) =>
        string.Create(SeedHelper.seedDefaultLength, random, static (span, random) =>
        {
            for (int index = 0; index < span.Length; index++)
                span[index] = SeedHelper._characters[random.Next(SeedHelper._characters.Length)];
        });

    private interface IBag
    {
        /// <summary>A relic reward's pull: the first relic of the rarity, or of the next one up.</summary>
        RelicModel? PullFromFront(RelicRarity rarity);

        /// <summary>A shop slot's pull: the last relic of the rarity that shops may sell.</summary>
        RelicModel? PullFromBackForShop(RelicRarity rarity);

        /// <summary>
        /// Takes the next relics shops would sell of exactly this rarity, soonest first; fewer
        /// when the rarity runs out.
        /// </summary>
        RelicModel[] TakeShopQueue(RelicRarity rarity, int count);
    }

    /// <summary>The player's bag for a seed, built and pulled from by the game's own code.</summary>
    private sealed class GameBag : IBag
    {
        private readonly RelicGrabBag _bag = new();
        private readonly RunState _runState;

        public GameBag(RelicSeedForecast forecast, ulong runSeed)
        {
            _runState = forecast._runState;
            // As RunManager.InitializeNewRun: the shared bag first, then the player's, on one RNG.
            var upFront = new Rng(runSeed + forecast._upFrontNameHash);
            new RelicGrabBag(refreshAllowed: false).Populate(forecast.SharedBagRelics(), upFront);
            _bag.Populate(forecast._player, upFront);
            foreach (RelicModel removed in forecast._removedAtRunStart)
                _bag.Remove(removed);
        }

        public RelicModel? PullFromFront(RelicRarity rarity) => _bag.PullFromFront(rarity, _runState);

        public RelicModel? PullFromBackForShop(RelicRarity rarity) =>
            _bag.PullFromBack(rarity, relic => relic.IsAllowedInShops, _runState);

        public RelicModel[] TakeShopQueue(RelicRarity rarity, int count)
        {
            var queue = new List<RelicModel>(count);
            while (queue.Count < count
                && PullFromBackForShop(rarity) is { } relic
                // A relic of another rarity means this one ran out and the bag moved up.
                && relic.Rarity == rarity)
            {
                queue.Add(relic);
            }
            return queue.ToArray();
        }
    }

    /// <summary>
    /// The same bag on arrays, reused from seed to seed. It reads only what the forecast captured
    /// on the main thread, so each search worker can own one.
    /// </summary>
    private sealed class FastBag : IBag
    {
        private readonly RelicSeedForecast _forecast;
        private readonly int[][] _deques;
        private readonly bool[] _taken;

        public FastBag(RelicSeedForecast forecast)
        {
            _forecast = forecast;
            _deques = forecast._dequeTemplates.Select(template => new int[template.Length]).ToArray();
            _taken = new bool[forecast._relics.Length];
        }

        /// <summary>Puts every relic back and shuffles the bag as a run with this seed hash does.</summary>
        public void Shuffle(ulong seedHash)
        {
            Array.Clear(_taken);
            var upFront = new Rng(seedHash + _forecast._upFrontNameHash);
            for (int shared = 0; shared < _forecast._sharedDequeCount; shared++)
                upFront.NextUnsignedLong();

            for (int dequeIndex = 0; dequeIndex < _deques.Length; dequeIndex++)
            {
                int[] deque = _deques[dequeIndex];
                Array.Copy(_forecast._dequeTemplates[dequeIndex], deque, deque.Length);
                // As ListExtensions.UnstableShuffle.
                var shuffleRng = new Rng(upFront.NextUnsignedLong());
                for (int last = deque.Length - 1; last > 0; last--)
                {
                    int other = shuffleRng.NextInt(last + 1);
                    (deque[other], deque[last]) = (deque[last], deque[other]);
                }
            }
        }

        public RelicModel? PullFromFront(RelicRarity rarity) => Pull(rarity, fromBack: false, forShop: false);

        public RelicModel? PullFromBackForShop(RelicRarity rarity) => Pull(rarity, fromBack: true, forShop: true);

        public RelicModel[] TakeShopQueue(RelicRarity rarity, int count)
        {
            var queue = new List<RelicModel>(count);
            int dequeIndex = Array.IndexOf(_forecast._dequeRarities, rarity);
            while (dequeIndex >= 0 && queue.Count < count && Take(dequeIndex, fromBack: true, forShop: true) is { } relic)
                queue.Add(relic);
            return queue.ToArray();
        }

        /// <summary>
        /// Whether a relic is among the next <paramref name="depth"/> relics of its rarity that
        /// shops would sell. Takes nothing out of the bag.
        /// </summary>
        public bool IsWithinShopQueue(int relic, int depth)
        {
            if (_taken[relic] || _forecast._blocked[relic] || !_forecast._allowedInShops[relic])
                return false;
            int dequeIndex = Array.IndexOf(_forecast._dequeRarities, _forecast._relics[relic].Rarity);
            int[] deque = _deques[dequeIndex];
            int ahead = 0;
            for (int position = deque.Length - 1; position >= 0 && ahead < depth; position--)
            {
                int other = deque[position];
                if (other == relic)
                    return true;
                if (!_taken[other] && !_forecast._blocked[other] && _forecast._allowedInShops[other])
                    ahead++;
            }
            return false;
        }

        // As RelicGrabBag.GetAvailableDeque: a rarity with nothing to give passes the pull upwards.
        private RelicModel? Pull(RelicRarity rarity, bool fromBack, bool forShop)
        {
            for (; rarity != RelicRarity.None; rarity = NextRarityUp(rarity))
            {
                int dequeIndex = Array.IndexOf(_forecast._dequeRarities, rarity);
                if (dequeIndex >= 0 && Take(dequeIndex, fromBack, forShop) is { } relic)
                    return relic;
            }
            return null;
        }

        private RelicModel? Take(int dequeIndex, bool fromBack, bool forShop)
        {
            int[] deque = _deques[dequeIndex];
            for (int step = 0; step < deque.Length; step++)
            {
                int relic = deque[fromBack ? deque.Length - 1 - step : step];
                if (_taken[relic] || _forecast._blocked[relic] || (forShop && !_forecast._allowedInShops[relic]))
                    continue;
                _taken[relic] = true;
                return _forecast._relics[relic];
            }
            return null;
        }

        private static RelicRarity NextRarityUp(RelicRarity rarity) => rarity switch
        {
            RelicRarity.Shop => RelicRarity.Common,
            RelicRarity.Common => RelicRarity.Uncommon,
            RelicRarity.Uncommon => RelicRarity.Rare,
            _ => RelicRarity.None,
        };
    }

    /// <summary>One thread's bag and result buffers for a search.</summary>
    private sealed class SearchWorker
    {
        private readonly RelicSeedForecast _forecast;
        private readonly RelicSeedQuery _query;
        private readonly FastBag _bag;
        private readonly RelicModel[] _menu;
        private readonly ForecastShopSlot[] _shop;

        public SearchWorker(RelicSeedForecast forecast, RelicSeedQuery query)
        {
            _forecast = forecast;
            _query = query;
            _bag = new FastBag(forecast);
            _menu = new RelicModel[query.MenuCount];
            _shop = new ForecastShopSlot[ForecastsShops ? Patches_RandomShopStock.RelicSlotCount : 0];
        }

        /// <summary>How many of the wanted relics a seed gives.</summary>
        public int CountMatches(ulong seedHash)
        {
            _bag.Shuffle(seedHash);
            _forecast.Roll(_bag, seedHash + (ulong)_forecast._playerSlotIndex, _menu, _shop);

            int matched = 0;
            foreach (RelicModel wanted in _query.WantedInMenu)
            {
                if (Array.IndexOf(_menu, wanted) >= 0)
                    matched++;
            }
            foreach (RelicModel wanted in _query.WantedInShop)
            {
                if (IsInFirstShop(wanted) || IsInShopQueue(wanted))
                    matched++;
            }
            return matched;
        }

        private bool IsInFirstShop(RelicModel wanted)
        {
            foreach (ForecastShopSlot slot in _shop)
            {
                if (slot.Relic == wanted)
                    return true;
            }
            return false;
        }

        // Event relics are not in the bag, so only a first shop can be known to sell one.
        private bool IsInShopQueue(RelicModel wanted)
        {
            int relic = _query.ShopQueueDepth > 0 ? Array.IndexOf(_forecast._relics, wanted) : -1;
            return relic >= 0 && _bag.IsWithinShopQueue(relic, _query.ShopQueueDepth);
        }
    }
}
