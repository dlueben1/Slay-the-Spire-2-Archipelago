using HarmonyLib;
using MegaCrit.Sts2.Core.Models;

namespace StS2AP.Utils;

/// <summary>
/// Bridges game API differences between the public 0.107.1 branch and newer beta branches.
/// Keep direct references to renamed or removed game types inside this reflection boundary.
/// </summary>
public static class BetaMainCompatibility
{
    /// <summary>
    /// Gets the selected local character without binding to LobbyPlayer, which was renamed
    /// to StartRunLobbyPlayer on the beta branch and changed StartRunLobby.LocalPlayer's
    /// binary return type.
    /// </summary>
    public static CharacterModel GetLocalCharacter(object lobby)
    {
        ArgumentNullException.ThrowIfNull(lobby);

        // LocalPlayer's declared return type changed from LobbyPlayer to
        // StartRunLobbyPlayer. Calling the property directly would bind this mod to
        // whichever return type was present in the sts2.dll used for compilation.
        object localPlayer = AccessTools.Property(lobby.GetType(), "LocalPlayer")?.GetValue(lobby)
            ?? throw new MissingMemberException(lobby.GetType().FullName, "LocalPlayer");

        // Both player types retain the same character field, so only the containing
        // player type needs to be resolved at runtime.
        return AccessTools.Field(localPlayer.GetType(), "character")?.GetValue(localPlayer) as CharacterModel
            ?? throw new InvalidCastException(
                $"Could not read a {nameof(CharacterModel)} from {localPlayer.GetType().FullName}.character."
            );
    }
}
