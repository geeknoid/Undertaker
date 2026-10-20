using System.Text.Json;
using System.Text.Json.Serialization;

namespace Undertaker.Graph.Misc;

/// <summary>Converts <see cref="SkinnyString"/> values to and from JSON strings.</summary>
public class SkinnyStringConverter : JsonConverter<SkinnyString>
{
    /// <summary>Reads a JSON string. JSON null and non-string tokens are rejected.</summary>
    public override SkinnyString Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Expected a JSON string for SkinnyString.");
        }

        try
        {
            return new SkinnyString(reader.GetString()!);
        }
        catch (ArgumentException ex)
        {
            throw new JsonException("SkinnyString requires valid Unicode.", ex);
        }
        catch (InvalidOperationException ex)
        {
            throw new JsonException("SkinnyString requires valid Unicode.", ex);
        }
    }

    public override void Write(Utf8JsonWriter writer, SkinnyString value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
