using Clicky.Core;
using Clicky.Platform.Linux;

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

        var layout = GridLayout.Build(screens, Alphabet);
        var sorted = PortalPointer.SortStreams(streams);

        var left = layout.Resolve("aa")!.Value;
        var right = layout.Resolve("sa")!.Value;
        Assert.Equal(70u, sorted[left.Monitor.Order].NodeId);
        Assert.Equal(71u, sorted[right.Monitor.Order].NodeId);
    }

    [Fact]
    public void MonitorLeftOfOriginPairsFirst()
    {
        var streams = new[]
        {
            new PortalStream(1, new ScreenBounds(0, 0, 1920, 1080)),
            new PortalStream(2, new ScreenBounds(-1280, 0, 1280, 1024)),
        };
        Assert.Equal([2u, 1u], PortalPointer.SortStreams(streams).Select(s => s.NodeId));
    }
}
