using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NetworkMonitor.Objects.ServiceMessage;

public class MonitorMLInitObj : IBackendSignedMessage
{
    public MonitorMLInitObj() { }
    private bool _isMLReady;
    private bool _totalReset = false;

    public bool IsMLReady { get => _isMLReady; set => _isMLReady = value; }
    public bool TotalReset { get => _totalReset; set => _totalReset = value; }
    public string BackendSignature { get; set; } = "";

}
