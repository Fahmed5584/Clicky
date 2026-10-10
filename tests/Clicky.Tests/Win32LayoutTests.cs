using System.Runtime.InteropServices;
using Clicky.Platform.Windows;

namespace Clicky.Tests;

#pragma warning disable CA1416 // layout check only; nothing is called
public class Win32LayoutTests
{
    // SendInput silently rejects a wrong cbSize, so pin the 64-bit INPUT layout.
    [Fact]
    public void InputStructMatchesWin64Layout()
    {
        Assert.Equal(40, Marshal.SizeOf<Win32.INPUT>());
        Assert.Equal(8, (int)Marshal.OffsetOf<Win32.INPUT>(nameof(Win32.INPUT.mi)));
    }
}
