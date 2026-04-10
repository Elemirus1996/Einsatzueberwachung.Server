using System.Text.Json;
using System.Text.Json.Serialization;

namespace Einsatzueberwachung.Server.Services;

/// <summary>
/// JsonConverter für (double Latitude, double Longitude) Tuples.
/// Serialisiert als { "Latitude": ..., "Longitude": ... }
/// </summary>
public sealed class CoordinateTupleConverter : JsonConverter<(double Latitude, double Longitude)>
{
    public override (double Latitude, double Longitude) Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException();

        double lat = 0, lng = 0;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                return (lat, lng);

            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                var prop = reader.GetString();
                reader.Read();
                if (string.Equals(prop, "Latitude", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(prop, "Item1", StringComparison.OrdinalIgnoreCase))
                    lat = reader.GetDouble();
                else if (string.Equals(prop, "Longitude", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(prop, "Item2", StringComparison.OrdinalIgnoreCase))
                    lng = reader.GetDouble();
            }
        }
        throw new JsonException();
    }

    public override void Write(Utf8JsonWriter writer, (double Latitude, double Longitude) value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("Latitude", value.Latitude);
        writer.WriteNumber("Longitude", value.Longitude);
        writer.WriteEndObject();
    }
}

/// <summary>
/// JsonConverter für nullable (double Latitude, double Longitude)? Tuples (z.B. ElwPosition).
/// </summary>
public sealed class NullableCoordinateTupleConverter : JsonConverter<(double Latitude, double Longitude)?>
{
    private static readonly CoordinateTupleConverter _inner = new();

    public override (double Latitude, double Longitude)? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        return _inner.Read(ref reader, typeof((double, double)), options);
    }

    public override void Write(Utf8JsonWriter writer, (double Latitude, double Longitude)? value, JsonSerializerOptions options)
    {
        if (value is null)
            writer.WriteNullValue();
        else
            _inner.Write(writer, value.Value, options);
    }
}

/// <summary>
/// JsonConverter für List&lt;(double Latitude, double Longitude)&gt; (z.B. SearchArea.Coordinates).
/// </summary>
public sealed class CoordinateTupleListConverter : JsonConverter<List<(double Latitude, double Longitude)>>
{
    private static readonly CoordinateTupleConverter _inner = new();

    public override List<(double Latitude, double Longitude)> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException();

        var list = new List<(double, double)>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
                return list;

            list.Add(_inner.Read(ref reader, typeof((double, double)), options));
        }
        throw new JsonException();
    }

    public override void Write(Utf8JsonWriter writer, List<(double Latitude, double Longitude)> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var item in value)
            _inner.Write(writer, item, options);
        writer.WriteEndArray();
    }
}
