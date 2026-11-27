using System.Diagnostics;
using XantaComfyLauncher.Models;
using XantaComfyLauncher.Services;
using XantaComfyLauncher.Utils;

namespace XantaComfyLauncher;

public sealed class LauncherSettingsForm : Form
{
    private readonly LauncherConfig _config;
    private readonly LauncherConfigService _configService;
    private readonly AppPaths? _paths;
    private readonly TextBox _modelDirText;
    private readonly TextBox _tokenText;
    private readonly Label _tokenStatus;
    private readonly CheckBox _darkModeCheck;

    public LauncherSettingsForm(LauncherConfig config, LauncherConfigService configService, AppPaths? paths)
    {
        _config = config;
        _configService = configService;
        _paths = paths;

        Text = "启动器设置";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(760, 540);
        MinimumSize = new Size(640, 460);
        Font = new Font("Segoe UI", 9F);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(245, 246, 248);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(14)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(new Label
        {
            Text = "启动器设置",
            AutoSize = true,
            Font = new Font("Segoe UI", 15F, FontStyle.Bold),
            ForeColor = Color.FromArgb(35, 40, 48),
            Margin = new Padding(2, 0, 0, 10)
        }, 0, 0);

        var sections = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        sections.RowStyles.Add(new RowStyle(SizeType.Absolute, 215));
        sections.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var tokenGroup = new GroupBox
        {
            Text = "GitHub Token",
            Dock = DockStyle.Fill,
            Padding = new Padding(10),
            Margin = new Padding(0, 0, 0, 10)
        };
        var tokenLayout = MakeLayout(4);
        tokenLayout.Controls.Add(new Label { Text = "GitHub Token", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        _tokenText = new TextBox
        {
            Dock = DockStyle.Fill,
            UseSystemPasswordChar = true,
            PlaceholderText = "粘贴 token 后点击保存；留空表示不修改",
            Margin = new Padding(0, 3, 0, 3)
        };
        tokenLayout.Controls.Add(_tokenText, 1, 0);

        var tokenButtons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = Padding.Empty };
        var saveTokenButton = MakeButton("保存 Token", 100);
        var clearTokenButton = MakeButton("清除 Token", 100);
        var helpButton = MakeButton("使用说明", 90);
        var createTokenLink = new LinkLabel { Text = "创建 GitHub Token", AutoSize = true, Margin = new Padding(10, 8, 0, 0) };
        createTokenLink.LinkClicked += (_, _) => OpenUrl("https://github.com/settings/tokens/new?scopes=&description=XantaComfyLauncher");
        tokenButtons.Controls.Add(saveTokenButton);
        tokenButtons.Controls.Add(clearTokenButton);
        tokenButtons.Controls.Add(helpButton);
        tokenButtons.Controls.Add(createTokenLink);
        tokenLayout.Controls.Add(tokenButtons, 1, 1);

        _tokenStatus = new Label
        {
            Text = "当前来源：未配置",
            AutoSize = true,
            ForeColor = Color.FromArgb(180, 60, 60),
            Margin = new Padding(0, 4, 0, 0)
        };
        tokenLayout.Controls.Add(_tokenStatus, 1, 2);
        tokenLayout.Controls.Add(new Label
        {
            Text = "Token 保存在 Windows 凭据管理器，不写入 config.json。",
            AutoSize = true,
            ForeColor = Color.FromArgb(90, 95, 105),
            Margin = new Padding(0, 8, 0, 0)
        }, 1, 3);
        tokenGroup.Controls.Add(tokenLayout);
        sections.Controls.Add(tokenGroup, 0, 0);

        var modelGroup = new GroupBox
        {
            Text = "模型目录",
            Dock = DockStyle.Fill,
            Padding = new Padding(10),
            Margin = Padding.Empty
        };
        var modelLayout = MakeLayout(3);
        modelLayout.Controls.Add(new Label { Text = "默认模型目录", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        _modelDirText = new TextBox
        {
            Dock = DockStyle.Fill,
            PlaceholderText = "主界面“模型”按钮将直接打开此目录",
            Margin = new Padding(0, 3, 0, 3)
        };
        modelLayout.Controls.Add(_modelDirText, 1, 0);

        var modelButtons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = Padding.Empty };
        var browseModelButton = MakeButton("浏览...", 82);
        var openModelButton = MakeButton("打开目录", 92);
        var managePathsButton = MakeButton("管理 extra_model_paths...", 178);
        browseModelButton.Click += (_, _) => BrowseModelDirectory();
        openModelButton.Click += (_, _) => OpenConfiguredModelDirectory();
        managePathsButton.Click += (_, _) => OpenModelPathsManager();
        modelButtons.Controls.Add(browseModelButton);
        modelButtons.Controls.Add(openModelButton);
        modelButtons.Controls.Add(managePathsButton);
        modelLayout.Controls.Add(modelButtons, 1, 1);
        modelLayout.Controls.Add(new Label
        {
            Text = "主界面不再显示模型目录下拉菜单；“模型”按钮会直接打开这里配置的路径。",
            AutoSize = true,
            ForeColor = Color.FromArgb(90, 95, 105),
            Margin = new Padding(0, 8, 0, 0)
        }, 1, 2);
        modelGroup.Controls.Add(modelLayout);
        sections.Controls.Add(modelGroup, 0, 1);
        root.Controls.Add(sections, 0, 1);

        var footer = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = new Padding(0, 10, 0, 0)
        };
        var closeButton = MakeButton("取消", 80);
        var saveButton = MakeButton("保存并关闭", 110);
        closeButton.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        saveButton.Click += (_, _) => SaveAndClose();
        footer.Controls.Add(closeButton);
        footer.Controls.Add(saveButton);
        _darkModeCheck = new CheckBox
        {
            Text = "深色模式（重启后生效）",
            AutoSize = true,
            Checked = _config.UseDarkTheme,
            Margin = new Padding(0, 8, 18, 0)
        };
        footer.Controls.Add(_darkModeCheck);
        root.Controls.Add(footer, 0, 2);

        saveTokenButton.Click += (_, _) => SaveToken();
        clearTokenButton.Click += (_, _) => ClearToken();
        helpButton.Click += (_, _) => ShowTokenHelp();
        _modelDirText.Text = !string.IsNullOrWhiteSpace(_config.LastModelDirectory)
            ? _config.LastModelDirectory
            : _paths?.ModelsDir ?? "";
        UpdateTokenStatus();
        Controls.Add(root);
        ThemeManager.Apply(this);
    }

    private static TableLayoutPanel MakeLayout(int rows)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = rows,
            Padding = new Padding(4)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < rows; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        return layout;
    }

    private static Button MakeButton(string text, int width) => new()
    {
        Text = text,
        AutoSize = true,
        MinimumSize = new Size(width, 30),
        Padding = new Padding(8, 0, 8, 0),
        Margin = new Padding(3)
    };

    private void UpdateTokenStatus()
    {
        var source = GitHubTokenStore.GetTokenSource();
        _tokenStatus.Text = "当前来源：" + source;
        _tokenStatus.ForeColor = source == "未配置"
            ? Color.FromArgb(180, 60, 60)
            : Color.FromArgb(32, 120, 72);
    }

    private void SaveToken()
    {
        var token = _tokenText.Text.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            MessageBox.Show(this, "请先粘贴 GitHub Token。", "GitHub Token",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            GitHubTokenStore.SaveToken(token);
            _tokenText.Clear();
            UpdateTokenStatus();
            MessageBox.Show(this, "GitHub Token 已保存到 Windows 凭据管理器，立即生效。",
                "GitHub Token", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "保存 GitHub Token 失败",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ClearToken()
    {
        if (MessageBox.Show(this, "确定清除已保存的 GitHub Token 吗？", "GitHub Token",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try
        {
            GitHubTokenStore.ClearToken();
            UpdateTokenStatus();
            MessageBox.Show(this, "GitHub Token 已清除。", "GitHub Token",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "清除 GitHub Token 失败",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowTokenHelp()
    {
        var nl = Environment.NewLine;
        MessageBox.Show(this,
            "GitHub Token 用于提高 GitHub API 搜索速率，也用于启动器更新检查。" + nl + nl +
            "推荐做法：" + nl +
            "1. GitHub → Settings → Developer settings → Personal access tokens。" + nl +
            "2. classic token，公开仓库搜索不需要 scope。" + nl +
            "3. 或 fine-grained token，授予 Public Repositories: Read-only。" + nl +
            "4. 粘贴到设置页并保存。" + nl + nl +
            "Token 保存在 Windows 凭据管理器，不写入 config.json。",
            "GitHub Token 使用说明", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void BrowseModelDirectory()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择默认模型目录",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(_modelDirText.Text)
                ? _modelDirText.Text
                : _paths?.ModelsDir ?? ""
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _modelDirText.Text = dialog.SelectedPath;
    }

    private void OpenConfiguredModelDirectory()
    {
        var path = _modelDirText.Text.Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            path = _paths?.ModelsDir ?? "";
        }
        if (string.IsNullOrWhiteSpace(path)) return;
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{path}\"",
            UseShellExecute = true
        });
    }

    private void OpenModelPathsManager()
    {
        if (_paths == null)
        {
            MessageBox.Show(this, "请先绑定 ComfyUI 实例。", "模型路径",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var form = new ModelPathsForm(_paths);
        form.ShowDialog(this);
    }

    private void SaveAndClose()
    {
        var path = _modelDirText.Text.Trim();
        if (!string.IsNullOrWhiteSpace(path))
        {
            try { Directory.CreateDirectory(path); }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "模型目录无效", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
        }
        var themeChanged = _config.UseDarkTheme != _darkModeCheck.Checked;
        _config.UseDarkTheme = _darkModeCheck.Checked;
        _config.LastModelDirectory = path;
        _configService.Save(_config);
        if (themeChanged)
        {
            MessageBox.Show(this, "深色模式设置将在重启启动器后生效。", "界面主题",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        DialogResult = DialogResult.OK;
        Close();
    }

    private void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
    }
}
