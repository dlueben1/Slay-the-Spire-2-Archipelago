namespace StS2AP.Persistence;

/// <summary>Names the one remote save owned by an AP slot participant and character.</summary>
internal static class RemoteSingleplayerSaveKey
{
    private const string Prefix = "StS2AP_RemoteSingleplayerSave_v1";

    internal static string For(string character, int playerNumber)
    {
        if (string.IsNullOrWhiteSpace(character))
            throw new ArgumentException("The remote-save character is required.", nameof(character));
        if (playerNumber is < 1 or > 4)
            throw new ArgumentOutOfRangeException(nameof(playerNumber));

        return $"{Prefix}_P{playerNumber}_{character}";
    }
}
