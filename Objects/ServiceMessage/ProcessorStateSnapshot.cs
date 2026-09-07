using System.Collections.Generic;

namespace NetworkMonitor.Objects.ServiceMessage;

/// <summary>
/// Signed envelope used when Data publishes the complete processor state.
/// A single signature binds the complete list to the fullProcessorList route.
/// </summary>
public sealed class ProcessorStateSnapshot : IBackendSignedMessage
{
    public List<ProcessorObj> Processors { get; set; } = new();
    public string BackendSignature { get; set; } = string.Empty;
}
