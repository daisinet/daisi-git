# Daisi Git

A GitHub-like git hosting service built on the Daisinet platform. Stores git objects in Daisi Drive, uses Cosmos DB for metadata, and provides a Blazor web UI for browsing code and managing repositories.

## Architecture

```
DaisiGit.Core/              — Git object models, pack format, pkt-line protocol, reserved names
DaisiGit.Data/              — Cosmos DB data access (repos, objects, refs, workflows, API keys)
DaisiGit.Services/          — Business logic (storage, refs, repos, workflows, API keys)
DaisiGit.Web/               — Blazor Server UI + HTTP smart protocol + REST API endpoints
DaisiGit.Worker/            — Isolated workflow execution worker (Container Apps Job)
DaisiGit.SDK/               — Client SDK for programmatic access to the REST API
DaisiGit.Cli/               — Command-line tool (dg) for repo/issue/PR management
DaisiGit.Tests/             — Unit tests (82 tests)
DaisiGit.IntegrationTests/  — Integration tests against live server (24 tests)
```

See also: [Workflow Isolation Architecture](docs/workflow-isolation.md)

## CLI Tool

The `dg` command-line tool provides full access to DaisiGit from your terminal.

### Install

```bash
# Linux / macOS
curl -fsSL https://git.daisi.ai/cli/install.sh | sh

# Windows (PowerShell)
irm https://git.daisi.ai/cli/install.ps1 | iex
```

### Usage

```bash
# Authenticate (also configures git credential helper)
dg auth login --server https://git.daisi.ai --token dg_YOUR_TOKEN

# Repository operations
dg repo list
dg clone myorg/my-project
dg push
dg pull

# After login, native git commands also work
git push origin main
git pull

# Issues and pull requests
dg issue create "Fix bug" --desc "Steps to reproduce..."
dg pr create "Add feature" --source feature-branch
dg pr merge 42
```

See the [full CLI documentation](docs/cli.md) for all commands and examples.

## How It Works

### Git Object Storage

Each repository gets a Daisi Drive repository for blob storage. Git objects are zlib-compressed and uploaded as loose files. A Cosmos DB container maps SHA-1 hashes to Drive file IDs for fast lookups.

### Smart HTTP Protocol

Standard git smart HTTP protocol endpoints enable `git clone`, `push`, and `pull`:

- `GET /{owner}/{repo}.git/info/refs?service=git-upload-pack` — ref advertisement for clone/fetch
- `POST /{owner}/{repo}.git/git-upload-pack` — serves pack files for clone/fetch
- `POST /{owner}/{repo}.git/git-receive-pack` — receives pushed pack files

### REST API

All endpoints are prefixed with `/api/git/`. Authenticated endpoints accept Daisi SSO cookies or API keys (`X-Api-Key` header). Read-only endpoints allow anonymous access for public repos.

| Endpoint | Description |
|---|---|
| `GET /repos` | List repositories |
| `GET /repos/{owner}/{slug}` | Get repository |
| `POST /repos` | Create repository |
| `GET /repos/{owner}/{slug}/branches` | List branches |
| `GET /repos/{owner}/{slug}/tree/{branch}/{path}` | Browse directory |
| `GET /repos/{owner}/{slug}/blob/{branch}/{path}` | Read file |
| `GET /repos/{owner}/{slug}/commits/{branch}` | List commits |
| `GET /repos/{owner}/{slug}/commit/{sha}` | Get commit with diffs |
| `GET /repos/{owner}/{slug}/issues` | List issues |
| `POST /repos/{owner}/{slug}/issues` | Create issue |
| `PATCH /repos/{owner}/{slug}/issues/{n}` | Update/close/reopen issue |
| `GET /repos/{owner}/{slug}/pulls` | List pull requests |
| `POST /repos/{owner}/{slug}/pulls` | Create pull request |
| `POST /repos/{owner}/{slug}/pulls/{n}/merge` | Merge pull request |
| `GET /repos/{owner}/{slug}/pulls/{n}/reviews` | List reviews |
| `POST /repos/{owner}/{slug}/pulls/{n}/reviews` | Submit review |
| `GET /repos/{owner}/{slug}/pulls/{n}/diff-comments` | List inline diff comments |
| `POST /repos/{owner}/{slug}/forks` | Fork a repository |
| `GET /repos/{owner}/{slug}/forks` | List forks |
| `PUT /repos/{owner}/{slug}/star` | Star a repository |
| `DELETE /repos/{owner}/{slug}/star` | Unstar a repository |
| `DELETE /repos/{owner}/{slug}` | Delete repository |
| `GET /explore` | Explore public repos by stars |
| `POST /orgs` | Create organization |
| `GET /orgs` | List organizations |
| `DELETE /orgs/{slug}` | Delete organization (cascades repos) |
| `GET /repos/{owner}/{slug}/workflows` | List workflows |
| `POST /repos/{owner}/{slug}/workflows` | Create workflow |
| `GET /repos/{owner}/{slug}/events` | List events |
| `POST /auth/keys` | Generate API key |
| `GET /auth/keys` | List API keys |
| `DELETE /auth/keys/{id}` | Revoke API key |
| `GET /auth/whoami` | Current user info |

### Ref Storage

Refs (branches, tags, HEAD) are stored in Cosmos DB for fast lookups and atomic updates. Each ref is a simple document mapping a ref name to a SHA.

## Getting Started

### Prerequisites

- .NET 10 SDK
- Azure Cosmos DB connection string
- Daisi ORC running (for Drive operations)

### Configuration

Set user secrets:

```bash
cd DaisiGit.Web
dotnet user-secrets set "Cosmo:ConnectionString" "<your-cosmos-connection-string>"
```

### Run

```bash
dotnet run --project DaisiGit.Web
```

To run the isolated workflow worker (optional for local dev):

```bash
dotnet run --project DaisiGit.Worker
```

### Use

1. Open the web UI and create a repository
2. Clone it: `git clone https://localhost:PORT/owner/repo.git`
3. Push code: `git push origin main`

