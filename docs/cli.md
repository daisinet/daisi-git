# DaisiGit CLI (`dg`)

The `dg` command-line tool provides full access to DaisiGit from your terminal. Manage repositories, issues, pull requests, and more without leaving the command line.

## Installation

Build from source:

```bash
cd DaisiGit.Cli
dotnet build
```

The executable is at `bin/Debug/net10.0/dg` (or `dg.exe` on Windows).

To install globally:

```bash
dotnet tool install --global --add-source ./nupkg DaisiGit.Cli
```

## Authentication

### Generate an API Key

1. Log into DaisiGit web UI
2. Go to **Settings > Personal Profile**
3. Under **API Keys**, enter a name and click **Generate Token**
4. Copy the `dg_...` token (shown only once)

### Log In

```bash
dg auth login --server https://git.daisi.ai --token dg_YOUR_TOKEN_HERE
```

Or interactively:

```bash
dg auth login
# Server URL: https://git.daisi.ai
# Session token: dg_YOUR_TOKEN_HERE
```

### Check Status

```bash
dg auth status
# Authenticated to https://git.daisi.ai
```

### Log Out

```bash
dg auth logout
```

Credentials are stored in `~/.daisigit/config.json`.

## Repositories

### List Your Repositories

```bash
dg repo list
```

Output:

```
NAME                                     VISIBILITY   STARS  FORKS
myhandle/my-project                      Private      0      0
myorg/shared-lib                         Public       5      2
```

### Create a Repository

```bash
# Personal repo (uses your handle)
dg repo create my-new-project

# With description and visibility
dg repo create my-new-project --desc "A cool project" --public

# Under an organization
dg repo create shared-lib --desc "Shared library" --public
```

### View Repository Details

```bash
dg repo view myhandle/my-project
```

Output:

```
Name:        myhandle/my-project
Description: A cool project
Visibility:  Public
Branch:      main
Stars:       3
Forks:       1
Created:     2026-03-25
```

### Fork a Repository

```bash
dg repo fork someorg/their-repo
# Forked to myhandle/their-repo
```

## Cloning

### Clone a Repository

```bash
dg clone myorg/my-project

# Clone to a specific directory
dg clone myorg/my-project ./my-local-copy
```

This wraps `git clone` with the correct server URL from your config.

## Issues

All issue commands auto-detect the repository from your current directory's git remote. Override with `--repo owner/slug`.

### List Issues

```bash
# Open issues (default)
dg issue list

# Closed issues
dg issue list --status closed

# For a specific repo
dg issue list --repo myorg/my-project
```

Output:

```
#      STATUS   TITLE                                              AUTHOR
#1     Open     Fix login redirect                                 alice
#2     Open     Add dark mode support                              bob
```

### Create an Issue

```bash
dg issue create "Fix the login page"

# With description
dg issue create "Fix the login page" --desc "The login page redirects incorrectly when..."
```

### View an Issue

```bash
dg issue view 1
```

Output:

```
#1 Fix the login page
Status: Open  Author: alice  Created: 2026-03-25

The login page redirects incorrectly when...
```

### Close an Issue

```bash
dg issue close 1
# Closed issue #1
```

## Pull Requests

### List Pull Requests

```bash
# Open PRs (default)
dg pr list

# All statuses
dg pr list --status merged
```

Output:

```
#      STATUS   TITLE                                          SOURCE          TARGET
#1     Open     Add dark mode                                  feature-dark    main
#2     Merged   Fix auth bug                                   fix-auth        main
```

### Create a Pull Request

```bash
dg pr create "Add dark mode" --source feature-dark

# With target branch and description
dg pr create "Add dark mode" --source feature-dark --target dev --desc "Implements dark mode theme"
```

### View a Pull Request

```bash
dg pr view 1
```

Output:

```
#1 Add dark mode
Status: Open  feature-dark -> main
Author: alice  Created: 2026-03-25

Implements dark mode theme
```

### Merge a Pull Request

```bash
# Default merge commit
dg pr merge 1

# Squash merge
dg pr merge 1 --strategy squash
```

Output:

```
Merged PR #1 (commit: abc1234)
```

## Other Commands

### Open in Browser

Opens the current repo in your default browser:

```bash
dg browse
```

### Version

```bash
dg version
# dg 0.1.0
```

### Help

```bash
dg help
dg --help
```

## Flags Reference

| Flag | Short | Description |
|------|-------|-------------|
| `--repo` | `-r` | Specify repo as `owner/slug` (auto-detected from git remote) |
| `--server` | `-s` | Server URL (for `auth login`) |
| `--token` | `-t` | API token (for `auth login`) |
| `--desc` | `-d` | Description (for `repo create`, `issue create`, `pr create`) |
| `--public` | | Set visibility to Public |
| `--private` | | Set visibility to Private (default) |
| `--internal` | | Set visibility to Internal |
| `--source` | `-s` | Source branch (for `pr create`) |
| `--target` | `-t` | Target branch (for `pr create`, default: `main`) |
| `--status` | | Filter by status: `open`, `closed`, `merged` |
| `--strategy` | | Merge strategy: `merge` (default), `squash` |

## Auto-Detection

When you run `dg` inside a cloned DaisiGit repository, it automatically detects the repo from your git remote:

```bash
cd my-project
dg issue list          # auto-detects owner/repo from origin remote
dg pr create "Fix" --source my-branch   # same
```

This works by parsing the `origin` remote URL. You can always override with `--repo`:

```bash
dg issue list --repo otherorg/other-repo
```

## Configuration File

Stored at `~/.daisigit/config.json`:

```json
{
  "ServerUrl": "https://git.daisi.ai",
  "SessionToken": "dg_YOUR_TOKEN_HERE",
  "UserName": null
}
```

## API Key Security

- Tokens are prefixed with `dg_` for easy identification
- Tokens are hashed with SHA-256 on the server — the raw token is never stored
- Tokens can be revoked from Settings > Personal Profile > API Keys
- Each token shows its creation date and last-used date
- Tokens work with both `X-Api-Key` header and `Authorization: Bearer` header
