namespace RightClickConvert.Infrastructure;

/// <summary>Append-only log. The shell gives us no console, so failures land here.</summary>
public static class Log
{
    private static readonly Lock Gate = new();

    public static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(Paths.AppData);
            lock (Gate)
            {
                File.AppendAllText(
                    Path.Combine(Paths.AppData, "log.txt"),
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{Environment.ProcessId}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never be the reason a conversion fails.
        }
    }
}
