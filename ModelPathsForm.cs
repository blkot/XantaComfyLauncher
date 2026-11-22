using XantaComfyLauncher.Models;
using XantaComfyLauncher.Services;
using XantaComfyLauncher.Utils;

namespace XantaComfyLauncher;

public sealed class ModelPathsForm : Form
{
    private readonly AppPaths _paths;
    private readonly ModelPathManagementService _service;
    private readonly CancellationTokenSource _cts = new();
    private ListView _list = null!;
    private TextBox _yamlEditor = null!;
    private Label _statusLabel = null!;
    private Button _refreshButton = null!;
    private Button _openButton = null!;
    private Button _editYamlButton = null!;
    private Button _saveYamlButton = null!;
    private Button _cancelYamlButton = null!;
    private Button _junctionButton = null!;
    private Button _migrateButton = null!;
    private Button _closeButton = null!;
    private bool _busy;

    public ModelPathsForm(AppPaths paths)
    {
        _paths = paths;
        _service = new ModelPathManagementService(paths);
        BuildUi();
        Icon = AppIcon.Load();
        Shown += (_, _) => RefreshList();
        FormClosing += (_, _) => _cts.Cancel();
    }

    private void BuildUi()
    {
        Text = "模型路径管理";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1040, 700);
        MinimumSize = new Size(820, 540);
        Font = new Font("Segoe UI", 9F);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(245, 246, 248);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(10) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 52));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        _refreshButton = MakeButton("刷新", 76);
        _openButton = MakeButton("打开选中目录", 118);
        _editYamlButton = MakeButton("编辑 YAML", 92);
        _saveYamlButton = MakeButton("保存 YAML", 92);
        _cancelYamlButton = MakeButton("取消编辑", 86);
        _junctionButton = MakeButton("创建 Junction", 118);
        _migrateButton = MakeButton("迁移目录", 92);
        _closeButton = MakeButton("关闭", 76);
        _statusLabel = new Label { Text = "就绪", AutoSize = true, Padding = new Padding(12, 8, 0, 0) };
        toolbar.Controls.AddRange(new Control[]
        {
            _refreshButton, _openButton, _editYamlButton, _saveYamlButton, _cancelYamlButton,
            _junctionButton, _migrateButton, _closeButton, _statusLabel
        });
        root.Controls.Add(toolbar, 0, 0);

        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            MultiSelect = false,
            HideSelection = false
        };
        _list.Columns.Add("类别", 180);
        _list.Columns.Add("路径", 620);
        _list.Columns.Add("状态", 180);
        _list.DoubleClick += (_, _) => OpenSelected();
        root.Controls.Add(_list, 0, 1);

        var yamlGroup = new GroupBox { Text = "extra_model_paths.yaml", Dock = DockStyle.Fill, Padding = new Padding(8) };
        _yamlEditor = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font("Consolas", 9F),
            BackColor = Color.White
        };
        yamlGroup.Controls.Add(_yamlEditor);
        root.Controls.Add(yamlGroup, 0, 2);

        var hint = new Label
        {
            Text = "保存 YAML 前会自动备份；Junction 和目录迁移需要明确确认。跨卷迁移只复制，不删除源目录。",
            AutoSize = true,
            ForeColor = Color.FromArgb(90, 95, 105),
            Padding = new Padding(2, 8, 2, 0)
        };
        root.Controls.Add(hint, 0, 3);

        _refreshButton.Click += (_, _) => RefreshList();
        _openButton.Click += (_, _) => OpenSelected();
        _editYamlButton.Click += (_, _) => LoadYamlEditor();
        _saveYamlButton.Click += (_, _) => SaveYamlEditor();
        _cancelYamlButton.Click += (_, _) => LoadYamlEditor();
        _junctionButton.Click += async (_, _) => await CreateJunctionAsync();
        _migrateButton.Click += async (_, _) => await MigrateAsync();
        _closeButton.Click += (_, _) => Close();
        Controls.Add(root);
    }

    private static Button MakeButton(string text, int width) =>
        new() { Text = text, AutoSize = true, MinimumSize = new Size(width, 30), Margin = new Padding(3) };

    private void RefreshList()
    {
        try
        {
            var entries = _service.GetConfiguredPaths();
            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                foreach (var entry in entries)
                {
                    var row = new ListViewItem(entry.Category) { Tag = entry };
                    row.SubItems.Add(entry.Path);
                    row.SubItems.Add(entry.Detail);
                    if (!entry.Exists || !entry.Readable) row.ForeColor = Color.Firebrick;
                    _list.Items.Add(row);
                }
            }
            finally { _list.EndUpdate(); }
            _yamlEditor.Text = _service.ReadYaml();
            _statusLabel.Text = $"共 {entries.Count} 个模型路径";
        }
        catch (Exception ex)
        {
            LauncherLog.Error("model-paths", ex);
            _statusLabel.Text = "读取模型路径失败：" + ex.Message;
        }
    }

    private void LoadYamlEditor() => _yamlEditor.Text = _service.ReadYaml();

    private void SaveYamlEditor()
    {
        if (MessageBox.Show(this, "保存 extra_model_paths.yaml 吗？保存前会自动创建备份。",
                "保存 YAML", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try
        {
            _service.SaveYaml(_yamlEditor.Text);
            RefreshList();
            _statusLabel.Text = "YAML 已保存并备份";
        }
        catch (Exception ex)
        {
            LauncherLog.Error("model-paths", ex);
            MessageBox.Show(this, ex.Message, "保存 YAML 失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenSelected()
    {
        if (_list.SelectedItems.Count == 0 || _list.SelectedItems[0].Tag is not ModelPathValidation entry) return;
        try
        {
            Directory.CreateDirectory(entry.Path);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{entry.Path}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "打开目录失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task CreateJunctionAsync()
    {
        if (_busy) return;
        using var target = new FolderBrowserDialog
        {
            Description = "选择真实模型目录（Junction 目标）",
            UseDescriptionForTitle = true,
            SelectedPath = _paths.ModelsDir
        };
        if (target.ShowDialog(this) != DialogResult.OK) return;
        using var linkParent = new FolderBrowserDialog
        {
            Description = "选择 Junction 链接所在目录",
            UseDescriptionForTitle = true,
            SelectedPath = _paths.ModelsDir
        };
        if (linkParent.ShowDialog(this) != DialogResult.OK) return;

        var link = Path.Combine(linkParent.SelectedPath, Path.GetFileName(target.SelectedPath) + "-link");
        if (MessageBox.Show(this, $"创建 Junction？\r\n\r\n链接：{link}\r\n目标：{target.SelectedPath}",
                "创建 Junction", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        _busy = true;
        try
        {
            await _service.CreateJunctionAsync(link, target.SelectedPath, _cts.Token);
            _statusLabel.Text = "Junction 已创建：" + link;
            RefreshList();
        }
        catch (Exception ex)
        {
            LauncherLog.Error("model-paths", ex);
            MessageBox.Show(this, ex.Message, "创建 Junction 失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { _busy = false; }
    }

    private async Task MigrateAsync()
    {
        if (_busy) return;
        using var source = new FolderBrowserDialog
        {
            Description = "选择要迁移的模型目录",
            UseDescriptionForTitle = true,
            SelectedPath = _paths.ModelsDir
        };
        if (source.ShowDialog(this) != DialogResult.OK) return;
        using var destination = new FolderBrowserDialog
        {
            Description = "选择目标目录（必须不存在或为空）",
            UseDescriptionForTitle = true
        };
        if (destination.ShowDialog(this) != DialogResult.OK) return;
        if (MessageBox.Show(this,
                $"迁移目录？\r\n\r\n源：{source.SelectedPath}\r\n目标：{destination.SelectedPath}\r\n\r\n" +
                "同卷会移动；跨卷只复制，不删除源目录。",
                "迁移模型目录", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        _busy = true;
        try
        {
            var result = await _service.MigrateDirectoryAsync(
                source.SelectedPath, destination.SelectedPath,
                new Progress<string>(text => _statusLabel.Text = text), _cts.Token);
            MessageBox.Show(this, result, "迁移完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            RefreshList();
        }
        catch (Exception ex)
        {
            LauncherLog.Error("model-paths", ex);
            MessageBox.Show(this, ex.Message, "迁移失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { _busy = false; }
    }
}
