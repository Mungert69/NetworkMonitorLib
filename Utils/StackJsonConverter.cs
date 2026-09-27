using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace NetworkMonitor.Utils;

/// <summary>Preserves the top-to-bottom wire order when reading a stack.</summary>
public sealed class StackJsonConverter<T> : JsonConverter<Stack<T>>
{
    public override Stack<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("Expected a stack array.");
        var items = new Stack<T>();
        var itemType = (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray) return new Stack<T>(items);
            items.Push(JsonSerializer.Deserialize(ref reader, itemType)!);
        }
        throw new JsonException("Incomplete stack array.");
    }

    public override void Write(Utf8JsonWriter writer, Stack<T> value, JsonSerializerOptions options)
    {
        var itemType = (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));
        writer.WriteStartArray();
        foreach (var item in value) JsonSerializer.Serialize(writer, item, itemType);
        writer.WriteEndArray();
    }
}
