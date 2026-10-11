using System.Text.Encodings.Web;
using System.Text.Json;

namespace Clicky;

/// <summary>Where clicky keeps its files.</summary>
public static class Paths
{
    // DoNotVerify: GetFolderPath otherwise returns "" when the folder doesn't exist yet,
    // which would silently turn every path below into a relative one.

    /// <summary>%APPDATA% on Windows, $XDG_CONFIG_HOME (~/.config) on Linux.</summary>
    public static string ConfigHome { get; } =
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify);

    /// <summary>%LOCALAPPDATA% on Windows, $XDG_DATA_HOME (~/.local/share) on Linux.</summary>
    public static string DataHome { get; } =
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify);

    public static string ConfigFile => Path.Combine(ConfigHome, "clicky", "config.json");
    public static string LogFile => Path.Combine(DataHome, "clicky", "clicky.log");
    public static string PortalTokenFile => Path.Combine(DataHome, "clicky", "portal.json");
}

/// <summary>config.json. Created with defaults on first run; read once at startup.</summary>
public sealed class Config
{
    public string Hotkey { get; set; } = "Ctrl+Alt+Shift+M";

    /// <summary>Row and column characters. Rows are split across monitors; every monitor gets all columns.</summary>
    public string Alphabet { get; set; } = "abcdefghijklmnopqrstuvwxyz0123456789";

    /// <summary>Label size in pixels; 0 fits the label to the cell.</summary>
    public double FontSize { get; set; }

    /// <summary>Delay between hiding the overlay and clicking.</summary>
    public int ClickDelayMs { get; set; } = 60;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        // Keep "Ctrl+Alt+Shift+M" readable instead of + escapes.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Loads and validates the config, writing the defaults first if the file is missing.</summary>
    public static Config Load(string? path = null)
    {
        path ??= Paths.ConfigFile;
        Config config;
        if (File.Exists(path))
        {
            config = JsonSerializer.Deserialize<Config>(File.ReadAllText(path), JsonOptions)
                ?? throw new InvalidDataException($"{path} is empty");
        }
        else
        {
            config = new Config();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(config, JsonOptions));
            Log.Info($"wrote default config to {path}");
        }

        Clicky.Hotkey.Parse(config.Hotkey);
        config.Alphabet = config.Alphabet.ToLowerInvariant();
        if (config.Alphabet.Length < 2 || config.Alphabet.Distinct().Count() != config.Alphabet.Length)
            throw new InvalidDataException("alphabet must have at least 2 characters and no duplicates");
        if (config.FontSize < 0)
            throw new InvalidDataException("fontSize must be >= 0");
        if (config.ClickDelayMs < 0)
            throw new InvalidDataException("clickDelayMs must be >= 0");
        return config;
    }
}

/// <summary>Writes to stderr and the log file (a WinExe has no console on Windows).</summary>
public static class Log
{
    private static readonly Lock Gate = new();

    /// <summary>Log file; null disables file logging (the tests do this).</summary>
    public static string? FilePath { get; set; } = Paths.LogFile;

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
