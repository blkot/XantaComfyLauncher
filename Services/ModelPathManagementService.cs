using XantaComfyLauncher.Models;
using XantaComfyLauncher.Utils;

namespace XantaComfyLauncher.Services;

public sealed class ModelPathManagementService
{
    private readonly AppPaths _paths;

    public ModelPathManagementService(AppPaths paths) => _paths = paths;

    public IReadOnlyList<ModelPathValidation> GetConfiguredPaths()
    {
        var result = new List<ModelPathValidation>
        {
            Validate("默认模型", _paths.ModelsDir)
        };
        foreach (var entry in ExtraModelPathsService.GetModelDirectories(_paths))
        {
            var category = entry.CategoryText;
            result.Add(Validate(category, entry.Path));
        }

        var yaml = _paths.ExtraModelPathsFile;
        if (File.Exists(yaml))
        {
            try
            {
                var basePath = "";
                foreach (var raw in File.ReadAllLines(yaml))
                {
                    var trimmed = raw.Trim();
                    if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
                    var indent = raw.Length - raw.TrimStart().Length;
                    if (indent == 0 && trimmed.EndsWith(':')) { basePath = ""; continue; }
                    var colon = trimmed.IndexOf(':');
                    if (colon <= 0) continue;
                    var key = trimmed[..colon].Trim();
                    var value = trimmed[(colon + 1)..].Trim();
                    if (key.Equals("base_path", StringComparison.OrdinalIgnoreCase))
                    {
                        basePath = value.Trim('"', '\'');
                        continue;
                    }
                    if (key.Equals("custom_nodes", StringComparison.OrdinalIgnoreCase) || value is "" or "|" or ">") continue;
                    var path = Resolve(basePath, value.Trim('"', '\''));
                    if (path != null && !result.Any(p => string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase)))
                        result.Add(Validate(key, path));
                }
            }
            catch (Exception ex)
            {
                LauncherLog.Write("model-paths", "解析 YAML 失败：" + ex.Message);
            }
        }
        return result;
    }

    public string ReadYaml() => File.Exists(_paths.ExtraModelPathsFile)
        ? File.ReadAllText(_paths.ExtraModelPathsFile)
        : "";

    public string SaveYaml(string text)
    {
        if (File.Exists(_paths.ExtraModelPathsFile))
        {
            var backup = _paths.ExtraModelPathsFile + $".bak-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Copy(_paths.ExtraModelPathsFile, backup, overwrite: true);
            LauncherLog.Write("model-paths", "已备份 extra_model_paths.yaml：" + backup);
        }
        File.WriteAllText(_paths.ExtraModelPathsFile, text);
        return _paths.ExtraModelPathsFile;
    }

    public async Task CreateJunctionAsync(string linkPath, string targetPath, CancellationToken ct = default)
    {
        if (!Directory.Exists(targetPath))
            throw new DirectoryNotFoundException("目标目录不存在：" + targetPath);
        if (Directory.Exists(linkPath) || File.Exists(linkPath))
            throw new IOException("链接路径已存在：" + linkPath);
        var parent = Path.GetDirectoryName(linkPath);
        if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
        var result = await ProcessRunner.RunAsync(
            "cmd.exe",
            new[] { "/c", "mklink", "/J", linkPath, targetPath },
            Environment.CurrentDirectory,
            cancellationToken: ct).ConfigureAwait(false);
        if (!result.Success)
            throw new InvalidOperationException(result.StandardError.Trim().Length > 0
                ? result.StandardError.Trim()
                : "mklink 创建 junction 失败");
        LauncherLog.Write("model-paths", $"已创建 junction：{linkPath} -> {targetPath}");
    }

    public Task<string> MigrateDirectoryAsync(
        string sourcePath,
        string destinationPath,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            if (!Directory.Exists(sourcePath))
                throw new DirectoryNotFoundException("源目录不存在：" + sourcePath);
            if (Directory.Exists(destinationPath) && Directory.EnumerateFileSystemEntries(destinationPath).Any())
                throw new IOException("目标目录非空：" + destinationPath);

            var sameVolume = string.Equals(Path.GetPathRoot(Path.GetFullPath(sourcePath)),
                Path.GetPathRoot(Path.GetFullPath(destinationPath)), StringComparison.OrdinalIgnoreCase);
            if (sameVolume)
            {
                progress?.Report("同卷移动中...");
                Directory.Move(sourcePath, destinationPath);
                LauncherLog.Write("model-paths", $"已移动目录：{sourcePath} -> {destinationPath}");
                return "同卷移动完成，源目录已移动。";
            }

            progress?.Report("跨卷复制中...");
            Directory.CreateDirectory(destinationPath);
            foreach (var dir in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(dir.Replace(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase));
            foreach (var file in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                var target = file.Replace(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: true);
            }
            LauncherLog.Write("model-paths", $"已跨卷复制：{sourcePath} -> {destinationPath}");
            return "跨卷复制完成。出于安全考虑，源目录未删除，请确认后手动清理。";
        }, ct);
    }

    private static ModelPathValidation Validate(string category, string path)
    {
        var result = new ModelPathValidation { Category = category, Path = path };
        try
        {
            result.Exists = Directory.Exists(path);
            if (result.Exists)
            {
                Directory.EnumerateFileSystemEntries(path).Take(1).ToList();
                result.Readable = true;
                result.Detail = "可访问";
            }
            else
            {
                result.Detail = "目录不存在";
            }
        }
        catch (Exception ex)
        {
            result.Detail = "不可访问：" + ex.Message;
        }
        return result;
    }

    private string? Resolve(string basePath, string configured)
    {
        try
        {
            var value = Environment.ExpandEnvironmentVariables(configured);
            if (string.IsNullOrWhiteSpace(value)) return null;
            var root = string.IsNullOrWhiteSpace(basePath)
                ? _paths.ComfyDir
                : Environment.ExpandEnvironmentVariables(basePath);
            return Path.GetFullPath(Path.IsPathRooted(value)
                ? value
                : Path.Combine(root, value));
        }
        catch { return null; }
    }
}
