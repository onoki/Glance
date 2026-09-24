using System.Text.Json;
using System.Text.Json.Serialization;
namespace Glance.Server;

// PATCH fields distinguish omission (null nullable) from an explicit JSON null.
public sealed class ExplicitJsonNullConverter : JsonConverter<JsonElement?>
{
    public override bool HandleNull => true;
    public override JsonElement? Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return document.RootElement.Clone();
    }
    public override void Write(Utf8JsonWriter writer, JsonElement? value, JsonSerializerOptions options)
    {
        if (value.HasValue) value.Value.WriteTo(writer); else writer.WriteNullValue();
    }
}
