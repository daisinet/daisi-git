# Daisi Git

A GitHub-like git hosting service built on the Daisinet platform. Stores git objects in Daisi Drive, uses Cosmos DB for metadata, and provides a Blazor web UI for browsing code and managing repositories.

## Architecture

```
DaisiGit.Core/        — Git object models (blob, tree, commit, tag), pack format, pkt-line protocol
DaisiGit.Data/         — Cosmos DB data access (repositories, object records, refs)
DaisiGit.Services/     — Business logic (object storage, ref management, repository lifecycle)
DaisiGit.Web/          — Blazor Server UI + HTTP smart protocol endpoints
DaisiGit.Tests/        — Unit tests
```

## How It Works

### Git Object Storage

Each repository gets a Daisi Drive repository for blob storage. Git objects are zlib-compressed and uploaded as loose files. A Cosmos DB container maps SHA-1 hashes to Drive file IDs for fast lookups.

### Smart HTTP Protocol

Standard git smart HTTP protocol endpoints enable `git clone`, `push`, and `pull`:

- `GET /{owner}/{repo}.git/info/refs?service=git-upload-pack` — ref advertisement for clone/fetch
- `POST /{owner}/{repo}.git/git-upload-pack` — serves pack files for clone/fetch
- `POST /{owner}/{repo}.git/git-receive-pack` — receives pushed pack files

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

### Use

1. Open the web UI and create a repository
2. Clone it: `git clone https://localhost:PORT/owner/repo.git`
3. Push code: `git push origin main`

## Development Status

### Phase 1 (Current) — Core Git Engine + Push/Pull
- Git object model (blob, tree, commit, tag)
- Pack file generation and parsing
- Pkt-line wire protocol
- HTTP smart protocol endpoints
- Repository creation with initial commit
- Web UI: dashboard, create repository, repository detail

### Phase 2 (Planned) — Web File Browser + Commit History
- Tree traversal and file browsing
- Commit history and diffs
- Syntax-highlighted file viewer

### Phase 3 (Planned) — Pull Requests + Issues
- PR creation, review, and merge
- Issue tracking with comments
- Myers diff algorithm

### Phase 4 (Planned) — Organizations + Permissions
- Org-level access control
- Teams and role-based permissions

### Phase 5 (Planned) — Bot Tools + SDK
- Daisinet bot tools for AI agents
- Client SDK for programmatic access
- REST API
