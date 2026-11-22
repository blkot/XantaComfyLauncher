using XantaComfyLauncher.Models;
using XantaComfyLauncher.Services;
using XantaComfyLauncher.Utils;

namespace XantaComfyLauncher;

public sealed class NodeConflictForm : Form
{
    private readonly AppPaths _paths;
    private readonly NodeConflictService _service;
    private readonly CancellationTokenSource _cts = new();
    private ListView _list = null!;
    private Label _statusLabel = null!;
    private Button _refreshButton = null!;
    private Button _openNodesButton = null!;
    private Button _closeButton = null!;
    private ProgressBar _progressBar = null!;
    private bool _scanning;

    public NodeConflictForm(AppPaths paths)
    {
        _paths = paths;
        _service = new NodeConflictService(paths);
        BuildUi();
        Icon = AppIcon.Load();
        Shown += async (_, _) => await ScanAsync();
        FormClosing += (_, _) => _cts.Cancel();
    }

    private void BuildUi()
    {
        Text = "节点冲突检测";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1000, 580);
        MinimumSize = new Size(780, 420);
        Font = new Font("Segoe UI", 9F);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(245, 246, 248);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(10)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        _refreshButton = new Button { Text = "重新扫描", AutoSize = true, MinimumSize = new Size(92, 30) };
        _openNodesButton = new Button { Text = "打开 custom_nodes", AutoSize = true, MinimumSize = new Size(132, 30) };
        _closeButton = new Button { Text = "关闭", AutoSize = true, MinimumSize = new Size(76, 30) };
        _statusLabel = new Label { Text = "就绪", AutoSize = true, Padding = new Padding(12, 8, 0, 0) };
        _progressBar = new ProgressBar
        {
            Visible = false,
            Style = ProgressBarStyle.Marquee,
            Width = 150,
            Height = 20,
            Margin = new Padding(0, 4, 0, 0)
        };
        toolbar.Controls.Add(_refreshButton);
        toolbar.Controls.Add(_openNodesButton);
        toolbar.Controls.Add(_closeButton);
        toolbar.Controls.Add(_statusLabel);
        toolbar.Controls.Add(_progressBar);
        root.Controls.Add(toolbar, 0, 0);

        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            HideSelection = false,
            MultiSelect = false
        };
        _list.Columns.Add("类型", 110);
        _list.Columns.Add("节点类型", 160);
        _list.Columns.Add("插件 A", 180);
        _list.Columns.Add("插件 B", 180);
        _list.Columns.Add("详情", 330);
        root.Controls.Add(_list, 0, 1);

        var hint = new Label
        {
            Text = "检测范围：重复插件目录、重复 NODE_CLASS_MAPPINGS 节点类型、pip check 依赖问题。扫描只读取文件，不加载插件。",
            AutoSize = true,
            ForeColor = Color.FromArgb(90, 95, 105),
            Padding = new Padding(2, 8, 2, 0)
        };
        root.Controls.Add(hint, 0, 2);

        _refreshButton.Click += async (_, _) => await ScanAsync();
        _openNodesButton.Click += (_, _) => OpenFolder(_paths.NodesDir);
        _closeButton.Click += (_, _) => Close();
        Controls.Add(root);
    }

    private async Task ScanAsync()
    {
        if (_scanning) return;
        _scanning = true;
        _refreshButton.Enabled = false;
        _progressBar.Visible = true;
        _statusLabel.Text = "正在扫描...";
        try
        {
            var conflicts = await _service.ScanAsync(new Progress<string>(text => _statusLabel.Text = text), _cts.Token);
            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                foreach (var conflict in conflicts)
                {
                    var row = new ListViewItem(conflict.Type) { Tag = conflict };
                    row.SubItems.Add(conflict.NodeType);
                    row.SubItems.Add(conflict.PackageA);
                    row.SubItems.Add(conflict.PackageB);
                    row.SubItems.Add(conflict.Details);
                    if (conflict.Type.Contains("失败", StringComparison.Ordinal) ||
                        conflict.Type.Contains("依赖", StringComparison.Ordinal))
                    {
                        row.ForeColor = Color.Firebrick;
                    }
                    _list.Items.Add(row);
                }
            }
            finally { _list.EndUpdate(); }
            _statusLabel.Text = conflicts.Count == 0 ? "未发现冲突" : $"发现 {conflicts.Count} 个问题";
        }
        catch (OperationCanceledException) { _statusLabel.Text = "扫描已取消"; }
        catch (Exception ex)
        {
            LauncherLog.Error("node-conflict", ex);
            _statusLabel.Text = "扫描失败：" + ex.Message;
        }
        finally
        {
            _scanning = false;
            _refreshButton.Enabled = true;
            _progressBar.Visible = false;
        }
    }

    private void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "打开目录失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
