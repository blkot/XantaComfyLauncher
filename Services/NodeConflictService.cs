using System.Text.Json;
using XantaComfyLauncher.Models;
using XantaComfyLauncher.Utils;

namespace XantaComfyLauncher.Services;

public sealed class NodeConflictService
{
    private readonly AppPaths _paths;

    public NodeConflictService(AppPaths paths) => _paths = paths;

    public async Task<IReadOnlyList<NodeConflict>> ScanAsync(
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var conflicts = new List<NodeConflict>();
        var roots = GetCustomNodeRoots();
        progress?.Report($"正在扫描 {roots.Count} 个 custom_nodes 根目录...");

        var packagesByName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            foreach (var dir in Directory.GetDirectories(root))
            {
                var name = Path.GetFileName(dir);
                if (string.IsNullOrWhiteSpace(name) || name == "__pycache__" ||
                    name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)) continue;
                if (!packagesByName.TryGetValue(name, out var list))
                {
                    list = new List<string>();
                    packagesByName[name] = list;
                }
                list.Add(dir);
            }
        }

        foreach (var pair in packagesByName.Where(p => p.Value.Count > 1))
        {
            conflicts.Add(new NodeConflict
            {
                Type = "重复包目录",
                PackageA = pair.Value[0],
                PackageB = pair.Value[1],
                Details = $"多个 custom_nodes 根目录存在同名目录：{pair.Key}"
            });
        }

        var byType = new Dictionary<string, List<(string Package, string Root)>>(StringComparer.OrdinalIgnoreCase);
        var script = Path.Combine(AppContext.BaseDirectory, "Assets", "scan_node_conflicts.py");
        if (!File.Exists(script))
        {
            conflicts.Add(new NodeConflict
            {
                Type = "扫描失败",
                Details = "缺少 Assets/scan_node_conflicts.py，无法检测重复节点类型。"
            });
        }
        else
        {
            try
            {
                var args = new List<string> { script };
                args.AddRange(roots);
                progress?.Report("正在执行 AST 节点类型扫描...");
                var result = await ProcessRunner.RunAsync(
                    _paths.PythonExe, args, _paths.ComfyDir, cancellationToken: ct).ConfigureAwait(false);
                if (result.Success && !string.IsNullOrWhiteSpace(result.StandardOutput))
                {
                    using var doc = JsonDocument.Parse(result.StandardOutput);
                    foreach (var rootEntry in doc.RootElement.EnumerateObject())
                    {
                        foreach (var packageEntry in rootEntry.Value.EnumerateObject())
                        {
                            foreach (var typeEntry in packageEntry.Value.EnumerateArray())
                            {
                                var type = typeEntry.GetString();
                                if (string.IsNullOrWhiteSpace(type)) continue;
                                if (!byType.TryGetValue(type, out var list))
                                {
                                    list = new List<(string, string)>();
                                    byType[type] = list;
                                }
                                list.Add((packageEntry.Name, rootEntry.Name));
                            }
                        }
                    }
                }
                else
                {
                    conflicts.Add(new NodeConflict
                    {
                        Type = "扫描警告",
                        Details = "AST 扫描没有返回有效结果：" +
                                  (string.IsNullOrWhiteSpace(result.StandardError)
                                      ? "Python 退出代码 " + result.ExitCode
                                      : result.StandardError.Trim())
                    });
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                LauncherLog.Error("node-conflict", ex);
                conflicts.Add(new NodeConflict { Type = "扫描失败", Details = ex.Message });
            }
        }

        foreach (var pair in byType.Where(p => p.Value.Count > 1))
        {
            var first = pair.Value[0];
            for (var i = 1; i < pair.Value.Count; i++)
            {
                var other = pair.Value[i];
                if (string.Equals(first.Package, other.Package, StringComparison.OrdinalIgnoreCase)) continue;
                conflicts.Add(new NodeConflict
                {
                    Type = "重复节点类型",
                    NodeType = pair.Key,
                    PackageA = first.Package,
                    PackageB = other.Package,
                    Details = $"两个插件包都注册了节点 {pair.Key}"
                });
            }
        }

        progress?.Report("正在运行 pip check...");
        try
        {
            var pip = await ProcessRunner.RunAsync(
                _paths.PythonExe, new[] { "-m", "pip", "check" }, _paths.ComfyDir,
                cancellationToken: ct).ConfigureAwait(false);
            var text = (pip.StandardOutput + "\n" + pip.StandardError)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(line => !line.StartsWith("No broken requirements", StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var line in text)
                conflicts.Add(new NodeConflict { Type = "Python 依赖", Details = line });
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            conflicts.Add(new NodeConflict { Type = "依赖检查失败", Details = ex.Message });
        }

        LauncherLog.Write("node-conflict", $"扫描完成：{conflicts.Count} 个问题");
        progress?.Report($"扫描完成：{conflicts.Count} 个问题");
        return conflicts
            .OrderBy(c => c.Type, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.NodeType, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<string> GetCustomNodeRoots()
    {
        var roots = new List<string> { _paths.NodesDir };
        var yaml = _paths.ExtraModelPathsFile;
        if (!File.Exists(yaml)) return roots;
        try
        {
            var lines = File.ReadAllLines(yaml);
            var basePath = "";
            for (var i = 0; i < lines.Length; i++)
            {
                var raw = lines[i];
                var trimmed = raw.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
                var indent = raw.Length - raw.TrimStart().Length;
                if (indent == 0 && trimmed.EndsWith(':'))
                {
                    basePath = "";
                    continue;
                }
                var colon = trimmed.IndexOf(':');
                if (colon <= 0) continue;
                var key = trimmed[..colon].Trim();
                var value = trimmed[(colon + 1)..].Trim();
                if (key.Equals("base_path", StringComparison.OrdinalIgnoreCase))
                {
                    basePath = value.Trim('"', '\'');
                    continue;
                }
                if (!key.Equals("custom_nodes", StringComparison.OrdinalIgnoreCase)) continue;

                if (value is "|" or ">")
                {
                    var keyIndent = indent;
                    i++;
                    while (i < lines.Length)
                    {
                        var next = lines[i];
                        var nextTrimmed = next.Trim();
                        var nextIndent = next.Length - next.TrimStart().Length;
                        if (nextTrimmed.Length > 0 && nextIndent <= keyIndent) { i--; break; }
                        if (nextTrimmed.Length > 0 && !nextTrimmed.StartsWith('#'))
                            AddResolvedRoot(nextTrimmed.Trim('"', '\''));
                        i++;
                    }
                }
                else if (!string.IsNullOrWhiteSpace(value))
                {
                    AddResolvedRoot(value.Trim('"', '\''));
                }
            }

            void AddResolvedRoot(string configured)
            {
                try
                {
                    var expanded = Environment.ExpandEnvironmentVariables(configured);
                    var full = Path.IsPathRooted(expanded)
                        ? expanded
                        : Path.Combine(string.IsNullOrWhiteSpace(basePath) ? _paths.ComfyDir : basePath, expanded);
                    full = Path.GetFullPath(full);
                    if (!roots.Contains(full, StringComparer.OrdinalIgnoreCase)) roots.Add(full);
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            LauncherLog.Write("node-conflict", "解析 extra_model_paths custom_nodes 失败：" + ex.Message);
        }
        return roots;
    }
}
