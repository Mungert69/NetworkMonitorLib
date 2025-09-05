using System;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
      ILaunchHelper? launchHelper = null);

    static abstract string TypeKey { get; }
}

public interface IRequireLaunchHelper
{
    void SetLaunchHelper(ILaunchHelper? launchHelper);
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
    private readonly ILaunchHelper _launchHelper;
    private readonly List<string> _requiresLaunchHelper = new List<string>();

    public CmdProcessorCompiler(ILoggerFactory loggerFactory, NetConnectConfig netConfig, IRabbitRepo rabbitRepo, Dictionary<string, ILocalCmdProcessorStates> processorStates, Dictionary<string, ICmdProcessor> processors, List<string> processorTypes, Dictionary<string, string> sourceCodeFileMap, ILaunchHelper launchHelper, List<string> requiresLaunchHelper)
    {
        _loggerFactory = loggerFactory;
        _netConfig = netConfig;
        _rabbitRepo = rabbitRepo;
        _logger = _loggerFactory.CreateLogger<CmdProcessorCompiler>();
        _processorStates = processorStates;
        _processors = processors;
        _processorTypes = processorTypes;
        _sourceCodeFileMap = sourceCodeFileMap;
        _launchHelper = launchHelper;
        _requiresLaunchHelper = requiresLaunchHelper;
    }

    private ICmdProcessor CreateProcessorInstance(
    Type type,
    ILogger logger,
    ILocalCmdProcessorStates states,
    IRabbitRepo repo,
    NetConnectConfig cfg,
    ILaunchHelper? launchHelper,
    bool requiresLaunchHelper)
    {
        // 0) Prefer a static Create(...) if author provided one (not required)
        var factory = type.GetMethod(
            "Create",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: new[] { typeof(ILogger), typeof(ILocalCmdProcessorStates), typeof(IRabbitRepo), typeof(NetConnectConfig), typeof(ILaunchHelper) },
            modifiers: null);
        if (factory != null)
        {
            return (ICmdProcessor)factory.Invoke(null, new object?[] { logger, states, repo, cfg, launchHelper })!;
        }

        // 1) Try 5-arg ctor
        var ctor5 = type.GetConstructor(new[] {
        typeof(ILogger), typeof(ILocalCmdProcessorStates), typeof(IRabbitRepo), typeof(NetConnectConfig), typeof(ILaunchHelper)
    });
        if (ctor5 != null)
        {
            return (ICmdProcessor)ctor5.Invoke(new object?[] { logger, states, repo, cfg, launchHelper })!;
        }

        // 2) Try 4-arg ctor (what we want the LLM to use)
        var ctor4 = type.GetConstructor(new[] {
        typeof(ILogger), typeof(ILocalCmdProcessorStates), typeof(IRabbitRepo), typeof(NetConnectConfig)
    });
        if (ctor4 != null)
        {
            var instance = (ICmdProcessor)ctor4.Invoke(new object?[] { logger, states, repo, cfg })!;
            if (launchHelper != null)
            {
                TryInjectLaunchHelper(type, instance, launchHelper);
            }
            if (requiresLaunchHelper && launchHelper != null)
            {
                // Validate that injection actually happened
                if (!DidInjectLaunchHelper(type, instance))
                {
                    throw new MissingMethodException(
                        $"{type.FullName} requires ILaunchHelper but does not expose a 5-arg ctor, a public settable property " +
                        "'ILaunchHelper LaunchHelper', a public method 'SetLaunchHelper(ILaunchHelper)', or implement IRequireLaunchHelper.");
                }
            }
            return instance;
        }

        throw new MissingMethodException(
            $"{type.FullName} must expose (ILogger, ILocalCmdProcessorStates, IRabbitRepo, NetConnectConfig[, ILaunchHelper]) or provide a static Create(...).");
    }

    private void TryInjectLaunchHelper(Type type, ICmdProcessor instance, ILaunchHelper launchHelper)
    {
        var lhType = typeof(ILaunchHelper);

        // a) Property injection: public ILaunchHelper? LaunchHelper { get; set; }
        var prop = type.GetProperty("LaunchHelper", BindingFlags.Public | BindingFlags.Instance);
        if (prop != null && prop.CanWrite && prop.PropertyType.IsAssignableFrom(lhType))
        {
            prop.SetValue(instance, launchHelper);
            return;
        }

        // b) Explicit setter method: public void SetLaunchHelper(ILaunchHelper)
        var setter = type.GetMethod("SetLaunchHelper",
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            types: new[] { lhType },
            modifiers: null);
        if (setter != null)
        {
            setter.Invoke(instance, new object[] { launchHelper });
            return;
        }

        // c) Interface-based setter: IRequireLaunchHelper.SetLaunchHelper
        if (instance is IRequireLaunchHelper req)
        {
            req.SetLaunchHelper(launchHelper);
            return;
        }
    }

    private bool DidInjectLaunchHelper(Type type, ICmdProcessor instance)
    {
        // Best-effort check: if property exists and is non-null after injection, consider it success.
        var prop = type.GetProperty("LaunchHelper", BindingFlags.Public | BindingFlags.Instance);
        if (prop != null && prop.CanRead && typeof(ILaunchHelper).IsAssignableFrom(prop.PropertyType))
        {
            return prop.GetValue(instance) != null;
        }
        // If there’s only a setter or interface method, we can’t reflectively verify; assume success.
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
            // (reuse your existing error summarizer from CompileAndCreateInstance<T>)
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
            // 1) Load source if not provided
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

            // 2) Ensure required usings are present (so LLM can send minimal code)
            processorSourceCode = EnsureRequiredUsingStatements(processorSourceCode);

            // 3) Create / register states
            var statesInstance = CreateProcessorStates(processorType);
            _processorStates[processorType] = statesInstance;
            statesInstance.IsCmdAvailable = !_netConfig.DisabledCommands.Contains(statesInstance.CmdName);

            // 4) Compile to an in-memory assembly and fetch the processor Type
            var typeName = $"NetworkMonitor.Connection.{processorType}CmdProcessor";
            var type = CompileAndGetType(processorSourceCode, typeName, argsEscaped, includeExample);

            // 5) Build logger and choose whether this processor needs LaunchHelper
            var processorLogger = _loggerFactory.CreateLogger(type);
            var requiresLaunchHelper = _requiresLaunchHelper.Contains(processorType);

            // 6) Centralized creation:
            //    prefers static Create(...), then 5-arg ctor, then 4-arg ctor (+ soft inject LaunchHelper)
            var processorInstance = CreateProcessorInstance(
                type,
                processorLogger,
                statesInstance,
                _rabbitRepo,
                _netConfig,
                _launchHelper,
                requiresLaunchHelper);

            if (processorInstance == null)
            {
                result.Success = false;
                result.Message += $" Error : cannot create instance for processor of type: {processorType}CmdProcessor";
                return result;
            }

            // 7) Register the instance
            _processors[processorType] = processorInstance;

            // 8) (Optional) persist the source for reuse
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
        // List of required namespaces
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

        // Extract existing using statements from the source code
        var existingNamespaces = new HashSet<string>(sourceCode.Split('\n')
            .Where(line => line.Trim().StartsWith("using "))
            .Select(line => line.Trim()), StringComparer.OrdinalIgnoreCase);

        // Identify and add missing namespaces
        var missingNamespaces = requiredNamespaces.Where(ns => !existingNamespaces.Contains(ns)).ToList();
        var updatedSourceCode = string.Join("\n", missingNamespaces) + "\n" + sourceCode;

        return updatedSourceCode;
    }


    private string GenerateStatesSourceCode(string processorType)
    {
        return $@"
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
    }

    private async Task<T?> CompileAndCreateInstance<T>(string sourceCode, string typeName, bool argsEscaped, bool includeExample, params object[] args) where T : class
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);

        var references = GetMetadataReferences();

        var compilation = CSharpCompilation.Create(
            "DynamicAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        using var ms = new System.IO.MemoryStream();
        var result = compilation.Emit(ms);

        if (!result.Success)
        {  // Get the top 5 errors for brevity


            var topErrors = result.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)              // 1️⃣ keep only errors :contentReference[oaicite:2]{index=2}
                .GroupBy(d => d.Id)                                              // 2️⃣ bucket by CS-code
                .Select(g => g.OrderBy(d => d.Location.SourceSpan.Start)
                              .First())                                          // 3️⃣ pick earliest instance in each file
                .OrderBy(d => d.Location.SourceSpan.Start)                       // 4️⃣ stable order across buckets
                .Take(10)                                                        // 5️⃣ cap at ten
                .Select(d =>
                {
                    var msg = $"error {d.Id}: {d.GetMessage(CultureInfo.InvariantCulture)}"; // formatted text :contentReference[oaicite:3]{index=3}
                    var link = $"https://learn.microsoft.com/dotnet/csharp/misc/{d.Id.ToLower()}"; // docs pattern :contentReference[oaicite:4]{index=4}
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
                            source_code = System.Text.Json.JsonSerializer.Serialize(source_code);
                            // Remove unnecessary quotes added by JsonSerializer
                            source_code = source_code.Trim('"');
                        }

                        exampleContent = "\nHere is a simple example implementation:\n" + source_code + "\n";

                        // Add a note if we need to give example with escapping allied. the default is false. You would set this at the source. Where does the source code come from a XML CDATA section then set false. Or escapped json escapped at source. Note its only to give an example in the format the sending is using
                        if (argsEscaped)
                        {
                            exampleContent +=
                                "NOTE: The example shows the .NET code properly escaped so it can be used as a string in the JSON source_code parameter.\n";
                        }

                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Error reading CompileErrorExample.cs: {ex.Message}");
            }

            // Build the final error message
            var message = $"Compilation failed with the following errors:\n{errorSummary}\n" +
                          "REMEMBER to include the full source code for the class, including all necessary using statements and methods. ";
            if (includeExample && !string.IsNullOrEmpty(exampleContent))
            {
                message += exampleContent;
            }

            _logger.LogError(message);
            throw new Exception(message);
        }

        ms.Seek(0, System.IO.SeekOrigin.Begin);
        var assembly = Assembly.Load(ms.ToArray());
        var type = assembly.GetType(typeName);

        if (type == null)
        {
            throw new Exception($"Type '{typeName}' not found in compiled assembly.");
        }

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
        var requiresLH = _requiresLaunchHelper.Contains(processorType);

        var instance = CreateProcessorInstance(type, logger, statesInstance, _rabbitRepo, _netConfig, _launchHelper, requiresLH);
        _processors[processorType] = instance;
    }


    private List<MetadataReference> GetMetadataReferences()
    {
        if (_cachedReferences != null)
        {
            _logger.LogInformation("Returning cached metadata references.");
            return _cachedReferences;
        }

        var uniqueReferences = new Dictionary<string, MetadataReference>(StringComparer.OrdinalIgnoreCase); // Use a dictionary for fast lookup and case-insensitive keys.

        // 1. Load Additional DLLs (Highest Priority)
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

        /*  // 2. Load AppDomain Assemblies (Second Priority)
          try
          {
              _logger.LogInformation("Loading assemblies from the current AppDomain.");
              foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location)))
              {
                  AddReference(assembly.Location, uniqueReferences);
              }
          }
          catch (Exception ex)
          {
              _logger.LogWarning($"Failed to load AppDomain assemblies: {ex.Message}");
          }

          // 3. Load Runtime Assemblies (Lowest Priority)
          try
          {
              string runtimeDirectory = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
              _logger.LogInformation($"Loading runtime assemblies from: {runtimeDirectory}");
              foreach (var file in Directory.GetFiles(runtimeDirectory, "*.dll"))
              {
                  AddReference(file, uniqueReferences);
              }
          }
          catch (Exception ex)
          {
              _logger.LogWarning($"Failed to load runtime assemblies: {ex.Message}");
          }*/

        // Cache and return references
        _cachedReferences = uniqueReferences.Values.ToList(); // Extract the unique references from the dictionary
        _logger.LogInformation($"Total unique metadata references loaded: {_cachedReferences.Count}");
        return _cachedReferences;
    }

    private void AddReference(string filePath, Dictionary<string, MetadataReference> uniqueReferences)
    {
        try
        {
            // Use the file name (e.g., "System.Runtime.dll") as the key
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
        try
        {
            return await File.ReadAllTextAsync(filePath);
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to load source code from {filePath}: {ex.Message}", ex);
        }
    }


}