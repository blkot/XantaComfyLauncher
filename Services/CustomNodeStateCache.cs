using XantaComfyLauncher.Models;

namespace XantaComfyLauncher.Services;

public sealed record CachedCustomNodeStates(
    IReadOnlyList<CustomNodeState> States,
    DateTimeOffset UpdatedAt,
    bool RemoteChecked);

public static class CustomNodeStateCache
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, CachedCustomNodeStates> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    public static bool TryGet(string instanceRoot, out CachedCustomNodeStates cached)
    {
        lock (Gate)
        {
            if (Cache.TryGetValue(instanceRoot, out var value))
            {
                cached = value;
                return true;
            }
        }
        cached = null!;
        return false;
    }

    public static void Set(
        string instanceRoot,
        IReadOnlyList<CustomNodeState> states,
        bool remoteChecked)
    {
        lock (Gate)
        {
            Cache[instanceRoot] = new CachedCustomNodeStates(
                states.ToList(),
                DateTimeOffset.Now,
                remoteChecked);
        }
    }

    public static void Clear(string instanceRoot)
    {
        lock (Gate) Cache.Remove(instanceRoot);
    }
}
