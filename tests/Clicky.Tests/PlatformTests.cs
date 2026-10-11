using System.Runtime.InteropServices;

namespace Clicky.Tests;

public class PortalStreamTests
{
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";

    [Fact]
    public void StreamsPairWithMonitorsByPositionWithMixedScaling()
    {
        // XWayland/Avalonia screens in physical pixels: 4K at 200% on the left, 1080p on the right.
        var screens = new List<ScreenBounds> { new(3840, 0, 1920, 1080), new(0, 0, 3840, 2160) };
        // Portal streams in logical coordinates, reported in a different order.
        var streams = new[]
        {
            new PortalStream(71, new ScreenBounds(1920, 0, 1920, 1080)),
            new PortalStream(70, new ScreenBounds(0, 0, 1920, 1080)),
        };

        var grid = Grid.Build(screens, Alphabet);
        var sorted = KdePlatform.SortStreams(streams);
        Assert.Equal(70u, sorted[grid.Resolve("aa")!.Value.Monitor.Index].NodeId);
        Assert.Equal(71u, sorted[grid.Resolve("sa")!.Value.Monitor.Index].NodeId);
    }

    [Fact]
    public void MonitorLeftOfOriginPairsFirst()
    {
        var streams = new[]
        {
            new PortalStream(1, new ScreenBounds(0, 0, 1920, 1080)),
            new PortalStream(2, new ScreenBounds(-1280, 0, 1280, 1024)),
        };
        Assert.Equal([2u, 1u], KdePlatform.SortStreams(streams).Select(s => s.NodeId));
    }
}

#pragma warning disable CA1416 // layout check only; nothing is called
public class Win32LayoutTests
{
    // SendInput silently rejects a wrong cbSize, so pin the 64-bit INPUT layout.
    [Fact]
    public void InputStructMatchesWin64Layout()
    {
        Assert.Equal(40, Marshal.SizeOf<WindowsPlatform.Native.INPUT>());
        Assert.Equal(8, (int)Marshal.OffsetOf<WindowsPlatform.Native.INPUT>(nameof(WindowsPlatform.Native.INPUT.mi)));
    }
}
