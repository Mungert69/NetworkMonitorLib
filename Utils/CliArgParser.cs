using System;
using System.Collections.Generic;
using System.Text;

namespace NetworkMonitor.Utils
{
    /// <summary>
    /// Robust parser for GNU-style "--key value" command lines.
    /// Supports:
    ///   --key value
    ///   --key=value
    ///   --key="va lue"  / --key='va lue'
    ///   escaped quotes inside values (\" or \')
    ///   kebab/underscore keys (tls-min-version, my_key)
    ///   boolean flags: --flag => "true"
    ///   end-of-options: "--"
    /// </summary>
    public static class CliArgParser
    {
        public static Dictionary<string, string> Parse(string arguments)
        {
            var args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(arguments)) return args;

            var tokens = Tokenize(arguments);
            bool parsingOptions = true;

            for (int i = 0; i < tokens.Count; i++)
            {
                var t = tokens[i];

                if (parsingOptions && t == "--")
                {
                    parsingOptions = false;
                    continue;
                }

                if (parsingOptions && t.StartsWith("--"))
                {
                    // --key or --key=value
                    var body = t.Substring(2);
                    string key, value;

                    int eq = body.IndexOf('=');
                    if (eq >= 0)
                    {
                        key = body.Substring(0, eq);
                        value = body.Substring(eq + 1);
                    }
                    else
                    {
                        key = body;
                        // If next token exists and is not another option, take it as the value
                        if (i + 1 < tokens.Count && !tokens[i + 1].StartsWith("--"))
                        {
                            value = tokens[++i];
                        }
                        else
                        {
                            value = "true"; // boolean flag
                        }
                    }

                    // Optional: enforce allowed key chars (letters, digits, underscore, dash)
                    // if (!System.Text.RegularExpressions.Regex.IsMatch(key, @"^[A-Za-z0-9_-]+$")) continue;

                    args[key.ToLowerInvariant()] = value;
                }
                else
                {
                    // Positional args: add if you want to capture them
                    // args[$"_positional{args.Count(kv => kv.Key.StartsWith("_positional"))}"] = t;
                }
            }

            return args;
        }

        private static List<string> Tokenize(string input)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(input)) return list;

            var sb = new StringBuilder();
            bool inQuotes = false;
            char quoteChar = '\0';

            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];

                if (inQuotes)
                {
                    if (c == '\\' && i + 1 < input.Length)
                    {
                        char next = input[i + 1];
                        if (next == quoteChar || next == '\\')
                        {
                            sb.Append(next);
                            i++;
                            continue;
                        }
                    }
                    if (c == quoteChar)
                    {
                        inQuotes = false;
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
                    else if (c == '\'' || c == '"')
                    {
                        inQuotes = true;
                        quoteChar = c;
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
            }

            Flush();
            return list;

            void Flush()
            {
                if (sb.Length > 0)
                {
                    list.Add(sb.ToString());
                    sb.Clear();
                }
            }
        }
    }
}
