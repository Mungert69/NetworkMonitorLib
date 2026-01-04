# NetworkMonitorLib

Core shared library for the NetworkMonitor platform. It provides reusable domain
objects, command processors, and connection helpers used by the agent runtime,
LLM services, and UI clients.

## What this library provides
- Shared objects and DTOs for processors, services, and UI clients.
- Command processors (static + dynamic) and the command processor framework.
- Connection factories (`ConnectFactory`, `EndPointTypeFactory`) for monitor checks.
- Utilities/helpers used by both service and client projects.

## Key folders
- `Objects/` shared domain models, DTOs, and service messages.
- `Objects/Connection/` connection types, command processors, and factories.
- `Utils/` and `Helpers/` cross-cutting utilities.

## Build
This library is referenced by components like `NetworkMonitorLLM`,
`NetworkMonitorBlazor`, and the processor agent. Build it before those projects
if you are working outside a solution that includes it.

```bash
dotnet restore
dotnet build -c Release
```

## Contribution guidelines
- Follow existing architectural patterns.
- Maintain interface compatibility.
- Document configuration changes when adding new processors or message types.


