using System.Text.Json;
using Tmds.DBus.Protocol;

namespace Clicky;

/// <summary>A monitor stream from the ScreenCast portal, in compositor logical coordinates.</summary>
public readonly record struct PortalStream(uint NodeId, ScreenBounds Bounds);

/// <summary>
/// KDE Plasma (Wayland) over D-Bus: the hotkey through kglobalaccel, clicks through the
/// xdg-desktop-portal RemoteDesktop interface, autostart through a .desktop file.
/// </summary>
/// <remarks>
/// Tmds.DBus.Protocol must stay at the version Avalonia.FreeDesktop is built against (0.21.x);
/// see CLAUDE.md.
/// </remarks>
public sealed class KdePlatform : IPlatform
{
    private delegate void BodyWriter(ref MessageWriter writer);

    // kglobalaccel
    private const string AccelService = "org.kde.kglobalaccel";
    private const string Component = "clicky";
    private const string Action = "show-grid";
    private static readonly string[] ActionId = [Component, Action, "clicky", "Show click grid"];
    private const uint IsDefault = 1, SetPresent = 2, NoAutoloading = 4; // KGlobalAccel::SetShortcutFlag

    // xdg-desktop-portal
    private const string PortalService = "org.freedesktop.portal.Desktop";
    private const string PortalPath = "/org/freedesktop/portal/desktop";
    private const string RemoteDesktop = "org.freedesktop.portal.RemoteDesktop";
    private const string ScreenCast = "org.freedesktop.portal.ScreenCast";
    private const uint DevicePointer = 2, SourceMonitor = 1, PersistUntilRevoked = 2;
    private const int BtnLeft = 0x110; // linux/input-event-codes.h

    private static string AutostartFile => Path.Combine(Paths.ConfigHome, "autostart", "clicky.desktop");

    private readonly SemaphoreSlim _portalGate = new(1, 1);
    private Connection? _connection;
    private IDisposable? _hotkeyWatch;
    private string? _session;
    private IReadOnlyList<PortalStream> _streams = [];
    private int _tokenCounter;

    public async Task<bool> RegisterHotkeyAsync(Hotkey hotkey, Action onPressed)
    {
        try
        {
            var connection = await ConnectAsync();
            int[] keys = [hotkey.ToQtKey()];
            await CallAsync(AccelService, "/kglobalaccel", "org.kde.KGlobalAccel", "doRegister", "as",
                (ref MessageWriter w) => w.WriteArray(ActionId));
            // First as the default shortcut, then as the active one (what KDE's own apps do).
            foreach (uint flags in new[] { IsDefault, SetPresent | NoAutoloading })
                await CallAsync(AccelService, "/kglobalaccel", "org.kde.KGlobalAccel", "setShortcut", "asaiu",
                    (ref MessageWriter w) =>
                    {
                        w.WriteArray(ActionId);
                        w.WriteArray(keys);
                        w.WriteUInt32(flags);
                    });
            string componentPath = await connection.CallMethodAsync(
                MethodCall(AccelService, "/kglobalaccel", "org.kde.KGlobalAccel", "getComponent", "s",
                    (ref MessageWriter w) => w.WriteString(Component)),
                (m, _) => m.GetBodyReader().ReadObjectPathAsString());

            _hotkeyWatch = await WatchSignalAsync(AccelService, componentPath,
                "org.kde.kglobalaccel.Component", "globalShortcutPressed",
                (m, _) =>
                {
                    var r = m.GetBodyReader();
                    return (Component: r.ReadString(), Action: r.ReadString());
                },
                (error, value) =>
                {
                    if (error is null && value is (Component, Action))
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
            var stream = _streams[target.Monitor.Index];
            string session = _session!;
            var noOptions = new Dictionary<string, VariantValue>();

            await CallAsync(PortalService, PortalPath, RemoteDesktop, "NotifyPointerMotionAbsolute", "oa{sv}udd",
                (ref MessageWriter w) =>
                {
                    w.WriteObjectPath(session);
                    w.WriteDictionary(noOptions);
                    w.WriteUInt32(stream.NodeId);
                    w.WriteDouble(target.Fx * stream.Bounds.Width);
                    w.WriteDouble(target.Fy * stream.Bounds.Height);
                });
            foreach (uint state in new[] { 1u, 0u })
                await CallAsync(PortalService, PortalPath, RemoteDesktop, "NotifyPointerButton", "oa{sv}iu",
                    (ref MessageWriter w) =>
                    {
                        w.WriteObjectPath(session);
                        w.WriteDictionary(noOptions);
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

    /// <summary>
    /// Opens a RemoteDesktop session with one ScreenCast stream per monitor, or reopens it if
    /// the monitor count changed. The streams are only used as coordinate spaces for pointer
    /// motion; the PipeWire streams themselves are never opened.
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

        var created = await RequestAsync(RemoteDesktop, "CreateSession", "a{sv}", token => (ref MessageWriter w) =>
            w.WriteDictionary(new Dictionary<string, VariantValue>
            {
                ["handle_token"] = token,
                ["session_handle_token"] = NextToken(),
            }));
        var handle = created["session_handle"];
        string session = handle.Type == VariantValueType.ObjectPath ? handle.GetObjectPathAsString() : handle.GetString();

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
        _streams = started.TryGetValue("streams", out var streams) ? SortStreams(ParseStreams(streams)) : [];
        _session = session;
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

    private async Task CloseSessionAsync()
    {
        if (_session is not null)
        {
            try
            {
                // Bounded so an unresponsive portal can't hang shutdown.
                await CallAsync(PortalService, _session, "org.freedesktop.portal.Session", "Close")
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

    /// <summary>Calls a portal method that returns a Request handle, and awaits the request's Response signal.</summary>
    private async Task<Dictionary<string, VariantValue>> RequestAsync(string @interface, string member,
        string signature, Func<string, BodyWriter> body)
    {
        string token = NextToken();
        string sender = _connection!.UniqueName!.TrimStart(':').Replace('.', '_');
        var response = new TaskCompletionSource<(uint Code, Dictionary<string, VariantValue> Results)>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        // Subscribe before calling so a fast Response can't be missed.
        using var watch = await WatchSignalAsync(PortalService, $"{PortalPath}/request/{sender}/{token}",
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

        await CallAsync(PortalService, PortalPath, @interface, member, signature, body(token));
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

    // --- D-Bus plumbing ---

    private async Task<Connection> ConnectAsync()
    {
        if (_connection is null)
        {
            var connection = new Connection(Address.Session
                ?? throw new InvalidOperationException("no D-Bus session bus address"));
            await connection.ConnectAsync();
            _connection = connection;
        }
        return _connection;
    }

    private MessageBuffer MethodCall(string destination, string path, string @interface, string member,
        string? signature = null, BodyWriter? body = null)
    {
        var writer = _connection!.GetMessageWriter();
        try
        {
            writer.WriteMethodCallHeader(destination, path, @interface, member, signature);
            body?.Invoke(ref writer);
            return writer.CreateMessage();
        }
        finally
        {
            writer.Dispose();
        }
    }

    private Task CallAsync(string destination, string path, string @interface, string member,
        string? signature = null, BodyWriter? body = null) =>
        _connection!.CallMethodAsync(MethodCall(destination, path, @interface, member, signature, body));

    /// <summary>Subscribes to a signal; the handler gets either an error or the decoded value.</summary>
    private async Task<IDisposable> WatchSignalAsync<T>(string sender, string path, string @interface,
        string member, MessageValueReader<T> reader, Action<Exception?, T> handler) =>
        await _connection!.AddMatchAsync(
            new MatchRule { Type = MessageType.Signal, Sender = sender, Path = path, Interface = @interface, Member = member },
            reader, (e, value, _, _) => handler(e, value),
            ObserverFlags.None, readerState: null, handlerState: null, emitOnCapturedContext: false);

    public void Dispose()
    {
        _hotkeyWatch?.Dispose();
        CloseSessionAsync().GetAwaiter().GetResult();
        _connection?.Dispose();
        _portalGate.Dispose();
    }
}
