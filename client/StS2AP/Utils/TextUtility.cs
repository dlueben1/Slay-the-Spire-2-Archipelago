using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using System.Reflection;

namespace StS2AP.Utils;

/// <summary>
/// Allows you to inject localization strings into the game's localization tables.
/// Too much of MegaCrit's UI requires localized strings and we can't just put raw/fallback values, so this the workaround.
/// </summary>
public static class TextUtility
{
    /// <summary>
    /// Registers a new LocTable at runtime by injecting it into LocManager's private table dictionary.
    /// This takes effect immediately for any subsequent LocString lookups.
    /// Uses reflection because LocManager exposes no public "Add table" API.
    /// </summary>
    public static void RegisterLocTableAtRuntime(string tableName, Dictionary<string, string> entries, string? fallbackTableName = null)
    {
        if (LocManager.Instance == null)
        {
            throw new InvalidOperationException("LocManager.Instance is not initialized.");
        }

        FieldInfo? tablesField = typeof(LocManager).GetField("_tables", BindingFlags.Instance | BindingFlags.NonPublic);
        if (tablesField == null)
        {
            throw new InvalidOperationException("Could not find _tables field on LocManager (reflection failure).");
        }

        var tables = tablesField.GetValue(LocManager.Instance) as Dictionary<string, LocTable>;
        if (tables == null)
        {
            throw new InvalidOperationException("Unexpected _tables field type.");
        }

        LocTable? fallback = null;
        if (!string.IsNullOrEmpty(fallbackTableName) && tables.TryGetValue(fallbackTableName!, out var fb))
        {
            fallback = fb;
        }

        var copy = new Dictionary<string, string>(entries);
        var newTable = new LocTable(tableName, copy, fallback);
        tables[tableName] = newTable;
    }

    /// <summary>
    /// Inject a localized string into the specified localization table.
    /// </summary>
    /// <param name="key">The key to log</param>
    /// <param name="englishText">The English text for the localization string</param>
    /// <param name="tableName">The name of the localization table</param>
    public static void RegisterLocString(string key, string englishText, string tableName)
    {
        try
        {
            LocTable table = LocManager.Instance.GetTable(tableName);
            table.MergeWith(new Dictionary<string, string> { { key, englishText } });
        }
        catch (LocException)
        {
            // You'll need to use reflection or find another way to inject the table
            Log.Warn($"Loc table '{tableName}' not found. Consider pre-creating it.");
        }
    }

    /// <summary>
    /// Inject multiple localized strings into the specified localization table.
    /// </summary>
    /// <param name="text">A dictionary of key-value pairs representing the localized strings</param>
    /// <param name="tableName">The name of the localization table</param>
    public static void RegisterLocStrings(Dictionary<string, string> text, string tableName)
    {
        try
        {
            LocTable table = LocManager.Instance.GetTable(tableName);
            table.MergeWith(text);
        }
        catch (LocException)
        {
            // You'll need to use reflection or find another way to inject the table
            LogUtility.Error($"Loc table '{tableName}' not found. Consider pre-creating it.");
        }
    }

    /// <summary>
    /// Retrieve a string from a localization table using the specified key.
    /// </summary>
    /// <param name="key">The key to retrieve</param>
    /// <param name="tableName">The name of the localization table</param>
    /// <returns>The localized string</returns>
    public static LocString GetLocString(string key, string tableName)
    {
        return new LocString(tableName, key);
    }
}