namespace StS2AP.Models
{
    /// <summary>
    /// Categories selected by the APWorld's remove_op_relics option. Slot data carries only
    /// these names; <c>OpRelicPolicy</c> decides which relics each category removes, so the
    /// exact relics follow the installed client version.
    /// </summary>
    [Flags]
    public enum OpRelicCategories
    {
        None = 0,
        Ancient = 1 << 0,
        Rare = 1 << 1,
    }

    public static class OpRelicCategoryNames
    {
        /// <summary>
        /// Converts slot-data category names into flags. Names this client does not know, such as
        /// a category added by a newer APWorld, are reported and ignored rather than rejecting
        /// the slot.
        /// </summary>
        public static OpRelicCategories Parse(
            IEnumerable<string?>? names,
            Action<string>? reportUnknown = null)
        {
            var categories = OpRelicCategories.None;
            if (names == null)
                return categories;

            foreach (string? name in names)
            {
                if (Enum.TryParse(name, ignoreCase: true, out OpRelicCategories category)
                    && category != OpRelicCategories.None
                    && Enum.IsDefined(category))
                {
                    categories |= category;
                }
                else
                {
                    reportUnknown?.Invoke(name ?? "<null>");
                }
            }
            return categories;
        }
    }
}
