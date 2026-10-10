using Clicky.Core;
using Tmds.DBus.Protocol;

namespace Clicky.Platform.Linux;

/// <summary>Global shortcut registered with KDE's kglobalaccel over D-Bus.</summary>
public sealed class KGlobalAccelHotkey(Hotkey hotkey) : IHotkeyService
{
    private const string Service = "org.kde.kglobalaccel";
    private const string Component = "clicky";
    private const string Action = "show-grid";

    // KGlobalAccel::SetShortcutFlag
    private const uint IsDefault = 1;
    private const uint SetPresent = 2;
    private const uint NoAutoloading = 4;

    private static readonly string[] ActionId = [Component, Action, "clicky", "Show click grid"];

    private Connection? _connection;
    private IDisposable? _watch;

    public event System.Action? Pressed;

    public async Task<bool> StartAsync()
    {
        try
        {
            _connection = new Connection(Address.Session
                ?? throw new InvalidOperationException("no D-Bus session bus address"));
            await _connection.ConnectAsync();

            int[] keys = [hotkey.ToQtKey()];
            await Call("doRegister", "as", (ref MessageWriter w) => w.WriteArray(ActionId));
            await Call("setShortcut", "asaiu", (ref MessageWriter w) =>
            {
                w.WriteArray(ActionId);
                w.WriteArray(keys);
                w.WriteUInt32(IsDefault);
            });
            await Call("setShortcut", "asaiu", (ref MessageWriter w) =>
            {
                w.WriteArray(ActionId);
                w.WriteArray(keys);
                w.WriteUInt32(SetPresent | NoAutoloading);
            });
            string componentPath = await DBus.CallAsync(_connection, Service, "/kglobalaccel",
                "org.kde.KGlobalAccel", "getComponent", "s", (ref MessageWriter w) => w.WriteString(Component),
                (m, _) => m.GetBodyReader().ReadObjectPathAsString());

            _watch = await DBus.WatchSignalAsync(_connection, Service, componentPath,
                "org.kde.kglobalaccel.Component", "globalShortcutPressed",
                (m, _) =>
                {
                    var r = m.GetBodyReader();
                    return (Component: r.ReadString(), Action: r.ReadString());
                },
                (error, value) =>
                {
                    if (error is null && value is (Component, Action))
                        Pressed?.Invoke();
                });

            Log.Info($"registered shortcut {hotkey} with kglobalaccel");
            return true;
        }
        catch (Exception e)
        {
            Log.Error($"kglobalaccel registration failed: {e.Message}");
            return false;
        }
    }

    private Task Call(string member, string signature, BodyWriter body) =>
        DBus.CallAsync(_connection!, Service, "/kglobalaccel", "org.kde.KGlobalAccel", member, signature, body);

    public void Dispose()
    {
        _watch?.Dispose();
        _connection?.Dispose();
    }
}
