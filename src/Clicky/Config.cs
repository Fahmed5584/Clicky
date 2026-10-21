using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

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

/// <summary>config.json. Created on first run with a grid sized to the screens; read once at startup.</summary>
public sealed class Config
{
    /// <summary>Cell size, in pixels, that a new config's columns and rows aim for.</summary>
    public const int TargetCellSize = 60;

    public string Hotkey { get; set; } = "Ctrl+Alt+Shift+M";

    /// <summary>Row and column characters. Rows are split across monitors; every monitor gets all columns.</summary>
    public string Alphabet { get; set; } = "abcdefghijklmnopqrstuvwxyz0123456789";

    /// <summary>Columns per monitor, taken from the start of the alphabet; 0 uses the whole alphabet.</summary>
    public int Columns { get; set; }

    /// <summary>Rows per monitor; 0 splits the whole alphabet between the monitors.</summary>
    public int Rows { get; set; }

    /// <summary>The chosen cell is split into SubGridSize x SubGridSize sub-cells (2..10).</summary>
    public int SubGridSize { get; set; } = Grid.DefaultSubSize;

    /// <summary>Label size in pixels; 0 fits the label to the cell.</summary>
    public double FontSize { get; set; }

    /// <summary>Delay between hiding the overlay and clicking.</summary>
    public int ClickDelayMs { get; set; } = 60;

    /// <summary>True when there was no file to load; <see cref="CreateFile"/> then writes one.</summary>
    [JsonIgnore]
    public bool IsNew { get; private set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        // Keep "Ctrl+Alt+Shift+M" readable instead of + escapes.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Loads and validates the config. A missing file gives the defaults with <see cref="IsNew"/> set;
    /// nothing is written until <see cref="CreateFile"/>, once the screens are known.
    /// </summary>
    public static Config Load(string? path = null)
    {
        path ??= Paths.ConfigFile;
        var config = File.Exists(path)
            ? JsonSerializer.Deserialize<Config>(File.ReadAllText(path), JsonOptions)
              ?? throw new InvalidDataException($"{path} is empty")
            : new Config { IsNew = true };

        Clicky.Hotkey.Parse(config.Hotkey);
        config.Alphabet = config.Alphabet.ToLowerInvariant();
        if (config.Alphabet.Length < 2 || config.Alphabet.Distinct().Count() != config.Alphabet.Length)
            throw new InvalidDataException("alphabet must have at least 2 characters and no duplicates");
        if (config.Columns < 0 || config.Columns > config.Alphabet.Length)
            throw new InvalidDataException($"columns must be between 0 and the alphabet length ({config.Alphabet.Length})");
        if (config.Rows < 0 || config.Rows > config.Alphabet.Length)
            throw new InvalidDataException($"rows must be between 0 and the alphabet length ({config.Alphabet.Length})");
        if (config.SubGridSize < 2 || config.SubGridSize > Grid.MaxSubSize)
            throw new InvalidDataException($"subGridSize must be between 2 and {Grid.MaxSubSize}");
        if (config.FontSize < 0)
            throw new InvalidDataException("fontSize must be >= 0");
        if (config.ClickDelayMs < 0)
            throw new InvalidDataException("clickDelayMs must be >= 0");
        return config;
    }

    /// <summary>
    /// Sizes the grid for <paramref name="screens"/> and writes the file. The counts are fixed from
    /// then on, so labels never move unless the user edits them.
    /// </summary>
    public void CreateFile(IReadOnlyList<ScreenBounds> screens, string? path = null)
    {
        path ??= Paths.ConfigFile;
        (Columns, Rows) = FitGrid(screens, Alphabet.Length);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
        IsNew = false;
        Log.Info($"wrote config with a {Columns}x{Rows} grid to {path}");
    }

    /// <summary>
    /// Columns and rows per monitor that give roughly square <see cref="TargetCellSize"/> cells on the
    /// smallest monitor, within what the alphabet allows. Bigger monitors get bigger cells.
    /// </summary>
    public static (int Columns, int Rows) FitGrid(IReadOnlyList<ScreenBounds> screens, int alphabetLength)
    {
        int width = screens.Min(s => s.Width), height = screens.Min(s => s.Height);
        int columns = Math.Clamp((int)Math.Round((double)width / TargetCellSize), 2, alphabetLength);
        int rows = Math.Clamp((int)Math.Round((double)height / TargetCellSize), 1,
            Math.Max(1, alphabetLength / screens.Count));
        return (columns, rows);
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
