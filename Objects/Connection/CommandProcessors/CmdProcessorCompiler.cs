using System;
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
    public CmdProcessorCompiler(ILoggerFactory loggerFactory, NetConnectConfig netConfig, IRabbitRepo rabbitRepo, Dictionary<string, ILocalCmdProcessorStates> processorStates, Dictionary<string, ICmdProcessor> processors, List<string> processorTypes, Dictionary<string, string> sourceCodeFileMap)
    {
        _loggerFactory = loggerFactory;
        _netConfig = netConfig;
        _rabbitRepo = rabbitRepo;
        _logger = _loggerFactory.CreateLogger<CmdProcessorCompiler>();
        _processorStates = processorStates;
        _processors = processors;
        _processorTypes = processorTypes;
        _sourceCodeFileMap = sourceCodeFileMap;
    }

    public async Task<ResultObj> HandleDynamicProcessor(string processorType, bool argsEscaped = false, string processorSourceCode = "")
    {

        var result = new ResultObj();
        try
        {
            if (string.IsNullOrEmpty(processorSourceCode))
            {
                // Load the source code from the file if not provided
                if (_sourceCodeFileMap.TryGetValue(processorType, out var sourceFilePath))
                {
                    processorSourceCode = await LoadSourceCode(sourceFilePath);
                }
                else
                {
                    result.Success = false;
                    result.Message += $" Error : no source code provided and no file mapping found for processor type '{processorType}";
                    return result;
                }
            }



           // Generate LocalCmdProcessorStates source code dynamically
            //string statesSourceCode = GenerateStatesSourceCode(processorType);

            // ** Add Missing Using Statements **
            processorSourceCode = EnsureRequiredUsingStatements(processorSourceCode);
/*
            // Combine both source codes
            string combinedSourceCode = $"{processorSourceCode}\n{statesSourceCode}";

            // Compile and create LocalCmdProcessorStates instance
            var statesInstance = await CompileAndCreateInstance<ILocalCmdProcessorStates>(
                combinedSourceCode,
                $"NetworkMonitor.Objects.Local{processorType}CmdProcessorStates",
                argsEscaped);

            if (statesInstance == null)
            {
                result.Success = false;
                result.Message += $" Error : cannot create instance for states of type: Local{processorType}CmdProcessorStates";
                return result;
            }*/
            var statesInstance = CreateProcessorStates(processorType);
      
            _processorStates[processorType] = statesInstance;

            // Set command availability
            statesInstance.IsCmdAvailable = !_netConfig.DisabledCommands.Contains(statesInstance.CmdName);

            // Compile and create CmdProcessor instance
            var processorLogger = _loggerFactory.CreateLogger($"NetworkMonitor.Connection.{processorType}CmdProcessor");
            var processorInstance = await CompileAndCreateInstance<ICmdProcessor>(
                processorSourceCode,
                $"NetworkMonitor.Connection.{processorType}CmdProcessor",
                argsEscaped,
                processorLogger, statesInstance, _rabbitRepo, _netConfig);

            if (processorInstance == null)
            {
                result.Success = false;
                result.Message += $" Error : cannot create instance for processor of type: {processorType}CmdProcessor";
                return result;
            }

            _processors[processorType] = processorInstance;
            if (!string.IsNullOrEmpty(processorSourceCode))
            {
                // If the source code is provided, you can optionally save it for reuse

                string savePath = Path.Combine(_netConfig.CommandPath, $"{processorType}CmdProcessor.cs");
                try
                {
                    await File.WriteAllTextAsync(savePath, processorSourceCode);

                    // Add to the _sourceCodeFileMap
                    if (!_sourceCodeFileMap.ContainsKey(processorType))
                    {
                        _sourceCodeFileMap[processorType] = savePath;
                        _processorTypes.Add(processorType); // Add to processor types
                    }

                    result.Message += $" Success : saved provided source code for processor '{processorType}' to '{savePath}'.";

                }
                catch (Exception e)
                {
                    result.Message += $" Warning : could no save provided source code for processor '{processorType}' to '{savePath}'. Error was : {e.Message}";

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

    private async Task<T?> CompileAndCreateInstance<T>(string sourceCode, string typeName, bool argsEscaped, params object[] args) where T : class
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
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Take(5)
                .Select(d => d.ToString())
                .ToArray();

            var errorSummary = string.Join("\n", topErrors);
            string exampleContent = string.Empty;

            try
            {
                var exampleFilePath = Path.Combine(_netConfig.CommandPath, "CompileErrorExample.cs");
                if (File.Exists(exampleFilePath))
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
                          "REMEMBER to include the full source code for the class, including all necessary using statements and methods." +
                          exampleContent;

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

        // Set command availability
        statesInstance.IsCmdAvailable = !_netConfig.DisabledCommands.Contains(statesInstance.CmdName);
        _logger.LogInformation($" Cmd Processor {statesInstance.CmdName} . Status enabled : {statesInstance.IsCmdAvailable}");
        // Create processor instance
        var processorTypeName = $"NetworkMonitor.Connection.{processorType}CmdProcessor";
        var processorTypeObj = assembly.GetType(processorTypeName);
        if (processorTypeObj == null)
        {
            throw new Exception($"Cannot find type {processorTypeName}");
        }

        var processorLogger = _loggerFactory.CreateLogger(processorTypeObj);
        var processorInstance = Activator.CreateInstance(processorTypeObj, processorLogger, statesInstance, _rabbitRepo, _netConfig) as ICmdProcessor;

        if (processorInstance == null)
        {
            throw new Exception($"Cannot create instance of {processorTypeName}");
        }

        _processors[processorType] = processorInstance;
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