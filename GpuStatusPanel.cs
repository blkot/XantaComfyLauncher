using XantaComfyLauncher.Models;

namespace XantaComfyLauncher;

public sealed class GpuStatusPanel : GroupBox
{
    private readonly ProgressBar _utilizationBar;
    private readonly ProgressBar _memoryBar;
    private readonly Label _utilizationValue;
    private readonly Label _memoryValue;
    private readonly Label _temperatureValue;

    public GpuStatusPanel()
    {
        Text = "GPU 实时状态";
        Dock = DockStyle.Fill;
        Padding = new Padding(8);
        Font = new Font("Segoe UI", 9F);
        BackColor = Color.FromArgb(245, 246, 248);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 4,
            Padding = new Padding(2)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _utilizationBar = MakeBar();
        _memoryBar = MakeBar();
        _utilizationValue = MakeValueLabel("--");
        _memoryValue = MakeValueLabel("--");
        _temperatureValue = new Label
        {
            Text = "--°C",
            AutoSize = false,
            Dock = DockStyle.Fill,
            Height = 22,
            Margin = new Padding(4, 6, 2, 0),
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font(Font, FontStyle.Bold),
            BackColor = Color.FromArgb(225, 228, 232),
            BorderStyle = BorderStyle.FixedSingle
        };
        layout.Controls.Add(MakeCaption("GPU 使用率"), 0, 0);
        layout.Controls.Add(MakeCaption("显存"), 1, 0);
        layout.Controls.Add(MakeCaption("温度"), 2, 0);
        layout.Controls.Add(_utilizationBar, 0, 1);
        layout.Controls.Add(_memoryBar, 1, 1);
        layout.Controls.Add(_temperatureValue, 2, 1);
        layout.Controls.Add(_utilizationValue, 0, 2);
        layout.Controls.Add(_memoryValue, 1, 2);
        Controls.Add(layout);
    }

    public void UpdateSnapshot(GpuSnapshot snapshot)
    {
        if (!snapshot.IsAvailable)
        {
            SetUnavailable(string.IsNullOrWhiteSpace(snapshot.Error) ? "GPU 监控不可用" : snapshot.Error);
            return;
        }

        _utilizationBar.Value = Math.Clamp(snapshot.UtilizationPercent ?? 0, 0, 100);
        _utilizationValue.Text = snapshot.UtilizationPercent is int util ? $"{util}%" : "--";

        var memPercent = snapshot.MemoryUsedMb is int used && snapshot.MemoryTotalMb is int total && total > 0
            ? (int)Math.Clamp(Math.Round(used * 100.0 / total), 0, 100)
            : 0;
        _memoryBar.Value = memPercent;
        _memoryValue.Text = snapshot.MemoryUsedMb is int usedMb && snapshot.MemoryTotalMb is int totalMb
            ? $"{usedMb / 1024.0:0.0} / {totalMb / 1024.0:0.0} GB"
            : "--";

        var temp = snapshot.TemperatureC;
        _temperatureValue.Text = temp is int t ? $"{t}°C" : "--°C";
        _temperatureValue.BackColor = temp switch
        {
            null => Color.FromArgb(225, 228, 232),
            <= 60 => Color.FromArgb(214, 245, 222),
            <= 80 => Color.FromArgb(255, 236, 200),
            _ => Color.FromArgb(255, 214, 214)
        };
        _temperatureValue.ForeColor = temp switch
        {
            null => Color.FromArgb(90, 95, 105),
            <= 60 => Color.FromArgb(24, 118, 60),
            <= 80 => Color.FromArgb(160, 92, 8),
            _ => Color.FromArgb(170, 35, 35)
        };

    }

    public void SetUnavailable(string message)
    {
        _utilizationBar.Value = 0;
        _memoryBar.Value = 0;
        _utilizationValue.Text = "--";
        _memoryValue.Text = "--";
        _temperatureValue.Text = "--°C";
        _temperatureValue.BackColor = Color.FromArgb(225, 228, 232);
        _temperatureValue.ForeColor = Color.FromArgb(90, 95, 105);
    }

    private static ProgressBar MakeBar() => new()
    {
        Dock = DockStyle.Fill,
        Height = 18,
        Minimum = 0,
        Maximum = 100,
        Margin = new Padding(4, 5, 4, 0),
        Style = ProgressBarStyle.Continuous
    };

    private static Label MakeCaption(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Color.FromArgb(70, 75, 85),
        Margin = new Padding(4, 0, 2, 0)
    };

    private static Label MakeValueLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = new Font("Segoe UI", 9F, FontStyle.Bold),
        ForeColor = Color.FromArgb(40, 44, 52),
        Margin = new Padding(4, 2, 2, 0)
    };
}
