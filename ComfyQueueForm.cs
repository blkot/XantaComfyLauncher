using XantaComfyLauncher.Models;
using XantaComfyLauncher.Services;
using XantaComfyLauncher.Utils;

namespace XantaComfyLauncher;

public sealed class ComfyQueueForm : Form
{
    private readonly ComfyApiService _api;
    private readonly ComfyWebSocketService _ws;
    private readonly CancellationTokenSource _cts = new();
    private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 2000 };

    private ListView _queueList = null!;
    private Button _refreshButton = null!;
    private Button _interruptButton = null!;
    private Button _clearButton = null!;
    private Button _deleteButton = null!;
    private Button _closeButton = null!;
    private Label _statusLabel = null!;
    private ProgressBar _progressBar = null!;
    private Label _executionStatusLabel = null!;
    private Label _executionDetailLabel = null!;
    private ProgressBar _executionProgressBar = null!;
    private bool _refreshing;
    private bool _actionBusy;

    public ComfyQueueForm(string baseUrl)
    {
        _api = new ComfyApiService(baseUrl);
        _ws = new ComfyWebSocketService(baseUrl);
        BuildUi();
        ThemeManager.Apply(this);
        Icon = AppIcon.Load();
        _ws.ProgressChanged += progress => OnUi(() => UpdateExecutionProgress(progress));
        _ws.ConnectionStatusChanged += status => OnUi(() => _executionDetailLabel.Text = status);
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
        Shown += async (_, _) =>
        {
            _refreshTimer.Start();
            _ws.Start();
            await RefreshAsync();
        };
        FormClosing += (_, _) =>
        {
            _refreshTimer.Stop();
            _cts.Cancel();
            _ws.Dispose();
            _api.Dispose();
        };
    }

    private void BuildUi()
    {
        Text = "ComfyUI 队列管理";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(920, 560);
        MinimumSize = new Size(720, 420);
        Font = new Font("Segoe UI", 9F);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(245, 246, 248);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(10)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        _refreshButton = MakeAutoButton("刷新", 76);
        _interruptButton = MakeAutoButton("中断当前", 92);
        _clearButton = MakeAutoButton("清空等待队列", 112);
        _deleteButton = MakeAutoButton("删除选中等待项", 124);
        _closeButton = MakeAutoButton("关闭", 76);
        toolbar.Controls.Add(_refreshButton);
        toolbar.Controls.Add(_interruptButton);
        toolbar.Controls.Add(_clearButton);
        toolbar.Controls.Add(_deleteButton);
        toolbar.Controls.Add(_closeButton);

        _statusLabel = new Label
        {
            Text = "正在连接 ComfyUI...",
            AutoSize = true,
            Padding = new Padding(10, 8, 8, 0)
        };
        _progressBar = new ProgressBar
        {
            Visible = false,
            Style = ProgressBarStyle.Marquee,
            Width = 140,
            Height = 20,
            Margin = new Padding(0, 4, 0, 0)
        };
        toolbar.Controls.Add(_statusLabel);
        toolbar.Controls.Add(_progressBar);
        root.Controls.Add(toolbar, 0, 0);

        var executionPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(2, 8, 2, 4)
        };
        executionPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        executionPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _executionStatusLabel = new Label
        {
            Text = "等待执行任务",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            ForeColor = Color.FromArgb(40, 44, 52)
        };
        _executionDetailLabel = new Label
        {
            Text = "实时进度连接中...",
            AutoSize = true,
            ForeColor = Color.FromArgb(90, 95, 105)
        };
        _executionProgressBar = new ProgressBar
        {
            Dock = DockStyle.Fill,
            Style = ProgressBarStyle.Continuous,
            Minimum = 0,
            Maximum = 100,
            Height = 18,
            Margin = new Padding(0, 5, 0, 0)
        };
        executionPanel.Controls.Add(_executionStatusLabel, 0, 0);
        executionPanel.Controls.Add(_executionDetailLabel, 1, 0);
        executionPanel.Controls.Add(_executionProgressBar, 0, 1);
        executionPanel.SetColumnSpan(_executionProgressBar, 2);
        root.Controls.Add(executionPanel, 0, 1);

        _queueList = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            HideSelection = false,
            MultiSelect = true
        };
        _queueList.Columns.Add("状态", 80);
        _queueList.Columns.Add("Prompt ID", 250);
        _queueList.Columns.Add("序号", 80);
        _queueList.Columns.Add("节点数", 70);
        _queueList.Columns.Add("内容", 400);
        _queueList.SelectedIndexChanged += (_, _) => UpdateActionButtons();
        root.Controls.Add(_queueList, 0, 2);

        var hint = new Label
        {
            Text = "队列每 2 秒自动刷新。中断只影响当前执行任务；删除和清空只作用于等待队列。",
            AutoSize = true,
            ForeColor = Color.FromArgb(90, 95, 105),
            Padding = new Padding(2, 8, 2, 0)
        };
        root.Controls.Add(hint, 0, 3);

        _refreshButton.Click += async (_, _) => await RefreshAsync();
        _interruptButton.Click += async (_, _) => await InterruptAsync();
        _clearButton.Click += async (_, _) => await ClearPendingAsync();
        _deleteButton.Click += async (_, _) => await DeleteSelectedAsync();
        _closeButton.Click += (_, _) => Close();

        Controls.Add(root);
        UpdateActionButtons();
    }

    private static Button MakeAutoButton(string text, int minimumWidth) => new()
    {
        Text = text,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        MinimumSize = new Size(minimumWidth, 30),
        Margin = new Padding(3),
        Padding = new Padding(8, 0, 8, 0)
    };

    private async Task RefreshAsync()
    {
        if (_refreshing || _actionBusy || _cts.IsCancellationRequested) return;
        _refreshing = true;
        _progressBar.Visible = true;
        try
        {
            var snapshot = await _api.GetQueueAsync(_cts.Token);
            Populate(snapshot);
            var running = snapshot.Running.FirstOrDefault();
            if (running != null) _ws.UpdateNodeTypes(running.NodeTypes);
            _statusLabel.Text = $"运行中 {snapshot.RunningCount} / 等待中 {snapshot.PendingCount}";
            _statusLabel.ForeColor = Color.FromArgb(32, 120, 72);
        }
        catch (OperationCanceledException)
        {
            // Form is closing.
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "无法连接 ComfyUI（" + ex.Message + "）";
            _statusLabel.ForeColor = Color.Firebrick;
            LauncherLog.Write("queue", "刷新队列失败：" + ex.Message);
        }
        finally
        {
            _refreshing = false;
            if (!IsDisposed)
            {
                _progressBar.Visible = false;
                UpdateActionButtons();
            }
        }
    }

    private void Populate(ComfyQueueSnapshot snapshot)
    {
        var selectedIds = SelectedPendingIds().ToHashSet(StringComparer.OrdinalIgnoreCase);
        _queueList.BeginUpdate();
        try
        {
            _queueList.Items.Clear();
            foreach (var item in snapshot.Running.Concat(snapshot.Pending))
            {
                var row = new ListViewItem(item.StateText) { Tag = item };
                row.SubItems.Add(item.PromptId);
                row.SubItems.Add(item.Number?.ToString() ?? "");
                row.SubItems.Add(item.NodeCount.ToString());
                row.SubItems.Add(item.Summary);
                if (item.IsRunning)
                {
                    row.ForeColor = Color.FromArgb(32, 120, 72);
                    row.Font = new Font(_queueList.Font, FontStyle.Bold);
                }
                _queueList.Items.Add(row);
            }

            // ListView ignores Selected=true before the row is inserted, and
            // Items.Clear() resets the selection. Restore by Prompt ID after all
            // rows have been added.
            foreach (ListViewItem row in _queueList.Items)
            {
                if (row.Tag is ComfyQueueItem item &&
                    selectedIds.Contains(item.PromptId))
                {
                    row.Selected = true;
                }
            }
        }
        finally
        {
            _queueList.EndUpdate();
        }
    }

    private async Task InterruptAsync()
    {
        if (_actionBusy) return;
        if (MessageBox.Show(this, "中断当前正在执行的任务吗？", "中断当前任务",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        SetActionBusy(true);
        try
        {
            await _api.InterruptAsync(_cts.Token);
            LauncherLog.Write("queue", "已请求中断当前任务");
        }
        catch (Exception ex)
        {
            ShowActionError("中断任务失败", ex);
        }
        finally
        {
            SetActionBusy(false);
            await RefreshAsync();
        }
    }

    private async Task ClearPendingAsync()
    {
        if (_actionBusy) return;
        if (MessageBox.Show(this, "清空所有等待中的任务吗？", "清空等待队列",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        SetActionBusy(true);
        try
        {
            await _api.ClearPendingAsync(_cts.Token);
            LauncherLog.Write("queue", "已清空等待队列");
        }
        catch (Exception ex)
        {
            ShowActionError("清空队列失败", ex);
        }
        finally
        {
            SetActionBusy(false);
            await RefreshAsync();
        }
    }

    private async Task DeleteSelectedAsync()
    {
        if (_actionBusy) return;
        var ids = SelectedPendingIds().ToList();
        if (ids.Count == 0)
        {
            MessageBox.Show(this, "请选择等待中的任务。", "删除等待项",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (MessageBox.Show(this, $"删除选中的 {ids.Count} 个等待任务吗？", "删除等待项",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        SetActionBusy(true);
        try
        {
            await _api.DeletePendingAsync(ids, _cts.Token);
            LauncherLog.Write("queue", $"已删除等待任务：{string.Join(", ", ids)}");
        }
        catch (Exception ex)
        {
            ShowActionError("删除等待项失败", ex);
        }
        finally
        {
            SetActionBusy(false);
            await RefreshAsync();
        }
    }

    private IEnumerable<string> SelectedPendingIds() =>
        _queueList.SelectedItems
            .Cast<ListViewItem>()
            .Select(item => item.Tag)
            .OfType<ComfyQueueItem>()
            .Where(item => !item.IsRunning && !string.IsNullOrWhiteSpace(item.PromptId))
            .Select(item => item.PromptId);

    private void UpdateExecutionProgress(ComfyExecutionProgress progress)
    {
        if (IsDisposed) return;
        if (!string.IsNullOrWhiteSpace(progress.Error))
        {
            _executionStatusLabel.Text = "执行失败";
            _executionStatusLabel.ForeColor = Color.Firebrick;
            _executionDetailLabel.Text = progress.Error;
            return;
        }

        _executionStatusLabel.ForeColor = Color.FromArgb(40, 44, 52);
        var node = string.IsNullOrWhiteSpace(progress.NodeClassType)
            ? progress.NodeId
            : $"{progress.NodeClassType} ({progress.NodeId})";
        _executionStatusLabel.Text = string.IsNullOrWhiteSpace(node)
            ? progress.Status
            : $"{progress.Status}：{node}";

        var details = new List<string>();
        if (progress.Value > 0 || progress.Max > 0)
            details.Add($"{progress.Value}/{progress.Max}");
        if (progress.NodeElapsed is TimeSpan elapsed)
            details.Add("已用 " + FormatDuration(elapsed));
        if (progress.Eta is TimeSpan eta)
            details.Add("预计剩余 " + FormatDuration(eta));
        if (progress.QueueRemaining > 0)
            details.Add($"队列剩余 {progress.QueueRemaining}");
        _executionDetailLabel.Text = details.Count > 0 ? string.Join(" · ", details) : progress.Status;

        if (progress.Percent > 0 || progress.Status == "执行完成")
        {
            _executionProgressBar.Value = (int)Math.Clamp(Math.Round(progress.Percent), 0, 100);
        }
        if (progress.Status == "执行完成")
        {
            _executionStatusLabel.Text = "执行完成";
            _executionStatusLabel.ForeColor = Color.FromArgb(32, 120, 72);
        }
    }

    private static string FormatDuration(TimeSpan value) =>
        value.TotalHours >= 1
            ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
            : $"{value.Minutes:00}:{value.Seconds:00}";

    private void OnUi(Action action)
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(action); } catch { }
        }
        else action();
    }

    private void UpdateActionButtons()
    {
        var busy = _refreshing || _actionBusy;
        var hasRunning = _queueList.Items.Cast<ListViewItem>()
            .Any(item => item.Tag is ComfyQueueItem { IsRunning: true });
        var hasPending = _queueList.Items.Cast<ListViewItem>()
            .Any(item => item.Tag is ComfyQueueItem { IsRunning: false });
        _refreshButton.Enabled = !busy;
        _interruptButton.Enabled = !busy && hasRunning;
        _clearButton.Enabled = !busy && hasPending;
        _deleteButton.Enabled = !busy && SelectedPendingIds().Any();
        _closeButton.Enabled = !_actionBusy;
    }

    private void SetActionBusy(bool busy)
    {
        _actionBusy = busy;
        UpdateActionButtons();
    }

    private void ShowActionError(string title, Exception ex)
    {
        LauncherLog.Error("queue", ex);
        _statusLabel.Text = title;
        _statusLabel.ForeColor = Color.Firebrick;
        MessageBox.Show(this, ex.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
