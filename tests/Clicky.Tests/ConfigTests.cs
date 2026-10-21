namespace Clicky.Tests;

public class ConfigTests : IDisposable
{
    private readonly string _path = Path.Combine(Directory.CreateTempSubdirectory("clicky-test").FullName, "config.json");

    private static readonly ScreenBounds Hd = new(0, 0, 1920, 1080);

    [Fact]
    public void WritesDefaultsSizedToTheScreensOnFirstRun()
    {
        var config = Config.Load(_path);
        Assert.True(config.IsNew);
        Assert.False(File.Exists(_path)); // waits for the screens
        Assert.Equal("Ctrl+Alt+Shift+M", config.Hotkey);
        Assert.Equal(36, config.Alphabet.Length);

        config.CreateFile([Hd, new ScreenBounds(1920, 0, 3440, 1440)], _path);
        Assert.False(config.IsNew);
        var json = File.ReadAllText(_path);
        Assert.Contains("\"columns\": 32", json);
        Assert.Contains("\"rows\": 18", json);
        Assert.Contains("\"subGridSize\": 4", json);
        Assert.Contains("\"clickDelayMs\": 60", json);
        Assert.DoesNotContain("isNew", json);

        var reloaded = Config.Load(_path);
        Assert.False(reloaded.IsNew);
        Assert.Equal((32, 18), (reloaded.Columns, reloaded.Rows));
    }

    [Theory]
    [InlineData(1920, 1080, 1, 32, 18)]
    [InlineData(1920, 1080, 2, 32, 18)]
    [InlineData(1920, 1080, 3, 32, 12)] // 3 x 18 rows would need 54 characters
    [InlineData(1366, 768, 1, 23, 13)]
    [InlineData(3440, 1440, 1, 36, 24)] // capped at the alphabet
    [InlineData(3840, 2160, 1, 36, 36)]
    public void FitGridAimsForSquareCells(int width, int height, int monitors, int columns, int rows)
    {
        var screens = Enumerable.Range(0, monitors).Select(i => new ScreenBounds(i * width, 0, width, height)).ToList();
        Assert.Equal((columns, rows), Config.FitGrid(screens, 36));
        Grid.Build(screens, new Config().Alphabet, columns, rows); // the result is always a valid grid
    }

    [Fact]
    public void FitGridUsesTheSmallestMonitor() =>
        Assert.Equal((32, 18), Config.FitGrid([new ScreenBounds(0, 0, 3840, 2160), new ScreenBounds(3840, 0, 1920, 1080)], 36));

    [Fact]
    public void ReadsOverridesAndLowercasesAlphabet()
    {
        File.WriteAllText(_path, """
            {
              // comments are allowed
              "hotkey": "Meta+G",
              "alphabet": "ABCD",
            }
            """);
        var config = Config.Load(_path);
        Assert.Equal("Meta+G", config.Hotkey);
        Assert.Equal("abcd", config.Alphabet);
        Assert.Equal(60, config.ClickDelayMs);
        Assert.Equal((0, 0, 4), (config.Columns, config.Rows, config.SubGridSize));
    }

    [Theory]
    [InlineData("""{ "alphabet": "aab" }""")]
    [InlineData("""{ "alphabet": "a" }""")]
    [InlineData("""{ "fontSize": -1 }""")]
    [InlineData("""{ "clickDelayMs": -1 }""")]
    [InlineData("""{ "columns": -1 }""")]
    [InlineData("""{ "columns": 37 }""")]
    [InlineData("""{ "rows": 37 }""")]
    [InlineData("""{ "subGridSize": 1 }""")]
    [InlineData("""{ "subGridSize": 11 }""")]
    public void RejectsInvalidValues(string json)
    {
        File.WriteAllText(_path, json);
        Assert.Throws<InvalidDataException>(() => Config.Load(_path));
    }

    [Fact]
    public void RejectsBadHotkey()
    {
        File.WriteAllText(_path, """{ "hotkey": "Ctrl+Nope" }""");
        Assert.Throws<FormatException>(() => Config.Load(_path));
    }

    public void Dispose() => Directory.Delete(Path.GetDirectoryName(_path)!, recursive: true);
}
