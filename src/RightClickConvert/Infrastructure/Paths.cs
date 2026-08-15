namespace RightClickConvert.Infrastructure;

public static class Paths
{
    public static string AppData { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RightClickConvert");

    /// <summary>
    /// LibreOffice overwrites an existing output file without asking, so every write
    /// goes through here instead.
    /// </summary>
    public static string Unique(string directory, string baseName, string ext)
    {
        string candidate = Path.Combine(directory, $"{baseName}.{ext}");
        for (int n = 1; File.Exists(candidate); n++)
        {
            candidate = Path.Combine(directory, $"{baseName} ({n}).{ext}");
        }

        return candidate;
    }
}
