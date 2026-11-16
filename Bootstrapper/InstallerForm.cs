using System.Diagnostics;

namespace XantaComfyLauncher.Bootstrapper;

public sealed class InstallerForm : Form
{
    private readonly TextBox _pathText;
    private readonly Button _browseButton;
    private readonly Button _installButton;
    private readonly Button _cancelButton;
    private readonly Label _statusLabel;
    private readonly ProgressBar _progressBar;

    public InstallerForm(string? initialPath)
    {
        Text = "XantaComfyLauncher 安装程序";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(620, 250);
        Font = new Font("Segoe UI", 9F);
        BackColor = Color.FromArgb(245, 246, 248);
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        var title = new Label
        {
            Text = "选择 XantaComfyLauncher 的安装位置",
            Font = new Font(Font, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(24, 22)
        };
        var description = new Label
        {
            Text = "启动器程序会安装到这里；ComfyUI 实例、模型和用户数据不会放进安装目录。",
            AutoSize = true,
            ForeColor = Color.FromArgb(90, 95, 105),
            Location = new Point(24, 52)
        };
        _pathText = new TextBox
        {
            Location = new Point(24, 88),
            Width = 470,
            Text = string.IsNullOrWhiteSpace(initialPath)
                ? InstallerEngine.GetDefaultInstallPath()
                : initialPath
        };
        _browseButton = new Button
        {
            Text = "浏览...",
            Location = new Point(504, 86),
            Width = 90,
            Height = 28
        };
        _progressBar = new ProgressBar
        {
            Location = new Point(24, 132),
            Width = 570,
            Height = 18,
            Style = ProgressBarStyle.Marquee,
            Visible = false
        };
        _statusLabel = new Label
        {
            Location = new Point(24, 160),
            Width = 570,
            Height = 40,
            Text = "",
            ForeColor = Color.FromArgb(90, 95, 105)
        };
        _installButton = new Button
        {
            Text = "安装",
            Location = new Point(424, 208),
            Width = 82,
            Height = 30
        };
        _cancelButton = new Button
        {
            Text = "取消",
            Location = new Point(512, 208),
            Width = 82,
            Height = 30
        };

        Controls.AddRange(new Control[]
        {
            title, description, _pathText, _browseButton,
            _progressBar, _statusLabel, _installButton, _cancelButton
        });

        _browseButton.Click += BrowseButton_Click;
        _installButton.Click += InstallButton_Click;
        _cancelButton.Click += (_, _) => Close();
    }

    private void BrowseButton_Click(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择 XantaComfyLauncher 安装目录",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(_pathText.Text)
                ? _pathText.Text
                : Path.GetDirectoryName(_pathText.Text) ?? ""
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _pathText.Text = dialog.SelectedPath;
        }
    }

    private async void InstallButton_Click(object? sender, EventArgs e)
    {
        try
        {
            var target = InstallerEngine.NormalizeInstallPath(_pathText.Text);
            if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
            {
                var answer = MessageBox.Show(this,
                    "目标目录不是空的，安装程序可能会覆盖其中的文件。\r\n\r\n继续吗？",
                    "目录非空", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (answer != DialogResult.Yes) return;
            }

            SetBusy(true);
            var progress = new Progress<string>(text => _statusLabel.Text = text);
            var exitCode = await InstallerEngine.InstallAsync(target, progress);
            SetBusy(false);
            if (exitCode != 0)
            {
                MessageBox.Show(this, "安装程序未成功完成。", "安装失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _statusLabel.Text = "安装完成：" + target;
            MessageBox.Show(this,
                "XantaComfyLauncher 已安装到：\r\n\r\n" + target +
                "\r\n\r\n后续启动器会自行检查并安装更新。",
                "安装完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
        }
        catch (Exception ex)
        {
            SetBusy(false);
            _statusLabel.Text = "安装失败";
            MessageBox.Show(this, ex.Message, "安装失败",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SetBusy(bool busy)
    {
        _pathText.Enabled = !busy;
        _browseButton.Enabled = !busy;
        _installButton.Enabled = !busy;
        _cancelButton.Enabled = !busy;
        _progressBar.Visible = busy;
        if (busy) _statusLabel.Text = "正在安装...";
    }
}
