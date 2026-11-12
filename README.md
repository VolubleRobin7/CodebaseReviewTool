# Codebase Review Tool

A local, single-process tool that reviews Azure DevOps pull requests at a high level.
Instead of diffs it produces a templated report: a text summary, an architecture diagram
and a table of issues, each produced by a user-configured Claude Code prompt or skill run
against a git worktree of the PR's source branch.

## Installation

Download `CodebaseReviewTool.zip` from the latest tagged release on the
[Releases page](https://github.com/VolubleRobin7/CodebaseReviewTool/releases) and unzip it anywhere.
The "Dev build" pre-release at the top of that page is the current state of the development branch; take a
numbered release unless you want to try unreleased changes.

### Prerequisites

- Windows 10 or 11 (the tool lives in the system tray)
- The .NET 10 Desktop Runtime and ASP.NET Core Runtime (the exe says which is missing if either is not installed)
- git
- Claude Code CLI, signed in (its path is set in `appsettings.json` under `Claude:ExecutablePath`)
- An Azure DevOps personal access token with **Code (Read)** scope
- Existing local clones of the repositories to review (the tool fetches but never clones)

### First Run

Before running the tool for the first time, you will need to fill in the `appsettings.json` in the zip
with any of your specific information, such as your remote repositories' PAT, the local paths to your 
repositories, and the root path for the worktrees.

The tool will not start if any of the required configuration values are missing or still set to their 
placeholder values. The configuration page (`/configuration`) will show the effective configuration
and any issues with the setup.

## Using the tool

Run `CodebaseReviewTool.Host.exe`. It starts to the system tray with no window; a left click on the
tray icon opens the UI at http://localhost:5210, and the right-click menu has Open UI, the number of
reviews running, Pause polling and Exit. Turn on "Run at login" under Tray on the Configuration page
to have it start with Windows.

The home page lists the open pull requests of the configured repositories, read from your repository remote
at startup and then on a timer, with Draft and Conflicts tags where the remote reports them. "Start
review" queues a review of a pull request. Each review gets its own git worktree, detached at the
pull request's source commit under the configured worktree root, and then runs the flow steps one
after another, each as a headless Claude Code run inside that worktree with a read-only tool
allowlist. By default the flow has three steps: a plain-text summary, an architecture diagram and a
table of issues with severities and suggested fixes.

When a review finishes, the row shows the issue count and links to the report. The report has the
summary, the diagram, the issues with severity filters, the review's duration and cost, a
collapsible "Run details" per step, the pull request's earlier reviews, and a "Download report"
button that saves the whole thing as a single HTML file. "Review again" reviews the pull request's
latest commit as a new review, keeping the earlier reports; a failed step can be re-run from the
report.

The Configuration page holds the flow: each step has a name, a prompt (or a Claude Code skill,
started with `/skill-name`), a model and a timeout, with "Reset to default" restoring the shipped
prompt and a Preview that renders the prompt against a real pull request. Below the flow are checks
for Claude, git, DevOps and each repository, the list of repositories, a link to the Worktrees page
where review worktrees can be removed, and the effective configuration.

### What it sends, and what it costs

- **Azure DevOps** is only ever read: the tool lists pull requests and their metadata. It never
  comments, votes or changes anything. The personal access token is stored in plain text in
  `appsettings.json`, so keep that folder private.
- **The code under review** is read by Claude Code from the worktree, which means it is sent to
  Anthropic under your own Claude Code account, the same as running `claude` in that folder
  yourself. The tool allowlist limits each run to reading files and running read-only git commands.
- **Each review costs Claude Code usage**, one headless run per step. The report shows what each
  step cost and the review's total; a small pull request comes to roughly a dollar. The home page
  shows how much of your plan's current session limit is used (hover it for the weekly limits),
  read with `claude -p /usage`, which costs no usage, every `Claude:UsageRefreshMinutes` and after
  each review change. It is hidden when the CLI is signed in with an API key rather than a plan.
- **Everything else stays on your machine**: the database in `%LOCALAPPDATA%\CodebaseReviewTool`,
  the worktrees under `Git:WorktreeRoot`, and the UI, which only listens on localhost.

## Updating and uninstalling

To update, exit the running copy from the tray icon, unzip the new build over the old one and keep
your existing `appsettings.json`. Database changes are applied on the next start.

To uninstall, turn off "Run at login" on the Configuration page, exit from the tray icon and delete
the folder. The database in `%LOCALAPPDATA%\CodebaseReviewTool` and any remaining review worktrees
under `Git:WorktreeRoot` are also left for you to delete.

## AI Disclaimer

This tool has been built with a majority of the code written by AI. Architecture and design were
brainstormed with the AI, the code is almost entirely generated by it, and each phase was run and
tested by hand before it was committed.

Eventually, I would like to go through and refactor the code and architecture to be written by humans.

## Development

### Machine-specific configuration (user-secrets)

`appsettings.json` holds only placeholders. For development, everything machine-specific lives
in .NET user-secrets, which the app reads in the Development environment only (Visual Studio
and `dotnet run`; the secrets id is already in the Host csproj). Set the values with Visual
Studio's "Manage User Secrets" or
`dotnet user-secrets set "<key>" "<value>" --project src/CodebaseReviewTool.Host`; 
the result should look like:

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
    { 
      "DisplayName": "My Repo", 
      "LocalPath": "C:\\Repos\\MyRepo" 
    }
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

An installed copy (see "Installing from source" below) reads no user-secrets: it is configured by
editing the `appsettings.json` beside its exe with the same keys.

The Configuration page (`/configuration`) shows the effective configuration (the PAT only as
"set") and has checks for Claude, git, DevOps and each repository.

### Run

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
Configuration) or automatically once the review has finished (completed or failed):
`Git:AutoCleanupAfterDays` days (default 14) after the review finished while its pull request is still
open, or `Git:AutoCleanupAfterPullRequestClosedDays` days (default 1) after the pull request is completed
or abandoned, whichever comes first. A review still queued or running is never cleaned up. Cleanup runs
every six hours, so `0` removes the worktree within six hours. A pull request counts as closed once the
DevOps poller no longer sees it as active.

Once the worktree is ready the review runs the flow steps one after another, each as its own 
headless Claude Code CLI run (`claude -p`) inside the
worktree with the read-only tool allowlist from `Claude:AllowedTools`. The prompts are edited on
the Configuration page under "Flow steps"; each step can also pick a model and a timeout, and
"Reset to default" restores the shipped prompt. Saved changes apply to reviews started afterwards.

### Installing from source

The release zip is built from the `Package` profile below. To install or update a copy on this machine
straight from the source tree instead:

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
