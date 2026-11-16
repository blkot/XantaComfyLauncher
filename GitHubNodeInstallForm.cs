using System.Diagnostics;
using System.Text;
using XantaComfyLauncher.Models;
using XantaComfyLauncher.Services;
using XantaComfyLauncher.Utils;

namespace XantaComfyLauncher;

public sealed class GitHubNodeInstallForm : Form
{
    private readonly AppPaths _paths;
    private readonly CustomNodeManager _manager;
    private readonly GitHubNodeSearchService _searchService = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Queue<string> _logQueue = new();
    private readonly System.Windows.Forms.Timer _logTimer = new() { Interval = 150 };

    private TextBox _queryText = null!;
    private Button _searchButton = null!;
    private ListView _results = null!;
    private Label _statusLabel = null!;
    private ProgressBar _progressBar = null!;
    private TextBox _logText = null!;
    private Button _installButton = null!;
    private Button _closeButton = null!;
    private bool _busy;

    public GitHubNodeInstallForm(AppPaths paths, CustomNodeManager manager)
    {
        _paths = paths;
        _manager = manager;
        BuildUi();
        _logTimer.Tick += (_, _) => FlushLogs();
        _logTimer.Start();
        FormClosing += (_, _) => _cts.Cancel();
    }

    private void BuildUi()
    {
        Text = "从 GitHub 安装节点";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(960, 640);
        MinimumSize = new Size(760, 500);
        Font = new Font("Segoe UI", 9F);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(245, 246, 248);
        Icon = AppIcon.Load();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(10)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var searchLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 3,
            RowCount = 2,
            Padding = new Padding(2)
        };
        searchLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        searchLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        searchLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        searchLayout.Controls.Add(new Label
        {
            Text = "GitHub URL / author/repo / 关键词",
            AutoSize = true,
            Anchor = AnchorStyles.Left
        }, 0, 0);
        _queryText = new TextBox
        {
            Dock = DockStyle.Fill,
            PlaceholderText = "例如 https://github.com/Comfy-Org/ComfyUI 或 ComfyUI-KJNodes"
        };
        _queryText.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                _ = SearchAsync();
            }
        };
        searchLayout.Controls.Add(_queryText, 1, 0);
        _searchButton = new Button { Text = "搜索 GitHub", Width = 112, Height = 28 };
        _searchButton.Click += async (_, _) => await SearchAsync();
        searchLayout.Controls.Add(_searchButton, 2, 0);
        var hint = new Label
        {
            Text = "输入完整 GitHub URL 会直接定位仓库；输入作者、仓库名或关键词会显示 GitHub 搜索结果。",
            AutoSize = true,
            ForeColor = Color.FromArgb(90, 95, 105),
            Padding = new Padding(0, 8, 0, 0)
        };
        searchLayout.Controls.Add(hint, 0, 1);
        searchLayout.SetColumnSpan(hint, 3);
        root.Controls.Add(searchLayout, 0, 0);

        _results = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            HideSelection = false,
            MultiSelect = false
        };
        _results.Columns.Add("GitHub 仓库", 260);
        _results.Columns.Add("默认分支", 100);
        _results.Columns.Add("Stars", 70);
        _results.Columns.Add("说明", 470);
        _results.SelectedIndexChanged += (_, _) => UpdateInstallButton();
        _results.DoubleClick += (_, _) => OpenSelectedRepository();
        root.Controls.Add(_results, 0, 1);

        var logGroup = new GroupBox { Text = "安装日志", Dock = DockStyle.Fill, Padding = new Padding(8) };
        var logLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        logLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        logLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var statusFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        _statusLabel = new Label { Text = "就绪", AutoSize = true, Padding = new Padding(2, 8, 12, 0) };
        _progressBar = new ProgressBar { Visible = false, Style = ProgressBarStyle.Marquee, Width = 160, Height = 20 };
        statusFlow.Controls.Add(_statusLabel);
        statusFlow.Controls.Add(_progressBar);
        _logText = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9F),
            BackColor = Color.FromArgb(24, 26, 30),
            ForeColor = Color.FromArgb(220, 224, 230),
            BorderStyle = BorderStyle.FixedSingle
        };
        logLayout.Controls.Add(statusFlow, 0, 0);
        logLayout.Controls.Add(_logText, 0, 1);
        logGroup.Controls.Add(logLayout);
        root.Controls.Add(logGroup, 0, 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        _closeButton = new Button { Text = "关闭", Width = 82, Height = 30 };
        _installButton = new Button { Text = "安装选中节点", Width = 122, Height = 30, Enabled = false };
        _closeButton.Click += (_, _) => Close();
        _installButton.Click += async (_, _) => await InstallSelectedAsync();
        buttons.Controls.Add(_closeButton);
        buttons.Controls.Add(_installButton);
        root.Controls.Add(buttons, 0, 3);

        Controls.Add(root);
    }

    private async Task SearchAsync()
    {
        if (_busy) return;
        var query = _queryText.Text.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            MessageBox.Show(this, "请输入 GitHub URL、author/repo 或关键词。", "搜索 GitHub",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SetBusy(true);
        SetStatus("正在搜索 GitHub...");
        try
        {
            var results = await _searchService.SearchAsync(query, _cts.Token);
            _results.BeginUpdate();
            try
            {
                _results.Items.Clear();
                foreach (var repo in results)
                {
                    var item = new ListViewItem(repo.FullName) { Tag = repo };
                    item.SubItems.Add(repo.DefaultBranch);
                    item.SubItems.Add(repo.StargazersCount.ToString());
                    item.SubItems.Add(repo.DisplayDescription);
                    _results.Items.Add(item);
                }
            }
            finally
            {
                _results.EndUpdate();
            }
            SetStatus(results.Count == 0 ? "没有找到匹配的 GitHub 仓库" : $"找到 {results.Count} 个仓库，请选择后安装");
            AppendLog(results.Count == 0 ? "搜索完成：无结果" : $"搜索完成：{results.Count} 个仓库");
        }
        catch (OperationCanceledException)
        {
            SetStatus("搜索已取消");
        }
        catch (Exception ex)
        {
            AppendLog("[error] " + ex.Message);
            SetStatus("搜索失败");
            MessageBox.Show(this, ex.Message, "搜索 GitHub 失败",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task InstallSelectedAsync()
    {
        if (_busy || _results.SelectedItems.Count == 0 ||
            _results.SelectedItems[0].Tag is not GitHubRepositoryInfo repo)
        {
            return;
        }

        var answer = MessageBox.Show(this,
            $"安装 {repo.FullName}？\r\n\r\n" +
            $"仓库：{repo.CloneUrl}\r\n\r\n" +
            "安装过程会执行 git clone，并在存在 requirements.txt 时执行 pip install。",
            "安装节点", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes) return;

        SetBusy(true);
        try
        {
            var progress = new Progress<string>(message =>
            {
                SetStatus(message);
                AppendLog(message);
            });
            var manifest = await _manager.LoadOrCreateManifestAsync(progress, _cts.Token);
            var existing = manifest.Nodes.FirstOrDefault(n =>
                GitHubNodeSearchService.RepositoryUrlsEqual(n.Repository, repo.CloneUrl))
                ?? manifest.Nodes.FirstOrDefault(n =>
                    string.Equals(n.Name, repo.Name, StringComparison.OrdinalIgnoreCase));

            CustomNodeState state;
            if (existing != null)
            {
                if (!GitHubNodeSearchService.RepositoryUrlsEqual(existing.Repository, repo.CloneUrl))
                {
                    throw new InvalidOperationException(
                        $"节点目录名 {repo.Name} 已被另一个仓库占用：\r\n{existing.Repository}");
                }

                var update = MessageBox.Show(this,
                    $"{repo.FullName} 已经在节点清单中，是否更新到默认分支 {repo.DefaultBranch}？",
                    "节点已存在", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (update != DialogResult.Yes) return;

                existing.Branch = string.IsNullOrWhiteSpace(existing.Branch)
                    ? repo.DefaultBranch
                    : existing.Branch;
                state = await _manager.InstallOrUpdateAsync(existing, progress, _cts.Token);
            }
            else
            {
                var definition = new CustomNodeDefinition
                {
                    Name = repo.Name,
                    Repository = repo.CloneUrl,
                    Branch = repo.DefaultBranch,
                    RequirementsFile = null,
                    Enabled = true,
                    UpdatePolicy = "notify"
                };
                state = await _manager.InstallOrUpdateAsync(definition, progress, _cts.Token);
            }

            AppendLog($"[ok] 已安装 {state.Definition.Name} @ {Short(state.InstalledCommit)}");
            SetStatus("安装完成，请重启 ComfyUI 使节点生效。");
            MessageBox.Show(this,
                $"已安装 {repo.FullName}。\r\n\r\n请重启 ComfyUI 使新节点生效。",
                "安装完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (OperationCanceledException)
        {
            SetStatus("安装已取消");
        }
        catch (Exception ex)
        {
            AppendLog("[error] " + ex.Message);
            SetStatus("安装失败");
            MessageBox.Show(this, ex.Message, "安装节点失败",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OpenSelectedRepository()
    {
        if (_results.SelectedItems.Count == 0 ||
            _results.SelectedItems[0].Tag is not GitHubRepositoryInfo repo) return;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = repo.HtmlUrl, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppendLog("[error] 打开 GitHub 页面失败：" + ex.Message);
        }
    }

    private void UpdateInstallButton()
    {
        _installButton.Enabled = !_busy && _results.SelectedItems.Count > 0;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _queryText.Enabled = !busy;
        _searchButton.Enabled = !busy;
        _results.Enabled = !busy;
        _installButton.Enabled = !busy && _results.SelectedItems.Count > 0;
        _closeButton.Enabled = !busy;
        _progressBar.Visible = busy;
        UseWaitCursor = busy;
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
        _logText.SelectionStart = _logText.TextLength;
        _logText.ScrollToCaret();
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
