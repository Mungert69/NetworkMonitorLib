using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.Repository;

namespace NetworkMonitor.Connection
{
    public interface IDynamicConnectFactory
    {
        static abstract INetConnect Create(
            ILogger logger,
            NetConnectConfig cfg,
            ICmdProcessorProvider? cmdProcessorProvider = null,
            IBrowserHost? browserHost = null);

        static abstract string TypeKey { get; }
    }

    public class ConnectCompiler
    {
        private List<MetadataReference>? _cachedReferences;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger _logger;
        private readonly IRabbitRepo _rabbitRepo;
        private readonly NetConnectConfig _netConfig;
        private readonly IBrowserHost? _browserHost;
        private readonly ICmdProcessorProvider? _cmdProcessorProvider;

        public ConnectCompiler(
            ILoggerFactory loggerFactory,
            NetConnectConfig netConfig,
            IRabbitRepo rabbitRepo,
            IBrowserHost? browserHost,
            ICmdProcessorProvider? cmdProcessorProvider)
        {
            _loggerFactory = loggerFactory;
            _netConfig = netConfig;
            _rabbitRepo = rabbitRepo;
            _browserHost = browserHost;
            _cmdProcessorProvider = cmdProcessorProvider;
            _logger = _loggerFactory.CreateLogger<ConnectCompiler>();
        }

        public Type CompileAndGetType(string sourceCode, string typeName)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
            var references = GetMetadataReferences();

            var compilation = CSharpCompilation.Create(
                "DynamicConnectAssembly",
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

        public INetConnect CreateConnectInstance(Type type)
        {
            var logger = _loggerFactory.CreateLogger(type);

            var factory5 = type.GetMethod(
                "Create",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(ILogger), typeof(NetConnectConfig), typeof(ICmdProcessorProvider), typeof(IBrowserHost) },
                modifiers: null);
            if (factory5 != null)
            {
                return (INetConnect)factory5.Invoke(null, new object?[] { logger, _netConfig, _cmdProcessorProvider, _browserHost })!;
            }

            var factory4 = type.GetMethod(
                "Create",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(ILogger), typeof(NetConnectConfig) },
                modifiers: null);
            if (factory4 != null)
            {
                return (INetConnect)factory4.Invoke(null, new object?[] { logger, _netConfig })!;
            }

            var ctor5 = type.GetConstructor(new[] { typeof(ILogger), typeof(NetConnectConfig), typeof(ICmdProcessorProvider), typeof(IBrowserHost) });
            if (ctor5 != null)
            {
                return (INetConnect)ctor5.Invoke(new object?[] { logger, _netConfig, _cmdProcessorProvider, _browserHost })!;
            }

            var ctor4 = type.GetConstructor(new[] { typeof(ILogger), typeof(NetConnectConfig) });
            if (ctor4 != null)
            {
                return (INetConnect)ctor4.Invoke(new object?[] { logger, _netConfig })!;
            }

            var ctor1 = type.GetConstructor(new[] { typeof(NetConnectConfig) });
            if (ctor1 != null)
            {
                return (INetConnect)ctor1.Invoke(new object?[] { _netConfig })!;
            }

            throw new MissingMethodException(
                $"{type.FullName} must expose a compatible Create(...) factory or constructor.");
        }

        private List<MetadataReference> GetMetadataReferences()
        {
            if (_cachedReferences != null)
            {
                return _cachedReferences;
            }

            var refs = new Dictionary<string, MetadataReference>(StringComparer.OrdinalIgnoreCase);
            var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;

            AddReference(Path.Combine(runtimeDir, "System.Private.CoreLib.dll"), refs);
            AddReference(Path.Combine(runtimeDir, "System.Runtime.dll"), refs);
            AddReference(Path.Combine(runtimeDir, "System.Linq.dll"), refs);
            AddReference(Path.Combine(runtimeDir, "System.Collections.dll"), refs);
            AddReference(Path.Combine(runtimeDir, "System.Net.Http.dll"), refs);

            AddReference(typeof(NetConnect).Assembly.Location, refs);
            AddReference(typeof(IRabbitRepo).Assembly.Location, refs);
            AddReference(typeof(ILogger).Assembly.Location, refs);

            try
            {
                string additionalDllsPath = Path.Combine(_netConfig.CommandPath ?? string.Empty, "dlls");
                if (Directory.Exists(additionalDllsPath))
                {
                    foreach (var file in Directory.GetFiles(additionalDllsPath, "*.dll"))
                    {
                        AddReference(file, refs);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to load additional DLLs: {Message}", ex.Message);
            }

            _cachedReferences = refs.Values.ToList();
            return _cachedReferences;
        }

        private static void AddReference(string filePath, Dictionary<string, MetadataReference> refs)
        {
            if (!File.Exists(filePath)) return;
            var fileName = Path.GetFileName(filePath);
            if (!refs.ContainsKey(fileName))
            {
                refs[fileName] = MetadataReference.CreateFromFile(filePath);
            }
        }
    }
}
