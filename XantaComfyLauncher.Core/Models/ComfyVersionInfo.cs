namespace XantaComfyLauncher.Models;

public sealed class ComfyVersionInfo
{
    public required string Tag { get; init; }
    public string? Commit { get; init; }
    public bool IsPrerelease { get; init; }
    public bool IsCurrent { get; set; }

    public string DisplayText
    {
        get
        {
            var current = IsCurrent ? "● " : "  ";
            var commit = string.IsNullOrWhiteSpace(Commit)
                ? "--------"
                : Commit[..Math.Min(8, Commit.Length)];
            var prerelease = IsPrerelease ? "  prerelease" : "";
            return $"{current}{Tag,-14} {commit}{prerelease}";
        }
    }

    public override string ToString() => DisplayText;
}
