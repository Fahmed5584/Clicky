using System.Text.Json;
using Clicky.Core;
using Tmds.DBus.Protocol;

namespace Clicky.Platform.Linux;

/// <summary>A monitor stream from the ScreenCast portal, in compositor logical coordinates.</summary>
public readonly record struct PortalStream(uint NodeId, ScreenBounds Bounds);

/// <summary>
/// Pointer control through the xdg-desktop-portal RemoteDesktop interface. A ScreenCast
/// source selection supplies one stream per monitor, which is what absolute motion is
/// relative to; the PipeWire streams themselves are never opened.
/// </summary>
public sealed class PortalPointer : IPointerService
{
    private const string Service = "org.freedesktop.portal.Desktop";
    private const string ObjectPath = "/org/freedesktop/portal/desktop";
    private const string RemoteDesktop = "org.freedesktop.portal.RemoteDesktop";
    private const string ScreenCast = "org.freedesktop.portal.ScreenCast";
    private const uint DevicePointer = 2;
    private const uint SourceMonitor = 1;
    private const uint PersistUntilRevoked = 2;
    private const int BtnLeft = 0x110; // linux/input-event-codes.h

    private static readonly string TokenFile = Path.Combine(Paths.DataDir, "portal.json");

    private readonly SemaphoreSlim _gate = new(1, 1);
    private Connection? _connection;
    private string? _session;
    private IReadOnlyList<PortalStream> _streams = [];
    private int _tokenCounter;

    public async Task InitializeAsync()
    {
        await _gate.WaitAsync();
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
            _gate.Release();
        }
    }

    public async Task ClickAsync(ClickTarget target)
    {
        await _gate.WaitAsync();
        try
        {
            await EnsureSessionAsync(target.ScreenCount);
            if (_session is null || _connection is null)
            {
                Log.Error("cannot click: no remote desktop portal session");
                return;
            }
            var stream = _streams[target.Order];
            double x = target.Fx * stream.Bounds.Width;
            double y = target.Fy * stream.Bounds.Height;

            await DBus.CallAsync(_connection, Service, ObjectPath, RemoteDesktop, "NotifyPointerMotionAbsolute",
                "oa{sv}udd", (ref MessageWriter w) =>
                {
                    w.WriteObjectPath(_session);
                    w.WriteDictionary(new Dictionary<string, VariantValue>());
                    w.WriteUInt32(stream.NodeId);
                    w.WriteDouble(x);
                    w.WriteDouble(y);
                });
            await Button(1);
            await Button(0);
        }
        catch (Exception e)
        {
            Log.Error($"portal click failed: {e.Message}");
            _session = null; // rebuild the session on the next attempt
        }
        finally
        {
            _gate.Release();
        }
    }

    private Task Button(uint state) =>
        DBus.CallAsync(_connection!, Service, ObjectPath, RemoteDesktop, "NotifyPointerButton",
            "oa{sv}iu", (ref MessageWriter w) =>
            {
                w.WriteObjectPath(_session!);
                w.WriteDictionary(new Dictionary<string, VariantValue>());
                w.WriteInt32(BtnLeft);
                w.WriteUInt32(state);
            });

    /// <summary>Creates the session if missing or if the monitor count changed.</summary>
    private async Task EnsureSessionAsync(int? expectedStreams)
    {
        if (_session is not null && (expectedStreams is null || expectedStreams == _streams.Count))
            return;
        if (_session is not null)
        {
            Log.Info($"monitor count changed ({_streams.Count} -> {expectedStreams}); recreating portal session");
            await CloseSessionAsync();
        }

        if (_connection is null)
        {
            _connection = new Connection(Address.Session
                ?? throw new InvalidOperationException("no D-Bus session bus address"));
            await _connection.ConnectAsync();
        }

        var created = await RequestAsync(RemoteDesktop, "CreateSession", "a{sv}", token => (ref MessageWriter w) =>
            w.WriteDictionary(new Dictionary<string, VariantValue>
            {
                ["handle_token"] = token,
                ["session_handle_token"] = NextToken(),
            }));
        var sessionValue = created["session_handle"];
        string session = sessionValue.Type == VariantValueType.ObjectPath
            ? sessionValue.GetObjectPathAsString()
            : sessionValue.GetString();

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
            w.WriteString("");
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
        if (_streams.Count == 0 || (expectedStreams is not null && expectedStreams > _streams.Count))
        {
            await CloseSessionAsync();
            throw new InvalidOperationException("portal did not provide a stream for every monitor");
        }
    }

    private async Task CloseSessionAsync()
    {
        if (_session is not null && _connection is not null)
        {
            try
            {
                await DBus.CallAsync(_connection, Service, _session, "org.freedesktop.portal.Session", "Close");
            }
            catch (DBusException)
            {
                // Already closed by the compositor.
            }
        }
        _session = null;
        _streams = [];
    }

    /// <summary>Calls a portal method returning a Request handle and awaits its Response signal.</summary>
    private async Task<Dictionary<string, VariantValue>> RequestAsync(string @interface, string member,
        string signature, Func<string, BodyWriter> body)
    {
        var connection = _connection!;
        string token = NextToken();
        string sender = connection.UniqueName!.TrimStart(':').Replace('.', '_');
        string requestPath = $"{ObjectPath}/request/{sender}/{token}";

        var response = new TaskCompletionSource<(uint Code, Dictionary<string, VariantValue> Results)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        // Subscribe before calling so a fast Response can't be missed.
        using var watch = await DBus.WatchSignalAsync(connection, Service, requestPath,
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

        await DBus.CallAsync(connection, Service, ObjectPath, @interface, member, signature, body(token));
        var (code, results) = await response.Task;
        return code switch
        {
            0 => results,
            1 => throw new OperationCanceledException($"{member} was cancelled or denied"),
            _ => throw new InvalidOperationException($"{member} failed (response {code})"),
        };
    }

    private string NextToken() => $"clicky{Environment.ProcessId}_{Interlocked.Increment(ref _tokenCounter)}";

    internal static IEnumerable<PortalStream> ParseStreams(VariantValue streams)
    {
        // a(ua{sv}) with optional "position" (ii) and "size" (ii) properties.
        for (int i = 0; i < streams.Count; i++)
        {
            var item = streams.GetItem(i);
            uint nodeId = item.GetItem(0).GetUInt32();
            var props = item.GetItem(1).GetDictionary<string, VariantValue>();
            (int X, int Y) Pair(string key) =>
                props.TryGetValue(key, out var v) ? (v.GetItem(0).GetInt32(), v.GetItem(1).GetInt32()) : (0, 0);
            var (x, y) = Pair("position");
            var (w, h) = Pair("size");
            yield return new PortalStream(nodeId, new ScreenBounds(x, y, w, h));
        }
    }

    /// <summary>Orders streams like <see cref="GridLayout.Order"/> so index k pairs with monitor order k.</summary>
    internal static IReadOnlyList<PortalStream> SortStreams(IEnumerable<PortalStream> streams)
    {
        var list = streams.ToList();
        return GridLayout.Order(list.Select(s => s.Bounds).ToList()).Select(i => list[i]).ToList();
    }

    private static string? LoadRestoreToken()
    {
        try
        {
            if (!File.Exists(TokenFile))
                return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(TokenFile));
            return doc.RootElement.TryGetProperty("restoreToken", out var t) ? t.GetString() : null;
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            Log.Warn($"ignoring unreadable {TokenFile}: {e.Message}");
            return null;
        }
    }

    private static void SaveRestoreToken(string token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(TokenFile)!);
        File.WriteAllText(TokenFile, JsonSerializer.Serialize(new Dictionary<string, string> { ["restoreToken"] = token }));
    }

    public void Dispose()
    {
        CloseSessionAsync().GetAwaiter().GetResult();
        _connection?.Dispose();
        _gate.Dispose();
    }
}
