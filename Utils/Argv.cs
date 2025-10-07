using System;
using System.Collections.Generic;
using System.Text;

namespace NetworkMonitor.Utils
{
    /// <summary>
    /// Robust command-line tokenizer &amp; builder for Unix-style argv.
    /// <para>Tokenize: split a single string into argv[] honoring quotes/escapes</para>
    /// <para>Build:    compose argv[] from option dictionary and positionals</para>
    /// </summary>
    public static class Argv
    {
        /// <summary>
        /// Tokenize a command line into argv[] honoring quotes (' " ),
        /// backslash escapes inside quotes (\" \' \\), and -- end-of-options.
        /// No shell expansion is performed.
        /// </summary>
        public static string[] Tokenize(string? commandLine)
        {
            if (string.IsNullOrWhiteSpace(commandLine)) return Array.Empty<string>();

            var argv = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;
            char quote = '\0';

            for (int i = 0; i < commandLine.Length; i++)
            {
                char c = commandLine[i];

                if (inQuotes)
                {
                    if (c == '\\' && i + 1 < commandLine.Length)
                    {
                        // Allow \" or \' or \\ inside the same-quote context
                        char next = commandLine[i + 1];
                        if (next == quote || next == '\\')
                        {
                            sb.Append(next);
                            i++;
                            continue;
                        }
                    }
                    if (c == quote)
                    {
                        inQuotes = false;
                        quote = '\0';
                        continue;
                    }
                    sb.Append(c);
                }
                else
                {
                    if (char.IsWhiteSpace(c))
                    {
                        Flush();
                    }
                    else if (c == '"' || c == '\'')
                    {
                        inQuotes = true;
                        quote = c;
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
            }

            Flush();
            return argv.ToArray();

            void Flush()
            {
                if (sb.Length > 0)
                {
                    argv.Add(sb.ToString());
                    sb.Clear();
                }
            }
        }

        /// <summary>
        /// Build argv[] from key/value options and positionals.
        /// Keys are emitted as --key value (or --key=value if value contains spaces and you prefer compact form).
        /// Boolean true will emit as --key (no value) if emitBooleanFlags is true.
        /// </summary>
        public static List<string> Build(
            IDictionary<string, string?>? options = null,
            IEnumerable<string>? positionals = null,
            bool emitBooleanFlags = true,
            bool useEqualsForValuesWithSpaces = false)
        {
            var list = new List<string>();

            if (options != null)
            {
                foreach (var kv in options)
                {
                    var key = kv.Key?.Trim();
                    if (string.IsNullOrEmpty(key)) continue;

                    // normalize to kebab-ish (optional)
                    // key = key.ToLowerInvariant();

                    string? value = kv.Value;

                    if (emitBooleanFlags && (value is null || value.Equals("true", StringComparison.OrdinalIgnoreCase)))
                    {
                        list.Add($"--{key}");
                        continue;
                    }

                    if (value is null)
                    {
                        // If boolean flags should carry explicit false, you could encode it here; for now, skip.
                        continue;
                    }

                    if (useEqualsForValuesWithSpaces && value.Contains(' '))
                    {
                        list.Add($"--{key}={value}");
                    }
                    else
                    {
                        list.Add($"--{key}");
                        list.Add(value);
                    }
                }
            }

            if (positionals != null)
            {
                foreach (var p in positionals)
                {
                    if (!string.IsNullOrEmpty(p)) list.Add(p);
                }
            }

            return list;
        }

        /// <summary>
        /// Pretty-print argv for logs: quotes args containing spaces/quotes safely.
        /// </summary>
        public static string ForLog(IEnumerable<string> argv)
        {
            var sb = new StringBuilder();
            foreach (var a in argv)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(QuoteIfNeeded(a));
            }
            return sb.ToString();

            static string QuoteIfNeeded(string s)
            {
                if (string.IsNullOrEmpty(s)) return "\"\"";
                bool need = s.IndexOfAny(new[] { ' ', '\t', '"', '\'', '\\' }) >= 0;
                if (!need) return s;

                // Prefer double quotes; escape internal " and \
                var escaped = s.Replace("\\", "\\\\").Replace("\"", "\\\"");
                return $"\"{escaped}\"";
            }
        }
    }
}
