using Godot;

namespace StS2AP.Models;

/// <summary>
/// An AP relic whose icon is drawn with a colour tint, so borrowed base-game art stays
/// recognisable as AP's own (see <c>Patches_BoonRelicTint</c>).
/// </summary>
public interface IApTintedRelic
{
    /// <summary>The colour the icon is multiplied by.</summary>
    Color IconTint { get; }

    /// <summary>The shared tint of Boon relics: a light Archipelago blue.</summary>
    public static readonly Color BoonTint = new(0.55f, 0.85f, 1f);
}
