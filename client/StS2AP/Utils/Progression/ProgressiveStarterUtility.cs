using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using StS2AP.Extensions;
using StS2AP.Domain;
using StS2AP.DomainAdapters;

namespace StS2AP.Utils
{
    /// <summary>
    /// Applies the two progressive starter tiers to the active run.
    ///
    /// The vanilla Orobas methods are deliberately the compatibility boundary: RitsuLib and
    /// BaseLib patch those methods for compatible modded characters. If the methods do not expose
    /// a real transformation, this utility leaves that character's starter untouched.
    /// </summary>
    public static class ProgressiveStarterUtility
    {
        private static readonly SemaphoreSlim ReconcileLock = new(1, 1);

        /// <summary>
        /// Returns whether an Ancient relic is reserved for AP's progressive starter tiers and
        /// therefore must not be offered by either the natural Ancient or an AP-built pool.
        /// </summary>
        internal static bool ShouldExcludeAncientRelic(
            RelicModel? relic,
            ArchipelagoSettings? settings = null)
        {
            settings ??= ArchipelagoClient.Settings;
            return relic switch
            {
                ArchaicTooth => settings?.ProgressiveStarterCard == true,
                TouchOfOrobas => settings?.ProgressiveStarterRelic == true,
                _ => false,
            };
        }

        /// <summary>
        /// Captures supported starter identities while the vanilla starting deck and relics are
        /// still present, then applies the tier already received from Archipelago.
        /// </summary>
        public static async Task InitializeForRun(Player player)
        {
            if (ArchipelagoClient.Settings?.ProgressiveStarterCard == true)
                CaptureStarterCard(player);

            if (ArchipelagoClient.Settings?.ProgressiveStarterRelic == true)
                CaptureStarterRelic(player);

            await ReconcileAsync(player, StarterContext.Initialization);
        }

        /// <summary>
        /// Incoming Archipelago items are processed off the Godot main thread. Defer all deck and
        /// relic mutations so the game commands execute on the main thread.
        /// </summary>
        public static void QueueReconcileCurrentPlayer()
        {
            Callable.From(() =>
            {
                if (!MultiplayerSupport.IsFeatureEnabled(MultiplayerFeature.ProgressiveStarters))
                    return;

                var player = GameUtility.CurrentPlayer;
                if (player != null)
                    TaskHelper.RunSafely(ReconcileAsync(player));
            }).CallDeferred();
        }

        private static void CaptureStarterCard(Player player)
        {
            try
            {
                var archaicTooth = (ArchaicTooth)ModelDb.Relic<ArchaicTooth>().ToMutable();
                var configured = archaicTooth.SetupForPlayer(player);
                var starterCardId = archaicTooth.StarterCard?.Id;
                var ancientCardId = archaicTooth.AncientCard?.Id;
                if (!configured || starterCardId == null || ancientCardId == null)
                {
                    LogUtility.Warn(
                        $"Progressive Starter Card is enabled, but {player.Character.Id.Entry} " +
                        "does not expose an Archaic Tooth starter-card mapping. Leaving its deck unchanged."
                    );
                    return;
                }

                var progress = ArchipelagoClient.Progress;
                progress.ProgressiveStarterCardBaseId = starterCardId.ToString();
                progress.ProgressiveStarterCardUpgradedId = ancientCardId.ToString();
                progress.ProgressiveStarterCardTier = ProgressiveStarterTier.Basic;
                LogUtility.Info(
                    $"Progressive Starter Card mapped {starterCardId} -> {ancientCardId} " +
                    $"for {player.Character.Id.Entry}."
                );
            }
            catch (Exception ex)
            {
                LogUtility.Warn(
                    $"Could not resolve an Archaic Tooth starter-card mapping for " +
                    $"{player.Character.Id.Entry}; leaving its deck unchanged. {ex.Message}"
                );
            }
        }

        private static void CaptureStarterRelic(Player player)
        {
            try
            {
                var touchOfOrobas = (TouchOfOrobas)ModelDb.Relic<TouchOfOrobas>().ToMutable();
                var configured = touchOfOrobas.SetupForPlayer(player);
                var starterRelicId = touchOfOrobas.StarterRelic;
                var upgradedRelicId = touchOfOrobas.UpgradedRelic;
                if (!configured || starterRelicId == null || upgradedRelicId == null)
                {
                    LogUtility.Warn(
                        $"Progressive Starter Relic is enabled, but {player.Character.Id.Entry} " +
                        "does not expose a Touch of Orobas starter-relic mapping. Leaving its relics unchanged."
                    );
                    return;
                }

                var progress = ArchipelagoClient.Progress;
                progress.ProgressiveStarterRelicBaseId = starterRelicId.ToString();
                progress.ProgressiveStarterRelicUpgradedId = upgradedRelicId.ToString();
                progress.ProgressiveStarterRelicTier = ProgressiveStarterTier.Basic;
                LogUtility.Info(
                    $"Progressive Starter Relic mapped {starterRelicId} -> {upgradedRelicId} " +
                    $"for {player.Character.Id.Entry}."
                );
            }
            catch (Exception ex)
            {
                LogUtility.Warn(
                    $"Could not resolve a Touch of Orobas starter-relic mapping for " +
                    $"{player.Character.Id.Entry}; leaving its relics unchanged. {ex.Message}"
                );
            }
        }

        private static async Task ReconcileAsync(Player player, StarterContext? context = null)
        {
            await ReconcileLock.WaitAsync();
            try
            {
                // A deferred item callback from a previous run must never mutate a new run.
                if (!ReferenceEquals(GameUtility.CurrentPlayer, player))
                    return;

                if (ArchipelagoClient.Settings?.ProgressiveStarterCard == true)
                {
                    try
                    {
                        await ReconcileStarterAsync(player, StarterKind.Card, context ?? StarterContext.Reconciliation);
                    }
                    catch (Exception ex)
                    {
                        LogUtility.Error(
                            $"Failed to reconcile progressive starter card for {player.Character.Id.Entry}: {ex}"
                        );
                    }
                }

                if (ArchipelagoClient.Settings?.ProgressiveStarterRelic == true)
                {
                    try
                    {
                        await ReconcileStarterAsync(player, StarterKind.Relic, context ?? StarterContext.Reconciliation);
                    }
                    catch (Exception ex)
                    {
                        LogUtility.Error(
                            $"Failed to reconcile progressive starter relic for {player.Character.Id.Entry}: {ex}"
                        );
                    }
                }
            }
            finally
            {
                ReconcileLock.Release();
            }
        }

        private static async Task ReconcileStarterAsync(Player player, StarterKind kind, StarterContext context)
        {
            var progress = ArchipelagoClient.Progress;
            var state = kind.Match(
                () => ProgressiveStarterAdapter.DecodeSingleplayer(kind, progress.ProgressiveStarterCardBaseId,
                    progress.ProgressiveStarterCardUpgradedId, progress.ProgressiveStarterCardTier),
                () => ProgressiveStarterAdapter.DecodeSingleplayer(kind, progress.ProgressiveStarterRelicBaseId,
                    progress.ProgressiveStarterRelicUpgradedId, progress.ProgressiveStarterRelicTier));
            var received = kind.Match(() => progress.ProgressiveStarterCards, () => progress.ProgressiveStarterRelics);
            var target = GetTargetTier(received, player);
            if (target == ProgressiveStarterTier.Unsupported)
                return; // No configured AP character offset: retain the existing fail-open behavior.
            int desired = state.Match(() => -1, () => -1, (_, _) => (int)target);
            var plan = ProgressiveStarterAdapter.Require(StarterProgression.Plan(state, state, context, desired));

            foreach (StarterOperation operation in plan.Operations)
            {
                bool applied = await state.Match(
                    () => Task.FromResult(false),
                    () => Task.FromResult(false),
                    (mapping, _) => mapping.Kind.Match(
                        () => ApplyCardOperation(player, mapping, operation),
                        () => ApplyRelicOperation(player, mapping, operation)));
                if (!applied)
                    return; // A missing native model leaves the prior applied state unchanged.

                state = ProgressiveStarterAdapter.Require(StarterProgression.AfterApplied(state, operation));
                var appliedTier = (ProgressiveStarterTier)state.AppliedWireValue;
                kind.Match(
                    () => progress.ProgressiveStarterCardTier = appliedTier,
                    () => progress.ProgressiveStarterRelicTier = appliedTier);
                state.Match(() => false, () => false, (mapping, tier) =>
                {
                    string modelId = operation.Match(() => mapping.BaseId, () => mapping.BaseId, () => mapping.UpgradedId);
                    LogTierTransition(kind.ToString(), player, appliedTier, modelId);
                    return true;
                });
            }
        }

        private static Task<bool> ApplyCardOperation(Player player, StarterMapping mapping, StarterOperation operation) =>
            operation.Match(
                async () =>
                {
                    var baseCard = FindDeckCard(player, mapping.BaseId);
                    if (baseCard == null)
                    {
                        LogUtility.Warn($"Could not find progressive starter card {mapping.BaseId} in the deck; leaving its current state unchanged.");
                        return false;
                    }
                    await CardPileCmd.RemoveFromDeck(baseCard, showPreview: false);
                    return true;
                },
                async () =>
                {
                    var baseCanonical = FindCanonicalCard(mapping.BaseId);
                    if (baseCanonical == null)
                    {
                        LogUtility.Warn($"Could not resolve progressive starter card {mapping.BaseId}; leaving its current state unchanged.");
                        return false;
                    }
                    var cardToAdd = player.RunState.CreateCard(baseCanonical, player);
                    var result = await CardPileCmd.Add(cardToAdd, PileType.Deck, skipVisuals: true);
                    if (!result.success)
                        throw new InvalidOperationException($"The game rejected starter card {cardToAdd.Id}.");
                    return true;
                },
                async () =>
                {
                    // The native Ancient owns transformations and BaseLib/RitsuLib compatibility.
                    var tooth = (ArchaicTooth)ModelDb.Relic<ArchaicTooth>().ToMutable();
                    if (!tooth.SetupForPlayer(player))
                        throw new InvalidOperationException("Archaic Tooth could not configure itself for the current starter card.");
                    await RelicCmd.Obtain(tooth, player);
                    if (FindOwnedRelic(player, tooth.Id.ToString()) == null)
                        throw new InvalidOperationException("The game did not add Archaic Tooth after receiving the upgraded starter-card tier.");
                    if (FindDeckCard(player, mapping.UpgradedId) == null)
                        LogUtility.Warn($"Archaic Tooth was obtained, but expected transformed starter card {mapping.UpgradedId} was not found.");
                    return true;
                });

        private static Task<bool> ApplyRelicOperation(Player player, StarterMapping mapping, StarterOperation operation) =>
            operation.Match(
                async () =>
                {
                    var baseRelic = FindOwnedRelic(player, mapping.BaseId);
                    if (baseRelic == null)
                    {
                        LogUtility.Warn($"Could not find progressive starter relic {mapping.BaseId}; leaving its current state unchanged.");
                        return false;
                    }
                    await RelicCmd.Remove(baseRelic);
                    return true;
                },
                async () =>
                {
                    var baseCanonical = FindCanonicalRelic(mapping.BaseId);
                    if (baseCanonical == null)
                    {
                        LogUtility.Warn($"Could not resolve progressive starter relic {mapping.BaseId}; leaving its current state unchanged.");
                        return false;
                    }
                    await RelicCmd.Obtain(baseCanonical.ToMutable(), player);
                    return true;
                },
                async () =>
                {
                    var touch = (TouchOfOrobas)ModelDb.Relic<TouchOfOrobas>().ToMutable();
                    if (!touch.SetupForPlayer(player))
                        throw new InvalidOperationException("Touch of Orobas could not configure itself for the current starter relic.");
                    await RelicCmd.Obtain(touch, player);
                    if (FindOwnedRelic(player, touch.Id.ToString()) == null)
                        throw new InvalidOperationException("The game did not add Touch of Orobas after receiving the upgraded starter-relic tier.");
                    if (FindOwnedRelic(player, mapping.UpgradedId) == null)
                        LogUtility.Warn($"Touch of Orobas was obtained, but expected upgraded starter relic {mapping.UpgradedId} was not found.");
                    return true;
                });

        private static void LogTierTransition(
            string kind,
            Player player,
            ProgressiveStarterTier tier,
            string modelId)
        {
            LogUtility.Success(
                $"Progressive Starter {kind} applied tier {tier} ({(int)tier}) for " +
                $"{player.Character.Id.Entry} (model: {modelId})."
            );
        }

        private static ProgressiveStarterTier GetTargetTier(
            Dictionary<long, int> received,
            Player player)
        {
            var offset = player.GetAPCharacterNumber();
            if (offset == null)
            {
                LogUtility.Warn(
                    $"Cannot reconcile progressive starters for unconfigured character " +
                    $"{player.Character.Id}; leaving its starting inventory unchanged."
                );
                return ProgressiveStarterTier.Unsupported;
            }

            received.TryGetValue(offset.Value, out var count);
            return ProgressiveStarterAdapter.ReceivedTier(count);
        }

        private static CardModel? FindDeckCard(Player player, string idEntry) =>
            player.Deck.Cards.FirstOrDefault(card =>
                string.Equals(card.Id.ToString(), idEntry, StringComparison.OrdinalIgnoreCase));

        private static RelicModel? FindOwnedRelic(Player player, string idEntry) =>
            player.Relics.FirstOrDefault(relic =>
                string.Equals(relic.Id.ToString(), idEntry, StringComparison.OrdinalIgnoreCase));

        private static CardModel? FindCanonicalCard(string idEntry) =>
            ModelDb.AllCards.FirstOrDefault(card =>
                string.Equals(card.Id.ToString(), idEntry, StringComparison.OrdinalIgnoreCase));

        private static RelicModel? FindCanonicalRelic(string idEntry) =>
            ModelDb.AllRelics.FirstOrDefault(relic =>
                string.Equals(relic.Id.ToString(), idEntry, StringComparison.OrdinalIgnoreCase));
    }
}
