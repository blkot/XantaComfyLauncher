using System.Diagnostics;
using System.Text;
using ComfyLauncher.Models;
using ComfyLauncher.Services;
using ComfyLauncher.Utils;

namespace ComfyLauncher;

public sealed class CustomNodesForm : Form
{
    private readonly AppPaths _paths;
    private readonly CustomNodeManager _manager;
    private readonly CancellationTokenSource _cts = new();
    private readonly Queue<string> _logQueue = new();
    private readonly System.Windows.Forms.Timer _logTimer = new() { Interval = 150 };

    private ListView _list = null!;
    private Button _checkButton = null!;
    private Button _installMissingButton = null!;
    private Button _updateSelectedButton = null!;
    private Button _updateAllButton = null!;
    private Button _openFolderButton = null!;
    private Button _closeButton = null!;
    private Label _statusLabel = null!;
    private ProgressBar _progressBar = null!;
    private RichTextBox _logText = null!;
    private bool _busy;

    public CustomNodesForm(AppPaths paths)
    {
        _paths = paths;
        _manager = new CustomNodeManager(paths);
        BuildUi();
        _logTimer.Tick += (_, _) => FlushLogs();
        _logTimer.Start();
        Shown += async (_, _) => await LoadStatesAsync(checkRemote: true);
        FormClosing += (_, _) => _cts.Cancel();
    }

    private void BuildUi()
    {
        Text = "ComfyUI 节点管理";
        StartPosition = FormStartPosition.Manual;
        Location = new Point(120, 100);
        ClientSize = new Size(1000, 650);
        MinimumSize = new Size(820, 540);
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
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 42));

        var hint = new Label
        {
            Text = "清单驱动安装 / 更新。更新前会检查已跟踪文件修改；requirements.txt 变化时自动同步依赖。更新完成后需要重启 ComfyUI。",
            AutoSize = true,
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(70, 75, 85),
            Padding = new Padding(2, 0, 2, 8)
        };
        root.Controls.Add(hint, 0, 0);

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        _checkButton = new Button { Text = "检查更新", Width = 92, Height = 30 };
        _installMissingButton = new Button { Text = "安装缺失", Width = 92, Height = 30 };
        _updateSelectedButton = new Button { Text = "更新选中", Width = 92, Height = 30 };
        _updateAllButton = new Button { Text = "全部更新", Width = 92, Height = 30 };
        _openFolderButton = new Button { Text = "打开节点目录", Width = 106, Height = 30 };
        _closeButton = new Button { Text = "关闭", Width = 76, Height = 30 };
        toolbar.Controls.Add(_checkButton);
        toolbar.Controls.Add(_installMissingButton);
        toolbar.Controls.Add(_updateSelectedButton);
        toolbar.Controls.Add(_updateAllButton);
        toolbar.Controls.Add(_openFolderButton);
        toolbar.Controls.Add(_closeButton);
        root.Controls.Add(toolbar, 0, 1);

        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            HideSelection = false,
            MultiSelect = false
        };
        _list.Columns.Add("节点", 220);
        _list.Columns.Add("分支", 90);
        _list.Columns.Add("已安装", 90);
        _list.Columns.Add("远程", 90);
        _list.Columns.Add("状态", 110);
        _list.Columns.Add("说明", 360);
        root.Controls.Add(_list, 0, 2);

        var logGroup = new GroupBox { Text = "操作日志", Dock = DockStyle.Fill, Padding = new Padding(8) };
        var logLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        logLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        logLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var statusFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        _statusLabel = new Label { Text = "就绪", AutoSize = true, Padding = new Padding(2, 8, 12, 0) };
        _progressBar = new ProgressBar { Visible = false, Style = ProgressBarStyle.Marquee, Width = 160, Height = 20 };
        statusFlow.Controls.Add(_statusLabel);
        statusFlow.Controls.Add(_progressBar);

        _logText = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            WordWrap = false,
            DetectUrls = false,
            Font = new Font("Consolas", 9F),
            BackColor = Color.FromArgb(24, 26, 30),
            ForeColor = Color.FromArgb(220, 224, 230),
            BorderStyle = BorderStyle.FixedSingle
        };
        logLayout.Controls.Add(statusFlow, 0, 0);
        logLayout.Controls.Add(_logText, 0, 1);
        logGroup.Controls.Add(logLayout);
        root.Controls.Add(logGroup, 0, 3);

        Controls.Add(root);

        _checkButton.Click += async (_, _) => await LoadStatesAsync(checkRemote: true);
        _installMissingButton.Click += async (_, _) => await InstallMissingAsync();
        _updateSelectedButton.Click += async (_, _) => await UpdateSelectedAsync();
        _updateAllButton.Click += async (_, _) => await UpdateAllAsync();
        _openFolderButton.Click += (_, _) => OpenFolder(_manager.CustomNodesDir);
        _closeButton.Click += (_, _) => Close();
    }

    private async Task LoadStatesAsync(bool checkRemote)
    {
        if (_busy) return;
        SetBusy(true);
        try
        {
            var progress = CreateProgress();
            var localStates = await _manager.GetStatesAsync(false, progress, _cts.Token);
            Populate(localStates);
            SetStatus($"本地状态已加载（{localStates.Count} 个节点），正在检查远程更新...");

            if (checkRemote)
            {
                var remoteStates = await _manager.GetStatesAsync(true, progress, _cts.Token);
                Populate(remoteStates);
                SetStatus($"已检查 {remoteStates.Count} 个节点包");
            }
        }
        catch (OperationCanceledException)
        {
            SetStatus("操作已取消");
        }
        catch (Exception ex)
        {
            AppendLog("[error] " + ex);
            SetStatus("加载节点状态失败");
            MessageBox.Show(this, ex.Message, "节点管理", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task InstallMissingAsync()
    {
        if (_busy) return;
        if (MessageBox.Show(this, "安装清单中所有缺失节点吗？", "安装缺失节点",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        SetBusy(true);
        try
        {
            var results = await _manager.InstallMissingAsync(CreateProgress(), _cts.Token);
            var errors = results.Where(r => r.Error != null).ToList();
            SetStatus($"安装完成：{results.Count - errors.Count} 成功，{errors.Count} 失败");
            foreach (var (definition, error) in errors)
                AppendLog($"[error] {definition.Name}: {error}");
            await ReloadAfterChangeAsync();
            if (results.Count > 0)
                MessageBox.Show(this, "节点安装操作完成。请重启 ComfyUI 使新节点生效。", "节点管理",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (OperationCanceledException)
        {
            SetStatus("操作已取消");
        }
        catch (Exception ex)
        {
            AppendLog("[error] " + ex);
            MessageBox.Show(this, ex.Message, "安装失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task UpdateSelectedAsync()
    {
        if (_busy || _list.SelectedItems.Count == 0) return;
        if (_list.SelectedItems[0].Tag is not CustomNodeState state) return;
        var def = state.Definition;
        if (MessageBox.Show(this, $"更新 {def.Name} 到远程分支 {def.Branch} 的最新提交吗？",
                "更新节点", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        SetBusy(true);
        try
        {
            await _manager.InstallOrUpdateAsync(def, CreateProgress(), _cts.Token);
            AppendLog($"[ok] 已更新 {def.Name}");
            await ReloadAfterChangeAsync();
            MessageBox.Show(this, "更新完成。请重启 ComfyUI 使改动生效。", "节点管理",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (OperationCanceledException)
        {
            SetStatus("操作已取消");
        }
        catch (Exception ex)
        {
            AppendLog("[error] " + ex);
            MessageBox.Show(this, ex.Message, "更新失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task UpdateAllAsync()
    {
        if (_busy) return;
        if (MessageBox.Show(this, "更新清单中全部节点吗？有本地修改的仓库会自动跳过。", "全部更新",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        SetBusy(true);
        try
        {
            var results = await _manager.UpdateAllAsync(CreateProgress(), _cts.Token);
            var errors = results.Where(r => r.Error != null).ToList();
            SetStatus($"更新完成：{results.Count - errors.Count} 成功，{errors.Count} 失败");
            foreach (var (definition, error) in errors)
                AppendLog($"[error] {definition.Name}: {error}");
            await ReloadAfterChangeAsync();
            MessageBox.Show(this, "节点更新操作完成。请重启 ComfyUI 使改动生效。", "节点管理",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (OperationCanceledException)
        {
            SetStatus("操作已取消");
        }
        catch (Exception ex)
        {
            AppendLog("[error] " + ex);
            MessageBox.Show(this, ex.Message, "更新失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ReloadAfterChangeAsync()
    {
        var states = await _manager.GetStatesAsync(checkRemote: true, CreateProgress(), _cts.Token);
        Populate(states);
    }

    private void Populate(IReadOnlyList<CustomNodeState> states)
    {
        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();
            foreach (var state in states.OrderBy(s => s.Definition.Name, StringComparer.OrdinalIgnoreCase))
            {
                var item = new ListViewItem(state.Definition.Name) { Tag = state };
                item.SubItems.Add(state.Definition.Branch ?? "");
                item.SubItems.Add(Short(state.InstalledCommit));
                item.SubItems.Add(Short(state.LatestCommit));
                item.SubItems.Add(state.StatusText);
                item.SubItems.Add(state.Error ?? (state.UpdateAvailable ? "可更新到远程最新提交" : ""));
                if (state.State == CustomNodeInstallState.UpdateAvailable)
                    item.ForeColor = Color.FromArgb(170, 90, 10);
                else if (state.State is CustomNodeInstallState.Error or CustomNodeInstallState.Dirty)
                    item.ForeColor = Color.Firebrick;
                else if (state.State == CustomNodeInstallState.Missing)
                    item.ForeColor = Color.DimGray;
                _list.Items.Add(item);
            }
        }
        finally
        {
            _list.EndUpdate();
        }
    }

    private IProgress<string> CreateProgress()
    {
        return new Progress<string>(message =>
        {
            SetStatus(message);
            AppendLog(message);
        });
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _progressBar.Visible = busy;
        UseWaitCursor = busy;
        _checkButton.Enabled = !busy;
        _installMissingButton.Enabled = !busy;
        _updateSelectedButton.Enabled = !busy;
        _updateAllButton.Enabled = !busy;
        _openFolderButton.Enabled = !busy;
        _closeButton.Enabled = !busy;
    }

    private void SetStatus(string message)
    {
        if (IsDisposed) return;
        _statusLabel.Text = message;
    }

    private void AppendLog(string message)
    {
        lock (_logQueue) _logQueue.Enqueue($"[{DateTime.Now:HH:mm:ss}] {message}");
    }

    private void FlushLogs()
    {
        StringBuilder? sb = null;
        lock (_logQueue)
        {
            if (_logQueue.Count == 0) return;
            sb = new StringBuilder();
            while (_logQueue.Count > 0) sb.AppendLine(_logQueue.Dequeue());
        }
        if (sb == null || IsDisposed) return;
        _logText.AppendText(sb.ToString());
        if (_logText.TextLength > 120_000)
        {
            _logText.Select(0, _logText.TextLength - 80_000);
            _logText.SelectedText = "";
        }
        _logText.SelectionStart = _logText.TextLength;
        _logText.ScrollToCaret();
    }

    private void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"\"{path}\"", UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppendLog("[error] 打开目录失败：" + ex.Message);
        }
    }

    private static string Short(string value) =>
        string.IsNullOrWhiteSpace(value) ? "-" : value[..Math.Min(8, value.Length)];

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _logTimer.Stop();
            _logTimer.Dispose();
            _cts.Cancel();
            _cts.Dispose();
        }
        base.Dispose(disposing);
    }
}
