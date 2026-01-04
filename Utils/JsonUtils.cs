using System.Text.Json;
using System.Text.Json.Serialization;
using System;
using System.Globalization;
using System.Collections.Generic;
using System.IO;
using Microsoft.IdentityModel.Tokens;
using NetworkMonitor.Objects;
namespace NetworkMonitor.Utils
{
    public class JsonUtils
    {

#pragma warning disable IL2026, IL3050
        // Suppressing marning as this is only affecting the JsonElement Type and it probably? wont be trimmed. Be aware of this.


        public static JsonWebKey? FindKeyByKid(string jwksJson, string tokenKid)
        {
            using var jsonResponse = JsonDocument.Parse(jwksJson);
            if (jsonResponse.RootElement.TryGetProperty("keys", out JsonElement keys) && keys.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement key in keys.EnumerateArray())
                {
                    if (key.TryGetProperty("kid", out JsonElement kid) && kid.GetString() == tokenKid)
                    {
                        string keyJson = key.GetRawText();
                        return JsonUtils.GetJsonObjectFromString<JsonWebKey>(keyJson);
                    }
                }
            }

            return null;
        }

        public static (string? e, string? n)? FindKeyENValuesByKid(string jwksJson, string tokenKid)
        {
            using var jsonResponse = JsonDocument.Parse(jwksJson);
            if (jsonResponse.RootElement.TryGetProperty("keys", out JsonElement keys) && keys.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement key in keys.EnumerateArray())
                {
                    if (key.TryGetProperty("kid", out JsonElement kid) && kid.GetString() == tokenKid)
                    {
                        string? e = key.GetProperty("e").GetString();
                        string? n = key.GetProperty("n").GetString();
                        return (e, n);
                    }
                }
            }

            return null;
        }



        public static string? GetStringFieldFromJson(string jsonStr, string field)
        {
            using JsonDocument jsonResponse = JsonDocument.Parse(jsonStr);

            if (jsonResponse.RootElement.TryGetProperty(field, out JsonElement element))
            {
                return element.GetString();
            }
            else
            {
                // Field not found in JSON, return null or handle as per your logic
                return null;
            }
        }

        public static int? GetIntFieldFromJson(string jsonStr, string field)
        {
            using JsonDocument jsonResponse = JsonDocument.Parse(jsonStr);

            if (jsonResponse.RootElement.TryGetProperty(field, out JsonElement element))
            {
                return element.GetInt32();
            }
            else
            {
                // Field not found in JSON, return null or handle as per your logic
                return null;
            }
        }

        public static bool? GetBooleanFieldFromJson(string jsonStr, string field)
        {
            using JsonDocument jsonResponse = JsonDocument.Parse(jsonStr);

            if (jsonResponse.RootElement.TryGetProperty(field, out JsonElement element))
            {
                return element.GetBoolean();
            }
            else
            {
                // Field not found in JSON, return null or handle as per your logic
                return null;
            }
        }


        public static T? GetObjectFieldFromJson<T>(string jsonStr, string field) where T : class
        {
            using JsonDocument jsonResponse = JsonDocument.Parse(jsonStr);

            if (jsonResponse.RootElement.TryGetProperty(field, out JsonElement element))
            {
                // Deserialize the JsonElement to an object of type T
                return (T?)JsonSerializer.Deserialize(element.GetRawText(), typeof(T), SourceGenerationContext.Default);
            }
            else
            {
                // Field not found in JSON, return null or handle as per your logic
                return null;
            }
        }



        public static JsonElement GetJsonElementFromString(string jsonStr)
        {
            return JsonSerializer.Deserialize<JsonElement>(jsonStr, SourceGenerationContext.Default.Options);
        }

        public static JsonElement GetJsonElementFromFile(string fileName)
        {
            using (StreamReader r = new StreamReader(fileName))
            {
                string json = r.ReadToEnd();
                return JsonSerializer.Deserialize<JsonElement>(json, SourceGenerationContext.Default.Options);
            }
        }
#pragma warning restore IL2026, IL3050

        public static List<string> ConvertJsonStr(string strInput)
        {
            string str = strInput;
            List<string> strObjs = new List<string>();
            strObjs.Add(str);
            return strObjs;
        }
        public static T? GetJsonObjectFromFile<T>(string fileName, out T? obj) where T : class
        {
            using (StreamReader r = new StreamReader(fileName))
            {
                string? json = r.ReadToEnd();
                obj = JsonSerializer.Deserialize(
                json, typeof(T), SourceGenerationContext.Default)
                as T;
            }
            return obj;
        }
        public static T? GetJsonObjectFromFile<T>(string fileName) where T : class
        {
            T? obj;
            using (StreamReader r = new StreamReader(fileName))
            {
                string? json = r.ReadToEnd();
                obj = JsonSerializer.Deserialize(
                json, typeof(T), SourceGenerationContext.Default)
                as T;
            }
            return obj;
        }
        public static T? GetJsonObjectFromString<T>(string jsonStr) where T : class
        {
            T? obj;
            obj = JsonSerializer.Deserialize(
                jsonStr, typeof(T), SourceGenerationContext.Default)
                as T;
            return obj;
        }
        public static T? GetJsonObjectFromStringNoCase<T>(string jsonStr) where T : class
        {
            return JsonSerializer.Deserialize<T>(jsonStr, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
        }
        public static string WriteJsonObjectToString<T>(T obj) where T : class
        {
            string jsonStr = JsonSerializer.Serialize(obj, typeof(T), SourceGenerationContext.Default);
            return jsonStr;
        }

        public static void WriteObjectToFile<T>(string filePath, T obj) where T : class
        {
            string jsonStr = JsonSerializer.Serialize(obj, typeof(T), SourceGenerationContext.Default);
            File.WriteAllText(filePath, jsonStr);
        }

#pragma warning disable CS8600
      public static T? GetValueOrCoerce<T>(Dictionary<string, object> dictionary, string key, T? defaultValue = default(T))
{
    try
    {
        if (dictionary.TryGetValue(key, out object? value))
        {
            // Check if the value is null and return the default value immediately
            if (value == null) return defaultValue;

            // Handle objects created from JsonElement
            if (value is JsonElement jsonElement)
            {
                // Check if jsonElement is in default state
                if (jsonElement.Equals(default(JsonElement))) return defaultValue;

                // Handle string representations of numbers
                if (jsonElement.ValueKind == JsonValueKind.String)
                {
                    string strValue = jsonElement.GetString();
                    
                    // Attempt to coerce string to expected type
                    if (typeof(T) == typeof(int) || typeof(T) == typeof(int?))
                    {
                        if (int.TryParse(strValue, out int resultInt)) return (T)(object)resultInt;
                    }
                    else if (typeof(T) == typeof(double) || typeof(T) == typeof(double?))
                    {
                        if (double.TryParse(strValue, NumberStyles.Any, CultureInfo.InvariantCulture, out double resultDouble)) return (T)(object)resultDouble;
                    }
                    else if (typeof(T) == typeof(long) || typeof(T) == typeof(long?))
                    {
                        if (long.TryParse(strValue, out long resultLong)) return (T)(object)resultLong;
                    }
                    else if (typeof(T) == typeof(ushort) || typeof(T) == typeof(ushort?))
                    {
                        if (ushort.TryParse(strValue, out ushort resultUShort)) return (T)(object)resultUShort;
                    }
                    else if (typeof(T) == typeof(short) || typeof(T) == typeof(short?))
                    {
                        if (short.TryParse(strValue, out short resultShort)) return (T)(object)resultShort;
                    }
                    else if (typeof(T) == typeof(uint) || typeof(T) == typeof(uint?))
                    {
                        if (uint.TryParse(strValue, out uint resultUInt)) return (T)(object)resultUInt;
                    }
                    else if (typeof(T) == typeof(ulong) || typeof(T) == typeof(ulong?))
                    {
                        if (ulong.TryParse(strValue, out ulong resultULong)) return (T)(object)resultULong;
                    }
                    else if (typeof(T) == typeof(bool) || typeof(T) == typeof(bool?))
                    {
                        if (bool.TryParse(strValue, out bool resultBool)) return (T)(object)resultBool;
                    }
                }

                // Continue with regular jsonElement parsing
                if (typeof(T) == typeof(string)) return (T)(object)jsonElement.GetString();
                if (typeof(T) == typeof(int) || typeof(T) == typeof(int?)) return (T)(object)jsonElement.GetInt32();
                if (typeof(T) == typeof(bool) || typeof(T) == typeof(bool?)) return (T)(object)jsonElement.GetBoolean();
                if (typeof(T) == typeof(double) || typeof(T) == typeof(double?)) return (T)(object)jsonElement.GetDouble();
                if (typeof(T) == typeof(long) || typeof(T) == typeof(long?)) return (T)(object)jsonElement.GetInt64();
                if (typeof(T) == typeof(float) || typeof(T) == typeof(float?)) return (T)(object)jsonElement.GetSingle();
                if (typeof(T) == typeof(ushort) || typeof(T) == typeof(ushort?)) return (T)(object)jsonElement.GetUInt16();
                if (typeof(T) == typeof(short) || typeof(T) == typeof(short?)) return (T)(object)jsonElement.GetInt16();
                if (typeof(T) == typeof(uint) || typeof(T) == typeof(uint?)) return (T)(object)jsonElement.GetUInt32();
                if (typeof(T) == typeof(ulong) || typeof(T) == typeof(ulong?)) return (T)(object)jsonElement.GetUInt64();
            }

            // Handle direct type match
            if (value is T) return (T)value;

            // Handle coercion between common types
            if (typeof(T) == typeof(int) || typeof(T) == typeof(int?))
            {
                if (int.TryParse(value.ToString(), out int resultInt)) return (T)(object)resultInt;
            }
            if (typeof(T) == typeof(double) || typeof(T) == typeof(double?))
            {
                if (double.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double resultDouble)) return (T)(object)resultDouble;
            }
            if (typeof(T) == typeof(bool) || typeof(T) == typeof(bool?))
            {
                if (bool.TryParse(value.ToString(), out bool resultBool)) return (T)(object)resultBool;
                // Try common alternatives
                if (value.ToString() == "1") return (T)(object)true;
                if (value.ToString() == "0") return (T)(object)false;
            }
            if (typeof(T) == typeof(DateTime) || typeof(T) == typeof(DateTime?))
            {
                if (DateTime.TryParse(value.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime resultDateTime))
                {
                    return (T)(object)DateTime.SpecifyKind(resultDateTime, DateTimeKind.Unspecified);
                }
            }
            if (typeof(T) == typeof(long) || typeof(T) == typeof(long?))
            {
                if (long.TryParse(value.ToString(), out long resultLong)) return (T)(object)resultLong;
            }
            if (typeof(T) == typeof(float) || typeof(T) == typeof(float?))
            {
                if (float.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out float resultFloat)) return (T)(object)resultFloat;
            }
            if (typeof(T) == typeof(ushort) || typeof(T) == typeof(ushort?))
            {
                if (ushort.TryParse(value.ToString(), out ushort resultUShort)) return (T)(object)resultUShort;
            }
            if (typeof(T) == typeof(short) || typeof(T) == typeof(short?))
            {
                if (short.TryParse(value.ToString(), out short resultShort)) return (T)(object)resultShort;
            }
            if (typeof(T) == typeof(uint) || typeof(T) == typeof(uint?))
            {
                if (uint.TryParse(value.ToString(), out uint resultUInt)) return (T)(object)resultUInt;
            }
            if (typeof(T) == typeof(ulong) || typeof(T) == typeof(ulong?))
            {
                if (ulong.TryParse(value.ToString(), out ulong resultULong)) return (T)(object)resultULong;
            }
        }

    }
    catch (Exception ex)
    {
        throw new Exception($"Error converting '{key}' to {typeof(T).Name}: {ex.Message}");
    }

    return defaultValue;
}

        public static T? GetValueOrDefault<T>(Dictionary<string, object> dictionary, string key, T? defaultValue = default(T))
        {
            try
            {
                if (dictionary.TryGetValue(key, out object? value))
                {
                    // Check if the value is null and return the default value immediately
                    if (value == null) return defaultValue;
                    // Handle objects created from Json
                    if (value is System.Text.Json.JsonElement jsonElement)
                    {
                        // Check if jsonElement is in default state
                        if (jsonElement.Equals(default(JsonElement)))
                        {
                            return defaultValue;
                        }
                        if (typeof(T) == typeof(string))
                        {
                            return (T)(object)jsonElement.GetString();
                        }
                        else if (typeof(T) == typeof(int) || typeof(T) == typeof(int?))
                        {
                            return (T)(object)jsonElement.GetInt32();
                        }
                        else if (typeof(T) == typeof(bool) || typeof(T) == typeof(bool?))
                        {
                            return (T)(object)jsonElement.GetBoolean();
                        }
                        else if (typeof(T) == typeof(DateTime) || typeof(T) == typeof(DateTime?))
                        {
                            var str = jsonElement.GetString();
                            if (str == null) return defaultValue;
                            var dateTime = DateTime.Parse(str, CultureInfo.InvariantCulture);
                            return (T)(object)DateTime.SpecifyKind(dateTime, DateTimeKind.Unspecified); // Important to specify as Unspecified
                        }

                        else if (typeof(T) == typeof(ushort) || typeof(T) == typeof(ushort?))
                        {
                            return (T)(object)jsonElement.GetUInt16();
                        }
                        else if (typeof(T) == typeof(short) || typeof(T) == typeof(short?))
                        {
                            return (T)(object)jsonElement.GetInt16();
                        }
                        else if (typeof(T) == typeof(uint) || typeof(T) == typeof(uint?))
                        {
                            return (T)(object)jsonElement.GetUInt32();
                        }
                        else if (typeof(T) == typeof(long) || typeof(T) == typeof(long?))
                        {
                            return (T)(object)jsonElement.GetInt64();
                        }
                        else if (typeof(T) == typeof(ulong) || typeof(T) == typeof(ulong?))
                        {
                            return (T)(object)jsonElement.GetUInt64();
                        }
                        else if (typeof(T) == typeof(double) || typeof(T) == typeof(double?))
                        {
                            return (T)(object)jsonElement.GetDouble();
                        }
                        else if (typeof(T) == typeof(float) || typeof(T) == typeof(float?))
                        {
                            return (T)(object)jsonElement.GetSingle();
                        }
                    }
                    // Handle regular C# object types
                    else if (value is T)
                    {
                        return (T)value;
                    }
                }

            }
            catch (Exception)
            {
                throw new Exception($"Error converting '{key}' to {typeof(T).Name}");

            }

            return defaultValue;
        }

    }
    #pragma warning restore CS8600
}
