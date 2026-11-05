# Codebase Review Tool – Implementation Plan

A local, single-process tool that reviews Azure DevOps pull requests at a high level.
Instead of diffs it produces a templated report: a text summary, an architecture diagram
(HTML excerpt) and a table of issues, each produced by a user-configured Claude Code prompt
or skill run against a git worktree of the PR's source branch.

Status: Phase 4 complete (agent runner, flow step editor, orchestration). Phases 5+ not started.

---

## 1. Decisions already made

These came from the discussion on 2026-11-03 and are not open for re-litigation in
implementation sessions unless Mitch changes them.

| Topic | Decision |
|---|---|
| Hosting | One ASP.NET Core process (Blazor Server UI + minimal API + background runner) running in the user's session with a system-tray icon. Not a Windows Service (session 0 has no tray and no Claude login). |
| UI | Blazor Web App, interactive **Server** render mode only. Hand-written CSS following the Claude Design mockup. No component library. |
| Runtime | .NET 10 (SDK 10.0.403 is installed). `net10.0-windows` for the host project because of the tray icon. |
| Claude | Local Claude Code CLI (2.1.305 at `%USERPROFILE%\.local\bin\claude.exe`, **not** on the PowerShell PATH). Path comes from appsettings. Headless `claude -p` runs. |
| Steps | Exactly three, fixed order: Summary → Diagram → Issues. Each is an **independent** `claude -p` run that sees only PR metadata and the worktree, never earlier step output. |
| Step source | A step is prompt text, stored as a single string. If that text starts with `/skill-name …`, Claude runs the skill, exactly as it does interactively (user-level skills/plugins plus the reviewed repo's `.claude` folder). The tool does not distinguish the two; it only renders placeholders and passes the text through. The tool ships one default prompt per step (embedded resources in Core, §5) used to seed the flow and restored by "Reset to default"; the issues JSON schema instruction is appended separately. |
| Permissions | Read-only tool allowlist passed via `--allowedTools` (Read, Glob, Grep, read-only git). Configurable in appsettings, not per step. |
| Flow | One global flow (three step definitions). Stored in the database, edited in the UI. |
| Triggering | Manual: the UI lists open PRs from DevOps and the user clicks Review. A background poll keeps the list fresh and never starts a review. |
| DevOps | Used only for PR metadata (list open PRs, PR details). Code is never read from DevOps. Nothing is written back to DevOps. |
| Worktrees | One new worktree per review, created from the local clone at the PR's source commit. Kept until removed from the UI; an auto-cleanup age in appsettings. |
| Diagram output | HTML excerpt, shown in a sandboxed iframe (`sandbox="allow-scripts"`, no same-origin) so the excerpt may load its own scripts (e.g. a Mermaid CDN) but cannot touch the host page. |
| Issues output | Fixed JSON schema owned by the tool (see §6). The tool appends the schema instruction to the issues prompt and enforces it with `--json-schema`. Parsed into a table. |
| Summary output | Plain text, used directly. |
| Config | appsettings.json holds the shape and placeholder or generic defaults and is committed. **.NET user-secrets holds everything machine-specific**: the DevOps PAT, organisation URL, worktree root and repositories. Secrets are never in appsettings. |
| Storage | SQLite file via EF Core with migrations. |
| API | Minimal API, one endpoint for now: `GET /api/health` → `200 "OK"`. Structured so more endpoints can be added later. |
| Repo | Personal tool; stays in this GitHub repo. No commits are made by Claude sessions unless asked. |

---

## 2. Architecture

```
+------------------------------- CodebaseReviewTool.Host (one exe) -------------------------------+
|                                                                                                 |
|  Tray (WinForms NotifyIcon, STA thread)          Kestrel  http://localhost:<port>                |
|   - Open UI  - Status  - Exit                     +-- Blazor Server UI (Razor components)        |
|                                                   +-- Minimal API  /api/health                   |
|                                                                                                 |
|  Hosted services                                                                                |
|   - DevOpsPollService      refreshes open-PR cache on an interval                               |
|   - ReviewQueueService     dequeues Review ids, runs ReviewOrchestrator one at a time           |
|   - WorktreeCleanupService removes worktrees older than the configured age                      |
|                                                                                                 |
|  Core services (CodebaseReviewTool.Core)                                                        |
|   DevOpsClient -> PullRequestCache -> ReviewOrchestrator -> WorktreeService -> IAgentRunner      |
|                                            |                                                    |
|                                            +-- PromptRenderer, IssuesParser, ReportBuilder      |
|                                                                                                 |
|  EF Core + SQLite (ReviewDbContext)                                                             |
+-------------------------------------------------------------------------------------------------+
          | HTTPS + PAT                     | git CLI                          | claude.exe -p
     Azure DevOps REST            local clone + worktree root            Claude Code CLI
```

Review lifecycle:

```
Queued -> PreparingWorktree -> RunningSummary -> RunningDiagram -> RunningIssues -> Completed
                                     +-------------- any step fails ---------------> Failed
```

A failed step stops the review (later steps are not run). A step can be re-run on its
own from the report page without recreating the worktree; the review then continues from
that step.

---

## 3. Solution layout

Two projects. Core is the "service" half (everything that does work); Host is the
"window" half (everything the user sees or clicks, plus process hosting).

```
CodebaseReviewTool.sln
Directory.Build.props                 nullable, implicit usings, warnings-as-errors, LangVersion
.editorconfig
src/
  CodebaseReviewTool.Core/            class library, net10.0
  CodebaseReviewTool.Host/            Blazor Web App (server), net10.0-windows, WinExe from Phase 6
docs/
  implementation-plan.md              this file
  mockup/                             exported Claude Design mockup (added by Mitch)
```

### 3.1 CodebaseReviewTool.Core

Owns every piece of behaviour and all persistence. It has no idea whether it is being
driven by a Blazor page, an API call, a tray menu or a background timer. Nothing in it
references Blazor, ASP.NET Core or WinForms, so the target is plain `net10.0`.

| Folder | Contents | Responsibility |
|---|---|---|
| `Configuration/` | `ClaudeOptions`, `GitOptions`, `DevOpsOptions`, `RepositoryOptions`, `DatabaseOptions`, `ReviewOptions`, `TrayOptions`; `PathExpansion` helper | Typed view of the appsettings sections in §4, with data-annotation validation and `%ENV%` expansion. Host binds them; everything else injects `IOptions<T>`. |
| `Data/` | `ReviewDbContext`, entity classes (§5), EF migrations, `ReviewDbContextFactory` | SQLite storage. Blazor Server circuits and background services both need their own short-lived contexts, so consumers use `IDbContextFactory<ReviewDbContext>` rather than a scoped context. |
| `Processes/` | `IProcessRunner`, `ProcessRunner`, `ProcessResult` | The only place an external process is started. Handles working directory, stdin writing, stdout/stderr capture, timeout with process-tree kill, and environment sanitisation (dropping `CLAUDECODE`). Git and Claude both go through it. |
| `Git/` | `GitRunner`, `RepositoryResolver`, `WorktreeService`, `WorktreeInfo` | `GitRunner` runs `git <args>` in a directory via `IProcessRunner`. `RepositoryResolver` turns each configured `LocalPath` into a resolved repository (remote name, organisation, project, repository, key) by reading and parsing its remote URL at startup. `WorktreeService` does the review-specific work: fetch, add a detached worktree at a commit, merge-base, changed-file list, remove + prune, path naming under `WorktreeRoot`, and the Windows removal failure handling. |
| `Prompts/` | `DefaultPrompts`, `PromptRenderer`, `IssuesSchema`, `Defaults/*.txt` | Agent-neutral prompt handling. `DefaultPrompts` reads the shipped prompt for each step from the embedded `Prompts/Defaults/<step>-*.txt` resources. `PromptRenderer` substitutes the §6.1 placeholders. `IssuesSchema` holds the fixed schema text and the instruction appended to the last step. Nothing here names a particular AI tool, and the shipped prompts avoid wording specific to one. |
| `Agents/` | `IAgentRunner`, `AgentResult`, `ClaudeCodeRunner` | The boundary to whichever AI tool runs a step. `IAgentRunner` takes a rendered prompt, a working directory, an optional model, timeout and optional JSON schema and returns an `AgentResult` (text, structured output, session id, cost, raw output, error). `ClaudeCodeRunner` is the only implementation for now: it builds the command line in §6.2, feeds the prompt through stdin and parses the CLI's result JSON. Another tool means another implementation, selected in `AddReviewCore`. |
| `DevOps/` | `IDevOpsClient`, `DevOpsClient`, REST DTOs, `PullRequestCache` | REST calls to Azure DevOps with PAT basic auth (list active PRs for a repo, get one PR, "who am I" for the Settings check). `PullRequestCache` merges polled results into the `PullRequest` table and flags PRs that have closed. |
| `Reviews/` | `ReviewQueue`, `ReviewOrchestrator`, `IssuesParser`, `ReviewEvents` | `ReviewQueue` is a `Channel<int>` of review ids. `ReviewOrchestrator` runs one review: status machine in §2, worktree creation, the flow steps in ascending order, one `ReviewStepRun` row per step, issues parsing for the last step, stop on first failure, re-run from a given step reusing the worktree. `ReviewEvents` is an in-process pub/sub (review or step changed) that the UI and the tray subscribe to. |
| `Reporting/` | `ReportBuilder` | Renders a completed review to a standalone HTML string (summary, diagram inlined as an iframe `srcdoc`, issues table) for the Download button. |
| `Hosting/` | `DevOpsPollService`, `ReviewQueueService`, `WorktreeCleanupService` | The three `BackgroundService` classes. They live here, not in Host, because they are behaviour; Host only registers them. |
| root | `ServiceCollectionExtensions.AddReviewCore(IConfiguration)` | One call that registers options, the DbContext factory, HttpClient for DevOps, every service above and the three hosted services. |

Package references: `Microsoft.EntityFrameworkCore.Sqlite`,
`Microsoft.Extensions.Hosting.Abstractions`, `Microsoft.Extensions.Http`,
`Microsoft.Extensions.Options.DataAnnotations`.

### 3.2 CodebaseReviewTool.Host

Owns the process and the presentation. It is the only project that targets Windows
and the only one with an entry point.

| Folder | Contents | Responsibility |
|---|---|---|
| `Program.cs` | | Builds the `WebApplication`, calls `AddReviewCore`, maps Razor components and the API, applies pending migrations, then starts Kestrel on a background thread and runs the tray message loop on the main STA thread. With `Tray:Enabled=false` (or `--console`) it blocks on the web host instead, which is how it runs under `dotnet run` and Claude Code's preview. |
| `Api/` | `HealthEndpoints.cs` (`MapHealthApi`) | Minimal API endpoint groups, one file per group. Only `/api/health` exists now; the growth path for the API is adding files here that call Core services. |
| `Components/Layout/` | `MainLayout`, `NavMenu`, `ReconnectModal` (template default, kept as is) | The mockup's header: brand, "Pull requests" nav link, theme toggle and the gear that opens Configuration. |
| `Components/Pages/` | `Home` (`/`, the pull request list), `ReviewReport` (`/reviews/{id}`), `Configuration` (`/configuration`: flow steps, checks, repositories, environment), `Worktrees` (`/worktrees`, linked from Configuration) | One page per screen (§10). Pages inject Core services directly; there is no HTTP hop between UI and service because they share a process. Pages subscribe to `ReviewEvents` for live status and call `InvokeAsync(StateHasChanged)`. |
| `Components/Shared/` | `IssuesTable`, `DiagramFrame`, `SeverityBadge`, `PlaceholderReference`, `RunDetails`, `StepEditor` | Reusable pieces of the report and configuration pages. `DiagramFrame` owns the sandboxed iframe and the height `postMessage` handshake. |
| `Tray/` | `TrayApplicationContext`, `StartupShortcut`, `SingleInstance` | NotifyIcon with context menu, open-browser action, balloon notifications from `ReviewEvents`, the Startup-folder shortcut toggle, and the named mutex that makes a second launch just open the browser. |
| `wwwroot/` | `app.css`, `theme.js`, `diagram-frame.js` | Design tokens and components from the mockup (§10); the theme script that applies the saved light/dark choice before paint; the few lines of JS for iframe height. System fonts only, so nothing loads from the internet and no font files ship. |
| `appsettings.json`, `appsettings.Development.json` | | Committed configuration with placeholder values only. Machine-specific values (PAT, organisation URL, worktree root, repositories) live in user-secrets; the id is set in the csproj. |
| `Properties/launchSettings.json` | | Single `http` profile on port 5210, Development environment. No https profile: the app is loopback-only. |

Package references: the Core project, WinForms via `<UseWindowsForms>true</UseWindowsForms>`.
The `dotnet ef` tooling targets Core on its own (Core holds `Microsoft.EntityFrameworkCore.Design`
and a design-time context factory), so adding a migration never starts the Host:

```
dotnet ef migrations add <Name> --project src/CodebaseReviewTool.Core --startup-project src/CodebaseReviewTool.Core --output-dir Data/Migrations
```

---

## 4. Configuration (appsettings.json shape)

```jsonc
{
  "Kestrel": { "Endpoints": { "Http": { "Url": "http://localhost:5210" } } },

  "Database": {
    "Path": "%LOCALAPPDATA%\\CodebaseReviewTool\\reviews.db"     // env vars expanded at startup
  },

  "Claude": {
    "ExecutablePath": "%USERPROFILE%\\.local\\bin\\claude.exe",
    "DefaultModel": "opus",                                       // optional: the code default is opus
    "Models": ["opus", "sonnet", "haiku"],                        // choices offered per step on the Configuration page
    "TimeoutMinutes": 20,
    "MaxTurns": 50,
    "AllowedTools": [
      "Read", "Glob", "Grep",
      "Bash(git diff:*)", "Bash(git log:*)", "Bash(git show:*)",
      "Bash(git status:*)", "Bash(git blame:*)", "Bash(git ls-files:*)"
    ]
  },

  "Git": {
    "ExecutablePath": "git",                                      // resolved via PATH if bare
    "WorktreeRoot": "C:\\Repos\\_review-worktrees",
    "AutoCleanupAfterDays": 14,
    "FetchBeforeReview": true,
    "CommandTimeoutMinutes": 10                                   // fetch of a large clone can be slow
  },

  "DevOps": {
    "OrganizationUrl": "https://dev.azure.com/<organisation>",
    "PollIntervalSeconds": 120
    // "Pat" lives in user-secrets:  dotnet user-secrets set "DevOps:Pat" "<token>"
  },

  "Repositories": [
    {
      "DisplayName": "<name shown in the UI>",
      "LocalPath": "<path to an existing local clone>",           // tool never clones
      // Optional overrides, only for clones whose remote URL is not a recognisable
      // Azure DevOps URL:
      // "Remote": "origin",  "Project": "<DevOps project>",  "Repository": "<DevOps repo>"
    }
  ],

  "Reviews": {
    "MaxConcurrent": 1
  },

  "Tray": {
    "Enabled": true,                                              // false for `dotnet run` during development
    "OpenBrowserOnStart": false
  }
}
```

All sections bind to options classes with `ValidateDataAnnotations().ValidateOnStart()`,
so a missing repo path or Claude exe fails fast at startup with a clear message rather
than at review time.

A repository entry needs only `DisplayName` and `LocalPath`. At startup
`RepositoryResolver` reads the clone's remotes: the remote is `origin` if present,
otherwise the only remote; its URL is parsed for organisation, project and repository
(`https://dev.azure.com/<org>/<project>/_git/<repo>`,
`git@ssh.dev.azure.com:v3/<org>/<project>/<repo>`, and the legacy
`https://<org>.visualstudio.com/<project>/_git/<repo>`). The organisation must match
`DevOps:OrganizationUrl`, or startup fails with a message naming both. The optional
`Remote`, `Project` and `Repository` overrides exist for clones the parser cannot read.
The stable id used in worktree paths and the database (`RepositoryKey`) is a slug of
`DisplayName`; renaming a repo in appsettings therefore starts a fresh history for it.

---

## 5. Data model (EF Core, SQLite)

| Entity | Purpose | Key fields |
|---|---|---|
| `FlowStep` | The global flow. Seeded with three rows; the schema allows any number. | `Step` (int PK, the sequence, starting at 0: 0 = Summary, 1 = Diagram, 2 = Issues), `PromptText`, `Model?`, `TimeoutMinutes?`, `UpdatedAt` |
| `PullRequest` | Cache of DevOps PR metadata. | `Id` (DevOps PR id), `RepositoryKey`, `Title`, `Description`, `AuthorName`, `SourceBranch`, `TargetBranch`, `SourceCommit`, `Status`, `CreatedAt`, `Url`, `LastSeenAt`, `IsOpen` |
| `Review` | One review run of one PR. | `Id`, `PullRequestId`, `RepositoryKey`, `SourceCommit`, `MergeBaseCommit`, `Status`, `WorktreePath`, `WorktreeRemovedAt?`, `QueuedAt`, `StartedAt?`, `CompletedAt?`, `Error?` |
| `ReviewStepRun` | One Claude run inside a review. | `Id`, `ReviewId`, `Step` (matches `FlowStep.Step`), `RenderedPrompt`, `CommandLine`, `RawOutputJson`, `ResultText`, `StdErr`, `ExitCode?`, `AgentSessionId?`, `DurationMs?`, `CostUsd?`, `StartedAt?`, `CompletedAt?`, `Error?` |
| `Issue` | Parsed rows from the Issues step. | `Id`, `ReviewId`, `Severity`, `Category`, `File`, `Line?`, `Title`, `Description`, `Suggestion?` |

Repositories are **not** in the database; they come from appsettings and are referenced
by `RepositoryKey`. The Flow is seeded with three steps (0, 1, 2) on first run, each
holding the shipped default prompt for that position.
Nothing in the schema limits the step count, so more steps or a customisable flow need
no migration later.

What a step's output means is decided by its position, not by a stored kind: the
orchestrator treats step 0's result as the summary text, step 1's as the diagram HTML,
and step 2's as the issues JSON (and only step 2 gets `--json-schema`). The UI labels
the three cards accordingly.

Review and step-run state is **not stored**. `Review.State` and `ReviewStepRun.State`
are unmapped properties derived from the timestamps, errors and step runs; `Review.CurrentStep`
gives the step in flight.

---

## 6. Contracts

### 6.1 Prompt placeholders

Available in prompt text and skill arguments. Rendered by `PromptRenderer` before the
run. Unknown placeholders are left as-is and flagged in the UI.

```
{{PR_ID}}  {{PR_TITLE}}  {{PR_DESCRIPTION}}  {{PR_AUTHOR}}  {{PR_URL}}
{{REPO_KEY}}  {{REPO_NAME}}
{{SOURCE_BRANCH}}  {{TARGET_BRANCH}}  {{SOURCE_COMMIT}}  {{MERGE_BASE}}
{{CHANGED_FILES}}        git diff --name-status MERGE_BASE..SOURCE_COMMIT, one per line
{{WORKTREE_PATH}}
```

Because steps are independent there is no `{{SUMMARY}}` placeholder.

### 6.2 Claude invocation

Run from the worktree as the working directory. Prompt is written to **stdin**, not the
command line, to avoid the Windows command-line length limit.

```
claude -p --output-format json --allowedTools <list from appsettings>
       [--model <step model>] [--max-turns <n>]
       [--json-schema <issues schema>]            (Issues step only)
```

- A prompt whose rendered text starts with `/<skill-name>` is a skill invocation. The
  tool does nothing special with it; Claude resolves the skill from the worktree's
  `.claude` folder and the user's own skills and plugins.
- The `CLAUDECODE` environment variable is removed from the child process so the tool
  still works when launched from inside a Claude Code session during development.
- Timeout: the tool kills the process tree after the step's timeout and marks the step
  Failed.
- Result parsing: the JSON `result` field is the step text. For the Issues step the
  `structured_output` field is preferred; if absent, the first JSON object in `result`
  is parsed instead. Full JSON is kept in `RawOutputJson`.
- Exit code != 0, `is_error: true`, or unparseable output -> step Failed with stderr
  attached.

### 6.3 Issues JSON schema

Appended to the issues prompt as a fixed instruction and passed as `--json-schema`.

```json
{
  "type": "object",
  "required": ["issues"],
  "properties": {
    "issues": {
      "type": "array",
      "items": {
        "type": "object",
        "required": ["severity", "category", "file", "title", "description"],
        "properties": {
          "severity":    { "type": "string", "enum": ["Critical", "High", "Medium", "Low", "Info"] },
          "category":    { "type": "string" },
          "file":        { "type": "string" },
          "line":        { "type": ["integer", "null"] },
          "title":       { "type": "string" },
          "description": { "type": "string" },
          "suggestion":  { "type": ["string", "null"] }
        }
      }
    }
  }
}
```

### 6.4 Diagram rendering

```html
<iframe sandbox="allow-scripts" srcdoc="<!doctype html><html><head><meta charset=utf-8>
  <style>body{margin:0;font-family:system-ui}</style></head><body>EXCERPT</body></html>">
```

No `allow-same-origin`, so the excerpt runs in an opaque origin: it may load CDN scripts
and draw, but cannot read the host page, cookies or local storage. Height is set from a
`postMessage` the wrapper sends after load, with a fixed fallback.

### 6.5 API

`GET /api/health` -> `200 text/plain "OK"`. Mapped in `Api/HealthEndpoints.cs` via a
`MapHealthApi()` extension so later endpoints follow the same pattern (`MapReviewApi()` ...).

---

## 7. Phases

Each phase ends in a buildable, runnable state. Phases are ordered so every phase can be
demonstrated in the UI, and so the risky integrations (Claude, git, DevOps) are proved
early, before the UI is polished.

### Phase 0 – Repository scaffolding

Goal: a solution that builds and runs, with the API endpoint live.

- Create solution, `Directory.Build.props`, `.editorconfig`, the two projects (§3).
- Host: Blazor Web App template (server interactivity only), `net10.0-windows`,
  `OutputType` left as `Exe` for now (switched to `WinExe` in Phase 6).
- `MapHealthApi()` with `GET /api/health`.
- `.gitignore` additions: `*.db`, `*.db-shm`, `*.db-wal`, and
  `docs/mockup/**/node_modules` if the mockup export carries them. (`.vs/`, `bin/`, `obj/`
  are already covered.)
- README rewritten: what it is, prerequisites (Claude Code CLI, git, a DevOps PAT with
  Code (Read) scope, local clones), how to set the PAT with `dotnet user-secrets`, how
  to run.
- `.claude/launch.json` entry so the app can be previewed from Claude Code.

Done when: `dotnet build` is clean, `dotnet run --project src/CodebaseReviewTool.Host`
serves the template page and `/api/health` returns OK.

### Phase 1 – Configuration and persistence

Goal: the app validates its configuration on start and owns a SQLite database.

- Options classes for every §4 section, env-var expansion for paths, data-annotation
  validation, `ValidateOnStart`.
- `RepositoryResolver`: derives remote, organisation, project, repository and key from
  each `LocalPath` (§4), failing startup with a clear message when a remote URL cannot
  be parsed and no overrides are given, or its organisation differs from
  `DevOps:OrganizationUrl`.
- User-secrets loaded in **every** environment: `builder.Configuration.AddUserSecrets<Program>()`
  explicitly in `Program.cs`. `WebApplication.CreateBuilder` only adds them in
  Development, and the tray-hosted exe runs as Production, so without this the built app
  would see none of the machine-specific config.
- `ReviewDbContext`, entities (§5), initial migration, `Database.Migrate()` on startup,
  seed of one `FlowStep` row per shipped default prompt (`DefaultPrompts`, embedded
  resources in Core), never overwriting rows that already exist.
- Settings page: read-only view of effective configuration plus "Check" buttons that run
  `claude --version`, `git --version`, a DevOps "who am I" call, and verify each repo
  `LocalPath` is a git repository. Results shown inline; PAT never displayed.

Done when: a bad path fails startup with a readable error; Settings page shows green
checks for Claude, git, DevOps and each repo.

### Phase 2 – DevOps integration and open-PR list

Goal: see open PRs for the configured repos and queue a review.

- `DevOpsClient` over `HttpClient` + PAT basic auth, REST `api-version=7.1`:
  list active PRs per repo, get one PR. Typed DTOs mapped to `PullRequest`.
- `DevOpsPollService` refreshes the cache every `PollIntervalSeconds`; PRs that
  disappear are marked `IsOpen = false`, not deleted (their reviews remain).
- Pull request list on the home page, laid out as the mockup's "Open pull requests"
  screen (§10): a counts line, a "Reviewed" section (PRs with a review, running ones
  included) above an "Open" section (never reviewed), each row showing number, title,
  repo, author and source branch, with a "Start review" button that creates a `Review`
  in `Queued` state and raises a `ReviewEvents` notification. No repo filter and no
  manual refresh: the poll keeps the list current and the row count is small.
- DevOps being unreachable logs a warning and keeps the last cache; it never crashes
  the poller.

Done when: open PRs for the configured repos appear within one poll interval and a
click creates a Queued review visible on a Reviews page (the queue does nothing yet).

### Phase 3 – Git worktrees

Goal: a review gets its own worktree at the PR's source commit.

- `ProcessRunner` (starts a process, captures stdout/stderr/exit code, timeout and
  process-tree kill), `GitRunner` on top of it, and `WorktreeService`:
  `fetch <remote>`, `worktree add --detach <root>/<repoKey>/pr-<id>-r<reviewId> <sourceCommit>`,
  `merge-base <remote>/<target> <sourceCommit>`, `diff --name-status` for
  `{{CHANGED_FILES}}`, `worktree remove --force` + `worktree prune`.
- Windows failure handling on removal: locked files and long paths (a worktree left locked
  by a crash needs `--force --force` and an unlock before prune); the error is shown under
  the row on the Worktrees page with Retry, not stored on the `Review`, since writing its
  error field would turn a completed review into a failed one. (The team's `close-worktree` skill documents the
  same failure modes; reuse its approach, not its code.)
- `WorktreeCleanupService` removes worktrees older than `AutoCleanupAfterDays` for
  Completed/Failed reviews.
- Worktrees page (`/worktrees`, linked from the Configuration page rather than the
  header, since the mockup's header has only Pull requests and the gear): path, PR, age,
  Remove button, "Open folder" button.
- `ReviewQueueService` now exists and takes a Queued review through PreparingWorktree
  and stops (no Claude yet).

Done when: queuing a review creates a worktree on disk at the right commit, and it can
be removed from the UI.

### Phase 4 – Claude runner, flow configuration and orchestration

Goal: a review runs end to end and stores the three outputs.

- Flow steps on the Configuration page, replacing the placeholder: the mockup's numbered
  rail with a card per step (0 Summary, 1 Diagram, 2 Issues). Each card has a title and
  description, a model dropdown fed by `Claude:Models` plus `Claude:DefaultModel`, one prompt
  text box (start it with `/skill-name …` to run a skill), optional timeout, an
  "Edited" marker, a collapsible placeholder reference panel and a Preview button that
  opens a dialog with a PR picker and the rendered prompt. Page-level Save / Discard
  buttons in the page header as in the mockup. The Issues card shows the fixed schema
  instruction that will be appended, read-only and collapsible. Per-step "Reset to
  default" asks for confirmation, then restores the shipped prompt and clears the model
  and timeout overrides; it is enabled only when the step differs from the default.
- `PromptRenderer` (placeholders §6.1), the `IAgentRunner` boundary and its
  `ClaudeCodeRunner` implementation (§6.2) over `ProcessRunner`.
- `IssuesParser` -> `Issue` rows; tolerant of a stray code fence around the JSON.
- `ReviewOrchestrator`: drives the status machine in §2, writes a `ReviewStepRun` per
  step, stops on first failure, supports "re-run from step X" which reuses the existing
  worktree.
- `ReviewQueueService` runs the orchestrator with `MaxConcurrent` (default 1) and
  cancels running Claude processes on shutdown.

Done when: a real PR reviewed against a real local clone produces a Completed review
with summary text, diagram HTML and a non-empty issues list in the database.

### Phase 5 – Report and review history UI

Goal: the report is readable and the mockup is matched.

- Pull request rows gain their post-review states from the mockup: "Reviewing…" with a
  spinner while running; "N issues / N high" plus "View report" when complete; and,
  beyond the mockup, "Review failed" with "Retry" on failure and a "Review again" link
  beside "View report" so a PR can be re-reviewed after new pushes. No separate Reviews
  page.
- Report page (`/reviews/{id}`), laid out as the mockup's Report screen: a left aside
  (repository, author, branch, reviewed time, severity counts, and a list of this PR's
  earlier reviews linking to their reports), the PR number and title, a Summary section
  (plain text paragraphs), an Architecture section (sandboxed iframe §6.4) and an Issues
  section with severity filter pills (All and one per severity) over a Severity /
  Description / Suggested fix grid, the file and line shown in mono under the
  description. Beyond the mockup: a collapsible "Run details" per step showing the
  rendered prompt, stderr and the raw JSON, and a link to the PR in DevOps.
- Failed step shows its error and a Re-run button in place of the section.
- "Download report" produces a standalone HTML file via `ReportBuilder` (same template,
  diagram inlined into the iframe `srcdoc`).
- Remaining CSS for the report and the pull request rows, on the tokens and components
  already in `app.css` (§10).

Done when: the pages match the mockup closely and a report for a real PR is usable
without opening the database.

### Phase 6 – Tray and process lifecycle

Goal: the tool behaves like a resident Windows app.

- `OutputType` -> `WinExe`; `Tray:Enabled=false` or `--console` keeps a console for
  development.
- `TrayApplicationContext` on the main STA thread: icon with menu (Open UI, running
  count, Pause polling, Exit); double-click opens the browser at the Kestrel URL;
  balloon notification when a review completes or fails.
- Single-instance guard (named mutex); a second launch just opens the browser.
- Graceful shutdown: stop the queue, kill child Claude processes, flush the database.
- "Run at login" toggle on the Settings page that creates or removes a shortcut in the
  user's Startup folder (no registry, no admin).

Done when: the exe starts silently to the tray, the UI opens from it, and Exit leaves no
orphaned `claude.exe` or `git.exe` processes.

### Phase 7 – Polish and operational extras

Candidates, ordered by value; pick after Phases 0–6 are in use.

- Live step progress on the report page using `--output-format stream-json`.
- Cost and duration per step and per review (from Claude's result JSON) on the Reviews
  page.
- Compare two reviews of the same PR (issues added/resolved between iterations).
- Health and status endpoints beyond `/api/health` (the API growth path).
- Opt-in auto-review on new PR iterations (explicitly out of scope now).

---

## 8. Assumptions stated (small ones; shout if wrong)

- Each configured repo already exists as a full local clone; the tool fetches but never
  clones.
- Worktrees are created **detached** at the source commit, so a branch already checked
  out elsewhere never blocks a review.
- One review at a time by default; Claude runs are heavy and share the user's rate limit.
- The summary is rendered as plain text with line breaks preserved (no Markdown
  rendering) because it is "just text". Easy to switch to Markdig later.
- Kestrel listens on `http://localhost:5210`, HTTP only, loopback only. No auth on the
  UI or API because nothing is exposed off the machine.
- The database lives in `%LOCALAPPDATA%\CodebaseReviewTool` so a rebuild or clean never
  deletes review history.
- Only the DevOps PAT is a secret. It needs Code (Read) scope only.
- Running with the `http` launch profile logs a harmless Kestrel warning about
  overriding addresses, because launchSettings and the Kestrel section name the same
  URL. Both are kept: launchSettings so Visual Studio opens the browser, Kestrel so
  the tray-hosted exe has a port without a launch profile.

## 9. Out of scope for this build

- Writing anything to DevOps (comments, votes, status).
- Multiple flows, per-repo flows, per-step tool permissions.
- Reading diffs or files from DevOps.
- Authentication or remote access.

## 10. Design (from the Claude Design mockup)

The mockup is `docs/mockup/Review Desk.html`. It is a bundled single-page prototype; its
page template and styles are what the UI follows. Decisions taken against it on
2026-11-04:

| Topic | Decision |
|---|---|
| Name | The mockup says "Review Desk"; the app keeps **Codebase Review Tool** in the header and titles. |
| Screens | Three in the mockup: Pull requests (home), Configuration (behind the gear icon), Report. The app adds Worktrees, linked from Configuration, and folds the environment view and checks into Configuration below the flow steps. |
| Header | Sticky, 64px: brand mark and name, one nav link "Pull requests", then a theme toggle and the Configuration gear on the right. No other nav items. |
| Theme | Light and dark via CSS custom properties on `:root[data-theme]`; `theme.js` applies the saved choice (localStorage, falling back to the OS preference) before first paint, and the header toggle flips it. |
| Type | System fonts only. Helvetica Neue / Arial for text; Cascadia Mono (Windows 11), falling back to Consolas, for numbers, branches, paths and prompts, in place of the mockup's JetBrains Mono. Page title 40px/500, section titles 20px/500, body 15px. |
| Layout | Content column 1120px wide with 32px gutters; page padding 64px top, 120px bottom; sections 72px apart. Tables are CSS grids with a dark rule under the header row and light rules between rows, not `<table>` elements. |
| Severities | Five, extending the mockup's three: Critical, High, Medium, Low, Info, each with its own colour token (`--critical`, `--high`, `--med`, `--low`, `--info`) and a filter pill. |
| Pull request row | Number (mono), title, then Repo / Author / Branch; the right column holds the action for the row's state (§7 Phase 2 and Phase 5). |
| Configuration | Numbered rail of step cards with model dropdown, prompt textarea, "Edited" marker and per-step "Reset to default"; page-level Save / Discard. Checks, repositories and environment sections follow below. |
| Report | Aside (metadata, severity counts, earlier reviews) beside the title and summary; Architecture iframe full width; Issues grid with filter pills. |
| Tokens | `--bg`, `--surface`, `--sunken`, `--ink`, `--muted`, `--faint`, `--line`, `--line-strong`, `--accent`, `--accent-bg`, `--on-accent`, `--accent-soft`, the five severity colours, `--mono`, `--sans`. Components: `.page`, `.page-head`, `.page-title`, `.page-lede`, `.section`, `.section-head`, `.list-head`, `.btn-primary`, `.btn-secondary`, `.btn-link`, `.icon-btn`, `.kv`, `.kv-stack`, `.rows` / `.row-head` / `.row`, `.sev-*`, `.spinner`, `.select`, `.textarea`. |

Where the mockup and the earlier plan disagreed, the mockup won except for the name,
the severity count and the additions listed above.
