// Drives the real KdePlatform against tools/dbus-mock/mock.py on a private session bus.
using Clicky;

using var platform = new KdePlatform();
var pressed = new TaskCompletionSource();
Console.WriteLine($"hotkey start: {await platform.RegisterHotkeyAsync(Hotkey.Parse("Ctrl+Alt+Shift+M"), () => pressed.TrySetResult())}");
Console.WriteLine($"pressed received: {await Task.WhenAny(pressed.Task, Task.Delay(3000)) == pressed.Task}");

await platform.InitializeClickAsync();
// Two 1080p monitors side by side; "sa" + "00" is the top-left sub-cell of the right monitor's first cell.
var grid = Grid.Build([new ScreenBounds(0, 0, 1920, 1080), new ScreenBounds(1920, 0, 1920, 1080)],
    "abcdefghijklmnopqrstuvwxyz0123456789");
await platform.ClickAsync(grid.Resolve("sa00")!.Value, grid.Monitors.Count);
