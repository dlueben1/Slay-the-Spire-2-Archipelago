using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;

namespace StS2AP.Patches
{
    public static class ShopPageUtility
    {
        private const float SlideDuration = 0.4f;

        private static NMerchantInventory? _vanillaPage;
        private static NMerchantInventory? _apPage;
        private static Tween? _slideTween;
        private static Vector2 _vanillaHomePosition;
        private static Vector2 _apHomePosition;
        private static bool _hasHomePositions;

        /// <summary>True once an AP page has actually been spawned and registered for the current shop visit.</summary>
        public static bool HasPages { get; private set; }

        /// <summary>True when the AP page is the one currently scrolled into view.</summary>
        public static bool IsApPageFront { get; private set; }

        public static NMerchantInventory? VanillaPageInstance => _vanillaPage;
        public static NMerchantInventory? ApPageInstance => _apPage;

        /// <summary>Clears any registration left over from a previous shop room, before a new spawn attempt.</summary>
        internal static void Reset()
        {
            KillSlideTween();
            _vanillaPage = null;
            _apPage = null;
            _vanillaHomePosition = Vector2.Zero;
            _apHomePosition = Vector2.Zero;
            _hasHomePositions = false;
            IsApPageFront = false;
            HasPages = false;
        }

        internal static void Register(NMerchantInventory vanillaPage, NMerchantInventory apPage)
        {
            _vanillaPage = vanillaPage;
            _apPage = apPage;
            _vanillaHomePosition = vanillaPage.Position;
            _apHomePosition = vanillaPage.Position + new Vector2(vanillaPage.Size.X, 0f);
            _hasHomePositions = true;
            IsApPageFront = false;
            HasPages = true;
        }

        internal static void RecordHomePositions()
        {
            if (_vanillaPage == null || _apPage == null
                || !GodotObject.IsInstanceValid(_vanillaPage)
                || !GodotObject.IsInstanceValid(_apPage))
            {
                return;
            }

            _vanillaHomePosition = _vanillaPage.Position;
            _apHomePosition = _apPage.Position;
            _hasHomePositions = true;
        }

        internal static void ResetToVanillaPage()
        {
            KillSlideTween();

            if (_hasHomePositions
                && _vanillaPage != null
                && _apPage != null
                && GodotObject.IsInstanceValid(_vanillaPage)
                && GodotObject.IsInstanceValid(_apPage))
            {
                _vanillaPage.Position = _vanillaHomePosition;
                _apPage.Position = _apHomePosition;
            }

            IsApPageFront = false;
        }

        public static void ShowApPage() => Slide(toApPage: true);

        public static void ShowVanillaPage() => Slide(toApPage: false);

        private static void Slide(bool toApPage)
        {
            if (_vanillaPage == null || _apPage == null || !GodotObject.IsInstanceValid(_vanillaPage) || !GodotObject.IsInstanceValid(_apPage))
            {
                return;
            }
            if (toApPage == IsApPageFront)
            {
                return; // Already there, or already mid-transition to there.
            }

            if (!_hasHomePositions)
            {
                RecordHomePositions();
            }

            float width = _vanillaPage.Size.X;
            Vector2 pageOffset = new Vector2(width, 0f);
            Vector2 vanillaTarget = toApPage ? _vanillaHomePosition - pageOffset : _vanillaHomePosition;
            Vector2 apTarget = toApPage ? _apHomePosition - pageOffset : _apHomePosition;

            KillSlideTween();
            _slideTween = _vanillaPage.CreateTween().SetParallel();
            _slideTween.TweenProperty(_vanillaPage, "position", vanillaTarget, SlideDuration)
                .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
            _slideTween.TweenProperty(_apPage, "position", apTarget, SlideDuration)
                .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);

            IsApPageFront = toApPage;
        }

        private static void KillSlideTween()
        {
            if (_slideTween != null && GodotObject.IsInstanceValid(_slideTween))
            {
                _slideTween.Kill();
            }
            _slideTween = null;
        }
    }
}
