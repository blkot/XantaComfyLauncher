using System.Text;

namespace ComfyLauncher.Utils;

public static class ArgumentTokenizer
{
    public static IReadOnlyList<string> Split(string? commandLine)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(commandLine)) return result;

        var current = new StringBuilder();
        var inQuotes = false;
        var backslashes = 0;
        for (var i = 0; i < commandLine.Length; i++)
        {
            var ch = commandLine[i];
            if (ch == '\\')
            {
                backslashes++;
                continue;
            }
            if (ch == '"')
            {
                current.Append('\\', backslashes / 2);
                if (backslashes % 2 == 0)
                {
                    inQuotes = !inQuotes;
                }
                else
                {
                    current.Append('"');
                }
                backslashes = 0;
                continue;
            }
            current.Append('\\', backslashes);
            backslashes = 0;
            if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(ch);
            }
        }
        current.Append('\\', backslashes);
        if (current.Length > 0) result.Add(current.ToString());
        return result;
    }
}
