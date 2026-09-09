namespace NotOnlyFiendsStudio.Studio;

/// <summary>
/// Reads the simple key/value environment files used by local development and tests.
/// </summary>
public static class EnvFile
{
    public static Dictionary<string, string> Load(string path)
    {
        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path))
            return vars;

        foreach (var line in File.ReadAllLines(path))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                continue;

            var eqIndex = trimmed.IndexOf('=');
            if (eqIndex <= 0)
                continue;

            var key = trimmed[..eqIndex].Trim();
            var value = trimmed[(eqIndex + 1)..].Trim();
            if (value.Length >= 2
                && ((value[0] == '\'' && value[^1] == '\'')
                    || (value[0] == '"' && value[^1] == '"')))
            {
                value = value[1..^1];
            }

            vars[key] = value;
        }

        return vars;
    }
}
