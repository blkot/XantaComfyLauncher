using XantaComfyLauncher.Services;

namespace XantaComfyLauncher.Utils;

public sealed class TrayIconManager : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _showItem;
    private readonly ToolStripMenuItem _startItem;
    private readonly ToolStripMenuItem _stopItem;
    private readonly ToolStripMenuItem _openItem;
    private readonly ToolStripMenuItem _queueItem;
    private readonly ToolStripMenuItem _minimizeItem;
    private readonly ToolStripMenuItem _autoStartItem;
    private readonly ToolStripMenuItem _autoRestartItem;
    private readonly ToolStripMenuItem _warnLowVramItem;

    public event Action? ShowRequested;
    public event Action? StartRequested;
    public event Action? StopRequested;
    public event Action? OpenRequested;
    public event Action? QueueRequested;
    public event Action? ExitRequested;
    public event Action<bool>? MinimizeToTrayChanged;
    public event Action<bool>? AutoStartWithWindowsChanged;
    public event Action<bool>? AutoRestartOnCrashChanged;
    public event Action<bool>? WarnLowVramBeforeStartChanged;

    public TrayIconManager(bool minimizeToTray, bool autoStartWithWindows, bool autoRestartOnCrash)
    {
        _menu = new ContextMenuStrip();
        _showItem = new ToolStripMenuItem("显示主界面", null, (_, _) => ShowRequested?.Invoke());
        _startItem = new ToolStripMenuItem("启动 ComfyUI", null, (_, _) => StartRequested?.Invoke());
        _stopItem = new ToolStripMenuItem("停止 ComfyUI", null, (_, _) => StopRequested?.Invoke());
        _openItem = new ToolStripMenuItem("打开 ComfyUI 界面", null, (_, _) => OpenRequested?.Invoke());
        _queueItem = new ToolStripMenuItem("队列管理", null, (_, _) => QueueRequested?.Invoke());
        _minimizeItem = new ToolStripMenuItem("最小化到托盘")
        {
            CheckOnClick = true,
            Checked = minimizeToTray
        };
        _autoStartItem = new ToolStripMenuItem("开机自动启动")
        {
            CheckOnClick = true,
            Checked = autoStartWithWindows
        };
        _autoRestartItem = new ToolStripMenuItem("意外退出后自动重启")
        {
            CheckOnClick = true,
            Checked = autoRestartOnCrash
        };
        _warnLowVramItem = new ToolStripMenuItem("启动前显存预警")
        {
            CheckOnClick = true,
            Checked = true
        };
        _minimizeItem.CheckedChanged += (_, _) => MinimizeToTrayChanged?.Invoke(_minimizeItem.Checked);
        _autoStartItem.CheckedChanged += (_, _) => AutoStartWithWindowsChanged?.Invoke(_autoStartItem.Checked);
        _autoRestartItem.CheckedChanged += (_, _) => AutoRestartOnCrashChanged?.Invoke(_autoRestartItem.Checked);
        _warnLowVramItem.CheckedChanged += (_, _) => WarnLowVramBeforeStartChanged?.Invoke(_warnLowVramItem.Checked);

        _menu.Items.Add(_showItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_startItem);
        _menu.Items.Add(_stopItem);
        _menu.Items.Add(_openItem);
        _menu.Items.Add(_queueItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_minimizeItem);
        _menu.Items.Add(_autoStartItem);
        _menu.Items.Add(_autoRestartItem);
        _menu.Items.Add(_warnLowVramItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("退出 XantaComfyLauncher", null, (_, _) => ExitRequested?.Invoke()));

        _notifyIcon = new NotifyIcon
        {
            Icon = AppIcon.Load(),
            Text = "XantaComfyLauncher",
            Visible = true,
            ContextMenuStrip = _menu
        };
        _notifyIcon.DoubleClick += (_, _) => ShowRequested?.Invoke();
    }

    public bool MinimizeToTray
    {
        get => _minimizeItem.Checked;
        set => _minimizeItem.Checked = value;
    }

    public bool AutoRestartOnCrash
    {
        get => _autoRestartItem.Checked;
        set => _autoRestartItem.Checked = value;
    }

    public bool WarnLowVramBeforeStart
    {
        get => _warnLowVramItem.Checked;
        set => _warnLowVramItem.Checked = value;
    }

    public void UpdateState(bool running, bool busy, bool hasPath)
    {
        _startItem.Enabled = !busy && hasPath && !running;
        _stopItem.Enabled = !busy && running;
        _openItem.Enabled = !busy && running;
        _queueItem.Enabled = !busy && running;
    }

    public string StatusText => _notifyIcon.Text;

    public void SetStatusText(string text)
    {
        var value = string.IsNullOrWhiteSpace(text) ? "XantaComfyLauncher" : text.Trim();
        _notifyIcon.Text = value.Length <= 63 ? value : value[..60] + "...";
    }

    public void ShowBalloon(string title, string text, ToolTipIcon icon = ToolTipIcon.Info)
    {
        // NotifyIcon balloon tips use NIIF_INFO/NIIF_WARNING/NIIF_ERROR and Windows
        // maps those to system sounds (Asterisk/Default Beep/Warning). WinForms does
        // not expose NIIF_NOSOUND, so keep the tray icon but log instead of showing
        // a balloon to avoid unrelated system notification sounds.
        LauncherLog.Write("tray", $"{title}: {text} ({icon})");
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
    }
}
