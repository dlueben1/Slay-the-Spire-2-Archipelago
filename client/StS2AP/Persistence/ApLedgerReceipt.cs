using StS2AP.Data;
using static StS2AP.Data.ItemTable;

namespace StS2AP.Persistence;

/// <summary>
/// One AP item a player received, as every peer's receipt ledger records it: the AP history index
/// (one-based) and the item id, which encodes the item type and its character.
/// </summary>
public sealed class ApLedgerReceipt
{
    public int Index { get; set; }
    public long ItemId { get; set; }

    /// <summary>Whether this is a character item of <paramref name="type"/> for that character.</summary>
    public bool IsCharacterItem(APItem type, long characterOffset) =>
        ArchipelagoIdCodec.IsCharacterItemId(ItemId)
        && (APItem)ArchipelagoIdCodec.GetCharacterItemTypeId(ItemId) == type
        && ArchipelagoIdCodec.GetAPCharacterNumberFromItemId(ItemId) == characterOffset;

    /// <summary>Whether this is the universal (character-less) item <paramref name="type"/>.</summary>
    public bool IsUniversalItem(APItem type) =>
        ArchipelagoIdCodec.IsUniversalItemId(ItemId) && (APItem)ItemId == type;
}
