using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.Repository;
using NetworkMonitor.Objects.ServiceMessage;
using NetworkMonitor.Utils;

namespace NetworkMonitor.Connection
{
    public class QuantumConnectCmdProcessor : QuantumTestBase
    {
        private readonly List<ArgSpec> _schema;

        private const int DefaultPort = 443;

        public QuantumConnectCmdProcessor(
            ILogger logger,
            ILocalCmdProcessorStates cmdProcessorStates,
            IRabbitRepo rabbitRepo,
            NetConnectConfig netConfig)
            : base(logger, cmdProcessorStates, rabbitRepo, netConfig,
                  "quantum", "Quantum Security Check")
        {
            _schema = new()
            {
                // Optional so we can still support positional target
                new()
                {
                    Key = "target",
                    Required = true,
                    IsFlag = false,
                    TypeHint = "value",
                    Help = "Hostname (or URL). If omitted, the first positional token is used."
                },
                new()
                {
                    Key = "algorithms",
                    Required = false,
                    IsFlag = false,
                    TypeHint = "value",
                    Help = "Algorithms list. Optional. Supports comma, space, semicolon, or colon separators."
                },
                new()
                {
                    Key = "port",
                    Required = false,
                    IsFlag = false,
                    TypeHint = "int",
                    DefaultValue = DefaultPort.ToString(),
                    Help = "TLS port (default 443)."
                },
                new()
                {
                    Key = "timeout",
                    Required = false,
                    IsFlag = false,
                    TypeHint = "int",
                    DefaultValue = _defaultTimeout.ToString(),
                    Help = "Per-connection timeout in ms."
                }
            };
        }

        public override async Task<ResultObj> RunCommand(
            string arguments,
            CancellationToken cancellationToken,
            ProcessorScanDataObj? processorScanDataObj = null)
        {
            try
            {
                if (!_cmdProcessorStates.IsCmdAvailable)
                    return new ResultObj { Success = false, Message = "Quantum security checks not available" };

                // Parse with validation for ints only; algorithms are passed through as-is
                var parsed = CliArgParser.Parse(arguments, _schema, allowUnknown: false, fillDefaults: true);
                if (!parsed.Success)
                {
                    var err = CliArgParser.BuildErrorMessage(_cmdProcessorStates.CmdDisplayName, parsed, _schema);
                    _logger.LogWarning("Arguments not valid {args}. {msg}", arguments, parsed.Message);
                    return new ResultObj { Success = false, Message = err };
                }

                // Resolve target (support both --target and positional)
                var target = parsed.GetString("target");
                if (string.IsNullOrWhiteSpace(target))
                {
                    target = GetPositionalArgument(arguments);
                }
                target = NormalizeTarget(target);

                if (string.IsNullOrWhiteSpace(target))
                {
                    var usage = GetCommandHelp();
                    return new ResultObj { Success = false, Message = "Missing required target.\n\n" + usage };
                }

                // Algorithms: parse with flexible separators; no validation/aliasing
                var algoRaw = parsed.GetString("algorithms");
                var userAlgos = ParseAlgoList(algoRaw);

                List<string> algosToUse;
                bool batch;
                if (userAlgos.Count == 0)
                {
                    algosToUse = GetDefaultAlgorithms(); // your base default set
                    batch = true;
                }
                else
                {
                    algosToUse = userAlgos;
                    batch = false;
                }

                var config = new QuantumTestConfig(
                    Target: target,
                    Port: parsed.GetInt("port", DefaultPort),
                    Algorithms: algosToUse,
                    Timeout: parsed.GetInt("timeout", _defaultTimeout)
                );

                cancellationToken.ThrowIfCancellationRequested();

                List<AlgorithmResult> results = batch
                    ? await ProcessAlgorithmGroup(config, algosToUse, cancellationToken)
                    : await ProcessAlgorithmGroupOneByOne(config, algosToUse, cancellationToken);

                return ProcessTestResults(results);
            }
            catch (OperationCanceledException)
            {
                return new ResultObj { Success = false, Message = "Quantum check canceled or timed out.\n" };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Quantum check failed");
                return new ResultObj { Success = false, Message = $"Quantum check failed: {ex.Message}" };
            }
        }

        public override string GetCommandHelp() => @"
Run a Quantum Security Check against a host.

Usage:
  --target <host|url> [--algorithms <list>] [--port <int>] [--timeout <ms>]
  (You may also pass the target as the first positional argument.)

Required:
 only --target is required.

Algorithms input:
  • Accepts comma, space, semicolon, or colon separators (mix freely).
  • Examples:
      --algorithms mlkem768
      --algorithms ""mlkem512, mlkem768, p256_mlkem512""
      --algorithms x25519_mlkem512 p384_mlkem768
      --algorithms ""frodo640aes:frodo640shake; p256_frodo640aes x25519_frodo640shake""
  • If --algorithms is omitted, a default batch is used.

Family examples:
  • FRODO:
      frodo640aes, p256_frodo640aes, x25519_frodo640aes
      frodo640shake, p256_frodo640shake, x25519_frodo640shake
      frodo976aes,  p384_frodo976aes,  x448_frodo976aes
      frodo976shake,p384_frodo976shake,x448_frodo976shake
      frodo1344aes, p521_frodo1344aes
      frodo1344shake,p521_frodo1344shake
  • ML-KEM (Kyber):
      mlkem512,  p256_mlkem512,  x25519_mlkem512
      mlkem768,  p384_mlkem768,  x448_mlkem768
      mlkem1024, p521_mlkem1024
  • BIKE:
      bikel1, p256_bikel1, x25519_bikel1
      bikel3, p384_bikel3, x448_bikel3
      bikel5, p521_bikel5

Long example (pasteable; separators can be changed to commas/spaces/semicolons):
  rodo640aes:p256_frodo640aes:x25519_frodo640aes:frodo640shake:p256_frodo640shake:x25519_frodo640shake:
  frodo976aes:p384_frodo976aes:x448_frodo976aes:frodo976shake:p384_frodo976shake:x448_frodo976shake:
  frodo1344aes:p521_frodo1344aes:frodo1344shake:p521_frodo1344shake:
  mlkem512:p256_mlkem512:x25519_mlkem512:mlkem768:p384_mlkem768:x448_mlkem768:X25519MLKEM768:SecP256r1MLKEM768:
  mlkem1024:p521_mlkem1024:SecP384r1MLKEM1024:
  bikel1:p256_bikel1:x25519_bikel1:bikel3:p384_bikel3:x448_bikel3:bikel5:p521_bikel5

Examples:
  example.com
  --target example.com
  --target https://example.com --port 8443
  example.com --algorithms ""p256_mlkem512, x25519_mlkem512"" --timeout 15000

Notes:
  • Target may be a hostname or http/https URL; schemes are stripped before testing.
";

        // ---------- helpers ----------

        private static string NormalizeTarget(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            var t = s.Trim();
            if (t.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                t = t.Substring("http://".Length);
            else if (t.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                t = t.Substring("https://".Length);
            if (t.EndsWith("/")) t = t.TrimEnd('/');
            return t;
        }

        private static List<string> ParseAlgoList(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return new();
            // split on comma, semicolon, colon, or whitespace; de-dupe; keep order
            var tokens = raw.Split(new[] { ',', ';', ':', ' ', '\t', '\r', '\n' },
                                   StringSplitOptions.RemoveEmptyEntries);

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();
            foreach (var tok in tokens)
            {
                var v = tok.Trim();
                if (v.Length == 0) continue;
                if (seen.Add(v)) result.Add(v);
            }
            return result;
        }
    }
}
