using Newtonsoft.Json.Linq;

namespace StS2AP.Utils;

internal static class ApSlotData
{
    internal static Dictionary<string, object> Normalize(IReadOnlyDictionary<string, object> slotData) =>
        slotData.ToDictionary(pair => pair.Key, pair => NormalizeValue(pair.Value));

    // Ordinary slots use characters at every APWorld version. Shared-slot worlds
    // may still provide the numbered-player fields; require both if either is present.
    internal static (int Count, int Number, JArray Characters) ReadPlayer(
        IReadOnlyDictionary<string, object> slotData, int selectedPlayer)
    {
        bool hasCount = slotData.TryGetValue("player_count", out object? countValue);
        bool hasPlayers = slotData.TryGetValue("players", out object? playersValue);
        if (hasCount != hasPlayers)
            throw new InvalidDataException("The AP slot is missing player_count or players.");

        int count = hasCount ? Convert.ToInt32(countValue) : 1;
        int number = hasPlayers ? selectedPlayer : 1;
        if (!CoopPlayerSelection.IsValid(count, number))
            throw new InvalidDataException($"Player {number} is outside this slot's player_count={count}. Change Shared-slot player number in Custom APWorld settings before connecting.");

        JArray? characters = hasPlayers
            ? (playersValue as JObject)?[number.ToString()] as JArray
            : slotData.GetValueOrDefault("characters") as JArray;
        if (characters == null)
            throw new InvalidDataException("The AP slot is missing the selected player's character configuration.");
        return (count, number, characters);
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
