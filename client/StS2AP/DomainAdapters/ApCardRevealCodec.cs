using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace StS2AP.DomainAdapters;

/// <summary>
/// Verifies independently generated offers. This carries no card payload or instructions to
/// mutate relics: each replica must already have the same ordered card offer.
/// </summary>
internal static class ApCardRevealCodec
{
    private const int DigestWords = 8;

    internal static List<int> Encode(ApMirroredRewardSpec spec, bool firstReveal = true)
    {
        if (spec.Kind != ApMirroredRewardKind.Card || !spec.CardHasBeenRevealed
            || spec.SerializedModels.Count == 0
            || spec.MaterializationStrategyId != "ap_rng_replicated_card_v1")
            throw new InvalidOperationException("Cannot verify an unfinished or non-replicated AP card offer.");
        _ = MirroredRewardAdapter.Decode(spec, 3);
        // Parse model JSON so object property order is irrelevant; array order remains
        // significant for picker indexes and any ordered card data.
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            spec.ApSlotId, spec.ReceivedItemIndex, spec.OwnerNetId,
            spec.IsRareCardReward, spec.CardRewardActIndex, spec.CardCanReroll, FirstReveal = firstReveal,
            Cards = spec.SerializedModels.Select(model => JsonSerializer.Deserialize<JsonElement>(model)),
        }));
        byte[] digest = SHA256.HashData(CanonicalBytes(document.RootElement));
        var result = new List<int>(DigestWords);
        for (int offset = 0; offset < digest.Length; offset += sizeof(int))
            result.Add(BinaryPrimitives.ReadInt32LittleEndian(digest.AsSpan(offset, sizeof(int))));
        return result;
    }

    internal static void Verify(IReadOnlyList<int> local, IReadOnlyList<int> owner, string receipt)
    {
        if (local.Count != DigestWords || owner.Count != DigestWords
            || !local.SequenceEqual(owner))
            throw new InvalidOperationException($"Replicated AP card offer {receipt} disagreed with the owner "
                + "(receipt or card offer). No picker choice was applied.");
    }

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
