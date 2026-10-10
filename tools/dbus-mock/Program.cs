using Clicky.Core;
using Clicky.Platform;
using Clicky.Platform.Linux;
var pressed = new TaskCompletionSource();
using var hk = new KGlobalAccelHotkey(Hotkey.Parse("Ctrl+Alt+Shift+M"));
hk.Pressed += () => pressed.TrySetResult();
Console.WriteLine($"hotkey start: {await hk.StartAsync()}");
Console.WriteLine($"pressed received: {await Task.WhenAny(pressed.Task, Task.Delay(3000)) == pressed.Task}");
using var p = new PortalPointer();
await p.InitializeAsync();
// Order 1 = right-hand monitor (stream 71); centre of cell at fx=0.5, fy=0.25.
await p.ClickAsync(new ClickTarget(1, 2, new ScreenBounds(1920, 0, 1920, 1080), 0.5, 0.25));
