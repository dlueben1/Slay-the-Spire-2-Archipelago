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
        int? playerSlotIndex = null)
    {
        string playerComponent = playerSlotIndex.HasValue
            ? $"|PLAYER_SLOT:{playerSlotIndex.Value}"
            : string.Empty;
        string material = $"{Domain}|{runSeed}|WAX_RELIC:{ordinal}{playerComponent}|{relicId}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }
}
