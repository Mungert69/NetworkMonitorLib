using System;
using System.Text.Json;
using System.Text.RegularExpressions;
using NetworkMonitor.Utils;

namespace NetworkMonitor.Objects.ServiceMessage
{
    public class JsonSanitizer
    {

        public static string RepairJson(string input, HashSet<string> ignoreParameters)
        {
            string json=input;
            try
            {
                JsonSerializer.Deserialize<Dictionary<string, object>>(input);
            }
            catch (JsonException e)
            {

                try
                {
                    string? field=e?.Path?.Replace("$.","");
                    if ( field!=null && !ignoreParameters.Contains(field))
                    {
                        Console.WriteLine("\n\nRepairing => " + field + " \n\n");
                        string repairedJson = JsonRepair.RepairJson(input);
                        JsonSerializer.Deserialize<Dictionary<string, object>>(repairedJson);
                       json=repairedJson;
                    }
                   
                }
                catch {}

            }
            return json;
        }
        public static string SanitizeJson(string input)
        {
            // Step 1: Remove single quotes around JSON objects and arrays
            // This regex looks for single quotes around {..} and [..], considering nested structures
            string patternObjectsAndArrays = @"'(\{(?:[^{}]|(?<open>\{)|(?<-open>\}))*\})'|'\[(?:[^\[\]]|(?<open>\[)|(?<-open>\]))*\]'";
            string sanitized = Regex.Replace(input, patternObjectsAndArrays, m =>
            {
                // Replace single quotes found with nothing, effectively removing them
                return m.Groups[1].Value;
            }, RegexOptions.Singleline | RegexOptions.IgnorePatternWhitespace);

            // Step 2: Replace all remaining single quotes with double quotes
            // This step avoids changing single quotes that are part of a double-quoted string
            sanitized = Regex.Replace(sanitized, @"(?<!\\)'", "\"");

            // Step 4: Convert string booleans ("true" or "false") to actual booleans (true, false)
            sanitized = Regex.Replace(sanitized, @"""true""", "true");
            sanitized = Regex.Replace(sanitized, @"""false""", "false");
            sanitized = Regex.Replace(sanitized, @"""null""", "null");

            // Step 3: Correct boolean values
            // Replace "True" and "False" with "true" and "false"
            sanitized = Regex.Replace(sanitized, @"\bTrue\b", "true", RegexOptions.IgnoreCase);
            sanitized = Regex.Replace(sanitized, @"\bFalse\b", "false", RegexOptions.IgnoreCase);



            return sanitized;
        }
    }
}
