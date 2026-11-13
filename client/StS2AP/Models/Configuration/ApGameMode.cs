namespace StS2AP.Models;

/// <summary>
/// The slot's Game Mode option. The values are the APWorld's, sent as game_mode in slot data.
/// </summary>
public enum ApGameMode
{
    Standard = 0,

    /// <summary>The game's Dread mode. Only game versions with it can honour it.</summary>
    Dread = 1,
}
