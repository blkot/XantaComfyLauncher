using XantaComfyLauncher.Models;

namespace XantaComfyLauncher.Utils;

public static class SemVer
{
    public static bool IsVersionTag(string tag) =>
        TryParse(tag, out _, out _);

    public static bool TryParse(string tag, out Version version, out string suffix)
    {
        version = new Version(0, 0, 0);
        suffix = "";
        if (string.IsNullOrWhiteSpace(tag)) return false;

        var text = tag.Trim();
        if (text.StartsWith('v') || text.StartsWith('V')) text = text[1..];
        var dash = text.IndexOf('-');
        if (dash >= 0)
        {
            suffix = text[(dash + 1)..];
            text = text[..dash];
        }
        var plus = text.IndexOf('+');
        if (plus >= 0) text = text[..plus];
        if (Version.TryParse(text, out var parsed))
        {
            version = parsed;
            return true;
        }
        version = new Version(0, 0, 0);
        return false;
    }

    public static bool IsNewerThan(string candidate, string current)
    {
        if (!TryParse(candidate, out var candidateVersion, out var candidateSuffix) ||
            !TryParse(current, out var currentVersion, out var currentSuffix))
        {
            return false;
        }

        var cmp = candidateVersion.CompareTo(currentVersion);
        if (cmp != 0) return cmp > 0;

        var candidateIsPrerelease = !string.IsNullOrWhiteSpace(candidateSuffix);
        var currentIsPrerelease = !string.IsNullOrWhiteSpace(currentSuffix);
        if (candidateIsPrerelease != currentIsPrerelease) return !candidateIsPrerelease;
        if (!candidateIsPrerelease) return false;
        return string.Compare(candidateSuffix, currentSuffix, StringComparison.OrdinalIgnoreCase) > 0;
    }

    public static int Compare(ComfyVersionInfo left, ComfyVersionInfo right)
    {
        var leftValid = TryParse(left.Tag, out var lv, out var ls);
        var rightValid = TryParse(right.Tag, out var rv, out var rs);
        if (leftValid && rightValid)
        {
            var cmp = rv.CompareTo(lv);
            if (cmp != 0) return cmp;
            var pre = string.IsNullOrEmpty(ls) ? 1 : 0;
            var preR = string.IsNullOrEmpty(rs) ? 1 : 0;
            cmp = preR.CompareTo(pre);
            if (cmp != 0) return cmp;
            return string.Compare(rs, ls, StringComparison.OrdinalIgnoreCase);
        }
        if (leftValid) return -1;
        if (rightValid) return 1;
        return string.Compare(right.Tag, left.Tag, StringComparison.OrdinalIgnoreCase);
    }
}
