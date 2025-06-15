# NetworkMonitorLib Technical Documentation

## Core Purpose
Provides a class library for the Quantum Network Monitor Service Components.

### Build this Library when building Components Like NetworkMonitorLLM, NetworkMonitorBlazor etc. 

To build the NetworkMonitor.dll that can be used with other Quantum Network Monitor Service Components

```bash
# Clone repository
git clone https://github.com/Mungert69/NetworkMonitorLib.git

# Restore dependencies
dotnet restore

# Build solution
dotnet build -c Release
```

## Contribution Guidelines

* Follow existing architectural patterns
* Maintain interface compatibility
* Document configuration changes



