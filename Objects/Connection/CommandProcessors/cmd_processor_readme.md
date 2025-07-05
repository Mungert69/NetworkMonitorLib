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

## Dynamic Cmd Processors — What They **can** and **can’t** do

> **Context recap:**
> `CmdProcessorCompiler` lets you push *raw C# source code* (via RabbitMQ, or by dropping a `*.cs` file in `CommandPath`) and have it compiled at runtime into a fully-fledged `ICmdProcessor`.
> The same agent process then instantiates the new class, wires in logging / config / Rabbit repo, and hosts it exactly like the baked-in Nmap, Ping, etc.

Below is an exhaustive list of capabilities, guard-rails, and hard limitations.

---

### 1  What you **can** do

| Area                            | Practical freedom                                                                                                                                                                  |
| ------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Language & APIs**             | Write **any C# 9/10** code (whatever the agent’s runtime supports) — async/await, LINQ, reflection, P/Invoke, sockets, file I/O, etc.                                              |
| **External tools**              | Spawn *any* executable reachable from the agent’s file-system (same trick that NmapCmdProcessor uses via `Process.Start`).                                                         |
| **Imports**                     | Reference **all assemblies** already loaded into the AppDomain **plus** any `*.dll` placed in `<CommandPath>/dlls`.                                                                |
| **Queues & Cancellation**       | Get the base-class goodies: automatic queuing, semaphore-based parallelism, `CancellationToken` kill-switch, pagination, RabbitMQ streaming.                                       |
| **State sharing**               | Access / mutate the injected `ILocalCmdProcessorStates` — e.g. expose extra flags and have another service flip them in real time.                                                 |
| **Persistence across restarts** | When source arrives via Rabbit, the compiler saves `YourTypeCmdProcessor.cs` in `CommandPath`; after a restart it’s treated as a “static” processor and re-compiled automatically. |
| **Full-trust execution**        | Code runs in-process with the agent’s own privileges, so you can open raw sockets, read environment variables, mount `DllImport` shims, etc.                                       |
| **Dynamic updates**             | Send a newer source file with the same `cmd_processor_type`; it overwrites the old file, re-compiles, and hot-swaps without redeploying the binary.                                |

---

### 2  Hard **requirements / guard-rails**

| Rule                                                                                                                                                      | Enforced by                        | Error message if violated                                    |
| --------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------- | ------------------------------------------------------------ |
| **Namespace** must be `NetworkMonitor.Connection`.                                                                                                        | Regex checks in `AddCmdProcessor`. | “you must include the namespace NetworkMonitor.Connection …” |
| **Class name** must be exactly `«Type»CmdProcessor`.                                                                                                      | Same.                              | “public class FooCmdProcessor … not found”                   |
| `cmd_processor_type` **string** (the Rabbit field / filename) **must not contain spaces or the word “CmdProcessor”**.                                     | Same.                              | Specific helper messages.                                    |
| Missing `using` lines get auto-injected, **but** you still need the usual .NET SDK namespaces for exotic APIs.                                            |                                    |                                                              |
| Roslyn compile **errors** abort the add-operation; only the **top 5** diagnostics are returned (plus an example stub).                                    |                                    |                                                              |
| Constructor **signature** must match the base-class: `(ILogger, ILocalCmdProcessorStates, IRabbitRepo, NetConnectConfig [, int queueLen])`.               |                                    |                                                              |
| **Disabled commands**: if `netConfig.DisabledCommands` contains the `CmdName`, `states.IsCmdAvailable` is set false and the processor will refuse to run. |                                    |                                                              |

---

### 3  Platform & dependency **limitations**

1. **No NuGet restore** — you can only reference assemblies already on disk (`dlls/` or shipped with the agent).
2. **Single AppDomain** — .NET can’t unload individual dynamic assemblies; repeated hot-reloads keep stacking in memory until the agent restarts.
3. **Semaphore bottleneck** — per-processor parallelism is capped (default 5). If you need >5 concurrent external commands you must raise it in your constructor.
4. **No built-in sandbox** — buggy or malicious code can crash the entire agent process; there’s no AppContainer or CAS isolation.
5. **Process-level rights** — dynamic code inherits the agent’s OS privileges; it cannot self-elevate beyond that.
6. **Disk paths** — compiled DLL + source are written under `CommandPath`; if that path is read-only the save-step fails (but you still get the in-memory instance for this session).
7. **Dependency search order** (for Roslyn references):

   1. `CommandPath/dlls/*.dll`
   2. Already-loaded AppDomain assemblies
   3. .NET runtime directory
      If a required assembly isn’t in those loci the compile fails.

---

### 4  Operational constraints

| Constraint              | Practical impact                                                                                                                                          |
| ----------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Message-driven API**  | All calls are routed through Rabbit → you must adhere to existing `ProcessorScanDataObj` schema (e.g. `arguments`, `LineLimit`, `Page`).                  |
| **Pagination footer**   | Consumers must strip the footer when `LineLimit != –2`, otherwise downstream JSON parsing may break.                                                      |
| **Cancellation window** | If the external CLI ignores `SIGKILL` (rare on Windows), the process may stay zombie; build timeout / watchdog logic into your processor if that matters. |
| **Resource leaks**      | Long-running `Task`s or `Timer`s you spin up won’t be auto-disposed when the provider removes the processor; manage your own lifetime.                    |

---

### 5  Security considerations & things you **shouldn’t** do

* **Don’t** run untrusted code — the compiler executes with full agent trust; it can `rm -rf /`, exfiltrate credentials, or embed reverse shells. Only allow code from authenticated, audited sources.
* **Don’t** block the queue pump — long-synchronous loops inside `Scan()` will hog the CPU thread and starve other tasks; always `await` I/O.
* **Don’t** swallow `OperationCanceledException`; let the base class see it so it can mark the task as cancelled.
* **Don’t** gossip huge blobs via `SendMessage`; paginate or save to a file and transmit a URL if output > a few MB — RabbitMQ memory pressure is real.
* **Don’t** rely on static-field singletons across dynamic recompiles; each compile makes a new assembly, and static state from old versions lingers.
* **Don’t** assume GUI Chromium — `LaunchHelper.CheckDisplay` may flip to headless if `DISPLAY`/`WAYLAND_DISPLAY` not set.

---

### 6  Extensibility checklist (✅ / ❌ quick view)

| Feature                                          | Supported?                                            |
| ------------------------------------------------ | ----------------------------------------------------- |
| Add brand-new processor at runtime               | ✅                                                     |
| Hot-replace existing processor                   | ✅                                                     |
| Use external NuGet packages on the fly           | ❌ (must pre-bundle DLL)                               |
| Raise per-processor concurrency >5               | ✅ (ctor arg)                                          |
| Reduce per-processor concurrency to 1            | ✅                                                     |
| Unload/destroy a processor without agent restart | ❌ (can stop calling it, but assembly stays in memory) |
| Persist custom state across restarts             | ❌ (unless you write to disk yourself)                 |
| Reference Win32 / libc via `DllImport`           | ✅ (same privs as agent)                               |
| Limit a processor to a specific OS user          | ❌ out of the box (would need `runas` wrapper)         |

---

### 7  Best-practice pointers

1. **Small adapters, big helpers** – keep each processor thin (parse args, call helper class). That way recompiles are cheap and business logic is testable outside the agent.
2. **Validate inputs early** – throw when `arguments` are obviously malformed so the caller sees an immediate Rabbit reply.
3. **Surface progress** – write interim status to `_cmdProcessorStates.RunningMessage` and call `SendMessage` periodically for long scans.
4. **Graceful shutdown** – tie every background loop to `CancellationToken`; the standard `CancelCommand` path then works automatically.
5. **Ship dependencies alongside code** – drop any extra `*.dll` in `CommandPath/dlls` *before* you push the new processor so the compile succeeds first time.

---

#### TL;DR

Dynamic Cmd Processors give you **nearly unrestricted C# + OS-level power** inside the agent, with first-class queuing, cancellation, and RabbitMQ streaming already solved.
The trade-offs are: no NuGet at runtime, no sandbox, and assemblies stay loaded until restart. Design accordingly, validate aggressively, and you can wrap almost any CLI or write pure-C# scanners on the fly.
