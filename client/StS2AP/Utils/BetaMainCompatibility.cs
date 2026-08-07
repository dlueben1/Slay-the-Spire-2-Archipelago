using HarmonyLib;
using MegaCrit.Sts2.Core.Models;

namespace StS2AP.Utils;

/// <summary>
/// Bridges game API differences between the public 0.107.1 branch and newer beta branches.
/// Keep direct references to renamed or removed game types inside this reflection boundary.
/// </summary>
public static class BetaMainCompatibility
{
    private const string MainSaveCacheTypeName =
        "MegaCrit.Sts2.Core.Saves.Runs.SavedPropertiesTypeCache";

    private const string BetaSaveCacheTypeName =
        "MegaCrit.Sts2.Core.Multiplayer.Serialization.ModelIdSerializationCache";

    /// <summary>
    /// Registers a model type containing saved properties with either the public-branch
    /// SavedPropertiesTypeCache or the beta-branch ModelIdSerializationCache.
    /// </summary>
    public static void CacheSavedProperties(Type modelType)
    {
        // The public branch and beta branch moved this cache to different types.
        // Resolve by capability so this assembly does not bind to either cache type directly.
        Type cacheType = AccessTools.TypeByName(MainSaveCacheTypeName)
            ?? AccessTools.TypeByName(BetaSaveCacheTypeName)
            ?? throw new TypeLoadException(
                $"Could not find {MainSaveCacheTypeName} or {BetaSaveCacheTypeName}."
            );

        // Public 0.107.1 takes only the model type. The beta method added two context
        // parameters; BaseLib passes null for both when registering a standalone mod type.
        object?[] arguments = cacheType.FullName == MainSaveCacheTypeName
            ? [modelType]
            : [modelType, null, null];

        // CachePropertiesForType is private in the game, so use Harmony's reflection helper.
        var cacheMethod = AccessTools.Method(cacheType, "CachePropertiesForType")
            ?? throw new MissingMethodException(cacheType.FullName, "CachePropertiesForType");

        cacheMethod.Invoke(null, arguments);
        LogUtility.Info(
            $"Registered saved properties for {modelType.FullName} through " +
            $"{cacheType.Name}.{cacheMethod.Name}."
        );
    }

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
