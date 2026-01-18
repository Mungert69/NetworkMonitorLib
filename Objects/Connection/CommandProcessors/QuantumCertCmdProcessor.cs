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
    public class QuantumCertCmdProcessor : CmdProcessor
    {
        private const int DefaultPort = 443;
        private readonly List<ArgSpec> _schema;
        private readonly QuantumCertificateProbe _probe;

        public QuantumCertCmdProcessor(
            ILogger logger,
            ILocalCmdProcessorStates cmdProcessorStates,
            IRabbitRepo rabbitRepo,
            NetConnectConfig netConfig)
            : base(logger, cmdProcessorStates, rabbitRepo, netConfig)
        {
            _probe = new QuantumCertificateProbe(netConfig, logger);

            _cmdProcessorStates.CmdName = "quantum-cert";
            _cmdProcessorStates.CmdDisplayName = "Quantum Certificate Check";

            _schema = new()
            {
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
                    DefaultValue = "59000",
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
                    return new ResultObj { Success = false, Message = "Quantum certificate checks not available" };

                var parsed = CliArgParser.Parse(arguments, _schema, allowUnknown: false, fillDefaults: true);
                if (!parsed.Success)
                {
                    var err = CliArgParser.BuildErrorMessage(_cmdProcessorStates.CmdDisplayName, parsed, _schema);
                    _logger.LogWarning("Arguments not valid {args}. {msg}", arguments, parsed.Message);
                    return new ResultObj { Success = false, Message = err };
                }

                var target = parsed.GetString("target");
                if (string.IsNullOrWhiteSpace(target))
                {
                    target = GetPositionalArgument(arguments);
                }
                target = NormalizeTarget(target);

                if (string.IsNullOrWhiteSpace(target))
                {
                    return new ResultObj { Success = false, Message = "Missing required target.\n\n" + GetCommandHelp() };
                }

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(parsed.GetInt("timeout", 59000));

                var result = await _probe.CheckAsync(target, parsed.GetInt("port", DefaultPort), cts.Token);
                return result;
            }
            catch (OperationCanceledException)
            {
                return new ResultObj { Success = false, Message = "Quantum certificate check canceled or timed out.\n" };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Quantum certificate check failed");
                return new ResultObj { Success = false, Message = $"Quantum certificate check failed: {ex.Message}" };
            }
        }

        public override string GetCommandHelp() => @"
Run a Quantum Certificate Check against a host.

Usage:
  --target <host|url> [--port <int>] [--timeout <ms>]
  (You may also pass the target as the first positional argument.)

Required:
 only --target is required.

Examples:
  example.com
  --target example.com --port 8443 --timeout 15000

Notes:
  • Target may be a hostname or http/https URL; schemes are stripped before testing.
";

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

        private static string GetPositionalArgument(string arguments)
        {
            return arguments.Split(' ')
                .FirstOrDefault(arg => !arg.StartsWith("--")) ?? string.Empty;
        }
    }
}
