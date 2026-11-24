using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using XantaComfyLauncher.Models;
using XantaComfyLauncher.Services;
using XantaComfyLauncher.Utils;

namespace XantaComfyLauncher.CoreHost;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly ConcurrentQueue<string> Logs = new();
    private static LauncherConfig _config = new();
    private static AppPaths? _paths;
    private static WorkflowProfileService? _profiles;
    private static ComfyProcessService? _process;
    private static string _state = "uninitialized";
    private static string _detail = "";
    private static string? _localUrl;
    private static int? _processId;
    private static string _token = "";

    public static async Task<int> Main(string[] args)
    {
        try
        {
            _config = new LauncherConfigService().LoadOrCreate();
            if (!AppPaths.TryCreate(_config.InstanceRoot, out _paths, out var pathError))
            {
                _state = "error";
                _detail = pathError;
            }
            else
            {
                var paths = _paths!;
                _profiles = new WorkflowProfileService(paths);
                _process = new ComfyProcessService(paths);
                _state = _process.State.ToString();
                _process.LogReceived += line =>
                {
                    Logs.Enqueue($"[{DateTime.Now:HH:mm:ss}] {line}");
                    while (Logs.Count > 500) Logs.TryDequeue(out _);
                };
                _process.StateChanged += state =>
                {
                    _state = state.ToString();
                    _processId = _process.ProcessId;
                };
                _process.Healthy += info =>
                {
                    _state = ComfyProcessState.Running.ToString();
                    _localUrl = info.LocalUrl;
                    _processId = _process.ProcessId;
                };
                _process.Exited += code =>
                {
                    _state = ComfyProcessState.Stopped.ToString();
                    _detail = $"ComfyUI exited: {code}";
                    _processId = null;
                };
            }

            _token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Console.Out.WriteLine($"COREHOST_READY {port} {_token}");
            Console.Out.Flush();

            if (args.Contains("--exit-on-stdin-eof", StringComparer.OrdinalIgnoreCase))
            {
                _ = Task.Run(() =>
                {
                    try { Console.In.ReadToEnd(); } catch { }
                    Environment.Exit(0);
                });
            }

            while (true)
            {
                var client = await listener.AcceptTcpClientAsync();
                _ = Task.Run(() => HandleClientAsync(client));
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("COREHOST_FATAL " + ex);
            return 1;
        }
    }

    private static async Task HandleClientAsync(TcpClient client)
    {
        try
        {
            using (client)
            using (var stream = client.GetStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true))
            {
                var requestLine = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(requestLine)) return;
                var parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) return;
                var method = parts[0];
                var path = parts[1];

                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string? line;
                while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync()))
                {
                    var index = line.IndexOf(':');
                    if (index > 0) headers[line[..index].Trim()] = line[(index + 1)..].Trim();
                }

                if (!headers.TryGetValue("X-Xanta-Token", out var token) || token != _token)
                {
                    await WriteJsonAsync(stream, 401, Error("unauthorized", "missing or invalid token"));
                    return;
                }

                try
                {
                    var data = await RouteAsync(method, path);
                    await WriteJsonAsync(stream, 200, new { ok = true, data, error = (object?)null });
                }
                catch (Exception ex)
                {
                    await WriteJsonAsync(stream, 200, Error("operation_failed", ex.Message));
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("COREHOST_REQUEST_ERROR " + ex.Message);
        }
    }

    private static async Task<object> RouteAsync(string method, string rawPath)
    {
        var split = rawPath.Split('?', 2);
        var path = split[0];
        var query = split.Length == 2 ? split[1] : "";

        if (method == "GET" && path == "/health")
            return new { state = _state, running = _process?.IsRunning ?? false };

        if (method == "GET" && path == "/status")
            return Status();

        if (method == "GET" && path == "/config")
            return new
            {
                instanceRoot = _paths?.InstanceRoot,
                port = _config.Port,
                listenMode = _config.ListenMode.ToString(),
                error = _paths == null ? _detail : null
            };

        if (method == "GET" && path == "/logs")
        {
            var tail = ParseTail(query);
            return new { lines = Logs.ToArray().TakeLast(tail).ToArray() };
        }

        if (method == "POST" && path == "/start")
        {
            if (_process == null) throw new InvalidOperationException(_detail);
            if (_process.IsRunning) return Status();
            _detail = "starting ComfyUI";
            var whitelist = _profiles?.ResolveActiveWhitelist();
            await _process.StartAsync(_config, whitelist?.Packages);
            _processId = _process.ProcessId;
            return Status();
        }

        if (method == "POST" && path == "/stop")
        {
            if (_process == null) throw new InvalidOperationException(_detail);
            await _process.StopAsync();
            _processId = null;
            _detail = "";
            return Status();
        }

        throw new InvalidOperationException($"unknown route: {method} {path}");
    }

    private static object Status()
    {
        return new
        {
            state = _state,
            running = _process?.IsRunning ?? false,
            detail = _detail,
            localUrl = _localUrl,
            processId = _processId,
            instanceRoot = _paths?.InstanceRoot
        };
    }

    private static int ParseTail(string query)
    {
        foreach (var item in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = item.Split('=', 2);
            if (pair.Length == 2 && pair[0] == "tail" && int.TryParse(pair[1], out var value))
                return Math.Clamp(value, 1, 500);
        }
        return 100;
    }

    private static object Error(string code, string message) =>
        new { ok = false, data = (object?)null, error = new { code, message } };

    private static async Task WriteJsonAsync(Stream stream, int statusCode, object body)
    {
        var json = JsonSerializer.Serialize(body, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        var reason = statusCode == 401 ? "Unauthorized" : "OK";
       
        var header = $"HTTP/1.1 {statusCode} {reason}\r\n" +
                     "Content-Type: application/json; charset=utf-8\r\n" +
                     $"Content-Length: {bytes.Length}\r\n" +
                     "Connection: close\r\n\r\n";
        var headerBytes = Encoding.ASCII.GetBytes(header);
        await stream.WriteAsync(headerBytes);
        await stream.WriteAsync(bytes);
    }
}
