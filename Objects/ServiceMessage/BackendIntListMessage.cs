using System.Collections.Generic;

namespace NetworkMonitor.Objects.ServiceMessage;

/// <summary>HMAC-authenticated envelope for backend operations carrying integer IDs.</summary>
public sealed class BackendIntListMessage : IBackendSignedMessage
{
    public List<int> Values { get; set; } = new();
    public string BackendSignature { get; set; } = string.Empty;
}
