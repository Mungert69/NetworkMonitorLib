using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.Repository;
using NetworkMonitor.Objects.ServiceMessage;
using System.Text;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Runtime.InteropServices;

namespace NetworkMonitor.Connection;

public abstract class QuantumTestBase : CmdProcessor
{
    protected readonly List<AlgorithmInfo> _algorithmInfoList;
    protected readonly QuantumConnect _quantumConnect;
    protected const int _defaultTimeout = 59000;
    protected const int _maxParallelTests = 3;

    protected QuantumTestBase(
        ILogger logger,
        ILocalCmdProcessorStates cmdProcessorStates,
        IRabbitRepo rabbitRepo,
        NetConnectConfig netConfig,
        string commandName,
        string displayName)
        : base(logger, cmdProcessorStates, rabbitRepo, netConfig)
    {
        _algorithmInfoList = ConnectHelper.GetAlgorithmInfoList(netConfig);
        _quantumConnect = new QuantumConnect(_algorithmInfoList, netConfig.OqsProviderPath, netConfig.CommandPath, logger);

        _cmdProcessorStates.CmdName = commandName;
        _cmdProcessorStates.CmdDisplayName = displayName;
    }

    protected async Task<List<AlgorithmResult>> ProcessAlgorithmGroup(
        QuantumTestConfig config,
        List<string> algorithms,
        CancellationToken ct)
    {
        if (algorithms.Count == 0) return new List<AlgorithmResult>();

        var semaphore = new SemaphoreSlim(_maxParallelTests);
        var tasks = new List<Task<AlgorithmResult>>();

        foreach (var algorithmName in algorithms)
        {
            await semaphore.WaitAsync(ct);
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var algorithm = _algorithmInfoList.First(a =>
                        a.AlgorithmName.Equals(algorithmName, StringComparison.OrdinalIgnoreCase));

                    return await TestAlgorithm(algorithm, config.Target, config.Port, ct);
                }
                finally
                {
                    semaphore.Release();
                }
            }, ct));
        }

        return (await Task.WhenAll(tasks)).ToList();
    }

    protected async Task<AlgorithmResult> TestAlgorithm(
        AlgorithmInfo algorithm,
        string address,
        int port,
        CancellationToken ct)
    {
        try
        {
            var result = await _quantumConnect.ProcessAlgorithm(algorithm, address, port);
            return result.Success
                ? AlgorithmResult.CreateSuccessful(algorithm.AlgorithmName, result.Data as string ?? "No additional data")
                : AlgorithmResult.CreateFailed(algorithm.AlgorithmName, result.Message);
        }
        catch (Exception ex)
        {
            return AlgorithmResult.CreateFailed(algorithm.AlgorithmName, $"Test failed: {ex.Message}");
        }
    }

    protected async Task<ResultObj> ExecuteQuantumTest(QuantumTestConfig config, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(config.Timeout);

        try
        {
            var validAlgorithms = ValidateAlgorithms(config.Algorithms);
            if (validAlgorithms.Count == 0)
                return new ResultObj { Message = "No valid/enabled algorithms specified" };

            var results = await ProcessAlgorithmGroup(config, validAlgorithms, ct);
            return ProcessTestResults(results);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            return new ResultObj { Message = $"Test timed out after {config.Timeout}ms" };
        }
    }

    protected List<string> ValidateAlgorithms(IEnumerable<string> algorithms)
    {
        return algorithms
            .Select(a => a.Trim())
            .Intersect(
                _algorithmInfoList
                    .Where(a => a.Enabled)
                    .Select(a => a.AlgorithmName),
                StringComparer.OrdinalIgnoreCase
            )
            .ToList();
    }

    protected ResultObj ProcessTestResults(List<AlgorithmResult> results)
    {
        var successResults = results.Where(r => r.IsSuccessful).ToList();
        if (successResults.Any())
        {
            var message = string.Join("\n", successResults
                .Select(r => $"{r.AlgorithmName}: {r.Message}"));

            return new ResultObj
            {
                Message = message,
                Success = true,
                Data = successResults.First().Message
            };
        }

        var errorMessages = results
            .Select(r => $"{r.AlgorithmName}: {r.ErrorMessage}")
            .ToList();

        return new ResultObj
        {
            Message = $"No quantum-safe algorithms supported. Errors:\n{string.Join("\n", errorMessages)}",
            Success = false
        };
    }
    protected string GetPositionalArgument(string arguments)
    {
        return arguments.Split(' ')
            .FirstOrDefault(arg => !arg.StartsWith("--")) ?? string.Empty;
    }

    protected void LogAndCapture(string? data, StringBuilder output)
    {
        if (string.IsNullOrEmpty(data)) return;
        output.AppendLine(data); // Append data to the StringBuilder
        _logger.LogDebug(data); // Log the data
    }
    protected List<string> GetDefaultAlgorithms()
    {
        return _algorithmInfoList
            .Where(a => a.Enabled)
            .Select(a => a.AlgorithmName)
            .ToList();
    }


    // Override methods from CmdProcessor
    public override  Task<ResultObj> RunCommand(
        string arguments,
        CancellationToken cancellationToken,
        ProcessorScanDataObj? processorScanDataObj = null)
    {
        // Default implementation or throw if this should be implemented by child classes
         return Task.FromException<ResultObj>(new NotImplementedException("Child classes must implement RunCommand"));

    }

    public override string GetCommandHelp()
    {
        var enabledAlgorithms = _algorithmInfoList
            .Where(a => a.Enabled)
            .Select(a => $"- {a.AlgorithmName}");

        return $@"
Quantum Security Processor Help
===============================
Tests TLS endpoints for quantum-safe cryptographic support

Usage:
  <target> [--port <number>] [--algorithms <list>] [--timeout <ms>]

Required:
  target        Server IP/hostname

Options:
  --port        TLS port (default: 443)
  --algorithms  Comma-separated list of algorithms (default: all enabled)
  --timeout     Operation timeout in milliseconds (default: {_defaultTimeout})

Enabled Algorithms:
{string.Join("\n", enabledAlgorithms)}

Examples:
  example.com --port 8443
  example.com --algorithms Kyber512,Dilithium2 --timeout 5000
";
    }

    protected record QuantumTestConfig(
        string Target,
        int Port,
        List<string> Algorithms,
        int Timeout);

    protected class AlgorithmResult
    {
        public string AlgorithmName { get; }
        public bool IsSuccessful { get; }
        public string? Message { get; }
        public string? ErrorMessage { get; }

        private AlgorithmResult(string algorithmName, bool isSuccessful,
                               string? message, string? error)
        {
            AlgorithmName = algorithmName;
            IsSuccessful = isSuccessful;
            Message = message;
            ErrorMessage = error;
        }

        public static AlgorithmResult CreateSuccessful(string algorithmName, string message)
            => new(algorithmName, true, message, null);

        public static AlgorithmResult CreateFailed(string algorithmName, string error)
            => new(algorithmName, false, null, error);
    }
}

