public static class DictionaryExtensions
{
    /// <summary>
    /// Gets an integer value from the dictionary or returns a default value
    /// </summary>
    public static int GetInt(this Dictionary<string, string> dict, string key, int defaultValue)
        => dict.TryGetValue(key, out var value) && int.TryParse(value, out var result) ? result : defaultValue;

    /// <summary>
    /// Gets a list of strings from the dictionary or returns a default value
    /// </summary>
    public static List<string> GetList(this Dictionary<string, string> dict, string key, List<string> defaultValue)
        => dict.TryGetValue(key, out var value) ? value.Split(',').ToList() : defaultValue;

    /// <summary>
    /// Gets a string value from the dictionary or returns a default value
    /// </summary>
    public static string GetString(this Dictionary<string, string> dict, string key, string defaultValue)
        => dict.TryGetValue(key, out var value) ? value : defaultValue;

    /// <summary>
    /// Gets a boolean value from the dictionary or returns a default value.
    /// </summary>
    public static bool GetBool(this Dictionary<string, string> dict, string key, bool defaultValue)
        => dict.TryGetValue(key, out var value) && bool.TryParse(value, out var result) ? result : defaultValue;

}