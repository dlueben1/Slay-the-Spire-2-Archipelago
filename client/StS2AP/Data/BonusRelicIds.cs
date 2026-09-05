using System.Collections.Generic;
using System.Linq;

namespace StS2AP.Data
{
    /// <summary>
    /// Converts between the screaming-snake relic ids used in the shared JSON (`CHOSEN_CHEESE`)
    /// and the PascalCase ids the game reports via `relic.Id.Entry` (`ChosenCheese`). This is the
    /// single canonical conversion for specific values, pool whitelists, and the blacklist.
    /// </summary>
    public static class BonusRelicIds
    {
        /// <summary>
        /// Normalizes a relic id for comparison. Splits screaming-snake on underscores, title-cases
        /// each token, and joins without separators, so `CHOSEN_CHEESE` and `TheBoot` both reduce to
        /// their canonical PascalCase form (`ChosenCheese`, `TheBoot`). Comparison is then done
        /// case-insensitively by callers.
        /// </summary>
        public static string Normalize(string? id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return string.Empty;

            return string.Concat(
                id.Trim()
                    .Split('_')
                    .Where(token => token.Length > 0)
                    .Select(token =>
                        char.ToUpperInvariant(token[0]) + token.Substring(1).ToLowerInvariant())
            );
        }
    }
}
