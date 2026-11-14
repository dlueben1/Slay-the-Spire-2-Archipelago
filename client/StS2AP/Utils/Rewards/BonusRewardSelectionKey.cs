using System.Security.Cryptography;
using System.Text;

namespace StS2AP.Utils;

internal static class BonusRewardSelectionKey
{
    private const string Domain = "sts2ap-bonus-relic-v1";

    internal static string Create(
        string runSeed,
        int ordinal,
        string relicId,
        int? playerSlotIndex = null,
        string category = "WAX_RELIC")
    {
        string playerComponent = playerSlotIndex.HasValue
            ? $"|PLAYER_SLOT:{playerSlotIndex.Value}"
            : string.Empty;
        // Wax relics keep the key they always had, so existing runs pick the same relic.
        string material = $"{Domain}|{runSeed}|{category}:{ordinal}{playerComponent}|{relicId}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }
}
