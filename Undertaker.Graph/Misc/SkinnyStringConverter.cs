using System.Text.Json;
using System.Text.Json.Serialization;

namespace Undertaker.Graph.Misc;

public class SkinnyStringConverter : JsonConverter<SkinnyString>
{
    public override SkinnyString Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }

    public override void Write(Utf8JsonWriter writer, SkinnyString value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
