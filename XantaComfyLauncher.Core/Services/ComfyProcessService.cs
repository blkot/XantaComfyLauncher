using System.Diagnostics;
using System.Net.Http;
using System.Net.NetworkInformation;
using XantaComfyLauncher.Models;
using XantaComfyLauncher.Utils;

namespace XantaComfyLauncher.Services;

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
    private readonly object _runtimeLogGate = new();
    private Process? _process;
    private StreamWriter? _runtimeLog;
    private CancellationTokenSource? _monitorCts;
    private Task? _monitorTask;
    private LauncherConfig? _currentConfig;
    private bool _healthyAnnounced;
    private int? _processId;

    public ComfyProcessService(AppPaths paths) => _paths = paths;

    public event Action<string>? LogReceived;
    public event Action<ComfyProcessState>? StateChanged;
    public event Action<ComfyRunInfo>? Healthy;
    public event Action<int>? Exited;

    public ComfyProcessState State { get; private set; } = ComfyProcessState.Stopped;
    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                if (_process == null) return false;
                try
                {
                    return !_process.HasExited;
                }
                catch (InvalidOperationException)
                {
                    // Process object exists but Start() has not completed yet.
                    return false;
                }
            }
        }
    }

    public LauncherConfig? CurrentConfig => _currentConfig;
    public string? RuntimeLogPath { get; private set; }
    public int? ProcessId
    {
        get
        {
            lock (_gate) return _processId;
        }
    }

    public Task StartAsync(LauncherConfig config, CancellationToken ct = default)
        => StartAsync(config, null, ct);

    public async Task StartAsync(
        LauncherConfig config,
        IReadOnlyList<string>? customNodeWhitelist,
        CancellationToken ct = default)
    {
        if (IsRunning) throw new InvalidOperationException("ComfyUI 已在运行。");
        if (!File.Exists(_paths.PythonExe)) throw new FileNotFoundException("找不到实例 Python。", _paths.PythonExe);
        if (!File.Exists(_paths.MainScript)) throw new FileNotFoundException("找不到 ComfyUI main.py。", _paths.MainScript);
        _paths.EnsureDirectories();

        if (IsPortInUse(config.Port))
        {
            throw new InvalidOperationException($"端口 {config.Port} 已被占用，请更换端口或先停止占用程序。");
        }

        OpenRuntimeLog();
        var listenAddress = config.ListenMode == ListenMode.Lan ? "0.0.0.0" : "127.0.0.1";
        var args = new List<string>
        {
            _paths.MainScript,
            "--listen", listenAddress,
            "--port", config.Port.ToString(),
            "--temp-directory", _paths.ComfyTempDir,
            "--disable-auto-launch"
        };
        if (customNodeWhitelist != null)
        {
            args.Add("--disable-all-custom-nodes");
            var packages = customNodeWhitelist
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (packages.Count > 0)
            {
                args.Add("--whitelist-custom-nodes");
                args.AddRange(packages);
            }
        }
        if (config.UseSageAttention) args.Add("--use-sage-attention");
        var extraArgs = ArgumentTokenizer.Split(config.ExtraArguments).ToList();
        if (config.UseFp16Accumulation && !extraArgs.Contains("--fast", StringComparer.Ordinal))
        {
            args.Add("--fast");
            args.Add("fp16_accumulation");
        }
        args.AddRange(extraArgs);

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
            if (e.Data == null) return;
            WriteRuntimeLog("stdout", e.Data);
            LogReceived?.Invoke("[ComfyUI] " + e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            WriteRuntimeLog("stderr", e.Data);
            LogReceived?.Invoke("[ComfyUI] " + e.Data);
        };

        lock (_gate)
        {
            _process = process;
            _currentConfig = config;
            _healthyAnnounced = false;
        }

        LogReceived?.Invoke($"[launcher] 启动：{_paths.PythonExe}");
        LogReceived?.Invoke($"[launcher] 工作目录：{_paths.ComfyDir}");
        LogReceived?.Invoke($"[launcher] 监听：{listenAddress}:{config.Port}");
        if (config.UseSageAttention) LogReceived?.Invoke("[launcher] 已启用 SageAttention");
        if (config.UseFp16Accumulation)
            LogReceived?.Invoke("[launcher] 已启用 FP16 累加（--fast fp16_accumulation）");
        if (customNodeWhitelist != null)
            LogReceived?.Invoke("[launcher] 节点加载：按工作流白名单（" +
                (customNodeWhitelist.Count == 0 ? "仅核心节点" : string.Join(", ", customNodeWhitelist)) + "）");
        LogReceived?.Invoke("[launcher] " + string.Join(" ", args.Select(QuoteForLog)));

        try
        {
            if (!process.Start()) throw new InvalidOperationException("无法启动 ComfyUI 进程。");
            lock (_gate) _processId = process.Id;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            SetState(ComfyProcessState.Starting);
        }
        catch
        {
            SetState(ComfyProcessState.Error);
            process.Dispose();
            CloseRuntimeLog();
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
            CloseRuntimeLog();
            return;
        }

        SetState(ComfyProcessState.Stopping);
        monitorCts?.Cancel();
        try
        {
            if (!SafeHasExited(process))
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
            _processId = null;
            _monitorCts?.Dispose();
            _monitorCts = null;
            _monitorTask = null;
        }
        SetState(ComfyProcessState.Stopped);
        LogReceived?.Invoke("[launcher] ComfyUI 已停止。");
        CloseRuntimeLog();
    }

    private async Task MonitorAsync(Process process, LauncherConfig config, CancellationToken ct)
    {
        var localUrl = _paths.GetLocalUrl(config.Port);
        var lanUrl = _paths.GetLanUrl(config.Port);
        while (!ct.IsCancellationRequested)
        {
            if (process.HasExited)
            {
                var exitCode = SafeExitCode(process);
                lock (_gate) _processId = null;
                WriteRuntimeLog("launcher", $"ComfyUI process exited with code {exitCode}");
                LogReceived?.Invoke($"[launcher] ComfyUI 进程已退出，代码 {exitCode}。");
                Exited?.Invoke(exitCode);
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

    private static bool SafeHasExited(Process process)
    {
        try { return process.HasExited; }
        catch (InvalidOperationException) { return true; }
    }

    private static int SafeExitCode(Process process)
    {
        try { return process.ExitCode; } catch { return -1; }
    }

    private static string QuoteForLog(string value) =>
        value.Any(char.IsWhiteSpace) ? '"' + value.Replace("\"", "\\\"") + '"' : value;

    private void OpenRuntimeLog()
    {
        CloseRuntimeLog();
        try
        {
            Directory.CreateDirectory(_paths.LogsDir);
            RuntimeLogPath = Path.Combine(_paths.LogsDir,
                $"comfyui-{DateTime.Now:yyyyMMdd-HHmmss-fff}.log");
            var stream = new FileStream(RuntimeLogPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            _runtimeLog = new StreamWriter(stream, new System.Text.UTF8Encoding(false)) { AutoFlush = true };
            WriteRuntimeLog("launcher", $"ComfyUI runtime log started {DateTimeOffset.Now:O}");
        }
        catch (Exception ex)
        {
            _runtimeLog = null;
            RuntimeLogPath = null;
            LauncherLog.Write("comfyui-runtime", "无法创建 ComfyUI 运行日志：" + ex.Message);
        }
    }

    private void WriteRuntimeLog(string source, string message)
    {
        lock (_runtimeLogGate)
        {
            try
            {
                _runtimeLog?.WriteLine($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff}] [{source}] {message}");
            }
            catch
            {
                // Runtime logging must not affect ComfyUI process handling.
            }
        }
    }

    private void CloseRuntimeLog()
    {
        lock (_runtimeLogGate)
        {
            try { _runtimeLog?.Dispose(); } catch { }
            _runtimeLog = null;
        }
    }

    public void Dispose()
    {
        try { _monitorCts?.Cancel(); } catch { }
        try
        {
            if (_process != null && !SafeHasExited(_process)) _process.Kill(entireProcessTree: true);
        }
        catch { }
        _process?.Dispose();
        _monitorCts?.Dispose();
        lock (_gate) _processId = null;
        CloseRuntimeLog();
        HealthClient.Dispose();
    }
}
