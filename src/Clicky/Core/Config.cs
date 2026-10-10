using System.Text.Encodings.Web;
using System.Text.Json;

namespace Clicky.Core;

public sealed class Config
{
    public string Hotkey { get; set; } = "Ctrl+Alt+Shift+M";

    /// <summary>Row/column characters. Rows are split across monitors; every monitor gets all columns.</summary>
    public string Alphabet { get; set; } = "abcdefghijklmnopqrstuvwxyz0123456789";

    /// <summary>Label size in pixels; 0 fits the label to the cell.</summary>
    public double FontSize { get; set; }

    /// <summary>Delay between hiding the overlay and clicking.</summary>
    public int ClickDelayMs { get; set; } = 60;

    public static string DefaultPath => Path.Combine(Paths.ConfigDir, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        // Keep "Ctrl+Alt+Shift+M" readable instead of \u002B escapes.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static Config Load(string? path = null)
    {
        path ??= DefaultPath;
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
        config.Validate();
        return config;
    }

    public void Validate()
    {
        Core.Hotkey.Parse(Hotkey);
        Alphabet = Alphabet.ToLowerInvariant();
        if (Alphabet.Length < 2 || Alphabet.Distinct().Count() != Alphabet.Length)
            throw new InvalidDataException("alphabet must have at least 2 characters and no duplicates");
        if (FontSize < 0)
            throw new InvalidDataException("fontSize must be >= 0");
        if (ClickDelayMs < 0)
            throw new InvalidDataException("clickDelayMs must be >= 0");
    }
}
