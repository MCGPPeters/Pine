using System.Text.Json.Serialization;

namespace Counter.Web.Browser;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false)]
[JsonSerializable(typeof(Command))]
public partial class SerializationContext: JsonSerializerContext
{
}


