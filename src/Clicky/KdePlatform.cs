using System.Text.Json;
using Tmds.DBus.Protocol;

namespace Clicky;

/// <summary>A monitor stream from the ScreenCast portal, in compositor logical coordinates.</summary>
public readonly record struct PortalStream(uint NodeId, ScreenBounds Bounds);

/// <summary>
/// KDE Plasma (Wayland) over D-Bus: the hotkey through kglobalaccel, clicks through the
/// xdg-desktop-portal RemoteDesktop interface, autostart through a .desktop file.
/// </summary>
public sealed class KdePlatform : IPlatform
{
    // kglobalaccel
    private const string AccelService = "org.kde.kglobalaccel";
    private const string AccelPath = "/kglobalaccel";
    private const string AccelInterface = "org.kde.KGlobalAccel";
    private const string ComponentName = "clicky";
    private const string ActionName = "show-grid";
    // Component and action, each as an id and then a display name.
    private static readonly string[] ActionId = [ComponentName, ActionName, "clicky", "Show click grid"];
    private const uint IsDefault = 1, SetPresent = 2, NoAutoloading = 4; // KGlobalAccel::SetShortcutFlag

    // xdg-desktop-portal
    private const string PortalService = "org.freedesktop.portal.Desktop";
    private const string PortalPath = "/org/freedesktop/portal/desktop";
    private const string RemoteDesktop = "org.freedesktop.portal.RemoteDesktop";
    private const string ScreenCast = "org.freedesktop.portal.ScreenCast";
    private const uint DevicePointer = 2, SourceMonitor = 1, PersistUntilRevoked = 2;
    private const uint ButtonPressed = 1, ButtonReleased = 0;
    private const int BtnLeft = 0x110; // linux/input-event-codes.h
    private static readonly Dictionary<string, VariantValue> NoOptions = new();

    private static string AutostartFile => Path.Combine(Paths.ConfigHome, "autostart", "clicky.desktop");

    /// <summary>Serialises portal use, so a click never runs while the session is being (re)built.</summary>
    private readonly SemaphoreSlim _portalGate = new(1, 1);
    private DBus? _bus;
    private IDisposable? _hotkeyWatch;
    private string? _session;
    private IReadOnlyList<PortalStream> _streams = [];
    private int _tokenCounter;

    public async Task<bool> RegisterHotkeyAsync(Hotkey hotkey, Action onPressed)
    {
        try
        {
            var bus = await ConnectAsync();
            int[] keys = [hotkey.ToQtKey()];
            await bus.CallAsync(AccelService, AccelPath, AccelInterface, "doRegister", "as",
                (ref MessageWriter w) => w.WriteArray(ActionId));
            // First as the default shortcut, then as the active one (what KDE's own apps do).
            foreach (uint flags in new[] { IsDefault, SetPresent | NoAutoloading })
                await bus.CallAsync(AccelService, AccelPath, AccelInterface, "setShortcut", "asaiu",
                    (ref MessageWriter w) =>
                    {
                        w.WriteArray(ActionId);
                        w.WriteArray(keys);
                        w.WriteUInt32(flags);
                    });
            string componentPath = await bus.CallAsync(AccelService, AccelPath, AccelInterface, "getComponent", "s",
                (ref MessageWriter w) => w.WriteString(ComponentName),
                (m, _) => m.GetBodyReader().ReadObjectPathAsString());

            _hotkeyWatch = await bus.WatchSignalAsync(AccelService, componentPath,
                "org.kde.kglobalaccel.Component", "globalShortcutPressed",
                (m, _) =>
                {
                    var r = m.GetBodyReader();
                    return (Component: r.ReadString(), Action: r.ReadString());
                },
                (error, value) =>
                {
                    if (error is null && value is (ComponentName, ActionName))
                        onPressed();
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

    public async Task InitializeClickAsync()
    {
        await _portalGate.WaitAsync();
        try
        {
            await EnsureSessionAsync(expectedStreams: null);
        }
        catch (Exception e)
        {
            Log.Error($"remote desktop portal setup failed: {e.Message}");
        }
        finally
        {
            _portalGate.Release();
        }
    }

    // The portal injects input regardless of focus, so the overlay can simply close first.
    public bool MakeClickThrough(Avalonia.Controls.Window window) => false;

    public async Task ClickAsync(Target target, int monitorCount)
    {
        await _portalGate.WaitAsync();
        try
        {
            await EnsureSessionAsync(monitorCount);
            // Each stream is its monitor's coordinate space, in logical pixels.
            var stream = _streams[target.Monitor.Index];
            double x = target.Fx * stream.Bounds.Width, y = target.Fy * stream.Bounds.Height;
            await NotifyAsync("NotifyPointerMotionAbsolute", "udd", (ref MessageWriter w) =>
            {
                w.WriteUInt32(stream.NodeId);
                w.WriteDouble(x);
                w.WriteDouble(y);
            });
            foreach (uint state in new[] { ButtonPressed, ButtonReleased })
                await NotifyAsync("NotifyPointerButton", "iu", (ref MessageWriter w) =>
                {
                    w.WriteInt32(BtnLeft);
                    w.WriteUInt32(state);
                });
        }
        catch (Exception e)
        {
            Log.Error($"portal click failed: {e.Message}");
            _session = null; // rebuild the session on the next attempt
        }
        finally
        {
            _portalGate.Release();
        }
    }

    public void InstallAutostart(string executable)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(AutostartFile)!);
        File.WriteAllText(AutostartFile, $"""
            [Desktop Entry]
            Type=Application
            Name=clicky
            Comment=Keyboard-driven grid clicker
            Exec="{executable}"
            Icon=input-mouse
            Terminal=false
            NoDisplay=true

            """);
    }

    public void UninstallAutostart() => File.Delete(AutostartFile);

    private async Task<DBus> ConnectAsync() => _bus ??= await DBus.ConnectSessionAsync();

    /// <summary>
    /// Makes sure a portal session is open, opening one if there is none or reopening it if the
    /// monitor count changed. <paramref name="expectedStreams"/> is the overlay's monitor count,
    /// or null to accept whatever the portal shares.
    /// </summary>
    private async Task EnsureSessionAsync(int? expectedStreams)
    {
        if (_session is not null && (expectedStreams is null || expectedStreams == _streams.Count))
            return;
        if (_session is not null)
        {
            Log.Info($"monitor count changed ({_streams.Count} -> {expectedStreams}); recreating portal session");
            await CloseSessionAsync();
        }
        await ConnectAsync();

        (_session, _streams) = await OpenSessionAsync();
        Log.Info($"remote desktop portal ready with {_streams.Count} monitor stream(s)");

        if (expectedStreams is not null && expectedStreams != _streams.Count)
            Log.Warn($"overlay has {expectedStreams} monitors but the portal shared {_streams.Count}; " +
                     "share all monitors in the permission dialog");
        if (_streams.Count == 0 || _streams.Count < expectedStreams)
        {
            await CloseSessionAsync();
            throw new InvalidOperationException("portal did not provide a stream for every monitor");
        }
    }

    /// <summary>
    /// Opens a RemoteDesktop session that can move the pointer, with one ScreenCast stream per
    /// monitor. The streams are only used as coordinate spaces for pointer motion; the PipeWire
    /// streams themselves are never opened.
    /// </summary>
    private async Task<(string Session, IReadOnlyList<PortalStream> Streams)> OpenSessionAsync()
    {
        var created = await RequestAsync(RemoteDesktop, "CreateSession", "a{sv}", token => (ref MessageWriter w) =>
            w.WriteDictionary(new Dictionary<string, VariantValue>
            {
                ["handle_token"] = token,
                ["session_handle_token"] = NextToken(),
            }));
        // Portal versions differ on whether this is a string or an object path.
        var handle = created["session_handle"];
        string session = handle.Type == VariantValueType.ObjectPath ? handle.GetObjectPathAsString() : handle.GetString();

        // With a saved restore token, KDE skips the permission dialog.
        string? restoreToken = LoadRestoreToken();
        await RequestAsync(RemoteDesktop, "SelectDevices", "oa{sv}", token => (ref MessageWriter w) =>
        {
            var options = new Dictionary<string, VariantValue>
            {
                ["handle_token"] = token,
                ["types"] = DevicePointer,
                ["persist_mode"] = PersistUntilRevoked,
            };
            if (restoreToken is not null)
                options["restore_token"] = restoreToken;
            w.WriteObjectPath(session);
            w.WriteDictionary(options);
        });
        await RequestAsync(ScreenCast, "SelectSources", "oa{sv}", token => (ref MessageWriter w) =>
        {
            w.WriteObjectPath(session);
            w.WriteDictionary(new Dictionary<string, VariantValue>
            {
                ["handle_token"] = token,
                ["types"] = SourceMonitor,
                ["multiple"] = true,
            });
        });

        Log.Info("starting remote desktop session (KDE asks for permission the first time)");
        var started = await RequestAsync(RemoteDesktop, "Start", "osa{sv}", token => (ref MessageWriter w) =>
        {
            w.WriteObjectPath(session);
            w.WriteString(""); // parent window
            w.WriteDictionary(new Dictionary<string, VariantValue> { ["handle_token"] = token });
        });

        if (started.TryGetValue("restore_token", out var newToken))
            SaveRestoreToken(newToken.GetString());
        var streams = started.TryGetValue("streams", out var value) ? SortStreams(ParseStreams(value)) : [];
        return (session, streams);
    }

    private async Task CloseSessionAsync()
    {
        if (_session is not null)
        {
            try
            {
                // Bounded so an unresponsive portal can't hang shutdown.
                await _bus!.CallAsync(PortalService, _session, "org.freedesktop.portal.Session", "Close")
                    .WaitAsync(TimeSpan.FromSeconds(1));
            }
            catch (Exception e) when (e is DBusException or TimeoutException)
            {
                // Already closed by the compositor, or the portal didn't answer.
            }
        }
        _session = null;
        _streams = [];
    }

    /// <summary>Calls a RemoteDesktop Notify* method, which all start with the session and an options dictionary.</summary>
    private Task NotifyAsync(string member, string argsSignature, DBus.BodyWriter args)
    {
        string session = _session!;
        return _bus!.CallAsync(PortalService, PortalPath, RemoteDesktop, member, "oa{sv}" + argsSignature,
            (ref MessageWriter w) =>
            {
                w.WriteObjectPath(session);
                w.WriteDictionary(NoOptions);
                args(ref w);
            });
    }

    /// <summary>
    /// Calls a portal method that answers through a Request object, and awaits that request's
    /// Response signal. <paramref name="body"/> gets the handle_token to put in the options.
    /// </summary>
    private async Task<Dictionary<string, VariantValue>> RequestAsync(string @interface, string member,
        string signature, Func<string, DBus.BodyWriter> body)
    {
        string token = NextToken();
        string sender = _bus!.UniqueName.TrimStart(':').Replace('.', '_');
        var response = new TaskCompletionSource<(uint Code, Dictionary<string, VariantValue> Results)>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        // Subscribe before calling so a fast Response can't be missed.
        using var watch = await _bus.WatchSignalAsync(PortalService, $"{PortalPath}/request/{sender}/{token}",
            "org.freedesktop.portal.Request", "Response",
            (m, _) =>
            {
                var r = m.GetBodyReader();
                return (Code: r.ReadUInt32(), Results: r.ReadDictionaryOfStringToVariantValue());
            },
            (error, value) =>
            {
                if (error is not null)
                    response.TrySetException(error);
                else
                    response.TrySetResult(value);
            });

        await _bus.CallAsync(PortalService, PortalPath, @interface, member, signature, body(token));
        var (code, results) = await response.Task;
        return code switch
        {
            0 => results,
            1 => throw new OperationCanceledException($"{member} was cancelled or denied"),
            _ => throw new InvalidOperationException($"{member} failed (response {code})"),
        };
    }

    private string NextToken() => $"clicky{Environment.ProcessId}_{Interlocked.Increment(ref _tokenCounter)}";

    /// <summary>Parses the a(ua{sv}) "streams" result, reading each stream's "position" and "size".</summary>
    internal static IEnumerable<PortalStream> ParseStreams(VariantValue streams)
    {
        for (int i = 0; i < streams.Count; i++)
        {
            var item = streams.GetItem(i);
            var props = item.GetItem(1).GetDictionary<string, VariantValue>();
            (int, int) Pair(string key) =>
                props.TryGetValue(key, out var v) ? (v.GetItem(0).GetInt32(), v.GetItem(1).GetInt32()) : (0, 0);
            var (x, y) = Pair("position");
            var (w, h) = Pair("size");
            yield return new PortalStream(item.GetItem(0).GetUInt32(), new ScreenBounds(x, y, w, h));
        }
    }

    /// <summary>Orders streams like <see cref="Grid.Order"/>, so stream k belongs to grid monitor k.</summary>
    internal static IReadOnlyList<PortalStream> SortStreams(IEnumerable<PortalStream> streams)
    {
        var list = streams.ToList();
        return Grid.Order(list.Select(s => s.Bounds).ToList()).Select(i => list[i]).ToList();
    }

    private static string? LoadRestoreToken()
    {
        try
        {
            if (!File.Exists(Paths.PortalTokenFile))
                return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(Paths.PortalTokenFile));
            return doc.RootElement.TryGetProperty("restoreToken", out var t) ? t.GetString() : null;
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            Log.Warn($"ignoring unreadable {Paths.PortalTokenFile}: {e.Message}");
            return null;
        }
    }

    private static void SaveRestoreToken(string token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Paths.PortalTokenFile)!);
        File.WriteAllText(Paths.PortalTokenFile,
            JsonSerializer.Serialize(new Dictionary<string, string> { ["restoreToken"] = token }));
    }

    public void Dispose()
    {
        _hotkeyWatch?.Dispose();
        CloseSessionAsync().GetAwaiter().GetResult();
        _bus?.Dispose();
        _portalGate.Dispose();
    }
}
