using Avalonia.Input;

namespace Clicky.Tests;

public class OverlayTests
{
    [Theory]
    [InlineData(Key.A, "a", 'a')]
    [InlineData(Key.M, "M", 'm')] // Shift held
    [InlineData(Key.Q, "a", 'a')] // AZERTY: the layout's symbol wins over the key position
    [InlineData(Key.D1, "&", '&')]
    [InlineData(Key.Z, null, 'z')] // no symbol: fall back to the key itself
    [InlineData(Key.D7, null, '7')]
    [InlineData(Key.NumPad3, null, '3')]
    [InlineData(Key.NumPad3, "\u001b", '3')] // control characters are not symbols
    [InlineData(Key.Escape, "\u001b", null)]
    [InlineData(Key.F5, null, null)]
    public void KeyCharPrefersTheLayoutSymbol(Key key, string? symbol, char? expected) =>
        Assert.Equal(expected, Overlay.KeyChar(key, symbol));
}
