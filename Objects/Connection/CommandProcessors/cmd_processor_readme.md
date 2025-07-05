# Quantum Network Monitor — CmdProcessor Architecture Guide

This guide provides a **conceptual and code‑level walkthrough** of the *CmdProcessor* subsystem found in the Quantum Network Monitor agent.
It is written for large‑language models (LLMs) and human developers who need to reason about, extend, or generate code that integrates with this framework.

---

## 1. High‑Level Overview

```
           ┌─────────────┐
           │  UI / API   │  (requests)
           └─────┬───────┘
                 │
      ◄──────────┴──────────► RabbitMQ message bus
                 │
           ┌─────▼──────┐
           │ CmdProvider │  ← maps processor type → ICmdProcessor
           └─────┬──────┘
                 │
         ┌───────▼────────┐
         │  ICmdProcessor  │  (contract)
         ├───────┬────────┤
         │ CmdProcessor    │  (abstract base)
         ├───────┴────────┤
         │Nmap│Meta│… etc.│ (concrete)
         └───────────────┘
```

* **CmdProcessor** — an abstract base that wraps OS‑level command execution, queueing, cancellation, pagination, and messaging.
* **Concrete processors** (e.g. `NmapCmdProcessor`) override *domain logic* such as `Scan` or `RunCommand`.
* **CmdProcessorProvider** acts as a factory/registry, returning processors on demand and orchestrating *dynamic* compilation via **CmdProcessorCompiler**.
* **RabbitRepo** bridges the processors with external services through RabbitMQ.
* **LocalCmdProcessorStates** stores per‑processor runtime state and preferences; one instance per processor.

---

## 2. Key Types & Their Responsibilities

| Type                      | Namespace                   | Key Responsibilities                                                                                                                      |
| ------------------------- | --------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------- |
| `ICmdProcessor`           | `NetworkMonitor.Connection` | Public façade. Exposes **Scan / Cancel / RunCommand** and property `UseDefaultEndpoint`.                                                  |
| `CmdProcessor` (abstract) | same                        | Implements **queueing**, **process spawning**, **stream aggregation**, **pagination**, **cancellation**, and **message publication**.     |
| `CommandTask`             | same                        | DTO that couples a `Func<Task>` with its `CancellationTokenSource`, plus status flags.                                                    |
| `LocalCmdProcessorStates` | `NetworkMonitor.Objects`    | Holds long‑lived state (*IsRunning*, *RunningMessage*, selected NIC, etc.). Declared once per concrete processor.                         |
| `CmdProcessorProvider`    | `NetworkMonitor.Connection` | Maintains dictionaries for **processors** & **states**; wires core processors statically; compiles dynamic ones; routes cancel requests.  |
| `CmdProcessorCompiler`    | same                        | Uses Roslyn to compile uploaded C# source into an assembly **in‑memory**, create instances, cache references, and persist the `.cs` file. |
| `NmapCmdProcessor`        | same                        | Example concrete implementation that shells out to `nmap`, parses XML output, and converts results to `MonitorIP` records.                |

---

## 3. Execution Flow

### 3.1 Fast‑Path (`Scan` request)

1. *Call chain:* UI → message → **CmdProcessorProvider.Scan()**
2. Provider fetches `ICmdProcessor` instance from `_processors` and awaits `Scan()`.
3. `CmdProcessor.Scan` (base) is **virtual** and by default logs a *not‑available* warning.
4. Concrete processor (e.g. **NmapCmdProcessor**) overrides `Scan`:

   1. Creates a new `CancellationTokenSource` (stored in field `_cancellationTokenSource`).
   2. Pushes tasks to the internal queue via `QueueCommand`.
   3. Uses helper methods like **ParseNmapOutput** to transform raw output.
   4. Writes progress into `LocalCmdProcessorStates` and pushes paginated updates through RabbitMQ with `SendMessage`.

### 3.2 Queue Processor

* **\_currentQueue** — `ConcurrentQueue<CommandTask>` shared by all callers to this processor instance.
* **\_semaphore** — limits parallelism (default = 5).
* `StartQueueProcessorAsync` spins forever, pulling tasks, waiting for available slots, flagging them as running, and launching them via `Task.Run`.
* After each task completes (success, fault, or cancel), `_semaphore.Release()` frees the slot.

### 3.3 Cancellation

* Each `CommandTask` owns its own `CancellationTokenSource`.
* Public API `CancelCommand(messageId)` looks up the running task in `_runningTasks`, calls `Cancel()`, and awaits graceful termination.

### 3.4 Process Execution (`RunCommand`)

```
ProcessStartInfo { FileName = _netConfig.CommandPath + _cmdName,
                   Arguments = userArgs,
                   RedirectStandard{Out,Err} = true,
                   CreateNoWindow = true }
```

* Streams are captured asynchronously via `OutputDataReceived`.
* When `CancellationToken` fires, the callback kills the OS process.
* Combined output is **optionally** split into pages (`LineLimit` logic) and serialized to JSON for Rabbit.

---

## 4. Dynamic Processor Compilation

1. An external service posts source code (string) and `cmd_processor_type`.
2. **CmdProcessorCompiler.HandleDynamicProcessor**:

   1. Ensures the code contains required `using`s and class naming (`TypeCmdProcessor`).
   2. Generates/uses *LocalCmdProcessorStates* implementation.
   3. Calls **Roslyn** (`CSharpCompilation`) and loads the resulting assembly into memory.
   4. Instantiates the states + processor with DI arguments (`ILogger`, states, `IRabbitRepo`, `NetConnectConfig`).
   5. Saves the `.cs` file under `CommandPath` and updates `_sourceCodeFileMap` for hot‑reload persistence.

> The compiler caches `MetadataReference`s, including **any DLLs placed in `commandPath/dlls`**, so dynamic processors can reference third‑party libraries.

---

## 5. Helper Utilities

| Helper                   | Purpose                                                                                                                                      |
| ------------------------ | -------------------------------------------------------------------------------------------------------------------------------------------- |
| **CrawlHelper**          | Browser automation via *PuppeteerSharp*; simulates scrolling, handles cookie‑consent pop‑ups, extracts content & links, retries resiliently. |
| **LaunchHelper**         | Downloads a headless Chromium build (if absent) and builds the `LaunchOptions` respecting GUI availability.                                  |
| **ConnectHelper**        | Loads algorithm metadata from JSON/CSV and correlates with enabled curves.                                                                   |
| **DictionaryExtensions** | Adds terse getters (`GetInt`, `GetBool`, etc.) for `Dictionary<string,string>` argument maps.                                                |

These helpers are **optional** to a given processor, but concrete implementations (e.g. *CrawlSiteCmdProcessor*) rely on them heavily.

---

## 6. Configuration Hot‑Points

* **`NetConnectConfig`** — central object injected everywhere; contains *CommandPath*, *Rabbit* credentials, and feature flags.
* **DisabledCommands** list → controls `IsCmdAvailable`.
* **`queueLength`** constructor arg → limits concurrency per processor instance (defaults to 5).
* **Environment variables (`DISPLAY`, `WAYLAND_DISPLAY`)** influence headless vs. GUI mode.

---

## 7. Extending the System

1. **Create a new class** `MyToolCmdProcessor` **inside** `namespace NetworkMonitor.Connection`.
2. **Inherit** from `CmdProcessor`.
3. **Override** any of:

   * `Scan()`
   * `AddServices()`
   * `RunCommand(...)`
   * `GetCommandHelp()`
4. Keep constructor signature: `(ILogger, ILocalCmdProcessorStates, IRabbitRepo, NetConnectConfig)`.
5. Publish your source via **AddCmdProcessor** message; the compiler takes care of integration.

> ⚠️ *Do **not** include spaces or the word “CmdProcessor” in the `cmd_processor_type` property—only the bare prefix (e.g. "MyTool").*

---

## 8. Error & Message Semantics

* Every public method returns **`ResultObj`** with `.Success`, `.Message`, and (optionally) `.Data`.
* Critical failures bubble up to RabbitMQ so remote callers receive explicit diagnostics.
* Compilation errors are truncated to the **top 5** Roslyn diagnostics plus an inline example stub.

---

## 9. Thread‑Safety & Concurrency Notes

* All shared collections are either **`Concurrent*`** or accessed under deterministic single‑writer patterns.
* **`SemaphoreSlim`** protects the OS from being overloaded by heavy scans.
* The long‑running queue poller uses `Task.Delay(1000)` back‑off to avoid tight loops when idle.

---

## 10. Signals for LLM Reasoning

When instructing LLMs to generate or refactor code:

1. Always include the **required `using` directives**; missing ones break dynamic compilation.
2. Follow the naming convention `TypeCmdProcessor` and `LocalTypeCmdProcessorStates`.
3. Ensure **asynchronous** work is awaited or explicitly fire‑and‑forgot (`_ = SomeTask();`).
4. Provide helpful **`GetCommandHelp()`** overrides; the agent surfaces these through UI/CLI.
5. Remember pagination: output > `LineLimit` needs chunking to avoid Rabbit message size limits.

---

### Appendix A — Minimal Processor Template

```csharp
using NetworkMonitor.Connection;
using Microsoft.Extensions.Logging;
using NetworkMonitor.Objects.Repository;
using NetworkMonitor.Objects.ServiceMessage;

namespace NetworkMonitor.Connection
{
    public class HelloCmdProcessor : CmdProcessor
    {
        public HelloCmdProcessor(
            ILogger logger,
            ILocalCmdProcessorStates states,
            IRabbitRepo rabbit,
            NetConnectConfig cfg) : base(logger, states, rabbit, cfg) { }

        public override async Task Scan()
        {
            _cmdProcessorStates.IsRunning = true;
            var res = await RunCommand("--version", CancellationToken.None);
            _cmdProcessorStates.CompletedMessage += res.Message;
            _cmdProcessorStates.IsRunning = false;
        }

        public override string GetCommandHelp() => "arguments: --version";
    }
}
```

---

