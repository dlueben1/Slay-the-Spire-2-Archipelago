using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.RestSite;
using StS2AP.Entities.RestSite;

namespace StS2AP.Patches;

/// <summary>
/// Boon campfire options borrow a base-game icon. The game derives every option's icon from its
/// ID through a private path, and all of its rest-site UI reads <see cref="RestSiteOption.Icon"/>.
/// </summary>
public static class Patches_BoonRestSiteIcons
{
    [HarmonyPatch(typeof(RestSiteOption), nameof(RestSiteOption.Icon), MethodType.Getter)]
    public static class Icon
    {
        [HarmonyPrefix]
        public static bool Prefix(RestSiteOption __instance, ref Texture2D __result)
        {
            if (__instance is not ApBoonRestSiteOption boonOption)
                return true;
            __result = PreloadManager.Cache.GetTexture2D(boonOption.BorrowedIconPath);
            return false;
        }
    }
}
