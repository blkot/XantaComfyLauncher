using System.Net.Http.Json;
using System.Text.Json;
using XantaComfyLauncher.Models;

namespace XantaComfyLauncher.Services;

public sealed class ComfyApiService : IDisposable
{
    private readonly HttpClient _http;

    public ComfyApiService(string baseUrl)
    {
        BaseUrl = baseUrl.TrimEnd('/');
        _http = new HttpClient
        {
            BaseAddress = new Uri(BaseUrl + "/"),
            Timeout = TimeSpan.FromSeconds(15)
        };
    }

    public string BaseUrl { get; }

    public async Task<ComfyQueueSnapshot> GetQueueAsync(CancellationToken ct = default)
    {
        using var response = await _http.GetAsync("queue", ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        var root = doc.RootElement;
        return new ComfyQueueSnapshot
        {
            Running = ParseQueue(root, "queue_running", isRunning: true),
            Pending = ParseQueue(root, "queue_pending", isRunning: false)
        };
    }

    public async Task<IReadOnlyDictionary<string, string>> GetObjectInfoModulesAsync(
        CancellationToken ct = default)
    {
        using var response = await _http.GetAsync("object_info", ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return result;
        foreach (var node in doc.RootElement.EnumerateObject())
        {
            if (node.Value.ValueKind != JsonValueKind.Object ||
                !node.Value.TryGetProperty("python_module", out var module) ||
                module.ValueKind != JsonValueKind.String)
            {
                continue;
            }
            var moduleName = module.GetString();
            if (!string.IsNullOrWhiteSpace(moduleName))
                result[node.Name] = moduleName;
        }
        return result;
    }

    public async Task InterruptAsync(CancellationToken ct = default)
    {
        using var response = await _http.PostAsync("interrupt", null, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
    }

    public async Task ClearPendingAsync(CancellationToken ct = default)
    {
        using var response = await _http.PostAsJsonAsync("queue", new { clear = true }, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
    }

    public async Task DeletePendingAsync(IEnumerable<string> promptIds, CancellationToken ct = default)
    {
        var ids = promptIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToArray();
        if (ids.Length == 0) return;
        using var response = await _http.PostAsJsonAsync("queue", new { delete = ids }, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
    }

    private static IReadOnlyList<ComfyQueueItem> ParseQueue(
        JsonElement root,
        string propertyName,
        bool isRunning)
    {
        if (!root.TryGetProperty(propertyName, out var queue) || queue.ValueKind != JsonValueKind.Array)
            return Array.Empty<ComfyQueueItem>();

        var items = new List<ComfyQueueItem>();
        foreach (var entry in queue.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Array || entry.GetArrayLength() < 3) continue;
            var number = entry[0].TryGetInt64(out var parsedNumber) ? parsedNumber : (long?)null;
            var promptId = entry[1].ValueKind == JsonValueKind.String
                ? entry[1].GetString() ?? ""
                : "";
            var (nodeCount, summary) = ParsePrompt(entry[2]);
            items.Add(new ComfyQueueItem(isRunning, number, promptId, nodeCount, summary));
        }
        return items;
    }

    private static (int NodeCount, string Summary) ParsePrompt(JsonElement prompt)
    {
        if (prompt.ValueKind != JsonValueKind.Object) return (0, "");
        var nodeCount = prompt.EnumerateObject().Count();
        var classTypes = prompt.EnumerateObject()
            .Select(property =>
                property.Value.ValueKind == JsonValueKind.Object &&
                property.Value.TryGetProperty("class_type", out var classType) &&
                classType.ValueKind == JsonValueKind.String
                    ? classType.GetString()
                    : null)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToList();
        return (nodeCount, string.Join(", ", classTypes));
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        throw new InvalidOperationException(
            $"ComfyUI API 返回 {(int)response.StatusCode} {response.ReasonPhrase}" +
            (string.IsNullOrWhiteSpace(body) ? "" : "：" + body));
    }

    public void Dispose() => _http.Dispose();
}
