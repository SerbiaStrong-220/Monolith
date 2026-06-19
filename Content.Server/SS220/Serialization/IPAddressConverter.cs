using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Content.Server.SS220.Serialization;

public sealed class IPAddressConverter : JsonConverter<IPAddress>
{
    public override IPAddress Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException($"Expected string token, got {reader.TokenType}");

        var ipString = reader.GetString();

        if (string.IsNullOrEmpty(ipString))
            throw new JsonException("IP address cannot be null or empty");

        if (!IPAddress.TryParse(ipString, out var ip))
            throw new JsonException($"Invalid IP address format: '{ipString}'");

        return ip;
    }

    public override void Write(Utf8JsonWriter writer, IPAddress value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}

public sealed class IPAddressArrayConverter : JsonConverter<IPAddress[]>
{
    public override IPAddress[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException($"Expected array for IP addresses, got {reader.TokenType}");

        var ips = new List<IPAddress>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
                break;

            if (reader.TokenType != JsonTokenType.String)
                throw new JsonException($"Expected string IP address in array, got {reader.TokenType}");

            var ipString = reader.GetString();

            if (!IPAddress.TryParse(ipString, out var ip))
                throw new JsonException($"Invalid IP address format in array: '{ipString}'");

            ips.Add(ip);
        }

        return ips.ToArray();
    }

    public override void Write(Utf8JsonWriter writer, IPAddress[] value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var ip in value)
        {
            writer.WriteStringValue(ip.ToString());
        }
        writer.WriteEndArray();
    }
}
