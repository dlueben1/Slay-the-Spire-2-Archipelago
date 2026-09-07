using System.Text.Json.Serialization;

namespace StS2AP.Persistence;

[JsonSerializable(typeof(SerializableAP))]
[JsonSerializable(typeof(ApRunProgressState))]
public partial class ApSerializationContext : JsonSerializerContext;
