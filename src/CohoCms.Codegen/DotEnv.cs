namespace CohoCms.Codegen;

/// <summary>A <c>.env</c> file: <c>NAME=value</c> lines, as the TypeScript generator reads them.</summary>
internal static class DotEnv
{
    /// <summary>The variables in <paramref name="path"/>, or none when there is no such file.</summary>
    public static Dictionary<string, string> Read(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(path))
        {
            return values;
        }
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }
            if (line.StartsWith("export ", StringComparison.Ordinal))
            {
                line = line["export ".Length..].TrimStart();
            }
            var equals = line.IndexOf('=', StringComparison.Ordinal);
            if (equals <= 0)
            {
                continue;
            }
            var value = line[(equals + 1)..].Trim();
            if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0])
            {
                value = value[1..^1];
            }
            values[line[..equals].Trim()] = value;
        }
        return values;
    }

    /// <summary>
    /// Writes <paramref name="path"/> from <paramref name="example"/>, with <paramref name="name"/>
    /// set to <paramref name="value"/>. Only its owner can read it.
    /// </summary>
    public static void Write(string path, string example, string name, string value)
    {
        var lines = File.Exists(example) ? File.ReadAllLines(example).ToList() : [];
        var index = lines.FindIndex(line => line.StartsWith($"{name}=", StringComparison.Ordinal));
        if (index >= 0)
        {
            lines[index] = $"{name}={value}";
        }
        else
        {
            lines.Insert(0, $"{name}={value}");
        }

        File.WriteAllText(path, "");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        File.WriteAllText(path, string.Join('\n', lines) + "\n");
    }
}
