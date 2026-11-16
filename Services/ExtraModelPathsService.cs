using XantaComfyLauncher.Models;
using XantaComfyLauncher.Utils;

namespace XantaComfyLauncher.Services;

public static class ExtraModelPathsService
{
    public static IReadOnlyList<ModelDirectoryEntry> GetModelDirectories(AppPaths paths)
    {
        var file = paths.ExtraModelPathsFile;
        if (!File.Exists(file)) return Array.Empty<ModelDirectoryEntry>();

        var result = new Dictionary<string, ModelDirectoryEntry>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var lines = File.ReadAllLines(file);
            var section = "comfyui";
            var basePath = "";
            var i = 0;
            while (i < lines.Length)
            {
                var raw = lines[i];
                var trimmed = raw.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                {
                    i++;
                    continue;
                }

                var indent = raw.Length - raw.TrimStart().Length;
                if (indent == 0 && trimmed.EndsWith(':'))
                {
                    section = trimmed.TrimEnd(':').Trim();
                    basePath = "";
                    i++;
                    continue;
                }

                var colon = trimmed.IndexOf(':');
                if (colon <= 0)
                {
                    i++;
                    continue;
                }

                var key = trimmed[..colon].Trim();
                var value = trimmed[(colon + 1)..].Trim();
                if (string.Equals(key, "base_path", StringComparison.OrdinalIgnoreCase))
                {
                    basePath = Unquote(value);
                    i++;
                    continue;
                }

                if (string.Equals(key, "is_default", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(key, "custom_nodes", StringComparison.OrdinalIgnoreCase))
                {
                    i++;
                    continue;
                }

                var configuredPaths = new List<string>();
                if (value is "|" or ">")
                {
                    var keyIndent = indent;
                    i++;
                    while (i < lines.Length)
                    {
                        var next = lines[i];
                        var nextTrimmed = next.Trim();
                        var nextIndent = next.Length - next.TrimStart().Length;
                        if (nextTrimmed.Length > 0 && nextIndent <= keyIndent) break;
                        if (nextTrimmed.Length == 0 || nextTrimmed.StartsWith('#'))
                        {
                            i++;
                            continue;
                        }
                        configuredPaths.Add(Unquote(nextTrimmed));
                        i++;
                    }
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(value)) configuredPaths.Add(Unquote(value));
                    i++;
                }

                foreach (var configuredPath in configuredPaths)
                {
                    var resolved = ResolvePath(paths, basePath, configuredPath);
                    if (resolved == null) continue;
                    if (!result.TryGetValue(resolved, out var entry))
                    {
                        entry = new ModelDirectoryEntry { Section = section, Path = resolved };
                        result[resolved] = entry;
                    }
                    if (!entry.Categories.Contains(key, StringComparer.OrdinalIgnoreCase))
                    {
                        entry.Categories.Add(key);
                    }
                }
            }
        }
        catch
        {
            // A malformed or unavailable YAML file must not break the launcher.
        }

        return result.Values
            .OrderBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? ResolvePath(AppPaths paths, string basePath, string configuredPath)
    {
        try
        {
            var value = Environment.ExpandEnvironmentVariables(Unquote(configuredPath));
            if (string.IsNullOrWhiteSpace(value)) return null;

            string full;
            if (Path.IsPathRooted(value))
            {
                full = Path.GetFullPath(value);
            }
            else
            {
                var root = string.IsNullOrWhiteSpace(basePath)
                    ? paths.ComfyDir
                    : Path.IsPathRooted(basePath)
                        ? basePath
                        : Path.Combine(paths.ComfyDir, basePath);
                full = Path.GetFullPath(Path.Combine(root, value));
            }

            return Directory.Exists(full) ? full : null;
        }
        catch
        {
            return null;
        }
    }

    private static string Unquote(string value)
    {
        var text = (value ?? "").Trim();
        if (text.Length >= 2 &&
            ((text[0] == '"' && text[^1] == '"') || (text[0] == '\'' && text[^1] == '\'')))
        {
            text = text[1..^1];
        }
        return text.Trim();
    }
}
