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

`appsettings.json` holds only placeholders. For development, everything machine-specific lives
in .NET user-secrets, which the app reads in the Development environment only (Visual Studio
and `dotnet run`; the secrets id is already in the Host csproj). Set the values with Visual
Studio's "Manage User Secrets" or
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

An installed copy (see Install below) reads no user-secrets: it is configured by editing the
`appsettings.json` beside its exe with the same keys.

The Configuration page (`/configuration`) shows the effective configuration (the PAT only as
"set") and has checks for Claude, git, DevOps and each repository.

## Run

```
dotnet run --project src/CodebaseReviewTool.Host
```

- UI: http://localhost:5211
- Health check: http://localhost:5211/api/health (returns `OK`)

The development environment listens on port 5211 and, through `appsettings.Development.json`,
uses its own database, so it runs beside an installed copy (port 5210). Give it its own
`Git:WorktreeRoot` in user-secrets too, since review ids repeat across the two databases.

The home page lists the open pull requests of the configured repositories, read from DevOps
at startup and then every `DevOps:PollIntervalSeconds`. "Start review" queues a review of a
pull request.

Each review gets its own git worktree, detached at the pull request's source commit, under
`Git:WorktreeRoot` (`<root>\<repository key>\pr-<id>-r<review id>`); "Start review" fetches
the clone and creates it. Worktrees are removed from the Worktrees page (linked from
Configuration) or automatically `Git:AutoCleanupAfterDays` days after a review finishes.

Once the worktree is ready the review runs the three flow steps (summary, architecture diagram,
issues) one after another, each as its own headless Claude Code CLI run (`claude -p`) inside the
worktree with the read-only tool allowlist from `Claude:AllowedTools`. The prompts are edited on
the Configuration page under "Flow steps"; each step can also pick a model and a timeout, and
"Reset to default" restores the shipped prompt. Saved changes apply to reviews started afterwards.

## Install

The tool is meant to live in the tray rather than in an IDE. To install or update it:

```
dotnet publish src/CodebaseReviewTool.Host -p:PublishProfile=Install
```

This publishes a Release, framework-dependent build (the .NET 10 runtime must be installed) to
`%LOCALAPPDATA%\Programs\CodebaseReviewTool`. Publishing the `Install` profile from Visual
Studio's Publish page does the same. Fill in the `appsettings.json` there (placeholders are
rejected at startup with a message naming each key), then run `CodebaseReviewTool.Host.exe`: it
starts to the tray with no console on port 5210, and a left click on the icon opens the UI. Turn on "Run at login" under Tray on the Configuration page
to have it start with Windows. Exit the running copy from the tray icon before publishing again;
the publish stops with a message if it is still running, and it leaves an `appsettings.json`
already in the folder alone. Pass `--console` to run it with a console window instead of the tray.

### Sharing it

```
dotnet publish src/CodebaseReviewTool.Host -p:PublishProfile=Package
```

This produces `src/CodebaseReviewTool.Host/bin/Release/CodebaseReviewTool.zip`. The recipient
unzips it anywhere, fills in `appsettings.json` (the placeholders are rejected at startup with a
message naming each key; the DevOps PAT goes in plain text there, so keep the folder private) and
runs `CodebaseReviewTool.Host.exe`. They need the .NET 10 Desktop and ASP.NET Core runtimes
(the exe says which are missing), git, a signed-in Claude Code CLI and local clones of the
repositories. When unzipping an update, keep the existing `appsettings.json`.

## Design

See [docs/implementation-plan.md](docs/implementation-plan.md) for the architecture,
configuration shape, data model and phased build plan.
