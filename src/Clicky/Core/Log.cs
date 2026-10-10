namespace Clicky.Core;

/// <summary>Minimal logger: stderr plus a log file (WinExe has no console on Windows).</summary>
public static class Log
{
    private static readonly object Gate = new();

    /// <summary>Log file; null disables file logging (tests).</summary>
    public static string? FilePath { get; set; } = Path.Combine(Paths.DataDir, "clicky.log");

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message) => Write("ERROR", message);

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss} clicky {level}: {message}";
        lock (Gate)
        {
            Console.Error.WriteLine(line);
            if (FilePath is null)
                return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.AppendAllText(FilePath, line + Environment.NewLine);
            }
            catch (IOException)
            {
                // Logging must never take the app down.
            }
        }
    }
}
