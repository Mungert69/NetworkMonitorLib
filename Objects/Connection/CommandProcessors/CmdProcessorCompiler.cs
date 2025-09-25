using System;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.IO;
using Microsoft.Extensions.Logging;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.Repository;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NetworkMonitor.Objects.ServiceMessage;

namespace NetworkMonitor.Connection;

public interface ICmdProcessorFactory
{
    static abstract ICmdProcessor Create(
        ILogger logger,
        ILocalCmdProcessorStates states,
        IRabbitRepo repo,
        NetConnectConfig cfg,
        IBrowserHost? browserHost = null);

    static abstract string TypeKey { get; }
}

// Optional hook for processors that want explicit setter injection
public interface IRequireBrowserHost
{
    void SetBrowserHost(IBrowserHost? browserHost);
}

public class CmdProcessorCompiler
{
    private List<MetadataReference>? _cachedReferences;
    private readonly Dictionary<string, ILocalCmdProcessorStates> _processorStates;
    private readonly Dictionary<string, ICmdProcessor> _processors;
    private readonly List<string> _processorTypes;
    private readonly Dictionary<string, string> _sourceCodeFileMap;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly IRabbitRepo _rabbitRepo;
    private readonly NetConnectConfig _netConfig;

    private readonly IBrowserHost _browserHost;

    // Single “requires” list: types that require browser automation
    private readonly List<string> _requiresWebAutomation = new();

    public CmdProcessorCompiler(
        ILoggerFactory loggerFactory,
        NetConnectConfig netConfig,
        IRabbitRepo rabbitRepo,
        Dictionary<string, ILocalCmdProcessorStates> processorStates,
        Dictionary<string, ICmdProcessor> processors,
        List<string> processorTypes,
        Dictionary<string, string> sourceCodeFileMap,
        IBrowserHost browserHost,
        List<string> requiresWebAutomation
    )
    {
        _loggerFactory = loggerFactory;
        _netConfig = netConfig;
        _rabbitRepo = rabbitRepo;
        _logger = _loggerFactory.CreateLogger<CmdProcessorCompiler>();
        _processorStates = processorStates;
        _processors = processors;
        _processorTypes = processorTypes;
        _sourceCodeFileMap = sourceCodeFileMap;
        _browserHost = browserHost;
        _requiresWebAutomation = requiresWebAutomation ?? new List<string>();
    }

    private ICmdProcessor CreateProcessorInstance(
        Type type,
        ILogger logger,
        ILocalCmdProcessorStates states,
        IRabbitRepo repo,
        NetConnectConfig cfg,
        bool requiresWebAutomation)
    {
        // 1) Preferred static factory: Create(..., IBrowserHost)
        var factory5 = type.GetMethod(
            "Create",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: new[] {
                typeof(ILogger), typeof(ILocalCmdProcessorStates),
                typeof(IRabbitRepo), typeof(NetConnectConfig),
                typeof(IBrowserHost)
            },
            modifiers: null);

        if (factory5 != null)
        {
            return (ICmdProcessor)factory5.Invoke(null, new object?[] {
                logger, states, repo, cfg, _browserHost
            })!;
        }

        // 2) 5-arg ctor: (ILogger, ILocalCmdProcessorStates, IRabbitRepo, NetConnectConfig, IBrowserHost)
        var ctor5 = type.GetConstructor(new[] {
            typeof(ILogger), typeof(ILocalCmdProcessorStates),
            typeof(IRabbitRepo), typeof(NetConnectConfig),
            typeof(IBrowserHost)
        });

        if (ctor5 != null)
        {
            return (ICmdProcessor)ctor5.Invoke(new object?[] {
                logger, states, repo, cfg, _browserHost
            })!;
        }

        // 3) Static factory without browser (4 args), then soft-inject BrowserHost
        var factory4 = type.GetMethod(
            "Create",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: new[] {
                typeof(ILogger), typeof(ILocalCmdProcessorStates),
                typeof(IRabbitRepo), typeof(NetConnectConfig)
            },
            modifiers: null);

        if (factory4 != null)
        {
            var inst = (ICmdProcessor)factory4.Invoke(null, new object?[] {
                logger, states, repo, cfg
            })!;
            SoftInjectBrowserHost(type, inst, enforce: requiresWebAutomation);
            return inst;
        }

        // 4) 4-arg ctor, then soft-inject BrowserHost
        var ctor4 = type.GetConstructor(new[] {
            typeof(ILogger), typeof(ILocalCmdProcessorStates),
            typeof(IRabbitRepo), typeof(NetConnectConfig)
        });

        if (ctor4 != null)
        {
            var instance = (ICmdProcessor)ctor4.Invoke(new object?[] {
                logger, states, repo, cfg
            })!;
            SoftInjectBrowserHost(type, instance, enforce: requiresWebAutomation);
            return instance;
        }

        throw new MissingMethodException(
            $"{type.FullName} must expose (ILogger, ILocalCmdProcessorStates, IRabbitRepo, NetConnectConfig[, IBrowserHost]) " +
            "or a static Create(...) variant.");
    }

    private void SoftInjectBrowserHost(Type type, ICmdProcessor instance, bool enforce)
    {
        TryInjectBrowserHost(type, instance);
        if (enforce && !DidInjectBrowserHost(type, instance))
        {
            throw new MissingMethodException(
                $"{type.FullName} requires IBrowserHost but does not expose a 5-arg ctor, " +
                "a public settable property 'IBrowserHost BrowserHost', a method 'SetBrowserHost(IBrowserHost)', " +
                "or implement IRequireBrowserHost.");
        }
    }

    private void TryInjectBrowserHost(Type type, ICmdProcessor instance)
    {
        var bhType = typeof(IBrowserHost);

        // Property injection: public IBrowserHost? BrowserHost { get; set; }
        var prop = type.GetProperty("BrowserHost", BindingFlags.Public | BindingFlags.Instance);
        if (prop is { CanWrite: true } && prop.PropertyType.IsAssignableFrom(bhType))
        {
            prop.SetValue(instance, _browserHost);
            return;
        }

        // Explicit setter method: public void SetBrowserHost(IBrowserHost)
        var setter = type.GetMethod("SetBrowserHost",
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            types: new[] { bhType },
            modifiers: null);
        if (setter != null)
        {
            setter.Invoke(instance, new object[] { _browserHost });
            return;
        }

        // Interface-based setter: IRequireBrowserHost
        if (instance is IRequireBrowserHost req)
        {
            req.SetBrowserHost(_browserHost);
            return;
        }
    }

    private bool DidInjectBrowserHost(Type type, ICmdProcessor instance)
    {
        // Best-effort check via readable property
        var prop = type.GetProperty("BrowserHost", BindingFlags.Public | BindingFlags.Instance);
        if (prop != null && prop.CanRead && typeof(IBrowserHost).IsAssignableFrom(prop.PropertyType))
        {
            return prop.GetValue(instance) != null;
        }
        // If only setter/interface was used, we can’t verify — assume success.
        return true;
    }

    private Type CompileAndGetType(string sourceCode, string typeName, bool argsEscaped, bool includeExample)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
        var references = GetMetadataReferences();

        var compilation = CSharpCompilation.Create(
            "DynamicAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        using var ms = new MemoryStream();
        var emit = compilation.Emit(ms);
        if (!emit.Success)
        {
            var topErrors = emit.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .GroupBy(d => d.Id)
                .Select(g => g.OrderBy(d => d.Location.SourceSpan.Start).First())
                .OrderBy(d => d.Location.SourceSpan.Start)
                .Take(10)
                .Select(d =>
                {
                    var msg = $"error {d.Id}: {d.GetMessage(CultureInfo.InvariantCulture)}";
                    var link = $"https://learn.microsoft.com/dotnet/csharp/misc/{d.Id.ToLower()}";
                    msg += $"\nLocation: {d.Location.GetLineSpan().Path}:{d.Location.GetLineSpan().StartLinePosition.Line + 1}";
                    msg += $"\nMore info see: {link}";
                    return msg;
                })
                .ToArray();

            var errorSummary = string.Join("\n\n", topErrors);
            throw new Exception($"Compilation failed:\n{errorSummary}\n" +
                                "REMEMBER to include the full class with all using statements.");
        }

        ms.Position = 0;
        var asm = Assembly.Load(ms.ToArray());
        return asm.GetType(typeName) ?? throw new Exception($"Type '{typeName}' not found in compiled assembly.");
    }

    public async Task<ResultObj> HandleDynamicProcessor(
        string processorType,
        bool argsEscaped = false,
        string processorSourceCode = "",
        bool includeExample = false)
    {
        var result = new ResultObj();

        try
        {
            // Load source if not provided
            if (string.IsNullOrWhiteSpace(processorSourceCode))
            {
                if (_sourceCodeFileMap.TryGetValue(processorType, out var sourceFilePath))
                {
                    processorSourceCode = await LoadSourceCode(sourceFilePath);
                }
                else
                {
                    result.Success = false;
                    result.Message += $" Error : no source code provided and no file mapping found for processor type '{processorType}'";
                    return result;
                }
            }

            processorSourceCode = EnsureRequiredUsingStatements(processorSourceCode);

            // Create / register states
            var statesInstance = CreateProcessorStates(processorType);
            _processorStates[processorType] = statesInstance;
            statesInstance.IsCmdAvailable = !_netConfig.DisabledCommands.Contains(statesInstance.CmdName);

            // Compile & locate type
            var typeName = $"NetworkMonitor.Connection.{processorType}CmdProcessor";
            var type = CompileAndGetType(processorSourceCode, typeName, argsEscaped, includeExample);

            // Create instance
            var processorLogger = _loggerFactory.CreateLogger(type);
            var requiresWebAutomation = _requiresWebAutomation.Contains(processorType);

            var processorInstance = CreateProcessorInstance(
                type, processorLogger, statesInstance, _rabbitRepo, _netConfig, requiresWebAutomation);

            if (processorInstance == null)
            {
                result.Success = false;
                result.Message += $" Error : cannot create instance for processor of type: {processorType}CmdProcessor";
                return result;
            }

            _processors[processorType] = processorInstance;

            // Persist source (optional)
            if (!string.IsNullOrEmpty(processorSourceCode) && !string.IsNullOrEmpty(_netConfig.CommandPath))
            {
                string savePath = Path.Combine(_netConfig.CommandPath, $"{processorType}CmdProcessor.cs");
                try
                {
                    await File.WriteAllTextAsync(savePath, processorSourceCode);

                    if (!_sourceCodeFileMap.ContainsKey(processorType))
                    {
                        _sourceCodeFileMap[processorType] = savePath;
                        _processorTypes.Add(processorType);
                    }

                    result.Message += $" Success : saved provided source code for processor '{processorType}' to '{savePath}'.";
                }
                catch (Exception e)
                {
                    result.Message += $" Warning : could not save provided source code for processor '{processorType}' to '{savePath}'. Error was : {e.Message}";
                }
            }

            result.Success = true;
            result.Message += $" Success : Added cmd_processor_type {processorType}";
        }
        catch (Exception e)
        {
            result.Success = false;
            result.Message += $" Error : {e.Message}";
        }

        return result;
    }

    private string EnsureRequiredUsingStatements(string sourceCode)
    {
        var requiredNamespaces = new List<string>
        {
            "using System;",
            "using System.Text;",
            "using System.Collections.Generic;",
            "using System.Diagnostics;",
            "using System.Threading.Tasks;",
            "using System.Text.RegularExpressions;",
            "using Microsoft.Extensions.Logging;",
            "using System.Linq;",
            "using NetworkMonitor.Objects;",
            "using NetworkMonitor.Objects.Repository;",
            "using NetworkMonitor.Objects.ServiceMessage;",
            "using NetworkMonitor.Connection;",
            "using NetworkMonitor.Utils;",
            "using System.Xml.Linq;",
            "using System.IO;",
            "using System.Threading;",
            "using System.Net;"
        };

        var existingNamespaces = new HashSet<string>(sourceCode.Split('\n')
            .Where(line => line.Trim().StartsWith("using "))
            .Select(line => line.Trim()), StringComparer.OrdinalIgnoreCase);

        var missingNamespaces = requiredNamespaces.Where(ns => !existingNamespaces.Contains(ns)).ToList();
        return string.Join("\n", missingNamespaces) + "\n" + sourceCode;
    }

    private string GenerateStatesSourceCode(string processorType) => $@"
        namespace NetworkMonitor.Objects
        {{
            public class Local{processorType}CmdProcessorStates : LocalCmdProcessorStates
            {{
                public Local{processorType}CmdProcessorStates()
                {{
                    CmdName = ""{processorType.ToLower()}"";
                    CmdDisplayName = ""{processorType}"";
                }}
            }}
        }}";

    private async Task<T?> CompileAndCreateInstance<T>(string sourceCode, string typeName, bool argsEscaped, bool includeExample, params object[] args) where T : class
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
        var references = GetMetadataReferences();

        var compilation = CSharpCompilation.Create("DynamicAssembly", new[] { syntaxTree }, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var ms = new System.IO.MemoryStream();
        var result = compilation.Emit(ms);

        if (!result.Success)
        {
            var topErrors = result.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .GroupBy(d => d.Id)
                .Select(g => g.OrderBy(d => d.Location.SourceSpan.Start).First())
                .OrderBy(d => d.Location.SourceSpan.Start)
                .Take(10)
                .Select(d =>
                {
                    var msg = $"error {d.Id}: {d.GetMessage(CultureInfo.InvariantCulture)}";
                    var link = $"https://learn.microsoft.com/dotnet/csharp/misc/{d.Id.ToLower()}";
                    msg += $"\nLocation: {d.Location.GetLineSpan().Path}:{d.Location.GetLineSpan().StartLinePosition.Line + 1}";
                    msg += $"\nMore info see: {link}";
                    return msg;
                })
                .ToArray();

            var errorSummary = string.Join("\n\n", topErrors);
            string exampleContent = "";

            try
            {
                var exampleFilePath = Path.Combine(_netConfig.CommandPath, "CompileErrorExample.cs");
                if (File.Exists(exampleFilePath) && includeExample)
                {
                    var source_code = await File.ReadAllTextAsync(exampleFilePath);

                    if (!string.IsNullOrWhiteSpace(source_code))
                    {
                        if (argsEscaped)
                        {
                            source_code = JsonSerializer.Serialize(source_code).Trim('"');
                        }

                        exampleContent = "\nHere is a simple example implementation:\n" + source_code + "\n";
                        if (argsEscaped) exampleContent += "NOTE: The example is JSON-escaped for the 'source_code' field.\n";
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Error reading CompileErrorExample.cs: {ex.Message}");
            }

            var message = $"Compilation failed with the following errors:\n{errorSummary}\n" +
                          "REMEMBER to include the full source code for the class.";
            if (includeExample && !string.IsNullOrEmpty(exampleContent)) message += exampleContent;

            _logger.LogError(message);
            throw new Exception(message);
        }

        ms.Seek(0, System.IO.SeekOrigin.Begin);
        var assembly = Assembly.Load(ms.ToArray());
        var type = assembly.GetType(typeName) ?? throw new Exception($"Type '{typeName}' not found in compiled assembly.");

        return Activator.CreateInstance(type, args) as T;
    }

    private static string Capitalize(string s) => char.ToUpper(s[0]) + s.Substring(1);

    private ILocalCmdProcessorStates CreateProcessorStates(string processorType)
    {
        var cmdName = processorType.ToLower();
        var cmdDisplayName = Capitalize(processorType);
        return new LocalCmdProcessorStates(cmdName, cmdDisplayName);
    }

    public void HandleStaticProcessor(string processorType)
    {
        var assembly = Assembly.GetExecutingAssembly();

        var statesInstance = CreateProcessorStates(processorType);
        _processorStates[processorType] = statesInstance;

        statesInstance.IsCmdAvailable = !_netConfig.DisabledCommands.Contains(statesInstance.CmdName);
        _logger.LogInformation($" Cmd Processor {statesInstance.CmdName} . Status enabled : {statesInstance.IsCmdAvailable}");

        var typeName = $"NetworkMonitor.Connection.{processorType}CmdProcessor";
        var type = assembly.GetType(typeName) ?? throw new Exception($"Cannot find type {typeName}");

        var logger = _loggerFactory.CreateLogger(type);
        var requiresWebAutomation = _requiresWebAutomation.Contains(processorType);

        var instance = CreateProcessorInstance(
            type, logger, statesInstance, _rabbitRepo, _netConfig, requiresWebAutomation);

        _processors[processorType] = instance;
    }

    private List<MetadataReference> GetMetadataReferences()
    {
        if (_cachedReferences != null)
        {
            _logger.LogInformation("Returning cached metadata references.");
            return _cachedReferences;
        }

        var uniqueReferences = new Dictionary<string, MetadataReference>(StringComparer.OrdinalIgnoreCase);

        try
        {
            string additionalDllsPath = Path.Combine(_netConfig.CommandPath ?? string.Empty, "dlls");
            if (Directory.Exists(additionalDllsPath))
            {
                _logger.LogInformation($"Loading additional DLLs from: {additionalDllsPath}");
                foreach (var file in Directory.GetFiles(additionalDllsPath, "*.dll"))
                {
                    AddReference(file, uniqueReferences);
                }
            }
            else _logger.LogWarning($"Failed to load additional DLLs : {additionalDllsPath} dir not found");
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to load additional DLLs: {ex.Message}");
        }

        _cachedReferences = uniqueReferences.Values.ToList();
        _logger.LogInformation($"Total unique metadata references loaded: {_cachedReferences.Count}");
        return _cachedReferences;
    }

    private void AddReference(string filePath, Dictionary<string, MetadataReference> uniqueReferences)
    {
        try
        {
            var fileName = Path.GetFileName(filePath);
            if (!uniqueReferences.ContainsKey(fileName))
            {
                var reference = MetadataReference.CreateFromFile(filePath);
                uniqueReferences[fileName] = reference;
                _logger.LogInformation($"Added reference: {filePath} (FileName: {fileName})");
            }
            else
            {
                _logger.LogDebug($"Skipped duplicate reference: {filePath} (FileName: {fileName})");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to load reference from {filePath}: {ex.Message}");
        }
    }

    private async Task<string> LoadSourceCode(string filePath)
    {
        try { return await File.ReadAllTextAsync(filePath); }
        catch (Exception ex) { throw new Exception($"Failed to load source code from {filePath}: {ex.Message}", ex); }
    }
}
