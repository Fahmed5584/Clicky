namespace Clicky.Tests;

public class ConfigTests : IDisposable
{
    private readonly string _path = Path.Combine(Directory.CreateTempSubdirectory("clicky-test").FullName, "config.json");

    [Fact]
    public void WritesDefaultsOnFirstLoad()
    {
        var config = Config.Load(_path);
        Assert.True(File.Exists(_path));
        Assert.Equal("Ctrl+Alt+Shift+M", config.Hotkey);
        Assert.Equal(36, config.Alphabet.Length);
        Assert.Contains("\"clickDelayMs\": 60", File.ReadAllText(_path));
    }

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
