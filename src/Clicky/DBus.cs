using Tmds.DBus.Protocol;

namespace Clicky;

/// <summary>
/// A session-bus connection with just the two things <see cref="KdePlatform"/> needs: method
/// calls and signal subscriptions.
/// </summary>
/// <remarks>
/// Tmds.DBus.Protocol must stay at the version Avalonia.FreeDesktop is built against (0.21.x);
/// see CLAUDE.md.
/// </remarks>
public sealed class DBus : IDisposable
{
    /// <summary>Writes a method call's arguments, matching the call's signature.</summary>
    public delegate void BodyWriter(ref MessageWriter writer);

    private readonly Connection _connection;

    private DBus(Connection connection) => _connection = connection;

    public static async Task<DBus> ConnectSessionAsync()
    {
        var connection = new Connection(Address.Session
            ?? throw new InvalidOperationException("no D-Bus session bus address"));
        await connection.ConnectAsync();
        return new DBus(connection);
    }

    /// <summary>This connection's unique bus name, such as ":1.42".</summary>
    public string UniqueName => _connection.UniqueName!;

    public Task CallAsync(string destination, string path, string @interface, string member,
        string? signature = null, BodyWriter? body = null) =>
        _connection.CallMethodAsync(MethodCall(destination, path, @interface, member, signature, body));

    /// <summary>Calls a method and decodes its reply with <paramref name="reply"/>.</summary>
    public Task<T> CallAsync<T>(string destination, string path, string @interface, string member,
        string signature, BodyWriter body, MessageValueReader<T> reply) =>
        _connection.CallMethodAsync(MethodCall(destination, path, @interface, member, signature, body), reply);

    /// <summary>Subscribes to a signal; the handler gets either an error or the decoded value.</summary>
    public async Task<IDisposable> WatchSignalAsync<T>(string sender, string path, string @interface,
        string member, MessageValueReader<T> reader, Action<Exception?, T> handler) =>
        await _connection.AddMatchAsync(
            new MatchRule { Type = MessageType.Signal, Sender = sender, Path = path, Interface = @interface, Member = member },
            reader, (e, value, _, _) => handler(e, value),
            ObserverFlags.None, readerState: null, handlerState: null, emitOnCapturedContext: false);

    private MessageBuffer MethodCall(string destination, string path, string @interface, string member,
        string? signature, BodyWriter? body)
    {
        // Not `using`: the body writer needs the writer by ref.
        var writer = _connection.GetMessageWriter();
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

    public void Dispose() => _connection.Dispose();
}
