using MegaCrit.Sts2.Core.Entities.Players;

namespace StS2AP.Extensions
{
    public static class PlayerExtensions
    {
        /// <summary>
        /// Returns the name of the current character, as their name appears in the Archipelago's APWorld.
        /// </summary>
        /// <example>An Ironclad instance returns "Ironclad", because items for that character include "Ironclad Card Reward", "Ironclad Relic", etc.</example>
        public static string APName(this Player player)
        {
            if (ApPlayerContextResolver.TryGetApCharacterName(
                    player,
                    out string name
                ))
            {
                return name;
            }

            string internalName = player.getInternalName();
            LogUtility.Warn(
                $"Could not resolve AP character name for player {player.NetId} "
                    + $"with character id '{internalName}'"
            );
            return internalName;
        }

        /// <summary>
        /// Returns this player's one-based AP character number using their multiplayer AP context.
        /// </summary>
        public static long? GetAPCharacterNumber(this Player player)
        {
            if (ApPlayerContextResolver.TryGetCharacterConfig(
                    player,
                    out CharacterConfig config
                ))
            {
                return config.CharOffset;
            }

            LogUtility.Warn(
                $"Could not resolve AP character number for player {player.NetId} "
                    + $"with character id '{player.getInternalName()}'"
            );
            return null;
        }

        /// <summary>
        /// What the game thinks the character's name is.
        /// </summary>
        public static string getInternalName(this Player player)
        {
            return player.Character.Id.Entry;
        }
    }
}
