using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using XantaComfyLauncher.Models;
using XantaComfyLauncher.Utils;

namespace XantaComfyLauncher.Services;

public sealed class ComfyWebSocketService : IDisposable
{
    private readonly string _wsUrl;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _gate = new();
    private Task? _loopTask;
    private ClientWebSocket? _socket;
    private IReadOnlyDictionary<string, string> _nodeTypes = new Dictionary<string, string>();
    private DateTimeOffset? _nodeStartedAt;
    private string _currentNodeId = "";
    private string _currentPromptId = "";

    public ComfyWebSocketService(string baseUrl)
    {
        var wsBase = baseUrl.TrimEnd('/')
            .Replace("https://", "wss://", StringComparison.OrdinalIgnoreCase)
            .Replace("http://", "ws://", StringComparison.OrdinalIgnoreCase);
        _wsUrl = wsBase + "/ws?clientId=" + Guid.NewGuid().ToString("N");
    }

    public event Action<ComfyExecutionProgress>? ProgressChanged;
    public event Action<string>? ConnectionStatusChanged;

    public void UpdateNodeTypes(IReadOnlyDictionary<string, string>? nodeTypes)
    {
        lock (_gate) _nodeTypes = nodeTypes ?? new Dictionary<string, string>();
    }

    public void Start()
    {
        if (_loopTask != null) return;
        _loopTask = Task.Run(() => RunAsync(_cts.Token));
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var retryDelay = TimeSpan.FromSeconds(2);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var socket = new ClientWebSocket();
                lock (_gate) _socket = socket;
                await socket.ConnectAsync(new Uri(_wsUrl), ct).ConfigureAwait(false);
                ConnectionStatusChanged?.Invoke("实时进度已连接");
                await ReceiveLoopAsync(socket, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                ConnectionStatusChanged?.Invoke("实时进度不可用");
                LauncherLog.Write("websocket", "连接断开：" + ex.Message);
            }
            finally
            {
                lock (_gate) _socket = null;
            }

            try { await Task.Delay(retryDelay, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        using var message = new MemoryStream();
        while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            message.SetLength(0);
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    ConnectionStatusChanged?.Invoke("实时进度已断开");
                    return;
                }
                message.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            if (result.MessageType != WebSocketMessageType.Text) continue;
            var json = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
            TryHandleMessage(json);
        }
    }

    private void TryHandleMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeElement) ||
                typeElement.ValueKind != JsonValueKind.String)
            {
                return;
            }
            var type = typeElement.GetString() ?? "";
            var data = root.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object
                ? d
                : default;
            if (data.ValueKind != JsonValueKind.Object) return;

            var promptId = GetString(data, "prompt_id");
            if (!string.IsNullOrWhiteSpace(promptId)) _currentPromptId = promptId;

            switch (type)
            {
                case "execution_start":
                    _nodeStartedAt = null;
                    _currentNodeId = "";
                    Emit(new ComfyExecutionProgress
                    {
                        PromptId = _currentPromptId,
                        Status = "开始执行",
                        Percent = 0
                    });
                    break;
                case "executing":
                {
                    var node = GetString(data, "node");
                    if (string.IsNullOrWhiteSpace(node))
                    {
                        _currentNodeId = "";
                        _nodeStartedAt = null;
                        Emit(new ComfyExecutionProgress
                        {
                            PromptId = _currentPromptId,
                            Status = "执行完成",
                            Percent = 100
                        });
                        break;
                    }
                    _currentNodeId = node;
                    _nodeStartedAt = DateTimeOffset.Now;
                    Emit(new ComfyExecutionProgress
                    {
                        PromptId = _currentPromptId,
                        NodeId = node,
                        NodeClassType = ResolveNodeType(node),
                        Status = "节点执行中",
                        Percent = 0
                    });
                    break;
                }
                case "progress":
                {
                    var value = GetInt(data, "value") ?? 0;
                    var max = GetInt(data, "max") ?? 0;
                    var percent = max > 0 ? Math.Clamp(value * 100.0 / max, 0, 100) : 0;
                    var elapsed = _nodeStartedAt is DateTimeOffset started
                        ? DateTimeOffset.Now - started
                        : (TimeSpan?)null;
                    TimeSpan? eta = null;
                    if (elapsed is TimeSpan e && value > 0 && max > value)
                        eta = TimeSpan.FromSeconds(e.TotalSeconds * (max - value) / value);
                    var nodeId = GetString(data, "node") ?? _currentNodeId;
                    Emit(new ComfyExecutionProgress
                    {
                        PromptId = _currentPromptId,
                        NodeId = nodeId,
                        NodeClassType = ResolveNodeType(nodeId),
                        Status = "节点进度",
                        Value = value,
                        Max = max,
                        Percent = percent,
                        NodeElapsed = elapsed,
                        Eta = eta
                    });
                    break;
                }
                case "executed":
                case "execution_cached":
                {
                    var node = GetString(data, "node");
                    Emit(new ComfyExecutionProgress
                    {
                        PromptId = _currentPromptId,
                        NodeId = node ?? _currentNodeId,
                        NodeClassType = ResolveNodeType(node ?? _currentNodeId),
                        Status = type == "execution_cached" ? "节点命中缓存" : "节点完成",
                        Percent = 0
                    });
                    break;
                }
                case "execution_error":
                {
                    var message = GetString(data, "exception_message")
                        ?? GetString(data, "exception_type")
                        ?? "未知执行错误";
                    Emit(new ComfyExecutionProgress
                    {
                        PromptId = _currentPromptId,
                        NodeId = GetString(data, "node_id") ?? _currentNodeId,
                        Status = "执行失败",
                        Error = message
                    });
                    break;
                }
                case "execution_interrupted":
                    Emit(new ComfyExecutionProgress
                    {
                        PromptId = _currentPromptId,
                        Status = "已中断"
                    });
                    break;
                case "status":
                {
                    var remaining = 0L;
                    if (data.TryGetProperty("status", out var status) &&
                        status.TryGetProperty("exec_info", out var execInfo) &&
                        execInfo.TryGetProperty("queue_remaining", out var queueRemaining) &&
                        queueRemaining.TryGetInt64(out var parsed))
                    {
                        remaining = parsed;
                    }
                    Emit(new ComfyExecutionProgress
                    {
                        PromptId = _currentPromptId,
                        Status = "队列状态",
                        QueueRemaining = remaining
                    });
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            LauncherLog.Write("websocket", "解析执行消息失败：" + ex.Message);
        }
    }

    private string ResolveNodeType(string nodeId)
    {
        lock (_gate)
        {
            return _nodeTypes.TryGetValue(nodeId, out var type) ? type : "";
        }
    }

    private void Emit(ComfyExecutionProgress progress) => ProgressChanged?.Invoke(progress);

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? GetInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var parsed)
            ? parsed
            : null;

    public void Dispose()
    {
        try { _cts.Cancel(); } catch { }
        try { _socket?.Abort(); } catch { }
        try { _socket?.Dispose(); } catch { }
        try { _loopTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _cts.Dispose();
    }
}
