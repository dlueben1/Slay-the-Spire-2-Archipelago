using Godot;
using MegaCrit.Sts2.Core.Models;

namespace StS2AP.Utils;

/// <summary>
/// Lists the internal IDs of installed modded characters for the Settings page and the
/// <c>ap characters</c> console command, and copies them in a form ready for a player YAML.
/// </summary>
internal static class InstalledCharacterIds
{
    private static readonly HashSet<string> VanillaCharacterIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "IRONCLAD", "SILENT", "REGENT", "DEFECT", "NECROBINDER",
    };

    public static string Status { get; private set; } =
        "Copies every installed modded character ID as a modded_characters YAML list.";

    /// <summary>Playable non-vanilla characters, in the game's registration order.</summary>
    public static IReadOnlyList<string> GetModdedIds() =>
        ModelDb.AllCharacters
            .Where(character => character.IsPlayable && !VanillaCharacterIds.Contains(character.Id.Entry))
            .Select(character => character.Id.Entry)
            .ToList();

    /// <summary>
    /// Formats the IDs as they would appear under the game's section of a player YAML,
    /// using the template's two-space option indentation.
    /// </summary>
    public static string ToYaml(IReadOnlyList<string> ids) =>
        "  modded_characters:\n" + string.Concat(ids.Select(id => $"    - \"{id}\"\n"));

    /// <summary>Copies the YAML list to the system clipboard. Call on the Godot main thread.</summary>
    public static bool TryCopyToClipboard(out string message)
    {
        IReadOnlyList<string> ids = GetModdedIds();
        if (ids.Count == 0)
        {
            message = Status = "No modded characters are installed, so nothing was copied.";
            return false;
        }

        try
        {
            DisplayServer.ClipboardSet(ToYaml(ids));
        }
        catch (Exception ex)
        {
            LogUtility.Error($"Could not copy modded character IDs to the clipboard: {ex}");
            message = Status = "Could not copy to the clipboard: " + ex.Message;
            return false;
        }

        message = Status = ids.Count == 1
            ? "Copied 1 modded character ID to the clipboard."
            : $"Copied {ids.Count} modded character IDs to the clipboard.";
        return true;
    }
}
