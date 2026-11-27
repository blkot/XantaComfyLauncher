using XantaComfyLauncher.Models;

namespace XantaComfyLauncher;

public sealed class GpuStatusPanel : GroupBox
{
    private readonly ProgressBar _cpuBar;
    private readonly ProgressBar _memoryBar;
    private readonly ProgressBar _gpuBar;
    private readonly ProgressBar _vramBar;
    private readonly Label _cpuValue;
    private readonly Label _memoryValue;
    private readonly Label _gpuValue;
    private readonly Label _vramValue;
    private readonly Label _temperatureValue;

    public GpuStatusPanel()
    {
        Text = "系统实时状态";
        Dock = DockStyle.Fill;
        Padding = new Padding(8);
        Font = new Font("Segoe UI", 9F);
        BackColor = Color.FromArgb(245, 246, 248);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 3,
            Padding = new Padding(2)
        };
        for (var i = 0; i < 5; i++)
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _cpuBar = MakeBar();
        _memoryBar = MakeBar();
        _gpuBar = MakeBar();
        _vramBar = MakeBar();
        _cpuValue = MakeValueLabel("--");
        _memoryValue = MakeValueLabel("--");
        _gpuValue = MakeValueLabel("--");
        _vramValue = MakeValueLabel("--");
        _temperatureValue = new Label
        {
            Text = "--°C",
            AutoSize = false,
            Dock = DockStyle.Fill,
            Height = 22,
            Margin = new Padding(4, 5, 4, 0),
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font(Font, FontStyle.Bold),
            BackColor = Color.FromArgb(225, 228, 232),
            BorderStyle = BorderStyle.FixedSingle
        };

        layout.Controls.Add(MakeCaption("CPU"), 0, 0);
        layout.Controls.Add(MakeCaption("内存"), 1, 0);
        layout.Controls.Add(MakeCaption("GPU"), 2, 0);
        layout.Controls.Add(MakeCaption("显存"), 3, 0);
        layout.Controls.Add(MakeCaption("温度"), 4, 0);
        layout.Controls.Add(_cpuBar, 0, 1);
        layout.Controls.Add(_memoryBar, 1, 1);
        layout.Controls.Add(_gpuBar, 2, 1);
        layout.Controls.Add(_vramBar, 3, 1);
        layout.Controls.Add(_temperatureValue, 4, 1);
        layout.Controls.Add(_cpuValue, 0, 2);
        layout.Controls.Add(_memoryValue, 1, 2);
        layout.Controls.Add(_gpuValue, 2, 2);
        layout.Controls.Add(_vramValue, 3, 2);
        Controls.Add(layout);
    }

    public void UpdateSnapshot(GpuSnapshot gpu, SystemStatusSnapshot system)
    {
        if (system.IsAvailable)
        {
            _cpuBar.Value = Math.Clamp(system.CpuPercent, 0, 100);
            _cpuValue.Text = $"{system.CpuPercent}%";
            _memoryBar.Value = Math.Clamp(system.MemoryPercent, 0, 100);
            _memoryValue.Text = $"{system.MemoryUsedMb / 1024.0:0.0} / {system.MemoryTotalMb / 1024.0:0.0} GB";
        }
        else
        {
            _cpuBar.Value = 0;
            _memoryBar.Value = 0;
            _cpuValue.Text = "--";
            _memoryValue.Text = "--";
        }

        if (!gpu.IsAvailable)
        {
            _gpuBar.Value = 0;
            _vramBar.Value = 0;
            _gpuValue.Text = "--";
            _vramValue.Text = "--";
            _temperatureValue.Text = "--°C";
            _temperatureValue.BackColor = Color.FromArgb(225, 228, 232);
            _temperatureValue.ForeColor = Color.FromArgb(90, 95, 105);
            return;
        }

        _gpuBar.Value = Math.Clamp(gpu.UtilizationPercent ?? 0, 0, 100);
        _gpuValue.Text = gpu.UtilizationPercent is int util ? $"{util}%" : "--";

        var vramPercent = gpu.MemoryUsedMb is int used && gpu.MemoryTotalMb is int total && total > 0
            ? (int)Math.Clamp(Math.Round(used * 100.0 / total), 0, 100)
            : 0;
        _vramBar.Value = vramPercent;
        _vramValue.Text = gpu.MemoryUsedMb is int usedMb && gpu.MemoryTotalMb is int totalMb
            ? $"{usedMb / 1024.0:0.0} / {totalMb / 1024.0:0.0} GB"
            : "--";

        var temp = gpu.TemperatureC;
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
        _cpuBar.Value = 0;
        _memoryBar.Value = 0;
        _gpuBar.Value = 0;
        _vramBar.Value = 0;
        _cpuValue.Text = "--";
        _memoryValue.Text = "--";
        _gpuValue.Text = "--";
        _vramValue.Text = "--";
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
