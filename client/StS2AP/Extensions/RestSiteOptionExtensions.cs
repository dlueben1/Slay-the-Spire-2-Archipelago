using MegaCrit.Sts2.Core.Entities.RestSite;
using System.Reflection;

namespace StS2AP.Extensions
{
    public static class RestSiteOptionExtensions
    {
        /// <summary>
        /// Sets the IsEnabled state of a RestSiteOption, using reflection since the property is no longer directly writable.
        /// </summary>
        public static void SetIsEnabled(this RestSiteOption option, bool isEnabled)
        {
            try
            {
                // Try to find and set via property setter
                var isEnabledProperty = typeof(RestSiteOption)
                    .GetProperty("IsEnabled", BindingFlags.Public | BindingFlags.Instance);

                if (isEnabledProperty?.CanWrite ?? false)
                {
                    isEnabledProperty.SetValue(option, isEnabled);
                    return;
                }

                // If property setter doesn't work, try to set the backing field
                var backingField = typeof(RestSiteOption)
                    .GetField("_isEnabled", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? typeof(RestSiteOption).GetField("isEnabled", BindingFlags.NonPublic | BindingFlags.Instance);

                if (backingField != null)
                {
                    backingField.SetValue(option, isEnabled);
                    return;
                }

                LogUtility.Warn($"Could not find writable IsEnabled property or backing field on RestSiteOption");
            }
            catch (Exception ex)
            {
                LogUtility.Error($"Failed to set RestSiteOption.IsEnabled: {ex.Message}");
            }
        }
    }
}