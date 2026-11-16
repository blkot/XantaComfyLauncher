using System.Text;
using XantaComfyLauncher.Models;
using XantaComfyLauncher.Services;
using XantaComfyLauncher.Utils;

namespace XantaComfyLauncher;

public sealed class WorkflowProfilesForm : Form
{
    private readonly AppPaths _paths;
    private readonly WorkflowProfileService _service;
    private readonly string _baseUrl;
    private readonly CancellationTokenSource _cts = new();

    private ComboBox _workflowCombo = null!;
    private ListView _profilesList = null!;
    private TextBox _detailsText = null!;
    private CheckedListBox _alwaysEnabledList = null!;
    private Button _refreshWorkflowsButton = null!;
    private Button _analyzeButton = null!;
    private Button _refreshIndexButton = null!;
    private Button _setActiveButton = null!;
    private Button _allModeButton = null!;
    private Button _deleteButton = null!;
    private Button _closeButton = null!;
    private Label _statusLabel = null!;
    private ProgressBar _progressBar = null!;
    private WorkflowProfileCatalog _catalog = new();
    private bool _busy;
    private bool _loadingAlwaysEnabled;

    public WorkflowProfilesForm(AppPaths paths, string baseUrl)
    {
        _paths = paths;
        _baseUrl = baseUrl;
        _service = new WorkflowProfileService(paths);
        BuildUi();
        Icon = AppIcon.Load();
        Shown += async (_, _) => await ReloadAsync();
        FormClosing += (_, _) => _cts.Cancel();
    }

    private void BuildUi()
    {
        Text = "工作流节点加载配置";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1040, 700);
        MinimumSize = new Size(860, 580);
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
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 52));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var workflowBar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 4,
            RowCount = 1
        };
        workflowBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        workflowBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        workflowBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        workflowBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        workflowBar.Controls.Add(new Label { Text = "工作流", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        _workflowCombo = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            IntegralHeight = false,
            DropDownHeight = 300
        };
        workflowBar.Controls.Add(_workflowCombo, 1, 0);
        _refreshWorkflowsButton = new Button { Text = "刷新列表", AutoSize = true, MinimumSize = new Size(88, 30) };
        _analyzeButton = new Button { Text = "分析并保存", AutoSize = true, MinimumSize = new Size(104, 30) };
        workflowBar.Controls.Add(_refreshWorkflowsButton, 2, 0);
        workflowBar.Controls.Add(_analyzeButton, 3, 0);
        root.Controls.Add(workflowBar, 0, 0);

        _profilesList = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            MultiSelect = false,
            HideSelection = false
        };
        _profilesList.Columns.Add("工作流 Profile", 210);
        _profilesList.Columns.Add("所需包", 70);
        _profilesList.Columns.Add("未解析", 70);
        _profilesList.Columns.Add("状态", 110);
        _profilesList.Columns.Add("更新时间", 140);
        _profilesList.Columns.Add("工作流路径", 360);
        _profilesList.SelectedIndexChanged += (_, _) => ShowSelectedProfile();
        root.Controls.Add(_profilesList, 0, 1);

        var bottomSplit = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1
        };
        bottomSplit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        bottomSplit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        var detailsGroup = new GroupBox { Text = "Profile 详情", Dock = DockStyle.Fill, Padding = new Padding(8) };
        _detailsText = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9F),
            BackColor = Color.White
        };
        detailsGroup.Controls.Add(_detailsText);
        bottomSplit.Controls.Add(detailsGroup, 0, 0);

        var alwaysGroup = new GroupBox
        {
            Text = "始终启用的插件包（UI/Manager/前端扩展等）",
            Dock = DockStyle.Fill,
            Padding = new Padding(8)
        };
        _alwaysEnabledList = new CheckedListBox
        {
            Dock = DockStyle.Fill,
            CheckOnClick = true,
            IntegralHeight = false
        };
        _alwaysEnabledList.ItemCheck += (_, e) =>
        {
            if (_loadingAlwaysEnabled) return;
            BeginInvoke(() => SaveAlwaysEnabledPackages());
        };
        alwaysGroup.Controls.Add(_alwaysEnabledList);
        bottomSplit.Controls.Add(alwaysGroup, 1, 0);
        root.Controls.Add(bottomSplit, 0, 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true
        };
        _refreshIndexButton = new Button { Text = "刷新节点索引（需全部加载模式运行）", AutoSize = true, MinimumSize = new Size(232, 30) };
        _setActiveButton = new Button { Text = "设为启动 Profile", AutoSize = true, MinimumSize = new Size(118, 30) };
        _allModeButton = new Button { Text = "启动时全部加载", AutoSize = true, MinimumSize = new Size(118, 30) };
        _deleteButton = new Button { Text = "删除 Profile", AutoSize = true, MinimumSize = new Size(98, 30) };
        _closeButton = new Button { Text = "关闭", AutoSize = true, MinimumSize = new Size(76, 30) };
        _statusLabel = new Label { Text = "就绪", AutoSize = true, Padding = new Padding(10, 8, 8, 0) };
        _progressBar = new ProgressBar { Visible = false, Style = ProgressBarStyle.Marquee, Width = 150, Height = 20, Margin = new Padding(0, 4, 0, 0) };
        buttons.Controls.Add(_refreshIndexButton);
        buttons.Controls.Add(_setActiveButton);
        buttons.Controls.Add(_allModeButton);
        buttons.Controls.Add(_deleteButton);
        buttons.Controls.Add(_closeButton);
        buttons.Controls.Add(_statusLabel);
        buttons.Controls.Add(_progressBar);
        root.Controls.Add(buttons, 0, 3);

        _refreshWorkflowsButton.Click += (_, _) => ReloadWorkflowFiles();
        _analyzeButton.Click += async (_, _) => await AnalyzeSelectedWorkflowAsync();
        _refreshIndexButton.Click += async (_, _) => await RefreshIndexAsync();
        _setActiveButton.Click += (_, _) => SetSelectedProfileAsActive();
        _allModeButton.Click += (_, _) => SetAllMode();
        _deleteButton.Click += (_, _) => DeleteSelectedProfile();
        _closeButton.Click += (_, _) => Close();

        Controls.Add(root);
    }

    private async Task ReloadAsync()
    {
        _catalog = _service.LoadCatalog();
        ReloadWorkflowFiles();
        try
        {
            var packages = await _service.GetInstalledPackageNamesAsync(
                new Progress<string>(SetStatus), _cts.Token);
            _loadingAlwaysEnabled = true;
            _alwaysEnabledList.Items.Clear();
            foreach (var package in packages) _alwaysEnabledList.Items.Add(package);
            for (var i = 0; i < _alwaysEnabledList.Items.Count; i++)
            {
                var name = _alwaysEnabledList.Items[i]?.ToString() ?? "";
                _alwaysEnabledList.SetItemChecked(i,
                    _catalog.AlwaysEnabledPackages.Contains(name, StringComparer.OrdinalIgnoreCase));
            }
        }
        catch (Exception ex)
        {
            LauncherLog.Error("workflow-profiles", ex);
            SetStatus("读取已安装插件失败");
        }
        finally
        {
            _loadingAlwaysEnabled = false;
        }
        PopulateProfiles();
        UpdateStatusSummary();
    }

    private void ReloadWorkflowFiles()
    {
        var selected = _workflowCombo.SelectedItem?.ToString();
        _workflowCombo.Items.Clear();
        foreach (var file in _service.GetWorkflowFiles())
        {
            _workflowCombo.Items.Add(new WorkflowItem(file,
                Path.GetRelativePath(_paths.InstanceRoot, file)));
        }
        if (_workflowCombo.Items.Count == 0)
        {
            SetStatus("未找到工作流文件");
            return;
        }
        _workflowCombo.SelectedIndex = selected == null ? 0 : 0;
        SetStatus($"已找到 {_workflowCombo.Items.Count} 个工作流文件");
    }

    private async Task AnalyzeSelectedWorkflowAsync()
    {
        if (_busy) return;
        if (_workflowCombo.SelectedItem is not WorkflowItem item)
        {
            MessageBox.Show(this, "请先选择工作流。", "工作流分析",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SetBusy(true);
        try
        {
            var progress = new Progress<string>(SetStatus);
            var profile = await _service.AnalyzeAndSaveAsync(item.FullPath, progress, _cts.Token);
            LauncherLog.Write("workflow-profiles",
                $"分析完成：{profile.Name}; packages={string.Join(",", profile.RequiredPackages)}; " +
                $"unresolved={profile.UnresolvedNodeTypes.Count + profile.UnresolvedCnrIds.Count}");
            _catalog = _service.LoadCatalog();
            PopulateProfiles(profile.Id);
            SetStatus($"已分析 {profile.Name}：{profile.RequiredPackages.Count} 个包，" +
                      $"{profile.UnresolvedNodeTypes.Count} 个未解析节点");
        }
        catch (OperationCanceledException)
        {
            SetStatus("操作已取消");
        }
        catch (Exception ex)
        {
            LauncherLog.Error("workflow-profiles", ex);
            SetStatus("分析工作流失败");
            MessageBox.Show(this, ex.Message, "分析工作流失败",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RefreshIndexAsync()
    {
        if (_busy) return;
        SetBusy(true);
        try
        {
            var progress = new Progress<string>(SetStatus);
            var map = await _service.RefreshNodeIndexAsync(_baseUrl, progress, _cts.Token);
            LauncherLog.Write("workflow-profiles",
                $"节点索引刷新：nodes={map.NodePackages.Count}; aliases={map.CnrIdPackages.Count}; source={_baseUrl}");
            SetStatus($"节点索引已刷新：{map.NodePackages.Count} 个后端节点，" +
                      $"{map.CnrIdPackages.Count} 个包别名");
            MessageBox.Show(this,
                "节点索引已刷新。请重新分析工作流以更新 Profile。\r\n\r\n" +
                "为获得完整索引，刷新时请先使用“启动时全部加载”模式启动 ComfyUI。" +
                "索引来源：ComfyUI /object_info。",
                "节点索引", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (OperationCanceledException)
        {
            SetStatus("操作已取消");
        }
        catch (Exception ex)
        {
            LauncherLog.Error("workflow-profiles", ex);
            SetStatus("刷新节点索引失败，请确认 ComfyUI 正在运行");
            MessageBox.Show(this,
                "刷新节点索引失败。请先用“全部加载”模式启动 ComfyUI，再重试。\r\n\r\n" + ex.Message,
                "刷新节点索引失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void PopulateProfiles(string? selectId = null)
    {
        var selected = selectId ?? (_profilesList.SelectedItems.Count > 0
            ? (_profilesList.SelectedItems[0].Tag as WorkflowProfile)?.Id
            : null);
        ListViewItem? selectedRow = null;
        _profilesList.BeginUpdate();
        try
        {
            _profilesList.Items.Clear();
            foreach (var profile in _catalog.Profiles.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                var row = new ListViewItem(profile.Name) { Tag = profile };
                row.SubItems.Add(profile.RequiredPackages.Count.ToString());
                row.SubItems.Add((profile.UnresolvedNodeTypes.Count + profile.UnresolvedCnrIds.Count).ToString());
                var changed = _service.IsWorkflowChanged(profile);
                row.SubItems.Add(!File.Exists(profile.WorkflowPath) ? "文件缺失" : changed ? "工作流已变化" : "有效");
                row.SubItems.Add(profile.UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
                row.SubItems.Add(profile.WorkflowPath);
                if (changed)
                {
                    row.ForeColor = Color.FromArgb(180, 90, 20);
                }
                if (string.Equals(profile.Id, _catalog.ActiveProfileId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(_catalog.LoadMode, CustomNodeLoadModes.Profile, StringComparison.OrdinalIgnoreCase))
                {
                    row.ForeColor = Color.FromArgb(32, 145, 74);
                    row.Font = new Font(_profilesList.Font, FontStyle.Bold);
                }
                _profilesList.Items.Add(row);
                if (selected != null && string.Equals(profile.Id, selected, StringComparison.OrdinalIgnoreCase))
                    selectedRow = row;
            }
        }
        finally
        {
            _profilesList.EndUpdate();
        }
        if (selectedRow != null)
        {
            selectedRow.Selected = true;
            _profilesList.EnsureVisible(selectedRow.Index);
        }
        else if (_profilesList.Items.Count > 0)
        {
            _profilesList.Items[0].Selected = true;
            _profilesList.EnsureVisible(0);
        }
        ShowSelectedProfile();
    }

    private void ShowSelectedProfile()
    {
        if (_profilesList.SelectedItems.Count == 0 || _profilesList.SelectedItems[0].Tag is not WorkflowProfile p)
        {
            _detailsText.Text = "";
            UpdateActionState();
            return;
        }
        var sb = new StringBuilder();
        sb.AppendLine("Profile：" + p.Name);
        sb.AppendLine("工作流：" + p.WorkflowPath);
        sb.AppendLine("Hash：" + p.WorkflowHash);
        sb.AppendLine("更新时间：" + p.UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine();
        sb.AppendLine("所需插件包：");
        foreach (var package in p.RequiredPackages) sb.AppendLine("  " + package);
        sb.AppendLine();
        sb.AppendLine("未解析节点类型：");
        foreach (var type in p.UnresolvedNodeTypes) sb.AppendLine("  " + type);
        sb.AppendLine();
        sb.AppendLine("未解析 CNR ID：");
        foreach (var id in p.UnresolvedCnrIds) sb.AppendLine("  " + id);
        _detailsText.Text = sb.ToString();
        UpdateActionState();
    }

    private void UpdateActionState()
    {
        var hasProfile = _profilesList.SelectedItems.Count > 0;
        _setActiveButton.Enabled = !_busy && hasProfile;
        _deleteButton.Enabled = !_busy && hasProfile;
    }

    private void SetSelectedProfileAsActive()
    {
        if (_profilesList.SelectedItems.Count == 0 ||
            _profilesList.SelectedItems[0].Tag is not WorkflowProfile profile) return;
        _catalog.LoadMode = CustomNodeLoadModes.Profile;
        _catalog.ActiveProfileId = profile.Id;
        _service.SaveCatalog(_catalog);
        LauncherLog.Write("workflow-profiles", "设置启动 Profile：" + profile.Name);
        PopulateProfiles(profile.Id);
        SetStatus($"启动配置：按工作流加载 {profile.Name}");
    }

    private void SetAllMode()
    {
        _catalog.LoadMode = CustomNodeLoadModes.All;
        _catalog.ActiveProfileId = "";
        _service.SaveCatalog(_catalog);
        LauncherLog.Write("workflow-profiles", "启动配置切换为全部加载");
        PopulateProfiles();
        SetStatus("启动配置：全部加载 custom_nodes");
    }

    private void DeleteSelectedProfile()
    {
        if (_profilesList.SelectedItems.Count == 0 ||
            _profilesList.SelectedItems[0].Tag is not WorkflowProfile profile) return;
        if (MessageBox.Show(this, "删除这个工作流 Profile 吗？", "删除 Profile",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        _catalog.Profiles.RemoveAll(p => string.Equals(p.Id, profile.Id, StringComparison.OrdinalIgnoreCase));
        if (string.Equals(_catalog.ActiveProfileId, profile.Id, StringComparison.OrdinalIgnoreCase))
        {
            _catalog.ActiveProfileId = "";
            _catalog.LoadMode = CustomNodeLoadModes.All;
        }
        _service.SaveCatalog(_catalog);
        LauncherLog.Write("workflow-profiles", "删除 Profile：" + profile.Name);
        PopulateProfiles();
        SetStatus("Profile 已删除");
    }

    private void SaveAlwaysEnabledPackages()
    {
        _catalog.AlwaysEnabledPackages = _alwaysEnabledList.CheckedItems
            .Cast<object>()
            .Select(item => item?.ToString() ?? "")
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _service.SaveCatalog(_catalog);
        SetStatus($"始终启用插件：{_catalog.AlwaysEnabledPackages.Count} 个");
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _progressBar.Visible = busy;
        _workflowCombo.Enabled = !busy;
        _profilesList.Enabled = !busy;
        _alwaysEnabledList.Enabled = !busy;
        _refreshWorkflowsButton.Enabled = !busy;
        _analyzeButton.Enabled = !busy;
        _refreshIndexButton.Enabled = !busy;
        _allModeButton.Enabled = !busy;
        _closeButton.Enabled = !busy;
        UpdateActionState();
    }

    private void SetStatus(string message)
    {
        if (IsDisposed) return;
        _statusLabel.Text = message;
    }

    private void UpdateStatusSummary()
    {
        var active = _catalog.Profiles.FirstOrDefault(p =>
            string.Equals(p.Id, _catalog.ActiveProfileId, StringComparison.OrdinalIgnoreCase));
        SetStatus(string.Equals(_catalog.LoadMode, CustomNodeLoadModes.Profile, StringComparison.OrdinalIgnoreCase)
            ? $"当前启动配置：按工作流 {active?.Name ?? "（未选择）"}"
            : "当前启动配置：全部加载");
    }

    private sealed record WorkflowItem(string FullPath, string DisplayPath)
    {
        public override string ToString() => DisplayPath;
    }
}
