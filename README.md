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

### Phase 6 — Code Review
- `Review`, `DiffComment` models with `ReviewState` (Commented/Approved/ChangesRequested/Dismissed) and `DiffSide` enums
- `ReviewService` — submit reviews with inline diff comments, list reviews, dismiss reviews, get review summaries
- `Reviews` Cosmos DB container storing both Review and DiffComment documents, partitioned by RepositoryId
- PR detail page shows review summary badges (approvals, changes requested), review history, and submit review form
- Inline diff comments rendered within the file diff view
- REST API: `GET/POST /pulls/{n}/reviews`, `GET /pulls/{n}/diff-comments`
- SDK: `ListReviewsAsync`, `SubmitReviewAsync`, `ListDiffCommentsAsync`
- Bot tools: `ListReviews` (information) and `SubmitReview` (action) in marketplace catalog
- 8 new unit tests for review models, enums, and field defaults

### Phase 7 — Forks & Stars
- `RepoStar` model for starring repositories, stored in Cosmos DB `Stars` container partitioned by `RepositoryId`
- `ForkedFromId`, `ForkedFromOwnerName`, `ForkedFromSlug`, `StarCount`, `ForkCount` fields on `GitRepository`
- Fork implementation: copies `GitObjectRecord` entries (sharing Drive files, no duplication), copies refs and HEAD, creates new Drive repo for future pushes
- Duplicate fork prevention: returns existing fork if user already forked the repo
- Star/unstar with idempotency and denormalized `StarCount` on the repository document
- Explore page (`/explore`) showing public repositories sorted by star count with pagination
- Dashboard "Starred" tab alongside "Your Repositories"
- Star and Fork buttons in repository header with live counts
- "Forked from owner/slug" subtitle on forked repositories
- REST API: `POST/GET /repos/{owner}/{slug}/forks`, `PUT/DELETE /repos/{owner}/{slug}/star`, `GET /explore`
- SDK: `ForkRepositoryAsync`, `ListForksAsync`, `StarRepositoryAsync`, `UnstarRepositoryAsync`, `ExploreRepositoriesAsync`
- Bot tools: `ForkRepository` (action) and `StarRepository` (action with star/unstar) in marketplace catalog
- 9 new unit tests for fork fields, star model, and counter behavior

### Phase 8 — Settings Hub
- Settings page (`/settings`) with card grid layout linking to configuration sections
- Organizations moved from top-level sidebar nav into the Settings hub
- Settings nav item in sidebar (`fa-duotone fa-gear`) replaces direct Organizations link
- Custom `.dg-settings-card` styling with hover elevation and border effects
