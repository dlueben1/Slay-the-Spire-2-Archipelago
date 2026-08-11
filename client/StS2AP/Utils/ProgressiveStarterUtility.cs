using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using StS2AP.Extensions;
using StS2AP.Models;

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
        /// Captures supported starter identities while the vanilla starting deck and relics are
        /// still present, then applies the tier already received from Archipelago.
        /// </summary>
        public static async Task InitializeForRun(Player player)
        {
            if (ArchipelagoClient.Settings?.ProgressiveStarterCard == true)
                CaptureStarterCard(player);

            if (ArchipelagoClient.Settings?.ProgressiveStarterRelic == true)
                CaptureStarterRelic(player);

            await ReconcileAsync(player);
        }

        /// <summary>
        /// Incoming Archipelago items are processed off the Godot main thread. Defer all deck and
        /// relic mutations so the game commands execute on the main thread.
        /// </summary>
        public static void QueueReconcileCurrentPlayer()
        {
            Callable.From(() =>
            {
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

        private static async Task ReconcileAsync(Player player)
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
                        await ReconcileCardAsync(player);
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
                        await ReconcileRelicAsync(player);
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

        private static async Task ReconcileCardAsync(Player player)
        {
            var progress = ArchipelagoClient.Progress;
            if (progress.ProgressiveStarterCardBaseId == null ||
                progress.ProgressiveStarterCardUpgradedId == null ||
                progress.ProgressiveStarterCardTier == ProgressiveStarterTier.Unsupported)
            {
                return;
            }

            var targetTier = GetTargetTier(progress.ProgressiveStarterCards, player);
            if (targetTier == ProgressiveStarterTier.Unsupported)
                return;
            var currentTier = progress.ProgressiveStarterCardTier;
            if (targetTier == currentTier)
                return;

            // A new run is constructed with its vanilla tier-one starter before AP reconciliation.
            // This is the only expected downward transition; received AP items only increase later.
            if (currentTier == ProgressiveStarterTier.Basic && targetTier == ProgressiveStarterTier.None)
            {
                var baseCard = FindDeckCard(player, progress.ProgressiveStarterCardBaseId);
                if (baseCard == null)
                {
                    LogUtility.Warn(
                        $"Could not find progressive starter card {progress.ProgressiveStarterCardBaseId} " +
                        "in the deck; leaving its current state unchanged."
                    );
                    return;
                }

                await CardPileCmd.RemoveFromDeck(baseCard, showPreview: false);
                progress.ProgressiveStarterCardTier = ProgressiveStarterTier.None;
                LogTierTransition("Card", player, ProgressiveStarterTier.None, progress.ProgressiveStarterCardBaseId);
                return;
            }

            if (currentTier == ProgressiveStarterTier.None &&
                targetTier is ProgressiveStarterTier.Basic or ProgressiveStarterTier.Upgraded)
            {
                var baseCanonical = FindCanonicalCard(progress.ProgressiveStarterCardBaseId);
                if (baseCanonical == null)
                {
                    LogUtility.Warn(
                        $"Could not resolve progressive starter card {progress.ProgressiveStarterCardBaseId}; " +
                        "leaving its current state unchanged."
                    );
                    return;
                }

                var cardToAdd = player.RunState.CreateCard(baseCanonical, player);
                var addResult = await CardPileCmd.Add(
                    cardToAdd,
                    PileType.Deck,
                    skipVisuals: true
                );
                if (!addResult.success)
                    throw new InvalidOperationException($"The game rejected starter card {cardToAdd.Id}.");

                currentTier = ProgressiveStarterTier.Basic;
                progress.ProgressiveStarterCardTier = currentTier;
                LogTierTransition("Card", player, currentTier, cardToAdd.Id.ToString());
            }

            if (currentTier == ProgressiveStarterTier.Basic && targetTier == ProgressiveStarterTier.Upgraded)
            {
                // Compatibility test point: grant the actual Ancient instead of reproducing its
                // transformation. This delegates upgrade/enchantment preservation, visual feedback,
                // and BaseLib/RitsuLib patches to Archaic Tooth's normal obtain behavior.
                var archaicTooth = (ArchaicTooth)ModelDb.Relic<ArchaicTooth>().ToMutable();
                if (!archaicTooth.SetupForPlayer(player))
                {
                    throw new InvalidOperationException(
                        "Archaic Tooth could not configure itself for the current starter card."
                    );
                }
                await RelicCmd.Obtain(archaicTooth, player);

                if (FindOwnedRelic(player, archaicTooth.Id.ToString()) == null)
                {
                    throw new InvalidOperationException(
                        "The game did not add Archaic Tooth after receiving the upgraded starter-card tier."
                    );
                }

                progress.ProgressiveStarterCardTier = ProgressiveStarterTier.Upgraded;
                if (FindDeckCard(player, progress.ProgressiveStarterCardUpgradedId) == null)
                {
                    LogUtility.Warn(
                        $"Archaic Tooth was obtained, but expected transformed starter card " +
                        $"{progress.ProgressiveStarterCardUpgradedId} was not found."
                    );
                }
                LogTierTransition(
                    "Card",
                    player,
                    ProgressiveStarterTier.Upgraded,
                    progress.ProgressiveStarterCardUpgradedId
                );
            }
        }

        private static async Task ReconcileRelicAsync(Player player)
        {
            var progress = ArchipelagoClient.Progress;
            if (progress.ProgressiveStarterRelicBaseId == null ||
                progress.ProgressiveStarterRelicUpgradedId == null ||
                progress.ProgressiveStarterRelicTier == ProgressiveStarterTier.Unsupported)
            {
                return;
            }

            var targetTier = GetTargetTier(progress.ProgressiveStarterRelics, player);
            if (targetTier == ProgressiveStarterTier.Unsupported)
                return;
            var currentTier = progress.ProgressiveStarterRelicTier;
            if (targetTier == currentTier)
                return;

            // As with cards, the run initially contains the vanilla tier-one relic. Removing it for
            // target tier zero is initialization, not a reversal of received AP progression.
            if (currentTier == ProgressiveStarterTier.Basic && targetTier == ProgressiveStarterTier.None)
            {
                var baseRelic = FindOwnedRelic(player, progress.ProgressiveStarterRelicBaseId);
                if (baseRelic == null)
                {
                    LogUtility.Warn(
                        $"Could not find progressive starter relic {progress.ProgressiveStarterRelicBaseId}; " +
                        "leaving its current state unchanged."
                    );
                    return;
                }

                await RelicCmd.Remove(baseRelic);
                progress.ProgressiveStarterRelicTier = ProgressiveStarterTier.None;
                LogTierTransition("Relic", player, ProgressiveStarterTier.None, progress.ProgressiveStarterRelicBaseId);
                return;
            }

            if (currentTier == ProgressiveStarterTier.None &&
                targetTier is ProgressiveStarterTier.Basic or ProgressiveStarterTier.Upgraded)
            {
                var relicCanonical = FindCanonicalRelic(progress.ProgressiveStarterRelicBaseId);
                if (relicCanonical == null)
                {
                    LogUtility.Warn(
                        $"Could not resolve progressive starter relic {progress.ProgressiveStarterRelicBaseId}; " +
                        "leaving its current state unchanged."
                    );
                    return;
                }

                await RelicCmd.Obtain(relicCanonical.ToMutable(), player);
                currentTier = ProgressiveStarterTier.Basic;
                progress.ProgressiveStarterRelicTier = currentTier;
                LogTierTransition("Relic", player, currentTier, relicCanonical.Id.ToString());
            }

            if (currentTier == ProgressiveStarterTier.Basic && targetTier == ProgressiveStarterTier.Upgraded)
            {
                // Compatibility test point: grant Touch itself so its normal obtain behavior owns
                // the replacement and any BaseLib/RitsuLib compatibility patches. If a modded
                // starter behaves unexpectedly, this is the isolated call to disable while testing.
                var touchOfOrobas = (TouchOfOrobas)ModelDb.Relic<TouchOfOrobas>().ToMutable();
                if (!touchOfOrobas.SetupForPlayer(player))
                {
                    throw new InvalidOperationException(
                        "Touch of Orobas could not configure itself for the current starter relic."
                    );
                }
                await RelicCmd.Obtain(touchOfOrobas, player);

                if (FindOwnedRelic(player, touchOfOrobas.Id.ToString()) == null)
                {
                    throw new InvalidOperationException(
                        "The game did not add Touch of Orobas after receiving the upgraded starter-relic tier."
                    );
                }

                progress.ProgressiveStarterRelicTier = ProgressiveStarterTier.Upgraded;
                if (FindOwnedRelic(player, progress.ProgressiveStarterRelicUpgradedId) == null)
                {
                    LogUtility.Warn(
                        $"Touch of Orobas was obtained, but expected upgraded starter relic " +
                        $"{progress.ProgressiveStarterRelicUpgradedId} was not found."
                    );
                }
                LogTierTransition(
                    "Relic",
                    player,
                    ProgressiveStarterTier.Upgraded,
                    progress.ProgressiveStarterRelicUpgradedId
                );
            }
        }

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
            var offset = player.Character.GetCharacterOffset();
            if (offset == null)
            {
                LogUtility.Warn(
                    $"Cannot reconcile progressive starters for unconfigured character " +
                    $"{player.Character.Id}; leaving its starting inventory unchanged."
                );
                return ProgressiveStarterTier.Unsupported;
            }

            received.TryGetValue(offset.Value, out var count);
            return (ProgressiveStarterTier)Math.Clamp(count, 0, 2);
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
