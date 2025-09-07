using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace NetworkMonitor.Utils
{
    public class ArgParseResult
    {
        public bool Success { get; set; }

        // Keep case-insensitive comparer no matter what the inner parser does.
        public Dictionary<string, string> Args { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);

        public List<string> MissingKeys { get; set; } = new();
        public List<string> UnknownKeys { get; set; } = new();

        // New: values that failed validation (e.g., "--max_depth abc")
        public List<string> InvalidKeys { get; set; } = new();

        public string Message { get; set; } = string.Empty;

        public bool Has(string key) => Args.ContainsKey(key);

        public string GetString(string key)
            => Args.TryGetValue(key, out var v) ? v : "";

        public bool GetBool(string key)
        {
            if (!Args.TryGetValue(key, out var v)) return false; // absent => false
            if (bool.TryParse(v, out var b)) return b;
            return v.Trim().ToLowerInvariant() is "1" or "yes" or "y" or "on";
        }

        public int GetInt(string key)
        {
            if (Args.TryGetValue(key, out var v) &&
                int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
                return i;

            throw new ArgumentException($"Argument --{key} must be an integer.");
        }

        // Overloads with fallbacks
        public string GetString(string key, string defaultValue) =>
            Args.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : defaultValue;

        public bool GetBool(string key, bool defaultValue) =>
            Has(key) ? GetBool(key) : defaultValue;

        public int GetInt(string key, int defaultValue) =>
            Has(key) ? GetInt(key) : defaultValue;
    }

    public class ArgSpec
    {
        public string Key { get; set; } = "";
        public bool Required { get; set; }
        public bool IsFlag { get; set; }
        public string TypeHint { get; set; } = "value"; // "int" | "bool" | "url" | "value"
        public string DefaultValue { get; set; } = "";
        public Func<string>? DefaultFactory { get; set; }
        public string Help { get; set; } = "";
    }

    public static class CliArgParser
    {
        public static Dictionary<string, string> Parse(string arguments)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(arguments)) return dict;

            var tokens = Tokenize(arguments);
            for (int i = 0; i < tokens.Count; i++)
            {
                var t = tokens[i];

                // Long form: --key[=value] or --key value
                if (t.StartsWith("--"))
                {
                    var rest = t.Substring(2);
                    if (string.IsNullOrWhiteSpace(rest)) continue;

                    string key, value;
                    var eq = rest.IndexOf('=');
                    if (eq >= 0)
                    {
                        key = rest[..eq].Trim();
                        value = Unquote(rest[(eq + 1)..].Trim());
                    }
                    else
                    {
                        key = rest.Trim();
                        if (i + 1 < tokens.Count && !IsLikelyOption(tokens[i + 1]))
                        {
                            value = Unquote(tokens[++i]);
                        }
                        else
                        {
                            // bare flag -> true (schema will normalize if needed)
                            value = "true";
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(key))
                        dict[key] = value;
                    continue;
                }

                // Short form: -k[=value] or -k value (no -abc compaction)
                if (t.StartsWith("-") && t.Length > 1)
                {
                    var rest = t.Substring(1);
                    string key = rest, value;

                    var eq = rest.IndexOf('=');
                    if (eq >= 0)
                    {
                        key = rest[..eq].Trim();
                        value = Unquote(rest[(eq + 1)..].Trim());
                    }
                    else
                    {
                        if (i + 1 < tokens.Count && !IsLikelyOption(tokens[i + 1]))
                        {
                            value = Unquote(tokens[++i]);
                        }
                        else
                        {
                            value = "true";
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(key))
                        dict[key] = value;
                    continue;
                }

                // Positional tokens ignored by design
            }

            return dict;

            static bool IsLikelyOption(string s)
            {
                if (string.IsNullOrEmpty(s) || s[0] != '-') return false;

                // --long or -k where next char is a letter => option
                if (s.StartsWith("--")) return true;
                if (s.Length >= 2 && char.IsLetter(s[1])) return true;

                // Looks like a negative number (-123, -3.14) -> treat as value, not option
                return false;
            }

            static string Unquote(string s)
            {
                if (string.IsNullOrEmpty(s)) return string.Empty;
                s = s.Trim();
                if ((s.Length >= 2 && s[0] == '"' && s[^1] == '"') ||
                    (s.Length >= 2 && s[0] == '\'' && s[^1] == '\''))
                {
                    s = s.Substring(1, s.Length - 2);
                }
                // unescape simple cases
                s = s.Replace("\\\"", "\"").Replace("\\'", "'").Replace("\\\\", "\\");
                return s;
            }

            // Tokenizer that preserves quoted substrings for the space-separated form
            static List<string> Tokenize(string s)
            {
                var list = new List<string>();
                var sb = new StringBuilder();
                bool inSingle = false, inDouble = false, escaped = false;

                for (int i = 0; i < s.Length; i++)
                {
                    char c = s[i];

                    if (escaped)
                    {
                        sb.Append(c);
                        escaped = false;
                        continue;
                    }

                    if (c == '\\')
                    {
                        escaped = true;
                        continue;
                    }

                    if (c == '\'' && !inDouble)
                    {
                        inSingle = !inSingle;
                        sb.Append(c);
                        continue;
                    }

                    if (c == '"' && !inSingle)
                    {
                        inDouble = !inDouble;
                        sb.Append(c);
                        continue;
                    }

                    if (!inSingle && !inDouble && char.IsWhiteSpace(c))
                    {
                        Flush();
                        continue;
                    }

                    sb.Append(c);
                }

                if (escaped) sb.Append('\\');
                Flush();
                return list;

                void Flush()
                {
                    if (sb.Length == 0) return;
                    list.Add(sb.ToString());
                    sb.Clear();
                }
            }
        }


        /// <summary>
        /// Schema-based parsing: validates values, fills defaults, reports unknown/missing/invalid.
        /// </summary>
        public static ArgParseResult Parse(
            string arguments,
            List<ArgSpec> schema,
            bool allowUnknown = false,
            bool fillDefaults = true)
        {
            // Wrap the raw parser output into a canonical, case-insensitive, normalized-key dict.
            // Also trim keys, strip leading dashes, and trim values.
            var raw = Parse(arguments);
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in raw)
            {
                var k = NormalizeKey(kv.Key);
                var v = (kv.Value ?? "").Trim();
                dict[k] = v;
            }

            // Fast lookup: what keys are allowed?
            var schemaByKey = schema
                .ToDictionary(s => NormalizeKey(s.Key), s => s, StringComparer.OrdinalIgnoreCase);

            // Track which keys user actually provided (before we add defaults),
            // so "unknown" only refers to user input, not defaults.
            var userProvidedKeys = new HashSet<string>(dict.Keys, StringComparer.OrdinalIgnoreCase);

            // Unknowns (user-provided only)
            var unknown = new List<string>();
            if (!allowUnknown)
            {
                foreach (var k in userProvidedKeys)
                {
                    if (!schemaByKey.ContainsKey(k)) unknown.Add(k);
                }
            }

            // Fill defaults (if requested)
            if (fillDefaults)
            {
                foreach (var s in schema)
                {
                    var key = NormalizeKey(s.Key);
                    if (dict.ContainsKey(key)) continue;

                    var def = ResolveDefault(s);
                    if (def is not null)
                    {
                        // For flags: store a canonical "true"/"false" (but we will re-validate below anyway).
                        dict[key] = s.IsFlag ? NormalizeBool(def) : def;
                    }
                }
            }

            // Required-but-missing AFTER defaults.
            var missing = new List<string>();
            foreach (var s in schema)
            {
                var key = NormalizeKey(s.Key);
                if (s.Required && !dict.ContainsKey(key))
                    missing.Add(key);
            }

            // Validate + normalize values for all present schema keys.
            var invalid = new List<string>();
            foreach (var s in schema)
            {
                var key = NormalizeKey(s.Key);
                if (!dict.TryGetValue(key, out var rawValue)) continue;

                if (!ValidateAndMaybeNormalize(s, rawValue, out var normalized, out var error))
                {
                    invalid.Add($"--{key}: {error}");
                }
                else
                {
                    dict[key] = normalized; // write back the normalized value
                }
            }

            var ok = missing.Count == 0 && invalid.Count == 0 && (allowUnknown || unknown.Count == 0);

            // Build a terse message for BuildErrorMessage()
            var parts = new List<string>();
            if (missing.Count > 0)
                parts.Add($"Missing required: {string.Join(", ", missing.Select(k => $"--{k}"))}");
            if (unknown.Count > 0)
                parts.Add($"Unknown args: {string.Join(", ", unknown.Select(k => $"--{k}"))}");
            if (invalid.Count > 0)
                parts.Add($"Invalid values: {string.Join("; ", invalid)}");

            return new ArgParseResult
            {
                Success = ok,
                Args = dict, // already case-insensitive
                MissingKeys = missing,
                UnknownKeys = unknown,
                InvalidKeys = invalid, // populated with messages for each invalid key
                Message = parts.Count == 0 ? "" : string.Join("; ", parts)
            };

            // --------- local helpers ---------

            static string NormalizeKey(string k)
                => (k ?? "").Trim().TrimStart('-').ToLowerInvariant();

            static string? ResolveDefault(ArgSpec s)
            {
                if (!string.IsNullOrWhiteSpace(s.DefaultValue)) return s.DefaultValue;
                if (s.DefaultFactory is not null)
                {
                    try { return s.DefaultFactory() ?? ""; } catch { /* swallow */ }
                }
                return null;
            }

            static string NormalizeBool(string v)
            {
                var t = (v ?? "").Trim().ToLowerInvariant();
                return t is "1" or "true" or "yes" or "y" or "on" ? "true" : "false";
            }

            static bool ValidateAndMaybeNormalize(
                ArgSpec spec,
                string value,
                out string normalized,
                out string error)
            {
                normalized = value;
                error = "";

                // Flags: presence => true, unless explicitly falsy
                if (spec.IsFlag)
                {
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        normalized = "true";
                        return true;
                    }

                    var t = value.Trim().ToLowerInvariant();
                    if (t is "1" or "true" or "yes" or "y" or "on")
                    {
                        normalized = "true";
                        return true;
                    }
                    if (t is "0" or "false" or "no" or "n" or "off")
                    {
                        normalized = "false";
                        return true;
                    }

                    error = $"expected a boolean (got '{value}')";
                    return false;
                }

                // Non-flag values: use TypeHint
                switch ((spec.TypeHint ?? "value").Trim().ToLowerInvariant())
                {
                    case "int":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
                        {
                            normalized = i.ToString(CultureInfo.InvariantCulture);
                            return true;
                        }
                        error = $"must be an integer (got '{value}')";
                        return false;

                    case "bool":
                        {
                            var t = value.Trim().ToLowerInvariant();
                            if (t is "1" or "true" or "yes" or "y" or "on") { normalized = "true"; return true; }
                            if (t is "0" or "false" or "no" or "n" or "off") { normalized = "false"; return true; }
                            error = $"must be a boolean (got '{value}')";
                            return false;
                        }

                    case "url":
                        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
                            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                        {
                            normalized = uri.ToString();
                            return true;
                        }
                        error = $"must be an absolute http/https URL (got '{value}')";
                        return false;

                    // default: treat as free-form string
                    default:
                        normalized = value?.Trim() ?? "";
                        return true;
                }
            }
        }

        public static string BuildUsage(string cmdName, List<ArgSpec> schema)
        {
            string Render(ArgSpec s)
                => s.IsFlag
                    ? $"--{s.Key}"
                    : $"--{s.Key} <{(string.IsNullOrWhiteSpace(s.TypeHint) ? "value" : s.TypeHint)}>";

            var required = schema.Where(s => s.Required).ToList();
            var optional = schema.Where(s => !s.Required).ToList();

            var usage = new StringBuilder();
            usage.Append("Usage: ").Append(cmdName);
            foreach (var s in required) usage.Append(' ').Append(Render(s));
            foreach (var s in optional) usage.Append(' ').Append('[').Append(Render(s)).Append(']');

            var opts = new StringBuilder();
            opts.AppendLine(usage.ToString());
            opts.AppendLine();
            opts.AppendLine("Options:");
            foreach (var s in schema.OrderByDescending(s => s.Required).ThenBy(s => s.Key))
            {
                var head = s.IsFlag
                    ? $"  --{s.Key}"
                    : $"  --{s.Key} <{(string.IsNullOrWhiteSpace(s.TypeHint) ? "value" : s.TypeHint)}>";

                var def = !string.IsNullOrWhiteSpace(s.DefaultValue)
                          ? s.DefaultValue
                          : s.DefaultFactory != null ? SafeEval(s.DefaultFactory) : "";

                var defBit = string.IsNullOrWhiteSpace(def) ? "" : $" (default: {def})";
                var reqBit = s.Required ? " [required]" : "";
                var helpBit = string.IsNullOrWhiteSpace(s.Help) ? "" : $" - {s.Help}";
                opts.AppendLine(head + reqBit + defBit + helpBit);
            }

            return opts.ToString();

            static string SafeEval(Func<string> f)
            {
                try { return f() ?? ""; } catch { return ""; }
            }
        }

        public static string BuildErrorMessage(string cmdName, ArgParseResult result, List<ArgSpec> schema)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Invalid arguments.");
            if (!string.IsNullOrWhiteSpace(result.Message))
                sb.AppendLine(result.Message);
            sb.AppendLine();
            sb.AppendLine(BuildUsage(cmdName, schema));
            return sb.ToString();
        }
    }
}
