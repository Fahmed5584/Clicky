using Tmds.DBus.Protocol;

namespace Clicky.Platform.Linux;

internal delegate void BodyWriter(ref MessageWriter writer);

internal static class DBus
{
    public static MessageBuffer MethodCall(Connection connection, string destination, string path,
        string @interface, string member, string? signature = null, BodyWriter? body = null)
    {
        var writer = connection.GetMessageWriter();
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

    public static Task CallAsync(Connection connection, string destination, string path,
        string @interface, string member, string? signature = null, BodyWriter? body = null) =>
        connection.CallMethodAsync(MethodCall(connection, destination, path, @interface, member, signature, body));

    public static Task<T> CallAsync<T>(Connection connection, string destination, string path,
        string @interface, string member, string? signature, BodyWriter? body, MessageValueReader<T> reader) =>
        connection.CallMethodAsync(MethodCall(connection, destination, path, @interface, member, signature, body),
            reader, null);

    /// <summary>Subscribes to a signal; the handler gets either an exception or a value.</summary>
    public static async Task<IDisposable> WatchSignalAsync<T>(Connection connection, string sender, string path,
        string @interface, string member, MessageValueReader<T> reader, Action<Exception?, T> handler) =>
        await connection.AddMatchAsync(
            new MatchRule { Type = MessageType.Signal, Sender = sender, Path = path, Interface = @interface, Member = member },
            reader, (e, value, _, _) => handler(e, value),
            ObserverFlags.None, readerState: null, handlerState: null, emitOnCapturedContext: false);
}
