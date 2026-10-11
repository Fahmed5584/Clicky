using System.IO.Pipes;

namespace Clicky;

/// <summary>
/// One daemon per user. `clicky show` and the already-running check talk to it over a
/// named pipe (a Unix domain socket on Linux).
/// </summary>
public static class Ipc
{
    public const string Show = "show";
    public const string Ping = "ping";

    private static string PipeName => "clicky-" + Environment.UserName;

    /// <summary>Sends a command to the running daemon; false if none is running.</summary>
    public static bool TrySend(string command)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(300);
            using var writer = new StreamWriter(client);
            writer.WriteLine(command);
            return true;
        }
        catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Listens for commands on a background task until disposed.</summary>
    public sealed class Server : IDisposable
    {
        private readonly CancellationTokenSource _cts = new();

        public Server(Action<string> onCommand) => _ = Task.Run(() => RunAsync(onCommand, _cts.Token));

        private static async Task RunAsync(Action<string> onCommand, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await using var pipe = Listen();
                    await pipe.WaitForConnectionAsync(ct);
                    using var reader = new StreamReader(pipe);
                    while (await reader.ReadLineAsync(ct) is { } line)
                        onCommand(line.Trim());
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (IOException e)
                {
                    Log.Warn($"IPC: {e.Message}");
                    await Task.Delay(1000, ct).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                }
            }
        }

        private static NamedPipeServerStream Listen()
        {
            NamedPipeServerStream Create() => new(PipeName, PipeDirection.In, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try
            {
                return Create();
            }
            catch (IOException) when (!OperatingSystem.IsWindows())
            {
                // A crashed daemon can leave its socket file behind.
                File.Delete(Path.Combine(Path.GetTempPath(), "CoreFxPipe_" + PipeName));
                return Create();
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
        }
    }
}
