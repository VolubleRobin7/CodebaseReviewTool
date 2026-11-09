using System.Drawing;
using System.Windows.Forms;
using CodebaseReviewTool.Core.Data;
using CodebaseReviewTool.Core.Data.Enums;
using CodebaseReviewTool.Core.Hosting;
using CodebaseReviewTool.Core.Reviews;
using Microsoft.EntityFrameworkCore;

namespace CodebaseReviewTool.Host.Tray;

/// <summary>
/// The tray icon and its menu, run on the main STA thread by <see cref="Application.Run(ApplicationContext)"/>. Opens
/// the UI, shows how many reviews are running, pauses the DevOps poller and exits. Shows a balloon when a review
/// completes or fails; clicking it opens that review's report. Ending the message loop is the signal for
/// <c>Program</c> to stop the web host.
/// </summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private const string AppName = "Codebase Review Tool";

    // Balloon titles longer than this are cut off by Windows.
    private const int MaxBalloonTitleLength = 63;

    // Tray tooltips longer than this are rejected by NotifyIcon.
    private const int MaxTooltipLength = 127;

    private readonly string _appUrl;

    // The app name, with the environment appended outside Production so a development copy's icon is recognisable.
    private readonly string _title;

    private readonly ReviewEvents _events;
    private readonly IDbContextFactory<ReviewDbContext> _dbContextFactory;
    private readonly IPollControl _pollControl;
    private readonly ILogger<TrayApplicationContext> _logger;
    private readonly SynchronizationContext _ui;
    private readonly Icon _icon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly NotifyIcon _notifyIcon;
    private readonly CloseRequestWindow _closeRequestWindow;
    private readonly CancellationTokenRegistration _stoppingRegistration;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    // Reviews last seen queued or running; one leaving this set as completed or failed raises a balloon. Only read
    // and written while holding _refreshLock.
    private HashSet<int> _activeReviews = [];

    // The review the visible balloon is about, so clicking it opens that report. UI thread only.
    private int? _balloonReviewId;
    private bool _exiting;

    public TrayApplicationContext(IServiceProvider services, string appUrl)
    {
        ArgumentNullException.ThrowIfNull(services);

        _appUrl = appUrl;
        var environment = services.GetRequiredService<IHostEnvironment>();
        _title = environment.IsProduction() ? AppName : $"{AppName} ({environment.EnvironmentName})";
        _events = services.GetRequiredService<ReviewEvents>();
        _dbContextFactory = services.GetRequiredService<IDbContextFactory<ReviewDbContext>>();
        _pollControl = services.GetRequiredService<IPollControl>();
        _logger = services.GetRequiredService<ILogger<TrayApplicationContext>>();
        _ui = new WindowsFormsSynchronizationContext();

        var openItem = new ToolStripMenuItem("Open UI", null, (_, _) => OpenUi());
        openItem.Font = new Font(openItem.Font, FontStyle.Bold);
        _statusItem = new ToolStripMenuItem("Idle") { Enabled = false };
        _pauseItem = new ToolStripMenuItem("Pause polling") { CheckOnClick = true, Checked = _pollControl.IsPaused };
        _pauseItem.CheckedChanged += (_, _) => _pollControl.SetPaused(_pauseItem.Checked);

        _menu = new ContextMenuStrip();
        _menu.Items.AddRange(
        [
            openItem,
            new ToolStripSeparator(),
            _statusItem,
            _pauseItem,
            new ToolStripSeparator(),
            new ToolStripMenuItem("Exit", null, (_, _) => ExitThread()),
        ]);

        _icon = AppIcon.Load(Math.Max(16, SystemInformation.SmallIconSize.Width));
        _notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = _menu,
            Icon = _icon,
            Text = _title,
            Visible = true,
        };
        // A left click opens the UI; the right click is left to Windows, which shows the context menu.
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                OpenUi();
            }
        };
        _notifyIcon.BalloonTipClicked += (_, _) => OpenBalloonReport();
        _notifyIcon.BalloonTipClosed += (_, _) => _balloonReviewId = null;

        _closeRequestWindow = new CloseRequestWindow(ExitThread);

        // The host can also stop without the menu (a console close event, for example); end the tray with it.
        _stoppingRegistration = services.GetRequiredService<IHostApplicationLifetime>()
            .ApplicationStopping.Register(() => _ui.Post(_ => ExitThread(), null));

        _events.ReviewChanged += OnReviewChanged;
        _pollControl.Changed += OnPollChanged;
        _ = RefreshAsync();
    }

    protected override void ExitThreadCore()
    {
        if (!_exiting)
        {
            _exiting = true;
            _events.ReviewChanged -= OnReviewChanged;
            _pollControl.Changed -= OnPollChanged;
            _stoppingRegistration.Dispose();

            // Hide first so the icon does not linger in the tray while the host shuts down.
            _notifyIcon.Visible = false;
        }

        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _closeRequestWindow.DestroyHandle();
            _notifyIcon.Dispose();
            _menu.Dispose();
            _icon.Dispose();
        }

        base.Dispose(disposing);
    }

    private void OpenUi() => Open(_appUrl);

    private void OpenBalloonReport()
    {
        if (_balloonReviewId is { } reviewId)
        {
            Open(AppUrl.Report(_appUrl, reviewId));
        }
    }

    private void Open(string url)
    {
        if (!Browser.TryOpen(url))
        {
            _logger.LogWarning("Could not open a browser at {Url}", url);
        }
    }

    // Raised on the publisher's thread; the refresh queries the database there and posts the UI changes.
    private void OnReviewChanged(ReviewChangedEventArgs args) => _ = RefreshAsync();

    private void OnPollChanged() => _ui.Post(_ => _pauseItem.Checked = _pollControl.IsPaused, null);

    /// <summary>
    /// Re-reads which reviews are queued or running, updates the menu and tooltip, and raises a balloon for each
    /// review that has finished since the last refresh.
    /// </summary>
    private async Task RefreshAsync()
    {
        try
        {
            await _refreshLock.WaitAsync();
            try
            {
                await using var db = await _dbContextFactory.CreateDbContextAsync();

                var unfinished = await db.Reviews
                    .AsNoTracking()
                    .Include(r => r.StepRuns)
                    .Where(r => r.CompletedAt == null && r.Error == null)
                    .ToListAsync();
                var active = unfinished
                    .Where(r => r.State is ReviewState.Queued or ReviewState.PreparingWorktree or ReviewState.Running)
                    .ToList();
                var running = active.Count(r => r.State != ReviewState.Queued);
                var queued = active.Count - running;

                var finishedIds = _activeReviews.Except(active.Select(r => r.Id)).ToList();
                _activeReviews = [.. active.Select(r => r.Id)];

                var notifications = finishedIds.Count == 0 ? [] : await LoadNotificationsAsync(db, finishedIds);
                _ui.Post(_ => Show(running, queued, notifications), null);
            }
            finally
            {
                _refreshLock.Release();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Refreshing the tray status failed");
        }
    }

    private static async Task<List<Notification>> LoadNotificationsAsync(ReviewDbContext db, List<int> reviewIds)
    {
        var reviews = await db.Reviews
            .AsNoTracking()
            .Include(r => r.StepRuns)
            .Include(r => r.PullRequest)
            .Where(r => reviewIds.Contains(r.Id))
            .OrderBy(r => r.Id)
            .ToListAsync();
        var issueCounts = await db.Issues
            .Where(i => reviewIds.Contains(i.ReviewId))
            .GroupBy(i => i.ReviewId)
            .Select(g => new { ReviewId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.ReviewId, g => g.Count);

        var notifications = new List<Notification>();
        foreach (var review in reviews)
        {
            var title = string.IsNullOrWhiteSpace(review.PullRequest?.Title) ? AppName : review.PullRequest.Title;
            switch (review.State)
            {
                case ReviewState.Completed:
                    var issues = issueCounts.GetValueOrDefault(review.Id);
                    var issueText = issues switch
                    {
                        0 => "no issues",
                        1 => "1 issue",
                        _ => $"{issues} issues",
                    };
                    notifications.Add(new Notification(review.Id, title, $"#{review.PullRequestId} reviewed: {issueText}", ToolTipIcon.Info));
                    break;

                case ReviewState.Failed:
                    notifications.Add(new Notification(review.Id, title, $"#{review.PullRequestId} review failed", ToolTipIcon.Warning));
                    break;

                default:
                    // Deleted or re-queued reviews leave the active set without finishing; nothing to announce.
                    break;
            }
        }

        return notifications;
    }

    private void Show(int running, int queued, List<Notification> notifications)
    {
        if (_exiting)
        {
            return;
        }

        var status = (running, queued) switch
        {
            (0, 0) => "Idle",
            (_, 0) => Plural(running, "review") + " running",
            (0, _) => Plural(queued, "review") + " queued",
            _ => $"{Plural(running, "review")} running, {queued} queued",
        };
        _statusItem.Text = status;

        var tooltip = $"{_title} - {status}";
        _notifyIcon.Text = tooltip.Length > MaxTooltipLength ? tooltip[..MaxTooltipLength] : tooltip;

        // Windows shows one balloon at a time, so a burst of finishes shows the latest.
        foreach (var notification in notifications)
        {
            _balloonReviewId = notification.ReviewId;
            _notifyIcon.BalloonTipTitle = notification.Title.Length > MaxBalloonTitleLength
                ? notification.Title[..(MaxBalloonTitleLength - 1)] + "…"
                : notification.Title;
            _notifyIcon.BalloonTipText = notification.Text;
            _notifyIcon.BalloonTipIcon = notification.Icon;
            _notifyIcon.ShowBalloonTip(10_000);
        }
    }

    private static string Plural(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    private sealed record Notification(int ReviewId, string Title, string Text, ToolTipIcon Icon);

    /// <summary>
    /// A hidden top-level window that turns a polite close request into a graceful exit: <c>taskkill</c> without
    /// <c>/F</c> posts <c>WM_CLOSE</c>, and Windows sends <c>WM_ENDSESSION</c> at sign-out or shutdown.
    /// </summary>
    private sealed class CloseRequestWindow : NativeWindow
    {
        private const int WmClose = 0x0010;
        private const int WmQueryEndSession = 0x0011;
        private const int WmEndSession = 0x0016;

        private readonly Action _onClose;

        public CloseRequestWindow(Action onClose)
        {
            _onClose = onClose;
            CreateHandle(new CreateParams { Caption = AppName });
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case WmClose:
                    _onClose();
                    return;

                case WmQueryEndSession:
                    m.Result = 1;
                    return;

                case WmEndSession when m.WParam != IntPtr.Zero:
                    _onClose();
                    return;

                default:
                    base.WndProc(ref m);
                    return;
            }
        }
    }
}
