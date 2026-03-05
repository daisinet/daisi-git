# Daisi Git

A GitHub-like git hosting service built on the Daisinet platform. Stores git objects in Daisi Drive, uses Cosmos DB for metadata, and provides a Blazor web UI for browsing code and managing repositories.

## Architecture

```
DaisiGit.Core/        — Git object models (blob, tree, commit, tag), pack format, pkt-line protocol
DaisiGit.Data/         — Cosmos DB data access (repositories, object records, refs)
DaisiGit.Services/     — Business logic (object storage, ref management, repository lifecycle)
DaisiGit.Web/          — Blazor Server UI + HTTP smart protocol + REST API endpoints
DaisiGit.SDK/          — Client SDK for programmatic access to the REST API
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

### REST API

All endpoints are prefixed with `/api/git/` and require Daisi SSO authentication:

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

### Phase 1 — Core Git Engine + Push/Pull
- Git object model (blob, tree, commit, tag)
- Pack file generation and parsing
- Pkt-line wire protocol
- HTTP smart protocol endpoints
- Repository creation with initial commit
- Web UI: dashboard, create repository, repository detail

### Phase 2 — Web File Browser + Commit History
- `BrowseService` — tree traversal, path resolution, commit log walking, tree diffing
- `RepositoryLayout` — shared layout with Code/Commits/Branches tab navigation
- `FileBrowser` — directory listing at any branch/path with breadcrumb navigation
- `FileViewer` — file content display with line numbers
- `CommitHistory` — paginated commit log grouped by date, with branch selector
- `CommitDetail` — single commit view with full file diffs (added/modified/deleted)
- `BranchList` — all branches with last commit info

### Phase 3 — Pull Requests + Issues
- `PullRequest`, `Issue`, `Comment`, `Label` models with Cosmos DB storage
- `PullRequestService` — create, list, close, reopen, update PRs
- `IssueService` — create, list, close, reopen, update issues
- `CommentService` — add/edit/delete comments on PRs and issues
- `MergeService` — merge PRs with merge commit or squash strategy
- `PullRequestList` — open/closed filter, PR creation link
- `CreatePullRequest` — branch selector, title/description form
- `PullRequestDetail` — merge/close/reopen actions, file diffs, comments
- `IssueList` — open/closed filter, issue creation link
- `CreateIssue` — title/description form
- `IssueDetail` — close/reopen actions, comments

### Phase 4 — Organizations + Permissions
- `GitOrganization`, `OrgMember`, `Team`, `TeamMember`, `RepoPermission` models
- `GitRole` enum (Read, Write, Maintain, Admin, Owner) and `GitPermissionLevel` enum
- `OrganizationService` — create orgs, manage members, role assignment
- `TeamService` — create teams, manage team members
- `PermissionService` — evaluates effective permissions (owner > org role > direct grant > team grant > public)
- Permission enforcement on git HTTP endpoints (read for clone/fetch, write for push)
- `OrgList` — organization listing with member/team counts
- `CreateOrg` — org creation form
- `DaisiUserService` — search and import users from Daisinet via `AccountClient.GetUsers` RPC
- `OrgDetail` — tabbed view with repos, members, teams; import members from Daisinet with search
- `OrgSettings` — edit org name/description
- `CreateTeam` — team creation with default permission level
- `TeamDetail` — team member management with Daisinet user import

### Phase 5 — Bot Tools (Marketplace Plug-in) + SDK + REST API
- **REST API** (`/api/git/`) — full CRUD for repos, branches, files, commits, issues, PRs, comments
- **Marketplace Plug-in** — DaisiGit secure tool provider in `daisi-tools-dotnet/Daisi.SecureTools/DaisiGit/`
  - `DaisiGitFunctions` — Azure Functions endpoint class extending `SecureToolFunctionBase`
  - 9 bot tools: ListRepos, BrowseFiles, ReadFile, ListCommits, ListIssues, CreateIssue, ListPulls, CreatePull, AddComment
  - Catalog entry in `marketplace/catalog.json` with provider, tools, and plugin definitions
- **DaisiGit.SDK** — typed client library (`DaisiGitClient`) for programmatic access to all REST API endpoints
- **Unit tests** — 31 tests covering git objects, pack files, pkt-line protocol, browse results, and domain models
