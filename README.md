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
- Existing local clones of the repositories to review (the tool fetches but never clones);
  configure them under `Repositories` in `src/CodebaseReviewTool.Host/appsettings.json`

## Set the DevOps PAT

The PAT is kept in .NET user-secrets, never in appsettings:

```
dotnet user-secrets set "DevOps:Pat" "<token>" --project src/CodebaseReviewTool.Host
```

## Run

```
dotnet run --project src/CodebaseReviewTool.Host
```

- UI: http://localhost:5210
- Health check: http://localhost:5210/api/health (returns `OK`)

## Design

See [docs/implementation-plan.md](docs/implementation-plan.md) for the architecture,
configuration shape, data model and phased build plan.
