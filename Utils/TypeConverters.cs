  using System;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace NetworkMonitor.Utils;
  

public class StringOrNumberToIntConverter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // If the token is a number, just read it directly
        if (reader.TokenType == JsonTokenType.Number)
        {
            return reader.GetInt32();
        }

        // If it's a string, try parsing it
        if (reader.TokenType == JsonTokenType.String)
        {
            string? stringValue = reader.GetString();
            if (int.TryParse(stringValue, out int intValue))
            {
                return intValue;
            }
            // You can throw an exception or return a default value here
            // Throwing an exception would mimic standard deserialization failure
            throw new JsonException($"Invalid number value: {stringValue}");
        }

        // Otherwise, not a valid type; throw
        throw new JsonException($"Unexpected token {reader.TokenType} when parsing an int.");
    }

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
    {
        // Always serialize as a number
        writer.WriteNumberValue(value);
    }
}


public class StringOrBooleanConverter : JsonConverter<bool>
{
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // If the token is a boolean, read it directly
        if (reader.TokenType == JsonTokenType.True)
        {
            return true;
        }
        if (reader.TokenType == JsonTokenType.False)
        {
            return false;
        }

        // If it's a string, try parsing it
        if (reader.TokenType == JsonTokenType.String)
        {
            string? stringValue = reader.GetString();
            if (bool.TryParse(stringValue, out bool boolValue))
            {
                return boolValue;
            }
            // Throw an exception for invalid strings
            throw new JsonException($"Invalid boolean value: {stringValue}");
        }

        // Otherwise, not a valid type; throw
        throw new JsonException($"Unexpected token {reader.TokenType} when parsing a bool.");
    }

    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
    {
        // Always serialize as a boolean
        writer.WriteBooleanValue(value);
    }
}

