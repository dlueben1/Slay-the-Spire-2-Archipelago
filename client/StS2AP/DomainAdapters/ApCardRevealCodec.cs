using System.Text;
using System.Text.Json;

namespace StS2AP.DomainAdapters;

/// <summary>
/// Compares serialized card models by meaning rather than by text.
/// </summary>
internal static class ApCardRevealCodec
{
    /// <summary>
    /// Whether two ordered lists of serialized models mean the same thing. Players can write the
    /// same model as different text, so this compares the parsed JSON.
    /// </summary>
    internal static bool SameModels(IReadOnlyList<string> left, IReadOnlyList<string> right) =>
        left.Select(Canonicalize).SequenceEqual(right.Select(Canonicalize), StringComparer.Ordinal);

    private static string Canonicalize(string model)
    {
        using var document = JsonDocument.Parse(model);
        return Encoding.UTF8.GetString(CanonicalBytes(document.RootElement));
    }

    private static byte[] CanonicalBytes(JsonElement value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteCanonical(writer, value);
        return stream.ToArray();
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (JsonProperty property in value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (JsonElement element in value.EnumerateArray())
                    WriteCanonical(writer, element);
                writer.WriteEndArray();
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }
}
