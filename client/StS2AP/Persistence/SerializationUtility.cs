using MegaCrit.Sts2.Core.Saves;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace StS2AP.Persistence;

public static class SerializationUtility
{
    public static JsonSerializerOptions CombinedOptions { get; }

    static SerializationUtility()
    {
        MegaCritSerializerContext megaResolver = MegaCritSerializerContext.Default;
        JsonSerializerOptions megaOptions = megaResolver.Options;

        CombinedOptions = new JsonSerializerOptions(megaOptions)
        {
            TypeInfoResolver = JsonTypeInfoResolver.Combine(
                megaResolver,
                ApSerializationContext.Default
            ),
            // Indented JSON otherwise uses the OS newline. These models travel between players,
            // so Windows and Linux or macOS must write the same text.
            NewLine = "\n",
        };
    }
}
