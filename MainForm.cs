using System.Diagnostics;
using System.Text;
using XantaComfyLauncher.Models;
using XantaComfyLauncher.Services;
using XantaComfyLauncher.Utils;
using Velopack;

namespace XantaComfyLauncher;

public sealed class MainForm : Form
{
    private AppPaths? _paths;
    private readonly LauncherConfigService _configService;
    private GitService? _git;
    private EnvironmentProbeService? _probe;
    private ComfyVersionService? _versionService;
    private ComfyVersionSwitcher? _switcher;
    private ComfyProcessService? _process;
    private readonly CancellationTokenSource _lifetimeCts = new();

    private readonly Queue<string> _logQueue = new();
    private readonly System.Windows.Forms.Timer _logTimer = new() { Interval = 150 };

    private LauncherConfig _config;
    private IReadOnlyList<ComfyVersionInfo> _versions = Array.Empty<ComfyVersionInfo>();
    private bool _busy;
    private bool _allowClose;
    private bool _readyAnnounced;
    private bool _hasRollbackState;
    private LauncherUpdateService _launcherUpdateService = null!;
    private UpdateInfo? _pendingLauncherUpdate;
    private string? _pendingLauncherVersion;

    private TextBox _instancePathText = null!;
    private Button _browseInstanceButton = null!;
    private Button _applyInstanceButton = null!;
    private RadioButton _localRadio = null!;
    private RadioButton _lanRadio = null!;
    private NumericUpDown _portNumeric = null!;
    private CheckBox _sageCheck = null!;
    private CheckBox _fp16AccumulationCheck = null!;
    private CheckBox _autoOpenCheck = null!;
    private CheckBox _updateDepsCheck = null!;
    private CheckBox _prereleaseCheck = null!;
    private TextBox _extraArgsText = null!;
    private Label _lanLabel = null!;
    private Button _startButton = null!;
    private Button _stopButton = null!;
    private Button _openBrowserButton = null!;
    private Button _queueButton = null!;
    private Button _customNodesButton = null!;
    private FlowLayoutPanel _directoryButtonsPanel = null!;
    private Button _refreshVersionsButton = null!;
    private Button _switchVersionButton = null!;
    private Button _rollbackButton = null!;
    private ComboBox _versionCombo = null!;
    private Label _currentVersionLabel = null!;
    private Label _versionUpdateLabel = null!;
    private Label _versionStatusLabel = null!;
    private TextBox _versionDetailsText = null!;
    private RichTextBox _logText = null!;

    private Label _launcherVersionLabel = null!;
    private Label _launcherStatusLabel = null!;
    private Button _checkLauncherUpdateButton = null!;
    private Button _installLauncherUpdateButton = null!;
    private Button _skipLauncherUpdateButton = null!;
    private ComboBox _launcherChannelCombo = null!;
    private CheckBox _launcherAutoCheck = null!;

    private Label _comfyLabel = null!;
    private Label _commitLabel = null!;
    private Label _pythonLabel = null!;
    private Label _torchLabel = null!;
    private Label _sageVersionLabel = null!;
    private Label _tritonLabel = null!;
    private Label _gpuLabel = null!;
    private ToolStripStatusLabel _statusLabel = null!;
    private ToolStripProgressBar _progressBar = null!;

    public MainForm()
    {
        _configService = new LauncherConfigService();
        _config = _configService.LoadOrCreate();
        _launcherUpdateService = new LauncherUpdateService(() => _config);

        BuildUi();
        Icon = AppIcon.Load();
        UpdateLauncherVersionLabel();
        ApplyConfig();
        WireEvents();

        _logTimer.Tick += (_, _) => FlushLogs();
        _logTimer.Start();
        Shown += OnShownAsync;
        FormClosing += OnFormClosingAsync;
    }

    private void BuildUi()
    {
        SuspendLayout();
        Text = "XantaComfyLauncher";
        StartPosition = FormStartPosition.Manual;
        var work = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1100, 760);
        var width = Math.Min(1100, Math.Max(640, work.Width - 40));
        var height = Math.Min(760, Math.Max(480, work.Height - 40));
        ClientSize = new Size(width, height);
        MinimumSize = new Size(Math.Min(900, width), Math.Min(620, height));
        Location = new Point(80, 60);
        Font = new Font("Segoe UI", 9F);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(245, 246, 248);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(10, 8, 10, 8)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 260));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var top = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = false
        };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        top.Controls.Add(BuildRunGroup(), 0, 0);
        top.Controls.Add(BuildInfoGroup(), 1, 0);
        root.Controls.Add(BuildInstanceGroup(), 0, 0);
        root.Controls.Add(top, 0, 1);
        root.Controls.Add(BuildVersionGroup(), 0, 2);
        root.Controls.Add(BuildLogGroup(), 0, 3);

        var statusStrip = new StatusStrip { SizingGrip = false };
        _statusLabel = new ToolStripStatusLabel("就绪") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        _progressBar = new ToolStripProgressBar { Visible = false, Style = ProgressBarStyle.Marquee, Width = 110 };
        statusStrip.Items.Add(_statusLabel);
        statusStrip.Items.Add(_progressBar);

        Controls.Add(root);
        Controls.Add(statusStrip);
        ResumeLayout(true);
    }

    private Control BuildInstanceGroup()
    {
        var group = new GroupBox { Text = "启动器与 ComfyUI 实例", Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(10) };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 4,
            RowCount = 2,
            Padding = new Padding(4)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layout.Controls.Add(new Label { Text = "实例路径", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        _instancePathText = new TextBox { Dock = DockStyle.Fill, PlaceholderText = @"例如 H:\Work\...\ComfyUI_261114" };
        _browseInstanceButton = new Button { Text = "浏览...", Width = 82, Height = 28 };
        _applyInstanceButton = new Button { Text = "应用路径", Width = 92, Height = 28 };
        layout.Controls.Add(_instancePathText, 1, 0);
        layout.Controls.Add(_browseInstanceButton, 2, 0);
        layout.Controls.Add(_applyInstanceButton, 3, 0);

        var updateBar = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            WrapContents = false,
            Margin = new Padding(0, 6, 0, 0)
        };
        _launcherVersionLabel = new Label
        {
            Text = "启动器版本：读取中...",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 7, 12, 0)
        };
        _checkLauncherUpdateButton = new Button { Text = "检查启动器更新", Width = 126, Height = 28 };
        _installLauncherUpdateButton = new Button { Text = "更新", Width = 112, Height = 28, Visible = false };
        _skipLauncherUpdateButton = new Button { Text = "跳过此版本", Width = 94, Height = 28, Visible = false };
        var channelLabel = new Label
        {
            Text = "通道",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(10, 7, 2, 0)
        };
        _launcherChannelCombo = new ComboBox
        {
            Width = 82,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(0, 3, 0, 0)
        };
        _launcherChannelCombo.Items.AddRange(new object[] { "stable", "beta" });
        _launcherAutoCheck = new CheckBox
        {
            Text = "启动时检查",
            AutoSize = true,
            Margin = new Padding(10, 7, 0, 0)
        };
        _launcherStatusLabel = new Label
        {
            Text = "",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            ForeColor = Color.FromArgb(90, 95, 105),
            Margin = new Padding(12, 7, 0, 0)
        };
        updateBar.Controls.Add(_launcherVersionLabel);
        updateBar.Controls.Add(_checkLauncherUpdateButton);
        updateBar.Controls.Add(_installLauncherUpdateButton);
        updateBar.Controls.Add(_skipLauncherUpdateButton);
        updateBar.Controls.Add(channelLabel);
        updateBar.Controls.Add(_launcherChannelCombo);
        updateBar.Controls.Add(_launcherAutoCheck);
        updateBar.Controls.Add(_launcherStatusLabel);
        layout.Controls.Add(updateBar, 0, 1);
        layout.SetColumnSpan(updateBar, 4);

        group.Controls.Add(layout);
        return group;
    }

    private Control BuildRunGroup()
    {
        var group = new GroupBox { Text = "启动设置", Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(10) };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 4,
            RowCount = 7,
            Padding = new Padding(4)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (var i = 0; i < 7; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var modeFlow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        _localRadio = new RadioButton { Text = "仅本机 127.0.0.1", AutoSize = true, Checked = true };
        _lanRadio = new RadioButton { Text = "局域网 0.0.0.0", AutoSize = true };
        modeFlow.Controls.Add(_localRadio);
        modeFlow.Controls.Add(_lanRadio);

        layout.Controls.Add(new Label { Text = "监听模式", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        layout.Controls.Add(modeFlow, 1, 0);
        layout.SetColumnSpan(modeFlow, 3);

        layout.Controls.Add(new Label { Text = "端口", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        _portNumeric = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = 8188, Width = 100 };
        layout.Controls.Add(_portNumeric, 1, 1);

        layout.Controls.Add(new Label { Text = "额外参数", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        _extraArgsText = new TextBox { Dock = DockStyle.Fill, PlaceholderText = "可选，例如 --fast fp16_accumulation" };
        layout.Controls.Add(_extraArgsText, 1, 2);
        layout.SetColumnSpan(_extraArgsText, 3);

        layout.Controls.Add(new Label { Text = "加速", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 3);
        var accelerationFlow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        _sageCheck = new CheckBox { Text = "SageAttention", AutoSize = true };
        _fp16AccumulationCheck = new CheckBox { Text = "FP16 累加加速", AutoSize = true };
        accelerationFlow.Controls.Add(_sageCheck);
        accelerationFlow.Controls.Add(_fp16AccumulationCheck);
        layout.Controls.Add(accelerationFlow, 1, 3);
        layout.SetColumnSpan(accelerationFlow, 3);

        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        _startButton = new Button { Text = "启动", Width = 92, Height = 32 };
        _stopButton = new Button { Text = "停止", Width = 92, Height = 32, Enabled = false };
        _openBrowserButton = new Button { Text = "打开界面", Width = 100, Height = 32, Enabled = false };
        _queueButton = new Button { Text = "队列管理", Width = 100, Height = 32, Enabled = false };
        _customNodesButton = new Button { Text = "节点管理", Width = 100, Height = 32 };
        _autoOpenCheck = new CheckBox { Text = "自动打开浏览器", AutoSize = true, Checked = true, Margin = new Padding(12, 8, 0, 0) };
        buttons.Controls.Add(_startButton);
        buttons.Controls.Add(_stopButton);
        buttons.Controls.Add(_openBrowserButton);
        buttons.Controls.Add(_queueButton);
        buttons.Controls.Add(_customNodesButton);
        buttons.Controls.Add(_autoOpenCheck);
        var toolTip = new ToolTip();
        toolTip.SetToolTip(_sageCheck, "ComfyUI 参数：--use-sage-attention");
        toolTip.SetToolTip(_fp16AccumulationCheck,
            "ComfyUI 参数：--fast fp16_accumulation。实验性加速，可能影响数值精度，建议先做同工作流/同 seed 对比。");
        layout.Controls.Add(buttons, 0, 4);
        layout.SetColumnSpan(buttons, 4);

        _directoryButtonsPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            WrapContents = true,
            Margin = new Padding(0, 4, 0, 0)
        };
        _directoryButtonsPanel.Controls.Add(new Label
        {
            Text = "快捷目录",
            AutoSize = true,
            Padding = new Padding(2, 8, 6, 0)
        });
        _directoryButtonsPanel.Controls.Add(CreateDirectoryButton("工作流", paths => paths.WorkflowsDir));
        _directoryButtonsPanel.Controls.Add(CreateDirectoryButton("插件", paths => paths.NodesDir));
        _directoryButtonsPanel.Controls.Add(CreateDirectoryButton("模型", paths => paths.ModelsDir));
        _directoryButtonsPanel.Controls.Add(CreateDirectoryButton("输入", paths => paths.InputDir));
        _directoryButtonsPanel.Controls.Add(CreateDirectoryButton("输出", paths => paths.OutputDir));
        layout.Controls.Add(_directoryButtonsPanel, 0, 5);
        layout.SetColumnSpan(_directoryButtonsPanel, 4);

        _lanLabel = new Label { Text = "局域网：未启用", AutoSize = true, ForeColor = Color.FromArgb(90, 95, 105) };
        layout.Controls.Add(_lanLabel, 0, 6);
        layout.SetColumnSpan(_lanLabel, 4);

        group.Controls.Add(layout);
        return group;
    }

    private Control BuildInfoGroup()
    {
        var group = new GroupBox { Text = "环境信息", Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(10) };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 7,
            Padding = new Padding(4)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 7; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _comfyLabel = AddInfoRow(layout, 0, "ComfyUI");
        _commitLabel = AddInfoRow(layout, 1, "提交");
        _pythonLabel = AddInfoRow(layout, 2, "Python");
        _torchLabel = AddInfoRow(layout, 3, "torch");
        _sageVersionLabel = AddInfoRow(layout, 4, "SageAttention");
        _tritonLabel = AddInfoRow(layout, 5, "Triton");
        _gpuLabel = AddInfoRow(layout, 6, "GPU");

        group.Controls.Add(layout);
        return group;
    }

    private static Label AddInfoRow(TableLayoutPanel layout, int row, string caption)
    {
        layout.Controls.Add(new Label { Text = caption, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        var value = new Label
        {
            Text = "读取中...",
            AutoSize = false,
            AutoEllipsis = true,
            Dock = DockStyle.Fill,
            Height = 22,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(40, 44, 52)
        };
        layout.Controls.Add(value, 1, row);
        return value;
    }

    private Control BuildVersionGroup()
    {
        var group = new GroupBox { Text = "ComfyUI 版本管理", Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(10) };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 6,
            RowCount = 4,
            Padding = new Padding(4)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        for (var i = 0; i < 4; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _currentVersionLabel = new Label { Text = "当前版本：读取中...", AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
        _versionUpdateLabel = new Label
        {
            Text = "",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            ForeColor = Color.FromArgb(32, 145, 74),
            Margin = new Padding(18, 0, 0, 0)
        };
        var versionHeader = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
            Margin = Padding.Empty
        };
        versionHeader.Controls.Add(_currentVersionLabel);
        versionHeader.Controls.Add(_versionUpdateLabel);
        layout.Controls.Add(versionHeader, 0, 0);
        layout.SetColumnSpan(versionHeader, 6);

        _versionCombo = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            IntegralHeight = false,
            DropDownHeight = 360
        };
        layout.Controls.Add(_versionCombo, 0, 1);
        layout.SetColumnSpan(_versionCombo, 3);

        _refreshVersionsButton = new Button { Text = "刷新版本", Width = 96, Height = 30 };
        _switchVersionButton = new Button { Text = "切换版本", Width = 96, Height = 30 };
        _rollbackButton = new Button { Text = "回滚", Width = 80, Height = 30, Enabled = false };
        layout.Controls.Add(_refreshVersionsButton, 3, 1);
        layout.Controls.Add(_switchVersionButton, 4, 1);
        layout.Controls.Add(_rollbackButton, 5, 1);

        var options = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        _updateDepsCheck = new CheckBox { Text = "切换后同步 Python 依赖", AutoSize = true, Checked = true };
        _prereleaseCheck = new CheckBox { Text = "包含预发布版本", AutoSize = true };
        options.Controls.Add(_updateDepsCheck);
        options.Controls.Add(_prereleaseCheck);
        layout.Controls.Add(options, 0, 2);
        layout.SetColumnSpan(options, 3);

        _versionStatusLabel = new Label
        {
            Text = "版本列表来自远程 Git tags",
            AutoSize = true,
            ForeColor = Color.FromArgb(90, 95, 105)
        };
        layout.Controls.Add(_versionStatusLabel, 3, 2);
        layout.SetColumnSpan(_versionStatusLabel, 3);

        _versionDetailsText = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Height = 58,
            BackColor = Color.White,
            PlaceholderText = "选中版本后显示 tag 和提交信息"
        };
        layout.Controls.Add(_versionDetailsText, 0, 3);
        layout.SetColumnSpan(_versionDetailsText, 6);

        group.Controls.Add(layout);
        return group;
    }

    private Control BuildLogGroup()
    {
        var group = new GroupBox { Text = "日志", Dock = DockStyle.Fill, Padding = new Padding(8) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        var clearButton = new Button { Text = "清空", Width = 70, Height = 26 };
        var openComfyLogsButton = new Button { Text = "ComfyUI 日志目录", Width = 128, Height = 26 };
        var openLauncherLogsButton = new Button { Text = "启动器日志", Width = 96, Height = 26 };
        clearButton.Click += (_, _) => { _logText.Clear(); lock (_logQueue) _logQueue.Clear(); };
        openComfyLogsButton.Click += (_, _) =>
        {
            if (_paths != null) OpenFolder(_paths.LogsDir);
        };
        openLauncherLogsButton.Click += (_, _) => OpenFolder(LauncherStorage.LogsDir);
        toolbar.Controls.Add(clearButton);
        toolbar.Controls.Add(openComfyLogsButton);
        toolbar.Controls.Add(openLauncherLogsButton);

        _logText = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            WordWrap = false,
            DetectUrls = false,
            Font = new Font("Consolas", 9F),
            BackColor = Color.FromArgb(24, 26, 30),
            ForeColor = Color.FromArgb(220, 224, 230),
            BorderStyle = BorderStyle.FixedSingle,
            HideSelection = false
        };
        layout.Controls.Add(toolbar, 0, 0);
        layout.Controls.Add(_logText, 0, 1);
        group.Controls.Add(layout);
        return group;
    }

    private void ApplyConfig()
    {
        _instancePathText.Text = _config.InstanceRoot;
        _portNumeric.Value = Math.Clamp(_config.Port, 1, 65535);
        _localRadio.Checked = _config.ListenMode == ListenMode.Local;
        _lanRadio.Checked = _config.ListenMode == ListenMode.Lan;
        _sageCheck.Checked = _config.UseSageAttention;
        _fp16AccumulationCheck.Checked = _config.UseFp16Accumulation;
        _autoOpenCheck.Checked = _config.AutoOpenBrowser;
        _updateDepsCheck.Checked = _config.UpdateDependenciesOnSwitch;
        _prereleaseCheck.Checked = _config.IncludePrerelease;
        _extraArgsText.Text = _config.ExtraArguments;
        _launcherChannelCombo.SelectedItem = string.IsNullOrWhiteSpace(_config.LauncherUpdateChannel)
            ? "stable"
            : _config.LauncherUpdateChannel.ToLowerInvariant();
        if (_launcherChannelCombo.SelectedIndex < 0) _launcherChannelCombo.SelectedItem = "stable";
        _launcherAutoCheck.Checked = _config.CheckLauncherUpdatesOnStartup;
        UpdateLanLabel();
    }

    private void WireEvents()
    {
        _localRadio.CheckedChanged += (_, _) => { if (_localRadio.Checked) UpdateLanLabel(); };
        _lanRadio.CheckedChanged += (_, _) => { if (_lanRadio.Checked) UpdateLanLabel(); };
        _portNumeric.ValueChanged += (_, _) => UpdateLanLabel();
        _sageCheck.CheckedChanged += (_, _) => SaveConfigQuietly();
        _fp16AccumulationCheck.CheckedChanged += (_, _) => SaveConfigQuietly();
        _autoOpenCheck.CheckedChanged += (_, _) => SaveConfigQuietly();
        _updateDepsCheck.CheckedChanged += (_, _) => SaveConfigQuietly();
        _prereleaseCheck.CheckedChanged += (_, _) => SaveConfigQuietly();
        _extraArgsText.Leave += (_, _) => SaveConfigQuietly();
        _browseInstanceButton.Click += BrowseInstanceButton_Click;
        _applyInstanceButton.Click += ApplyInstancePathButton_Click;
        _instancePathText.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                ApplyInstancePathButton_Click(_applyInstanceButton, EventArgs.Empty);
            }
        };

        _startButton.Click += StartButton_Click;
        _stopButton.Click += StopButton_Click;
        _openBrowserButton.Click += (_, _) => OpenUrl($"http://127.0.0.1:{(int)_portNumeric.Value}");
        _queueButton.Click += QueueButton_Click;
        _customNodesButton.Click += CustomNodesButton_Click;
        _refreshVersionsButton.Click += RefreshVersionsButton_Click;
        _switchVersionButton.Click += SwitchVersionButton_Click;
        _rollbackButton.Click += RollbackButton_Click;
        _versionCombo.SelectedIndexChanged += (_, _) => UpdateVersionDetails();
        _checkLauncherUpdateButton.Click += CheckLauncherUpdateButton_Click;
        _installLauncherUpdateButton.Click += InstallLauncherUpdateButton_Click;
        _skipLauncherUpdateButton.Click += SkipLauncherUpdateButton_Click;
        _launcherChannelCombo.SelectedIndexChanged += (_, _) =>
        {
            _pendingLauncherUpdate = null;
            _pendingLauncherVersion = null;
            _installLauncherUpdateButton.Visible = false;
            _skipLauncherUpdateButton.Visible = false;
            _launcherStatusLabel.Text = "";
            SaveConfigQuietly();
        };
        _launcherAutoCheck.CheckedChanged += (_, _) => SaveConfigQuietly();
    }

    private async void OnShownAsync(object? sender, EventArgs e)
    {
        if (AppPaths.TryCreate(_config.InstanceRoot, out var configured, out _) && configured != null)
        {
            await RunBusyAsync(
                () => BindToInstanceAsync(configured, saveConfig: false),
                "加载 ComfyUI 实例失败",
                showDialog: false);
        }
        else
        {
            var discovered = AppPaths.Discover(Environment.GetCommandLineArgs());
            if (discovered != null)
            {
                _instancePathText.Text = discovered.InstanceRoot;
                PostLog("[launcher] 已检测到候选实例，请确认路径后点击“应用路径”。");
            }
            else
            {
                _instancePathText.Text = _config.InstanceRoot;
                PostLog("[launcher] 尚未绑定实例，请指定 ComfyUI 实例路径。");
            }
            SetStatus("请指定 ComfyUI 实例路径并点击“应用路径”");
            UpdateActionButtons();
        }

        PostLog("[launcher] 配置文件：" + _configService.ConfigFile);
        if (_config.CheckLauncherUpdatesOnStartup)
        {
            _ = CheckLauncherUpdateAsync(silent: true);
        }
    }

    private async void BrowseInstanceButton_Click(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = @"选择 ComfyUI 实例根目录（应包含 ComfyUI\main.py 和 .venv\Scripts\python.exe）",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(_instancePathText.Text) ? _instancePathText.Text : _paths?.InstanceRoot ?? ""
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _instancePathText.Text = dialog.SelectedPath;
            await ApplyInstancePathAsync();
        }
    }

    private async void ApplyInstancePathButton_Click(object? sender, EventArgs e)
    {
        await ApplyInstancePathAsync();
    }

    private async Task ApplyInstancePathAsync()
    {
        if (_busy) return;
        if (_process?.IsRunning == true)
        {
            MessageBox.Show(this, "请先停止 ComfyUI，再更换实例路径。", "无法应用路径",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!AppPaths.TryCreate(_instancePathText.Text, out var paths, out var error) || paths == null)
        {
            MessageBox.Show(this, error, "实例路径无效", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        await RunBusyAsync(
            () => BindToInstanceAsync(paths, saveConfig: true),
            "应用实例路径失败");
    }

    private async Task BindToInstanceAsync(AppPaths paths, bool saveConfig)
    {
        if (_process?.IsRunning == true)
        {
            throw new InvalidOperationException("ComfyUI 正在运行，不能更换实例。");
        }

        _process?.Dispose();
        _process = null;
        _paths = paths;
        paths.EnsureDirectories();

        _git = new GitService(paths);
        _probe = new EnvironmentProbeService(paths, _git);
        _versionService = new ComfyVersionService(_git);
        _switcher = new ComfyVersionSwitcher(paths, _git);
        _process = new ComfyProcessService(paths);
        AttachProcessEvents(_process);

        _config.InstanceRoot = paths.InstanceRoot;
        if (saveConfig) _configService.Save(_config);
        _instancePathText.Text = paths.InstanceRoot;
        _currentVersionLabel.Text = "当前版本：读取中...";
        _versionStatusLabel.Text = "版本列表加载中...";
        _versionCombo.Items.Clear();
        _versions = Array.Empty<ComfyVersionInfo>();
        _hasRollbackState = false;
        ResetEnvironmentLabels();

        PostLog($"[launcher] 实例目录：{paths.InstanceRoot}");
        PostLog("[launcher] 启动器日志：" + LauncherLog.LogFile);
        PostLog("[launcher] ComfyUI 日志目录：" + paths.LogsDir);
        SetStatus("正在读取实例信息...");
        await RefreshEnvironmentAsync();
        await RefreshVersionsCoreAsync();
        SetStatus("实例已绑定：" + paths.InstanceRoot);
        UpdateActionButtons();
    }

    private void AttachProcessEvents(ComfyProcessService process)
    {
        process.LogReceived += line => PostLog(line);
        process.StateChanged += state => OnUi(() =>
        {
            UpdateActionButtons();
            if (state == ComfyProcessState.Starting) SetStatus("正在启动...");
            if (state == ComfyProcessState.Stopping) SetStatus("正在停止...");
            if (state == ComfyProcessState.Stopped) SetStatus("已停止");
            if (state == ComfyProcessState.Error) SetStatus("启动失败");
        });
        process.Healthy += info => OnUi(() =>
        {
            if (_readyAnnounced) return;
            _readyAnnounced = true;
            SetStatus($"运行中 · {info.LocalUrl}");
            UpdateActionButtons();
            if (_autoOpenCheck.Checked) OpenUrl(info.LocalUrl);
        });
    }

    private void ResetEnvironmentLabels()
    {
        _comfyLabel.Text = "读取中...";
        _commitLabel.Text = "读取中...";
        _pythonLabel.Text = "读取中...";
        _torchLabel.Text = "读取中...";
        _sageVersionLabel.Text = "读取中...";
        _tritonLabel.Text = "读取中...";
        _gpuLabel.Text = "读取中...";
    }

    private async void StartButton_Click(object? sender, EventArgs e)
    {
        if (_busy) return;
        if (_paths == null || _process == null)
        {
            MessageBox.Show(this, "请先指定并应用 ComfyUI 实例路径。", "未绑定实例",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (_process.IsRunning) return;
        var config = CollectConfig();
        _config = config;
        SaveConfigQuietly();

        await RunBusyAsync(async () =>
        {
            _readyAnnounced = false;
            await _process.StartAsync(config, _lifetimeCts.Token);
            if (!string.IsNullOrWhiteSpace(_process.RuntimeLogPath))
                PostLog("[launcher] ComfyUI 运行日志：" + _process.RuntimeLogPath);
            SetStatus("进程已启动，等待服务就绪...");
        }, "启动失败");
    }

    private async void StopButton_Click(object? sender, EventArgs e)
    {
        if (_busy || _process == null) return;
        await RunBusyAsync(async () =>
        {
            await _process.StopAsync();
            _readyAnnounced = false;
        }, "停止失败");
    }

    private void QueueButton_Click(object? sender, EventArgs e)
    {
        if (_paths == null || _process?.IsRunning != true) return;
        using var form = new ComfyQueueForm(_paths.GetLocalUrl((int)_portNumeric.Value));
        form.ShowDialog(this);
    }

    private Button CreateDirectoryButton(string text, Func<AppPaths, string> pathSelector)
    {
        var button = new Button { Text = text, Width = 82, Height = 28 };
        button.Click += (_, _) =>
        {
            if (_paths == null) return;
            OpenFolder(pathSelector(_paths));
        };
        return button;
    }

    private async void CustomNodesButton_Click(object? sender, EventArgs e)
    {
        if (_busy) return;
        if (_paths == null)
        {
            MessageBox.Show(this, "请先绑定 ComfyUI 实例。", "未绑定实例",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (_process?.IsRunning == true)
        {
            MessageBox.Show(this, "请先停止 ComfyUI，再安装或更新节点。", "节点管理",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var form = new CustomNodesForm(_paths);
        form.ShowDialog(this);
        await RefreshEnvironmentAsync();
        UpdateActionButtons();
    }

    private async void RefreshVersionsButton_Click(object? sender, EventArgs e)
    {
        if (_busy || _versionService == null) return;
        await RunBusyAsync(
            () => RefreshVersionsCoreAsync(),
            "获取版本列表失败");
    }

    private async void SwitchVersionButton_Click(object? sender, EventArgs e)
    {
        if (_busy) return;
        if (_switcher == null || _paths == null)
        {
            MessageBox.Show(this, "请先绑定 ComfyUI 实例。", "未绑定实例",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (_process?.IsRunning == true)
        {
            MessageBox.Show(this, "请先停止 ComfyUI，再切换版本。", "无法切换",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_versionCombo.SelectedItem is not ComfyVersionInfo version)
        {
            MessageBox.Show(this, "请先选择一个版本。", "提示",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (version.IsCurrent)
        {
            MessageBox.Show(this, "这已经是当前版本。", "提示",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var answer = MessageBox.Show(this,
            $"当前版本：{_currentVersionLabel.Text}\r\n目标版本：{version.Tag}\r\n\r\n" +
            "切换前会检查工作区修改并保存当前版本信息。\r\n" +
            "如果切换失败，会自动尝试回滚。\r\n\r\n继续吗？",
            "切换 ComfyUI 版本", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (answer != DialogResult.Yes) return;

        await RunBusyAsync(async () =>
        {
            var progress = new Progress<string>(msg =>
            {
                PostLog("[version] " + msg);
                SetStatus(msg);
            });
            await _switcher.SwitchAsync(version, _updateDepsCheck.Checked, progress, _lifetimeCts.Token);
            await RefreshEnvironmentAsync();
            await RefreshVersionsCoreAsync();
            MessageBox.Show(this, $"已切换到 {version.Tag}。", "版本切换完成",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }, "版本切换失败");
    }

    private async void RollbackButton_Click(object? sender, EventArgs e)
    {
        if (_busy) return;
        if (_switcher == null || _paths == null)
        {
            MessageBox.Show(this, "请先绑定 ComfyUI 实例。", "未绑定实例",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (_process?.IsRunning == true)
        {
            MessageBox.Show(this, "请先停止 ComfyUI，再执行回滚。", "无法回滚",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var answer = MessageBox.Show(this, "确定回滚到上一次切换前的 ComfyUI 版本吗？",
            "版本回滚", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (answer != DialogResult.Yes) return;

        await RunBusyAsync(async () =>
        {
            var progress = new Progress<string>(msg =>
            {
                PostLog("[rollback] " + msg);
                SetStatus(msg);
            });
            await _switcher.RollbackAsync(_updateDepsCheck.Checked, progress, _lifetimeCts.Token);
            await RefreshEnvironmentAsync();
            await RefreshVersionsCoreAsync();
            MessageBox.Show(this, "已回滚到切换前版本。", "回滚完成",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }, "回滚失败");
    }

    private async Task RefreshEnvironmentAsync()
    {
        if (_probe == null) return;
        try
        {
            var info = await _probe.ProbeAsync(_lifetimeCts.Token);
            OnUi(() =>
            {
                _comfyLabel.Text = info.ComfyVersion;
                _commitLabel.Text = $"{info.ComfyCommit}  {info.ComfyCommitDate}";
                _pythonLabel.Text = info.PythonVersion;
                _torchLabel.Text = info.TorchVersion;
                _sageVersionLabel.Text = info.SageAttentionVersion;
                _tritonLabel.Text = info.TritonVersion;
                _gpuLabel.Text = string.Join("  ",
                    new[] { info.GpuName, info.GpuMemory, string.IsNullOrWhiteSpace(info.DriverVersion) ? "" : "Driver " + info.DriverVersion }
                        .Where(x => !string.IsNullOrWhiteSpace(x)));
                _currentVersionLabel.Text = $"当前版本：{info.ComfyVersion}  /  {info.ComfyCommit}";
                _lanLabel.Text = _paths == null
                    ? "实例未绑定"
                    : $"局域网地址：{_paths.GetLanUrl((int)_portNumeric.Value)}";
            });
        }
        catch (Exception ex)
        {
            PostLog("[error] 读取环境信息失败：" + ex.Message);
            OnUi(() => SetStatus("环境信息读取失败"));
        }
    }

    private async Task RefreshVersionsCoreAsync()
    {
        if (_versionService == null || _switcher == null) return;
        var selectedTag = (_versionCombo.SelectedItem as ComfyVersionInfo)?.Tag;
        var progress = new Progress<string>(msg =>
        {
            PostLog("[version] " + msg);
            SetStatus(msg);
        });
        var catalog = await _versionService.GetVersionsAsync(
            _prereleaseCheck.Checked, progress, _lifetimeCts.Token);
        _versions = catalog.Versions;
        OnUi(() => PopulateVersionCombo(selectedTag));
        _versionStatusLabel.Text = $"已从远程 Git tags 加载 {catalog.Versions.Count} 个版本";
        var state = await _switcher.GetLastStateAsync(_lifetimeCts.Token);
        _hasRollbackState = state != null;
        OnUi(() => _rollbackButton.Enabled = state != null && !_busy && _process?.IsRunning != true);
    }

    private void PopulateVersionCombo(string? preferredTag)
    {
        var current = _versions.FirstOrDefault(v => v.IsCurrent)?.Tag;
        var select = preferredTag ?? current;
        _versionCombo.BeginUpdate();
        try
        {
            _versionCombo.Items.Clear();
            foreach (var version in _versions) _versionCombo.Items.Add(version);
            if (_versionCombo.Items.Count > 0)
            {
                var index = select == null
                    ? 0
                    : _versions.ToList().FindIndex(v => string.Equals(v.Tag, select, StringComparison.OrdinalIgnoreCase));
                _versionCombo.SelectedIndex = index >= 0 ? index : 0;
            }
        }
        finally
        {
            _versionCombo.EndUpdate();
        }
        UpdateVersionDetails();
        UpdateVersionUpdateIndicator();
    }

    private void UpdateVersionUpdateIndicator()
    {
        var currentTag = _versions.FirstOrDefault(v => v.IsCurrent)?.Tag ?? "";
        var latestTag = _versions.FirstOrDefault(v => !v.IsPrerelease)?.Tag
            ?? _versions.FirstOrDefault()?.Tag
            ?? "";
        if (!string.IsNullOrWhiteSpace(currentTag) &&
            !string.IsNullOrWhiteSpace(latestTag) &&
            !string.Equals(currentTag, latestTag, StringComparison.OrdinalIgnoreCase) &&
            SemVer.IsNewerThan(latestTag, currentTag))
        {
            _versionUpdateLabel.Text = $"有新版本可更新：{latestTag}";
            _versionUpdateLabel.ForeColor = Color.FromArgb(32, 145, 74);
        }
        else
        {
            _versionUpdateLabel.Text = "";
        }
    }

    private void UpdateVersionDetails()
    {
        if (_versionCombo.SelectedItem is not ComfyVersionInfo version)
        {
            _versionDetailsText.Text = "";
            return;
        }
        var commit = string.IsNullOrWhiteSpace(version.Commit) ? "未知" : version.Commit;
        _versionDetailsText.Text = $"版本 tag：{version.Tag}\r\n提交：{commit}\r\n预发布：{(version.IsPrerelease ? "是" : "否")}";
    }

    private async Task RunBusyAsync(Func<Task> action, string errorTitle, bool showDialog = true)
    {
        if (_busy) return;
        SetBusy(true);
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
            PostLog("[launcher] 操作已取消。");
            SetStatus("操作已取消");
        }
        catch (Exception ex)
        {
            PostLog("[error] " + ex);
            SetStatus(errorTitle);
            if (showDialog)
            {
                MessageBox.Show(this, ex.Message, errorTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        finally
        {
            SetBusy(false);
            UpdateActionButtons();
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _progressBar.Visible = busy;
        UseWaitCursor = busy;
        var running = _process?.IsRunning == true;
        _startButton.Enabled = !busy && _paths != null && !running;
        _stopButton.Enabled = !busy && running;
        _queueButton.Enabled = !busy && running;
        _instancePathText.Enabled = !busy && !running;
        _browseInstanceButton.Enabled = !busy && !running;
        _applyInstanceButton.Enabled = !busy && !running;
        _directoryButtonsPanel.Enabled = _paths != null && !busy;
        _customNodesButton.Enabled = !busy && !running && _paths != null;
        _refreshVersionsButton.Enabled = !busy && _paths != null;
        _switchVersionButton.Enabled = !busy && !running && _paths != null;
        _portNumeric.Enabled = !busy;
        _localRadio.Enabled = !busy && !running;
        _lanRadio.Enabled = !busy && !running;
        _sageCheck.Enabled = !busy && !running;
        _fp16AccumulationCheck.Enabled = !busy && !running;
        _extraArgsText.Enabled = !busy && !running;
        _updateDepsCheck.Enabled = !busy && !running;
        _prereleaseCheck.Enabled = !busy && !running;
        _checkLauncherUpdateButton.Enabled = !busy;
        _installLauncherUpdateButton.Enabled = !busy && _pendingLauncherUpdate != null;
        _skipLauncherUpdateButton.Enabled = !busy && _pendingLauncherUpdate != null;
    }

    private void UpdateActionButtons()
    {
        var running = _process?.IsRunning == true;
        _startButton.Enabled = !_busy && _paths != null && !running;
        _stopButton.Enabled = !_busy && running;
        _openBrowserButton.Enabled = !_busy && running;
        _queueButton.Enabled = !_busy && running;
        _switchVersionButton.Enabled = !_busy && !running && _paths != null && _versionCombo.Items.Count > 0;
        _refreshVersionsButton.Enabled = !_busy && _paths != null;
        _rollbackButton.Enabled = !_busy && !running && _paths != null && _hasRollbackState;
        _customNodesButton.Enabled = !_busy && !running && _paths != null;
        _instancePathText.Enabled = !_busy && !running;
        _browseInstanceButton.Enabled = !_busy && !running;
        _applyInstanceButton.Enabled = !_busy && !running;
        _directoryButtonsPanel.Enabled = _paths != null && !_busy;
        _checkLauncherUpdateButton.Enabled = !_busy;
        _installLauncherUpdateButton.Enabled = !_busy && _pendingLauncherUpdate != null;
        _skipLauncherUpdateButton.Enabled = !_busy && _pendingLauncherUpdate != null;
    }

    private LauncherConfig CollectConfig()
    {
        return new LauncherConfig
        {
            InstanceRoot = _paths?.InstanceRoot ?? _config.InstanceRoot,
            Port = (int)_portNumeric.Value,
            ListenMode = _lanRadio.Checked ? ListenMode.Lan : ListenMode.Local,
            UseSageAttention = _sageCheck.Checked,
            UseFp16Accumulation = _fp16AccumulationCheck.Checked,
            AutoOpenBrowser = _autoOpenCheck.Checked,
            UpdateDependenciesOnSwitch = _updateDepsCheck.Checked,
            IncludePrerelease = _prereleaseCheck.Checked,
            ExtraArguments = _extraArgsText.Text.Trim(),
            LauncherUpdateChannel = _launcherChannelCombo.SelectedItem?.ToString() ?? "stable",
            CheckLauncherUpdatesOnStartup = _launcherAutoCheck.Checked,
            UpdateFeedUrl = _config.UpdateFeedUrl,
            SkippedLauncherVersion = _config.SkippedLauncherVersion
        };
    }

    private void SaveConfigQuietly()
    {
        try
        {
            _config = CollectConfig();
            _configService.Save(_config);
        }
        catch (Exception ex)
        {
            PostLog("[error] 保存配置失败：" + ex.Message);
        }
    }

    private void UpdateLauncherVersionLabel()
    {
        if (_launcherVersionLabel == null) return;
        _launcherVersionLabel.Text = $"启动器版本：{_launcherUpdateService.CurrentVersion}";
    }

    private async void CheckLauncherUpdateButton_Click(object? sender, EventArgs e)
    {
        await CheckLauncherUpdateAsync(silent: false);
    }

    private async Task CheckLauncherUpdateAsync(bool silent)
    {
        if (_busy) return;

        OnUi(() =>
        {
            _checkLauncherUpdateButton.Enabled = false;
            _launcherStatusLabel.ForeColor = Color.FromArgb(90, 95, 105);
            _launcherStatusLabel.Text = "正在检查启动器更新...";
        });

        try
        {
            var result = await _launcherUpdateService.CheckForUpdatesAsync();
            OnUi(() => ApplyLauncherUpdateResult(result, silent));
        }
        catch (Exception ex)
        {
            PostLog("[update] 检查启动器更新失败：" + ex.Message);
            OnUi(() =>
            {
                _checkLauncherUpdateButton.Enabled = !_busy;
                _launcherStatusLabel.ForeColor = Color.Firebrick;
                _launcherStatusLabel.Text = "检查更新失败（详见日志）";
                if (!silent)
                {
                    MessageBox.Show(this, ex.Message, "检查启动器更新失败",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            });
        }
    }

    private void ApplyLauncherUpdateResult(LauncherUpdateCheckResult result, bool silent)
    {
        UpdateLauncherVersionLabel();
        _checkLauncherUpdateButton.Enabled = !_busy;

        if (!result.IsInstalled)
        {
            _installLauncherUpdateButton.Visible = false;
            _skipLauncherUpdateButton.Visible = false;
            _pendingLauncherUpdate = null;
            _pendingLauncherVersion = null;
            _launcherStatusLabel.ForeColor = Color.FromArgb(90, 95, 105);
            _launcherStatusLabel.Text = "当前为开发/便携运行，安装版才启用自动更新。";
            if (!silent)
            {
                MessageBox.Show(this,
                    "当前进程不是 Velopack 安装版，无法执行自更新。请通过 Setup.exe 安装后再测试更新流程。",
                    "启动器更新", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            return;
        }

        if (result.Update == null || string.IsNullOrWhiteSpace(result.AvailableVersion))
        {
            _installLauncherUpdateButton.Visible = false;
            _skipLauncherUpdateButton.Visible = false;
            _pendingLauncherUpdate = null;
            _pendingLauncherVersion = null;
            _launcherStatusLabel.ForeColor = Color.FromArgb(32, 120, 72);
            _launcherStatusLabel.Text = "已是最新版本";
            if (!silent)
            {
                MessageBox.Show(this, "当前已经是最新版本。", "启动器更新",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            return;
        }

        _pendingLauncherUpdate = result.Update;
        _pendingLauncherVersion = result.AvailableVersion;
        var skipped = string.Equals(_config.SkippedLauncherVersion, result.AvailableVersion,
            StringComparison.OrdinalIgnoreCase);
        _installLauncherUpdateButton.Text = $"更新到 {result.AvailableVersion}";
        _installLauncherUpdateButton.Visible = true;
        _installLauncherUpdateButton.Enabled = !_busy;
        _skipLauncherUpdateButton.Visible = !skipped;
        _launcherStatusLabel.ForeColor = Color.FromArgb(180, 90, 20);
        _launcherStatusLabel.Text = skipped
            ? $"发现新版本 {result.AvailableVersion}（已跳过，可手动更新）"
            : $"发现新版本 {result.AvailableVersion}（{result.Channel}）";

        if (!silent)
        {
            MessageBox.Show(this,
                $"发现启动器新版本 {result.AvailableVersion}。\r\n当前版本：{result.CurrentVersion}\r\n通道：{result.Channel}",
                "启动器更新", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private async void InstallLauncherUpdateButton_Click(object? sender, EventArgs e)
    {
        if (_busy || _pendingLauncherUpdate == null) return;

        if (_process?.IsRunning == true)
        {
            var answer = MessageBox.Show(this,
                "ComfyUI 正在运行。安装启动器更新前需要先停止 ComfyUI，是否继续？",
                "更新启动器", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return;

            try
            {
                await _process.StopAsync();
                _readyAnnounced = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "停止 ComfyUI 失败，已取消更新。\r\n" + ex.Message,
                    "更新启动器", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
        }

        SaveConfigQuietly();
        SetBusy(true);
        _progressBar.Style = ProgressBarStyle.Continuous;
        _progressBar.Maximum = 100;
        _progressBar.Value = 0;
        _launcherStatusLabel.ForeColor = Color.FromArgb(90, 95, 105);

        try
        {
            var update = _pendingLauncherUpdate;
            var progress = new Progress<int>(percent => OnUi(() =>
            {
                var value = Math.Clamp(percent, 0, 100);
                if (_progressBar.Style == ProgressBarStyle.Continuous &&
                    value >= _progressBar.Minimum && value <= _progressBar.Maximum)
                {
                    _progressBar.Value = value;
                }
                _launcherStatusLabel.Text = $"正在下载启动器更新... {value}%";
            }));

            await _launcherUpdateService.DownloadAsync(update, progress, _lifetimeCts.Token);
            OnUi(() => _launcherStatusLabel.Text = "下载完成，正在重启并应用更新...");
            await Task.Delay(350);
            _launcherUpdateService.ApplyAndRestart(update);
        }
        catch (OperationCanceledException)
        {
            OnUi(() => _launcherStatusLabel.Text = "启动器更新已取消");
        }
        catch (Exception ex)
        {
            PostLog("[update] 安装启动器更新失败：" + ex);
            OnUi(() =>
            {
                _launcherStatusLabel.ForeColor = Color.Firebrick;
                _launcherStatusLabel.Text = "安装更新失败（详见日志）";
                MessageBox.Show(this, ex.Message, "安装启动器更新失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            });
        }
        finally
        {
            if (!IsDisposed)
            {
                _progressBar.Style = ProgressBarStyle.Marquee;
                SetBusy(false);
            }
        }
    }

    private void SkipLauncherUpdateButton_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_pendingLauncherVersion)) return;
        _config.SkippedLauncherVersion = _pendingLauncherVersion;
        SaveConfigQuietly();
        _skipLauncherUpdateButton.Visible = false;
        _launcherStatusLabel.ForeColor = Color.FromArgb(90, 95, 105);
        _launcherStatusLabel.Text = $"已跳过 {_pendingLauncherVersion}，可随时手动检查更新。";
    }

    private void UpdateLanLabel()
    {
        if (_paths == null)
        {
            _lanLabel.Text = "实例未绑定";
            _lanLabel.ForeColor = Color.Firebrick;
            return;
        }

        if (_lanRadio.Checked)
        {
            _lanLabel.Text = $"局域网地址：{_paths.GetLanUrl((int)_portNumeric.Value)}（ComfyUI 默认无登录认证，请勿映射到公网）";
            _lanLabel.ForeColor = Color.FromArgb(180, 90, 20);
        }
        else
        {
            _lanLabel.Text = $"本机地址：{_paths.GetLocalUrl((int)_portNumeric.Value)}";
            _lanLabel.ForeColor = Color.FromArgb(90, 95, 105);
        }
    }

    private void PostLog(string line)
    {
        if (!line.StartsWith("[ComfyUI", StringComparison.OrdinalIgnoreCase))
        {
            LauncherLog.Write("launcher", line);
        }
        lock (_logQueue) _logQueue.Enqueue($"[{DateTime.Now:HH:mm:ss}] {line}");
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
        if (_logText.TextLength > 220_000)
        {
            _logText.Select(0, _logText.TextLength - 160_000);
            _logText.SelectedText = "";
        }
        _logText.SelectionStart = _logText.TextLength;
        _logText.ScrollToCaret();
    }

    private void SetStatus(string text)
    {
        if (IsDisposed) return;
        _statusLabel.Text = text;
    }

    private void OnUi(Action action)
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(action); } catch { }
        }
        else
        {
            action();
        }
    }

    private void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            LauncherLog.Write("launcher", "打开目录：" + path);
            Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"\"{path}\"", UseShellExecute = true });
        }
        catch (Exception ex)
        {
            PostLog("[error] 打开目录失败：" + ex.Message);
        }
    }

    private void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            PostLog("[error] 打开浏览器失败：" + ex.Message);
        }
    }

    private async void OnFormClosingAsync(object? sender, FormClosingEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        if (_process?.IsRunning == true)
        {
            var answer = MessageBox.Show(this, "ComfyUI 正在运行，是否停止并退出？", "退出",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return;
            try { if (_process != null) await _process.StopAsync(); } catch { }
        }
        SaveConfigQuietly();
        _allowClose = true;
        Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _logTimer.Stop();
            _logTimer.Dispose();
            _lifetimeCts.Cancel();
            _lifetimeCts.Dispose();
            _process?.Dispose();
        }
        base.Dispose(disposing);
    }
}
