# Codebase Review Tool

A local, single-process tool that reviews Azure DevOps pull requests at a high level.
Instead of diffs it produces a templated report: a text summary, an architecture diagram
and a table of issues, each produced by a user-configured Claude Code prompt or skill run
against a git worktree of the PR's source branch.

## Prerequisites

- .NET 10 SDK
- git
- Claude Code CLI, signed in (its path is set in `appsettings.json` under `Claude:ExecutablePath`)
- An Azure DevOps personal access token with **Code (Read)** scope
- Existing local clones of the repositories to review (the tool fetches but never clones)

## Machine-specific configuration (user-secrets)

`appsettings.json` holds only placeholders. Everything machine-specific lives in .NET
user-secrets, which the app reads in every environment (the secrets id is already in the
Host csproj). Set the values with Visual Studio's "Manage User Secrets" or
`dotnet user-secrets set "<key>" "<value>" --project src/CodebaseReviewTool.Host`
(e.g. key `Repositories:0:LocalPath`); the result should look like:

```json
{
  "DevOps": {
    "Pat": "<token>",
    "OrganizationUrl": "https://dev.azure.com/<organisation>"
  },
  "Git": {
    "WorktreeRoot": "C:\\Repos\\_review-worktrees"
  },
  "Repositories": [
    { "DisplayName": "My Repo", "LocalPath": "C:\\Repos\\MyRepo" }
  ]
}
```

For Azure DevOps Server use the collection URL (e.g. `http://server:8080/tfs/Collection`)
as `OrganizationUrl`. Each repository's remote (`origin`, or the only remote) must be an
Azure DevOps URL in that organisation or collection; organisation, project and repository are read from it. Startup fails
with a message naming the key if a value is missing, still a placeholder, or a path does
not exist.

`DevOps:ApiVersion` (optional) pins the REST api-version, e.g. `3.0` for TFS 2017; leave it
unset to have the tool detect the newest version the server supports (TFS 2017 up to 7.1).
Set `DevOps:UseDefaultCredentials` to `true` to authenticate to an on-prem server with your
Windows account instead of a PAT (the PAT may then be left empty).

The Settings page (http://localhost:5210/settings) shows the effective configuration
(the PAT only as "set") and has checks for Claude, git, DevOps and each repository.

## Run

```
dotnet run --project src/CodebaseReviewTool.Host
```

- UI: http://localhost:5210
- Health check: http://localhost:5210/api/health (returns `OK`)

## Design

See [docs/implementation-plan.md](docs/implementation-plan.md) for the architecture,
configuration shape, data model and phased build plan.
