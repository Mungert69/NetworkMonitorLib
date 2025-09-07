using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.Repository;
using NetworkMonitor.Objects.ServiceMessage;

namespace NetworkMonitor.Connection
{
    public class QuantumInfoCmdProcessor : CmdProcessor
    {
        private readonly Dictionary<string, AlgorithmRecord> _index; // canonical key -> record
        private readonly List<AlgorithmRecord> _records;             // all records
        private readonly ILogger _logger;

        public QuantumInfoCmdProcessor(
            ILogger logger,
            ILocalCmdProcessorStates cmdProcessorStates,
            IRabbitRepo rabbitRepo,
            NetConnectConfig netConfig)
            : base(logger, cmdProcessorStates, rabbitRepo, netConfig)
        {
            _logger = logger;

            // Prefer the new modernized file; fall back to the legacy name if needed.
            var primaryPath = Path.Combine(netConfig.OqsProviderPath, "quantum_algos_modernized.json");
            var fallbackPath = Path.Combine(netConfig.OqsProviderPath, "algo_info.json");

            string jsonFilePath = File.Exists(primaryPath) ? primaryPath : fallbackPath;

            try
            {
                (_records, _index) = LoadAndIndex(jsonFilePath, _logger);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load PQC algorithms from {Path}", jsonFilePath);
                _records = new List<AlgorithmRecord>();
                _index = new Dictionary<string, AlgorithmRecord>(StringComparer.OrdinalIgnoreCase);
            }
        }

        public override Task<ResultObj> RunCommand(
            string arguments,
            CancellationToken cancellationToken,
            ProcessorScanDataObj? processorScanDataObj = null)
        {
            try
            {
                var parsedArgs = base.ParseArguments(arguments);
                var query = parsedArgs.GetString("algorithm", "");

                if (string.IsNullOrWhiteSpace(query))
                {
                    return Task.FromResult(new ResultObj { Message = "Algorithm name is required. Try --algorithm mlkem768", Success = false });
                }

                // Exact/alias match via canonical key
                var canonical = Canonical(query);
                if (_index.TryGetValue(canonical, out var exact))
                {
                    return Task.FromResult(new ResultObj
                    {
                        Message = FormatRecord(exact),
                        Success = true,
                        Data = exact
                    });
                }

                // Otherwise do a broad match across names, families, categories, keywords, aliases
                var matches = Search(query);

                if (matches.Count == 0)
                {
                    return Task.FromResult(new ResultObj
                    {
                        Message = $"No algorithms found matching '{query}'.",
                        Success = false
                    });
                }

                if (matches.Count == 1)
                {
                    var one = matches[0];
                    return Task.FromResult(new ResultObj
                    {
                        Message = FormatRecord(one),
                        Success = true,
                        Data = one
                    });
                }

                // Multiple matches: show a compact list with hints
                var list = string.Join("\n", matches
                    .OrderBy(m => m.Category)
                    .ThenBy(m => m.Family)
                    .ThenBy(m => m.AlgorithmName)
                    .Select(m => $"- {m.AlgorithmName}  ({m.Category} / {m.Family})"));

                var msg = $@"
Multiple algorithms matched '{query}'. Please specify one of:
{list}
";
                return Task.FromResult(new ResultObj { Message = msg, Success = false });
            }
            catch (Exception ex)
            {
                return Task.FromResult(new ResultObj { Message = $"Failed to retrieve algorithm info: {ex.Message}", Success = false });
            }
        }

        public override string GetCommandHelp()
        {
            var names = _records.Select(r => r.AlgorithmName)
                                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                                .Take(60); // don't flood

            var supported = string.Join("\n  - ", names);

            return $@"
Quantum Algorithm Info
======================
Retrieves modern PQC / hybrid algorithm details from quantum_algos_modernized.json.

Usage:
  --algorithm <name-or-alias>

Examples:
  --algorithm mlkem768
  --algorithm kyber768
  --algorithm x25519mlkem768
  --algorithm mldsa65
  --algorithm sphincs+         # aliases to SLH-DSA
  --algorithm hqc128
  --algorithm falcon512

Notes:
  • Supports aliases: kyber* → mlkem*, dilithium* → mldsa*, sphincs+ → slh-dsa, etc.
  • Partial searches match name, family, category, and keywords.

Some Available Algorithms:
  - {supported}
";
        }

        // ---------- Search & Formatting ----------

        private List<AlgorithmRecord> Search(string raw)
        {
            var q = raw.Trim();
            var cq = Canonical(q);

            // Broad predicate across fields + alias map (index keys)
            var res = _records.Where(r =>
                    ContainsCI(r.AlgorithmName, q) ||
                    ContainsCI(r.Family, q) ||
                    ContainsCI(r.Category, q) ||
                    (r.Keywords?.Any(k => ContainsCI(k, q)) ?? false) ||
                    // canonical substring match over aliases and algorithmName
                    Canonical(r.AlgorithmName).Contains(cq, StringComparison.OrdinalIgnoreCase) ||
                    (r.Aliases?.Any(a => a.Contains(q, StringComparison.OrdinalIgnoreCase) || Canonical(a).Contains(cq)) ?? false)
                )
                .Distinct()
                .ToList();

            // Also, check the alias index for direct (non-exact) canonical substring matches
            var canonicalHits = _index.Keys
                .Where(k => k.Contains(cq, StringComparison.OrdinalIgnoreCase))
                .Select(k => _index[k]);

            foreach (var rec in canonicalHits)
                if (!res.Contains(rec)) res.Add(rec);

            return res;
        }

        private static bool ContainsCI(string? hay, string needle)
            => hay?.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        private string FormatRecord(AlgorithmRecord r)
        {
            var sb = new StringBuilder();

            sb.AppendLine($"Algorithm: {r.AlgorithmName}");
            sb.AppendLine($"Family / Category: {r.Family} / {r.Category}");
            if (!string.IsNullOrWhiteSpace(r.NistStatus))
                sb.AppendLine($"NIST Status: {r.NistStatus}");

            if (!string.IsNullOrWhiteSpace(r.SecurityLevel))
                sb.AppendLine($"Security: {r.SecurityLevel}" + (r.SecurityCategory is int c ? $" (Category {c})" : ""));

            if (!string.IsNullOrWhiteSpace(r.Variant))
                sb.AppendLine($"Variant: {r.Variant}");

            if (r.Category?.Contains("Hybrid", StringComparison.OrdinalIgnoreCase) == true)
            {
                if (!string.IsNullOrWhiteSpace(r.BaseAlgorithm))
                    sb.AppendLine($"Hybrid Base: {r.BaseAlgorithm}");
                if (!string.IsNullOrWhiteSpace(r.ClassicalAlgorithm))
                    sb.AppendLine($"Hybrid Classical: {r.ClassicalAlgorithm}");
                if (!string.IsNullOrWhiteSpace(r.SizeImpactNote))
                    sb.AppendLine($"Size Note: {r.SizeImpactNote}");
            }

            if (r.Sizes is not null)
            {
                var s = r.Sizes!;
                // KEM-ish
                if (s.EncapsulationKeyBytes.HasValue || s.DecapsulationKeyBytes.HasValue || s.CiphertextBytes.HasValue)
                {
                    sb.AppendLine("Sizes (KEM):");
                    if (s.EncapsulationKeyBytes.HasValue)
                        sb.AppendLine($"  Encapsulation key (pub): {FmtBytes(s.EncapsulationKeyBytes.Value)}");
                    if (s.DecapsulationKeyBytes.HasValue)
                        sb.AppendLine($"  Decapsulation key (priv): {FmtBytes(s.DecapsulationKeyBytes.Value)}");
                    if (s.CiphertextBytes.HasValue)
                        sb.AppendLine($"  Ciphertext: {FmtBytes(s.CiphertextBytes.Value)}");
                    if (s.SharedSecretBytes.HasValue)
                        sb.AppendLine($"  Shared secret: {FmtBytes(s.SharedSecretBytes.Value)}");
                }
                // Signature-ish
                if (s.PublicKeyBytes.HasValue || s.PrivateKeyBytes.HasValue || s.SignatureBytes.HasValue)
                {
                    sb.AppendLine("Sizes (Signature):");
                    if (s.PublicKeyBytes.HasValue)
                        sb.AppendLine($"  Public key: {FmtBytes(s.PublicKeyBytes.Value)}");
                    if (s.PrivateKeyBytes.HasValue)
                        sb.AppendLine($"  Private key: {FmtBytes(s.PrivateKeyBytes.Value)}");
                    if (s.SignatureBytes.HasValue)
                        sb.AppendLine($"  Signature: {FmtBytes(s.SignatureBytes.Value)}");
                }
            }

            if (!string.IsNullOrWhiteSpace(r.Description))
            {
                sb.AppendLine();
                sb.AppendLine("Description:");
                sb.AppendLine($"  {r.Description}");
            }

            if (r.ImplementationNotes?.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Implementation Notes:");
                foreach (var note in r.ImplementationNotes)
                    sb.AppendLine($"  • {note}");
            }

            if (!string.IsNullOrWhiteSpace(r.Advisory))
            {
                sb.AppendLine();
                sb.AppendLine("Advisory:");
                sb.AppendLine($"  {r.Advisory}");
            }

            if (r.References?.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("References:");
                foreach (var re in r.References)
                {
                    var tag = string.IsNullOrWhiteSpace(re.Type) ? "Ref" : re.Type;
                    var title = !string.IsNullOrWhiteSpace(re.Id) ? $"{re.Id} — {re.Title}" : re.Title;
                    if (!string.IsNullOrWhiteSpace(re.Url))
                        sb.AppendLine($"  • [{tag}] {title} — {re.Url}");
                    else
                        sb.AppendLine($"  • [{tag}] {title}");
                }
            }

            return sb.ToString();
        }

        private static string FmtBytes(int bytes)
        {
            // Show bytes and an approximate KiB for quick mental math
            double kib = bytes / 1024.0;
            return $"{bytes} bytes (~{kib:0.##} KiB)";
        }

        // ---------- Loading & Indexing ----------

        private static (List<AlgorithmRecord> records, Dictionary<string, AlgorithmRecord> index)
            LoadAndIndex(string path, ILogger logger)
        {
            using var fs = File.OpenRead(path);
            var opts = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };

            // Try modern schema first
            QuantumAlgoFile? file = null;
            try
            {
                file = JsonSerializer.Deserialize<QuantumAlgoFile>(fs, opts);
            }
            catch (JsonException)
            {
                // Rewind and try legacy schema (list of algorithms directly)
                fs.Position = 0;
                var legacy = JsonSerializer.Deserialize<List<LegacyAlgorithmInfo>>(fs, opts) ?? new();
                var converted = legacy.Select(LegacyToModern).ToList();
                var idxLegacy = BuildIndex(converted, logger);
                return (converted, idxLegacy);
            }

            var records = (file?.Algorithms ?? new List<AlgorithmRecord>())
                .Where(r => !string.IsNullOrWhiteSpace(r.AlgorithmName))
                .ToList();

            var index = BuildIndex(records, logger);
            return (records, index);
        }

        private static Dictionary<string, AlgorithmRecord> BuildIndex(List<AlgorithmRecord> records, ILogger logger)
        {
            var dict = new Dictionary<string, AlgorithmRecord>(StringComparer.OrdinalIgnoreCase);

            foreach (var r in records)
            {
                // Primary canonical key
                var key = Canonical(r.AlgorithmName);
                dict[key] = r;

                // Build and attach aliases on the record for richer search
                r.Aliases = BuildAliases(r).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                foreach (var a in r.Aliases)
                {
                    var ca = Canonical(a);
                    if (!dict.ContainsKey(ca))
                        dict[ca] = r;
                }
            }

            return dict;
        }

        private static IEnumerable<string> BuildAliases(AlgorithmRecord r)
        {
            var list = new List<string>();

            // base variants: raw, squashed, underscores removed, hyphens removed
            list.Add(r.AlgorithmName);
            list.Add(Squash(r.AlgorithmName));

            // Typical synonyms
            var lower = r.AlgorithmName.ToLowerInvariant();

            // Kyber <-> ML-KEM
            if (lower.Contains("ml-kem") || lower.Contains("ml-kem") || lower.Contains("mlkem") || lower.Contains("kyber"))
            {
                var digits = new string(lower.Where(char.IsDigit).ToArray());
                if (!string.IsNullOrEmpty(digits))
                {
                    list.Add($"mlkem{digits}");
                    list.Add($"kyber{digits}");
                }
            }

            // Dilithium <-> ML-DSA
            if (lower.Contains("ml-dsa") || lower.Contains("ml-dsa") || lower.Contains("mldsa") || lower.Contains("dilithium"))
            {
                var digits = new string(lower.Where(char.IsDigit).ToArray());
                if (!string.IsNullOrEmpty(digits))
                {
                    // NIST levels: 44≈2, 65≈3, 87≈5; common synonyms used in the wild
                    if (digits == "44") list.Add("dilithium2");
                    if (digits == "65") list.Add("dilithium3");
                    if (digits == "87") list.Add("dilithium5");
                    list.Add($"mldsa{digits}");
                }
            }

            // SPHINCS+ <-> SLH-DSA
            if (lower.Contains("slh-dsa") || lower.Contains("slh-dsa") || lower.Contains("slhdsa") || lower.Contains("sphincs"))
            {
                list.Add("sphincs+");
                list.Add("sphincs");
                // keep variants like 128s, 128f etc via squash
                list.Add(Squash(r.AlgorithmName).Replace("slhdsa", "sphincs"));
            }

            // Hybrids: join tokens (x25519_mlkem768 -> x25519mlkem768, secp256r1mlkem768, p256mlkem768)
            if (r.Category?.IndexOf("Hybrid", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var joined = Squash(r.AlgorithmName);
                list.Add(joined);

                // add crypto-friendly short forms for common classical parts
                var addIf = new Action<string?>(alias =>
                {
                    if (!string.IsNullOrWhiteSpace(alias)) list.Add(Squash(alias!));
                });

                addIf(r.BaseAlgorithm);
                addIf(r.ClassicalAlgorithm);

                // Some common shorteners
                if (!string.IsNullOrWhiteSpace(r.ClassicalAlgorithm))
                {
                    var ca = r.ClassicalAlgorithm.ToLowerInvariant()
                        .Replace("secp256r1", "p256")
                        .Replace("secp384r1", "p384")
                        .Replace("secp521r1", "p521")
                        .Replace(" (ecdhe)", "")
                        .Replace("ecdhe", "")
                        .Replace(" ", "");
                    if (!string.IsNullOrEmpty(ca) && !string.IsNullOrWhiteSpace(r.BaseAlgorithm))
                        list.Add(Squash($"{ca}{r.BaseAlgorithm}"));
                }
            }

            return list;
        }

        private static string Canonical(string s)
        {
            // Lowercase & remove all non-alnum to build a robust lookup key
            Span<char> buf = stackalloc char[s.Length];
            int j = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = char.ToLowerInvariant(s[i]);
                if (char.IsLetterOrDigit(c)) buf[j++] = c;
            }
            var core = new string(buf[..j]);

            // Normalize common synonyms to one family to improve hits
            core = core.Replace("kyber", "mlkem")
                       .Replace("dilithium", "mldsa")
                       .Replace("sphincsplus", "slhdsa")
                       .Replace("sphincs", "slhdsa");

            return core;
        }

        private static string Squash(string s)
            => new string(s.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

        // ---------- Legacy (fallback) ----------

        private static AlgorithmRecord LegacyToModern(LegacyAlgorithmInfo li)
        {
            // Map legacy minimal fields into modern shape (best-effort)
            return new AlgorithmRecord
            {
                AlgorithmName = li.AlgorithmName ?? "unknown",
                Family = GuessFamily(li.AlgorithmName ?? string.Empty),
                Category = GuessCategory(li.AlgorithmName ?? string.Empty),
                NistStatus = "Unknown (legacy source)",
                SecurityLevel = li.SecurityLevel,
                SecurityCategory = ParseLevel(li.SecurityLevel),
                Description = li.Description,
                Sizes = new Sizes
                {
                    // No true size info in legacy; leave nulls
                },
                Keywords = new List<string> { "legacy" }
            };
        }

        private static string GuessFamily(string name)
        {
            var l = name.ToLowerInvariant();
            if (l.Contains("kyber") || l.Contains("mlkem")) return "ML-KEM (Kyber)";
            if (l.Contains("dilithium") || l.Contains("mldsa")) return "ML-DSA (Dilithium)";
            if (l.Contains("sphincs")) return "SLH-DSA (SPHINCS+)";
            if (l.Contains("falcon")) return "Falcon";
            if (l.Contains("bike")) return "BIKE";
            if (l.Contains("frodo")) return "FrodoKEM";
            if (l.Contains("hqc")) return "HQC";
            return "Unknown";
        }

        private static string GuessCategory(string name)
        {
            var l = name.ToLowerInvariant();
            if (l.Contains("dsa") || l.Contains("dilithium") || l.Contains("falcon") || l.Contains("sphincs")) return "Signature";
            if (l.Contains("_")) return "Hybrid KEM";
            return "KEM";
        }

        private static int? ParseLevel(string? sec)
        {
            if (string.IsNullOrWhiteSpace(sec)) return null;
            // Accept "Level 3" or "3"
            var digits = new string(sec.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var n)) return n;
            return null;
        }
    }

    // ---------- JSON POCOs for modern file ----------

    public class QuantumAlgoFile
    {
        public string? Generated { get; set; }
        public string? Source { get; set; }
        public List<string>? Notes { get; set; }
        public List<AlgorithmRecord> Algorithms { get; set; } = new();
    }

    public class AlgorithmRecord
    {
        public string AlgorithmName { get; set; } = "";
        public string? Family { get; set; }
        public string? Category { get; set; }
        public string? NistStatus { get; set; }
        public string? SecurityLevel { get; set; }
        public int? SecurityCategory { get; set; }
        public Sizes? Sizes { get; set; }
        public string? Variant { get; set; }
        public string? Description { get; set; }
        public List<string>? StandardIds { get; set; }
        public List<Reference>? References { get; set; }
        public List<string>? ImplementationNotes { get; set; }
        public List<string>? Keywords { get; set; }

        // Hybrid extras (when Category contains "Hybrid")
        public string? BaseAlgorithm { get; set; }
        public string? ClassicalAlgorithm { get; set; }
        public string? Advisory { get; set; }
        public string? SizeImpactNote { get; set; }

        // Populated during indexing for better search
        [JsonIgnore] public List<string>? Aliases { get; set; }
    }

    public class Sizes
    {
        public int? EncapsulationKeyBytes { get; set; }
        public int? DecapsulationKeyBytes { get; set; }
        public int? CiphertextBytes { get; set; }
        public int? SharedSecretBytes { get; set; }
        public int? PublicKeyBytes { get; set; }
        public int? PrivateKeyBytes { get; set; }
        public int? SignatureBytes { get; set; }
    }

    public class Reference
    {
        public string? Type { get; set; }
        public string? Id { get; set; }
        public string? Title { get; set; }
        public string? Url { get; set; }
    }

    // ---------- Legacy fallback POCO ----------

    public class LegacyAlgorithmInfo
    {
        public string? AlgorithmName { get; set; }
        public string? Description { get; set; }
        public int? KeySize { get; set; }       // legacy: not accurate PQ sizes
        public string? SecurityLevel { get; set; }
    }
}
