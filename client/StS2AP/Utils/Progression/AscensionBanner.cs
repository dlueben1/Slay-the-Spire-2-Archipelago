namespace StS2AP.Utils;

internal static class AscensionBanner
{
    public static string FormatLevels(IEnumerable<int> levels)
    {
        int[] sorted = levels.Distinct().Order().ToArray();
        if (sorted.Length == 0)
            return "A0";

        var ranges = new List<string>();
        for (int index = 0; index < sorted.Length; index++)
        {
            int start = sorted[index];
            while (index + 1 < sorted.Length && sorted[index + 1] == sorted[index] + 1)
                index++;

            int end = sorted[index];
            ranges.Add(start == end ? $"A{start}" : $"A{start}-A{end}");
        }

        return string.Join(" + ", ranges);
    }
}
