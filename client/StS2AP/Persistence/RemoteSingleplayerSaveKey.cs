namespace StS2AP.Persistence;

/// <summary>Names the one remote save owned by an AP slot and character.</summary>
internal static class RemoteSingleplayerSaveKey
{
    // "_P1" is the player segment from the removed shared-slot co-op. Every slot is now that
    // player, and keeping the segment keeps saves already stored on AP servers loadable.
    private const string Prefix = "StS2AP_RemoteSingleplayerSave_v1_P1";

    internal static string For(string character)
    {
        if (string.IsNullOrWhiteSpace(character))
            throw new ArgumentException("The remote-save character is required.", nameof(character));

        return $"{Prefix}_{character}";
    }
}
