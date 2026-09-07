namespace StS2AP.Models;

/// <summary>Native mirrored reward shapes currently supported by the AP reward menu.
/// CONFIRM: Gold is handled separately I think
/// </summary>
public enum ApMirroredRewardKind
{
    Card,
    Relic,
    Potion,
    Ancient,
    Unavailable,
}
