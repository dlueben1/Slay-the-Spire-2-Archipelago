using Newtonsoft.Json.Linq;

namespace StS2AP.Utils;

internal static class ApSlotData
{
    internal static Dictionary<string, object> Normalize(IReadOnlyDictionary<string, object> slotData) =>
        slotData.ToDictionary(pair => pair.Key, pair => NormalizeValue(pair.Value));

    // Ordinary slots use characters at every APWorld version. The removed shared-slot co-op
    // gave players 2-4 extra 1,000,000-ID partitions; this client reads those IDs as
    // character blocks, so reject such slots instead of misreading their items and checks.
    internal static JArray ReadCharacters(IReadOnlyDictionary<string, object> slotData)
    {
        if (slotData.TryGetValue("player_count", out object? countValue) && Convert.ToInt32(countValue) != 1)
            throw new InvalidDataException(
                $"This AP slot was generated for shared-slot co-op (player_count={countValue}), "
                + "which this mod version no longer supports. Give each player their own AP slot.");

        return slotData.GetValueOrDefault("characters") as JArray
            ?? throw new InvalidDataException("The AP slot is missing its character configuration.");
    }

    private static object NormalizeValue(object value)
    {
        // Keep null, numbers, booleans and strings as they are. JSON objects and
        // arrays also need no conversion if they already use our Newtonsoft DLL.
        if (value is null or IConvertible or JToken)
            return value!;

        // MultiClient's remaining values are JSON objects or arrays from another
        // loaded copy of Newtonsoft. Ask that object to write its JSON, then read
        // the text with our copy so our JObject/JArray checks will work.
        // Do not use JsonConvert.SerializeObject(value) here: in the separate-DLL
        // test it wrote {"locked":true} as {"locked":[]} and lost the value.
        return JToken.Parse(value.ToString()!);
    }
}
