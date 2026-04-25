# Workflow Execution Isolation

DaisiGit workflows (CI/CD automations triggered by git events) execute in isolated Azure Container Apps Jobs rather than inside the web application process. This provides strong security boundaries for user-defined scripts and deployments.

## Why Isolation?

Workflows can execute arbitrary shell commands via the **RunScript** step type. Running these in-process with the web app creates security risks:

- **No filesystem isolation** — scripts could access other workflow workspaces or app files
- **No resource limits** — a runaway script could exhaust the web server's CPU/memory
- **Secret exposure** — decrypted secrets live in the web app's memory space
- **No network isolation** — scripts or HTTP steps could make requests to internal services

By running each workflow execution in its own container, we get process-level, filesystem, and resource isolation per execution.

## Architecture

```
Git Event (push, PR, issue, etc.)
    │
    ▼
WorkflowTriggerService
    │  Creates WorkflowExecution in Cosmos DB (status: "Running")
    │
    ▼
GitWorkflowBackgroundWorker (polls every 30s)
    │  Enqueues { executionId, accountId } to Azure Storage Queue
    │  Updates status to "Dispatched"
    │
    ▼
Azure Container Apps Job (queue-triggered)
    │  Dequeues message
    │  Loads execution + workflow from Cosmos DB
    │  Runs WorkflowEngine.ProcessExecutionAsync()
    │  All steps execute inside the container
    │  Results written back to Cosmos DB
    │  Container destroyed after completion
    │
    ▼
Web UI reads execution results from Cosmos DB
```

### Key components

| Component | Location | Role |
|-----------|----------|------|
| `WorkflowEngine` | `DaisiGit.Services/WorkflowEngine.cs` | Executes workflow steps (unchanged) |
| `WorkflowDispatchService` | `DaisiGit.Web/Services/WorkflowDispatchService.cs` | Enqueues executions to Storage Queue |
| `GitWorkflowBackgroundWorker` | `DaisiGit.Web/Services/GitWorkflowBackgroundWorker.cs` | Dispatches + watchdog for stuck executions |
| `WorkflowQueueProcessor` | `DaisiGit.Worker/WorkflowQueueProcessor.cs` | Dequeues and processes in container |

### Execution statuses

| Status | Meaning |
|--------|---------|
| `Running` | Newly created, not yet dispatched |
| `Dispatched` | Sent to queue, waiting for worker pickup |
| `Completed` | All steps finished successfully |
| `Failed` | A step failed or execution timed out |
| `Cancelled` | Manually cancelled |

## Infrastructure

The worker runs on Azure Container Apps Jobs (Consumption plan), which scales to zero when idle and spins up a container per queued execution.

### Resources (provisioned via Bicep)

- **Azure Container Registry** — stores the worker Docker image
- **Azure Storage Queue** (`workflow-executions`) — dispatch queue
- **Container Apps Environment** — Consumption plan, scale-to-zero
- **Container Apps Job** — queue-triggered, 0.5 vCPU / 1 GB per execution, 30-min timeout

Bicep template: `DaisiGit.Worker/infra/main.bicep`

### Deploying infrastructure

```bash
az deployment group create \
  --resource-group <rg> \
  --template-file DaisiGit.Worker/infra/main.bicep \
  --parameters cosmoConnectionString='<cosmos-connection>' daisiSecretKey='<secret-key>'
```

### Deploying the worker image

The GitHub Actions workflow (`.github/workflows/deploy-worker.yml`) builds the Docker image and pushes it to ACR. Trigger manually via `workflow_dispatch`.

## Configuration

### Web app (`DaisiGit.Web/appsettings.json`)

```json
{
  "WorkflowQueue": {
    "ConnectionString": "<azure-storage-connection-string>"
  }
}
```

When `WorkflowQueue:ConnectionString` is empty, the web app falls back to in-process execution (useful for local development).

### Worker (`DaisiGit.Worker/appsettings.json`)

```json
{
  "Cosmo": {
    "ConnectionString": "<cosmos-connection-string>"
  },
  "WorkflowQueue": {
    "ConnectionString": "<azure-storage-connection-string>"
  }
}
```

In production, these are injected as environment variables by the Container Apps Job configuration.

## Local Development

For local dev, the worker is optional. When no `WorkflowQueue:ConnectionString` is configured, the web app processes workflows in-process as before.

To test the full queue-based flow locally:

1. Install and start [Azurite](https://learn.microsoft.com/en-us/azure/storage/common/storage-use-azurite) for local Storage Queue emulation
2. Set `WorkflowQueue:ConnectionString` to `UseDevelopmentStorage=true` in both web and worker `appsettings.json`
3. Run the web app: `dotnet run --project DaisiGit.Web`
4. Run the worker: `dotnet run --project DaisiGit.Worker`
5. Create and trigger a workflow — it will be dispatched to the queue and processed by the worker

## Watchdog

The `GitWorkflowBackgroundWorker` acts as a watchdog for stuck executions. If an execution remains in `Dispatched` status for longer than 35 minutes (the container's maximum timeout plus buffer), it is automatically marked as `Failed` with the error "Execution timed out waiting for worker".

This handles cases where the worker container crashes, exceeds its timeout, or fails to write results back to Cosmos DB.

## Runtime Environments

Each workflow has a **Runtime** setting that selects which container image to use. This determines what language runtimes and tools are pre-installed for `RunScript` steps.

| Runtime | Image | Includes | Use case |
|---------|-------|----------|----------|
| **Minimal** | `Dockerfile` | git, curl, wget, jq, unzip | Shell scripts, HTTP calls, simple automation |
| **.NET** | `Dockerfile.dotnet` | .NET 10 SDK + base tools | `dotnet build`, `dotnet publish`, NuGet |
| **Node** | `Dockerfile.node` | Node.js 22 LTS + npm + base tools | `npm install`, `npm run build`, JS/TS projects |
| **Python** | `Dockerfile.python` | Python 3 + pip + venv + base tools | `pip install`, Python scripts |
| **Full** | `Dockerfile.full` | .NET SDK + Node.js + Python + base tools | Multi-language projects, polyglot builds |

All images are stored in ACR with the naming convention `daisigit-worker-{runtime}:latest`. The dispatch message includes the runtime so the Container Apps Job can select the correct image.

### Custom tool installation

You can install additional tools on any base image using a `RunScript` step. For example, on the Minimal image:

```bash
# Install Go
curl -fsSL https://go.dev/dl/go1.23.0.linux-amd64.tar.gz | tar -C /usr/local -xzf -
export PATH=$PATH:/usr/local/go/bin
go build -o myapp .
```

This gives maximum flexibility — pick the closest base image and add what you need.

## `run-minion` action (daisinet-powered AI step)

The `run-minion` step runs a [daisi-minion](https://github.com/daisinet/daisi-minion) autonomously in the workflow's checked-out workspace. The minion can read/write files and run shell commands inside the workspace. Inference is powered by the daisinet ORC — no local GPU required in the container — so this step must run on a runtime that includes the .NET SDK (`dotnet` or `full`).

### Minimal example

```yaml
name: Auto-doc on PR
on:
  pull_request:
    types: [opened]
jobs:
  doc:
    runtime: dotnet
    steps:
      - uses: checkout
        with: { path: "." }
      - uses: run-minion
        with:
          instructions-file: ".minion/doc-task.md"
          model: ""                       # empty ⇒ ORC's default text-gen model
          role: coder
          max-iterations: "20"
          json: "true"
      - uses: add-comment
        with:
          body: "Minion said:\n\n{{steps.1.output}}"
```

### Inputs

| Key | Description |
|-----|-------------|
| `instructions` | Inline prompt. **Mutually exclusive with `instructions-file`.** Merge fields supported. |
| `instructions-file` | Workspace-relative path to a file whose contents become the prompt. Mutually exclusive with `instructions`. |
| `working-directory` | Workspace-relative subdir the minion runs in. Default = workspace root. |
| `model` | Daisinet model name (e.g. `Qwen 3.5`). Empty ⇒ ORC's default text-gen model. |
| `context` | Model context size override. |
| `max-tokens` | Per-turn token cap. |
| `max-iterations` | Goal-loop iteration cap. Default `20`. |
| `role` | Role name (`coder`, `cto`, etc.). |
| `kv-quant` | KV cache quantization (pass-through to minion). |
| `json` | `true` ⇒ minion emits structured JSON events on stderr. |
| `grammar` | `true` ⇒ GBNF-constrained tool calls. |
| `timeout` | Seconds. Default `1500`, hard-capped at `1800`. |
| `orc-address` | ORC override as `host:port`. Default = SDK config. |

### Output

The minion's stdout is captured into `{{steps.N.output}}` (or `{{steps.<name>.output}}` if the step has a `name`), the same merge field as `run`. Exit code is surfaced as `{{steps.N.exitCode}}`.

### One-time platform setup

The action authenticates to ORC as a dedicated app called **DaisiGit Workers**. A platform admin must:

1. Register the app on ORC via `DappsRPC.Create` → capture the returned `AppId` and `SecretKey`.
2. Store the `SecretKey` as a system-scope secret named `DAISIGIT_WORKERS_SECRET_KEY` using `SecretService.SetSystemSecretAsync`. This is **not** a repo/org-scoped secret and is not user-manageable.
3. Set the worker environment variable `DAISIGIT_MINION_FEED_URL` to the NuGet feed URL where the `Daisi.Minion` tool is published (defaults to nuget.org when that package is on the public registry).

> Client keys on ORC are short-lived (60 minutes for apps). The action never stores or forwards a client key — it passes the `SecretKey` to the minion via `DAISI_SECRET_KEY`, and the minion calls `AuthClientFactory.CreateStaticClientKey()` at startup to exchange for a fresh client key per run.

### Runtime requirements

| Runtime | `run-minion` works? |
|---------|---------------------|
| Minimal | No — missing `dotnet` SDK |
| .NET    | Yes |
| Node    | No — aspnet runtime only, no SDK |
| Python  | No — aspnet runtime only, no SDK |
| Full    | Yes |

The step fails fast with a clear error if you pick an unsupported runtime.

### Security notes

- The minion's `ShellExecuteTool` runs inside the workspace directory with the container's permissions. It cannot access other executions' workspaces (isolation guaranteed by Container Apps Jobs).
- The SECRET-KEY is injected only into the minion subprocess's environment; it is not written to disk.
- Output in `{{steps.N.output}}` is truncated to 8000 characters. If you need full output, pipe it to a file in the workspace and upload via a subsequent step.
