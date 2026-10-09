using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace NetworkMonitor.Connection.CommandProcessors.Tests;

public class BleSnapshotProcessorCompilationTests
{
    [Theory]
    [InlineData("BleBroadcastCmdProcessor", typeof(IBleBroadcastSnapshotProcessor))]
    [InlineData("BleBroadcastListenCmdProcessor", typeof(IBleListenSnapshotProcessor))]
    public void RuntimeCompiledProcessorsImplementSharedSnapshotContracts(string name, Type contract)
    {
        // CommandPath can load these classes into a separate assembly: concrete-type casts would fail.
        string path = Path.Combine(Path.GetDirectoryName(SourcePath())!, "..", name + ".cs");
        var locations = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Concat(AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Select(a => a.Location)).Distinct();
        var references = locations.Select(p => MetadataReference.CreateFromFile(p));
        var compilation = CSharpCompilation.Create("BleSnapshotCompilation_" + Guid.NewGuid().ToString("N"),
            new[] { CSharpSyntaxTree.ParseText(File.ReadAllText(path)) }, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var output = new MemoryStream();
        var result = compilation.Emit(output);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        var assembly = Assembly.Load(output.ToArray());
        Assert.True(contract.IsAssignableFrom(assembly.GetType("NetworkMonitor.Connection." + name)));
    }
    private static string SourcePath([CallerFilePath] string path = "") => path;
}
