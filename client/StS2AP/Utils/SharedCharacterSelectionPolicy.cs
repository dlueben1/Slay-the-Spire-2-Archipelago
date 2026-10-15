namespace StS2AP.Utils;

internal static class SharedCharacterSelectionPolicy
{
    // Opening a menu must find a playable family member where possible. Explicit
    // arrow clicks can still display a configured, locked member and its lock.
    internal static T? Choose<T>(IReadOnlyList<T> choices, T current,
        Func<T, bool> isUnlocked, bool preferUnlocked) where T : class
    {
        if (choices.Contains(current) && (!preferUnlocked || isUnlocked(current)))
            return current;

        return choices.FirstOrDefault(isUnlocked)
            ?? (choices.Contains(current) ? current : choices.FirstOrDefault());
    }
}
