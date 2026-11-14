using System.Diagnostics;
using System.Net.Http;
using System.Net.NetworkInformation;
using ComfyLauncher.Models;
using ComfyLauncher.Utils;

namespace ComfyLauncher.Services;

public enum ComfyProcessState
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Error
}

public sealed record ComfyRunInfo(string LocalUrl, string LanUrl, int Port);

public sealed class ComfyProcessService : IDisposable
{
    private static readonly HttpClient HealthClient = new() { Timeout = TimeSpan.FromSeconds(2) };
    private readonly AppPaths _paths;
    private readonly object _gate = new();
    private Process? _process;
    private CancellationTokenSource? _monitorCts;
    private Task? _monitorTask;
    private LauncherConfig? _currentConfig;
    private bool _healthyAnnounced;

    public ComfyProcessService(AppPaths paths) => _paths = paths;

    public event Action<string>? LogReceived;
    public event Action<ComfyProcessState>? StateChanged;
    public event Action<ComfyRunInfo>? Healthy;

    public ComfyProcessState State { get; private set; } = ComfyProcessState.Stopped;
    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _process is { HasExited: false };
            }
        }
    }

    public LauncherConfig? CurrentConfig => _currentConfig;

    public async Task StartAsync(LauncherConfig config, CancellationToken ct = default)
    {
        if (IsRunning) throw new InvalidOperationException("ComfyUI 已在运行。");
        if (!File.Exists(_paths.PythonExe)) throw new FileNotFoundException("找不到实例 Python。", _paths.PythonExe);
        if (!File.Exists(_paths.MainScript)) throw new FileNotFoundException("找不到 ComfyUI main.py。", _paths.MainScript);
        _paths.EnsureDirectories();

        if (IsPortInUse(config.Port))
        {
            throw new InvalidOperationException($"端口 {config.Port} 已被占用，请更换端口或先停止占用程序。");
        }

        var listenAddress = config.ListenMode == ListenMode.Lan ? "0.0.0.0" : "127.0.0.1";
        var args = new List<string>
        {
            _paths.MainScript,
            "--listen", listenAddress,
            "--port", config.Port.ToString(),
            "--temp-directory", _paths.ComfyTempDir,
            "--disable-auto-launch"
        };
        if (config.UseSageAttention) args.Add("--use-sage-attention");
        args.AddRange(ArgumentTokenizer.Split(config.ExtraArguments));

        var psi = new ProcessStartInfo
        {
            FileName = _paths.PythonExe,
            WorkingDirectory = _paths.ComfyDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        InstanceEnvironment.Apply(psi, _paths);

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null) LogReceived?.Invoke("[ComfyUI] " + e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) LogReceived?.Invoke("[ComfyUI:err] " + e.Data);
        };

        lock (_gate)
        {
            _process = process;
            _currentConfig = config;
            _healthyAnnounced = false;
        }

        SetState(ComfyProcessState.Starting);
        LogReceived?.Invoke($"[launcher] 启动：{_paths.PythonExe}");
        LogReceived?.Invoke($"[launcher] 工作目录：{_paths.ComfyDir}");
        LogReceived?.Invoke($"[launcher] 监听：{listenAddress}:{config.Port}");
        if (config.UseSageAttention) LogReceived?.Invoke("[launcher] 已启用 SageAttention");
        LogReceived?.Invoke("[launcher] " + string.Join(" ", args.Select(QuoteForLog)));

        try
        {
            if (!process.Start()) throw new InvalidOperationException("无法启动 ComfyUI 进程。");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch
        {
            SetState(ComfyProcessState.Error);
            process.Dispose();
            throw;
        }

        _monitorCts = new CancellationTokenSource();
        _monitorTask = MonitorAsync(process, config, _monitorCts.Token);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    public async Task StopAsync()
    {
        Process? process;
        CancellationTokenSource? monitorCts;
        Task? monitorTask;
        lock (_gate)
        {
            process = _process;
            monitorCts = _monitorCts;
            monitorTask = _monitorTask;
        }

        if (process == null)
        {
            SetState(ComfyProcessState.Stopped);
            return;
        }

        SetState(ComfyProcessState.Stopping);
        monitorCts?.Cancel();
        try
        {
            if (!process.HasExited)
            {
                LogReceived?.Invoke("[launcher] 正在停止 ComfyUI 进程树...");
                process.Kill(entireProcessTree: true);
                using var waitCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await process.WaitForExitAsync(waitCts.Token).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            LogReceived?.Invoke("[launcher] 停止时出现问题：" + ex.Message);
        }

        if (monitorTask != null)
        {
            try { await monitorTask.ConfigureAwait(false); } catch { }
        }

        lock (_gate)
        {
            _process?.Dispose();
            _process = null;
            _monitorCts?.Dispose();
            _monitorCts = null;
            _monitorTask = null;
        }
        SetState(ComfyProcessState.Stopped);
        LogReceived?.Invoke("[launcher] ComfyUI 已停止。");
    }

    private async Task MonitorAsync(Process process, LauncherConfig config, CancellationToken ct)
    {
        var localUrl = _paths.GetLocalUrl(config.Port);
        var lanUrl = _paths.GetLanUrl(config.Port);
        while (!ct.IsCancellationRequested)
        {
            if (process.HasExited)
            {
                LogReceived?.Invoke($"[launcher] ComfyUI 进程已退出，代码 {SafeExitCode(process)}。");
                SetState(ComfyProcessState.Stopped);
                return;
            }

            if (!_healthyAnnounced)
            {
                try
                {
                    using var response = await HealthClient.GetAsync(localUrl + "/system_stats", ct)
                        .ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        _healthyAnnounced = true;
                        SetState(ComfyProcessState.Running);
                        LogReceived?.Invoke($"[launcher] 服务已就绪：{localUrl}");
                        Healthy?.Invoke(new ComfyRunInfo(localUrl, lanUrl, config.Port));
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch
                {
                    // Server may still be starting.
                }
            }

            try
            {
                await Task.Delay(1000, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private void SetState(ComfyProcessState state)
    {
        if (State == state) return;
        State = state;
        StateChanged?.Invoke(state);
    }

    private static bool IsPortInUse(int port)
    {
        try
        {
            return IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveTcpListeners()
                .Any(x => x.Port == port);
        }
        catch
        {
            return false;
        }
    }

    private static int SafeExitCode(Process process)
    {
        try { return process.ExitCode; } catch { return -1; }
    }

    private static string QuoteForLog(string value) =>
        value.Any(char.IsWhiteSpace) ? '"' + value.Replace("\"", "\\\"") + '"' : value;

    public void Dispose()
    {
        try { _monitorCts?.Cancel(); } catch { }
        try
        {
            if (_process is { HasExited: false }) _process.Kill(entireProcessTree: true);
        }
        catch { }
        _process?.Dispose();
        _monitorCts?.Dispose();
        HealthClient.Dispose();
    }
}
