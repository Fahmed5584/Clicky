using Clicky.Core;

namespace Clicky.Tests;

public class HotkeyTests
{
    [Fact]
    public void ParsesDefault()
    {
        var h = Hotkey.Parse("Ctrl+Alt+Shift+M");
        Assert.Equal(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Shift, h.Modifiers);
        Assert.Equal("M", h.Key);
        Assert.Equal("Ctrl+Alt+Shift+M", h.ToString());
    }

    [Fact]
    public void QtEncodingMatchesQKeySequence() =>
        // Value printed by PyQt6 for QKeySequence("Ctrl+Alt+Shift+M")[0].toCombined().
        Assert.Equal(0x0E00004D, Hotkey.Parse("Ctrl+Alt+Shift+M").ToQtKey());

    [Theory]
    [InlineData("Meta+F1", 0x10000000 | 0x01000030)]
    [InlineData("ctrl + space", 0x04000000 | 0x20)]
    [InlineData("Alt+5", 0x08000000 | '5')]
    public void QtEncodingOtherKeys(string text, int expected) =>
        Assert.Equal(expected, Hotkey.Parse(text).ToQtKey());

    [Theory]
    [InlineData("Ctrl+Alt+Shift+M", 0x4000u | 0x1 | 0x2 | 0x4, 0x4Du)]
    [InlineData("Win+F12", 0x4000u | 0x8, 0x7Bu)]
    [InlineData("Ctrl+Space", 0x4000u | 0x2, 0x20u)]
    public void Win32Encoding(string text, uint mods, uint vk) =>
        Assert.Equal((mods, vk), Hotkey.Parse(text).ToWin32());

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+M+N")]
    [InlineData("Ctrl+F25")]
    [InlineData("Ctrl+Tab")]
    public void RejectsInvalid(string text) =>
        Assert.Throws<FormatException>(() => Hotkey.Parse(text));
}

public class ConfigTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("clicky-test").FullName;
    private string PathFor(string name) => Path.Combine(_dir, name);

    [Fact]
    public void WritesDefaultsOnFirstLoad()
    {
        var path = PathFor("config.json");
        var config = Config.Load(path);
        Assert.True(File.Exists(path));
        Assert.Equal("Ctrl+Alt+Shift+M", config.Hotkey);
        Assert.Equal(36, config.Alphabet.Length);
        Assert.Contains("\"clickDelayMs\": 60", File.ReadAllText(path));
    }

    [Fact]
    public void ReadsOverridesAndLowercasesAlphabet()
    {
        var path = PathFor("config.json");
        File.WriteAllText(path, """
            {
              // comments are allowed
              "hotkey": "Meta+G",
              "alphabet": "ABCD",
            }
            """);
        var config = Config.Load(path);
        Assert.Equal("Meta+G", config.Hotkey);
        Assert.Equal("abcd", config.Alphabet);
        Assert.Equal(60, config.ClickDelayMs);
    }

    [Theory]
    [InlineData("""{ "alphabet": "aab" }""")]
    [InlineData("""{ "alphabet": "a" }""")]
    [InlineData("""{ "clickDelayMs": -1 }""")]
    public void RejectsInvalidValues(string json)
    {
        var path = PathFor("config.json");
        File.WriteAllText(path, json);
        Assert.Throws<InvalidDataException>(() => Config.Load(path));
    }

    [Fact]
    public void RejectsBadHotkey()
    {
        var path = PathFor("config.json");
        File.WriteAllText(path, """{ "hotkey": "Ctrl+Nope" }""");
        Assert.Throws<FormatException>(() => Config.Load(path));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
