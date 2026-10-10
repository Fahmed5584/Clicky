using System.IO.Pipes;

namespace Clicky.Core;

/// <summary>
/// One daemon per user. `clicky show` talks to it over a named pipe
/// (a Unix domain socket on Linux).
/// </summary>
public static class SingleInstance
{
    public static string PipeName => "clicky-" + Environment.UserName;

    /// <summary>Sends a command to a running daemon; false if none is running.</summary>
    public static bool TrySend(string command)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(300);
            using var writer = new StreamWriter(client);
            writer.WriteLine(command);
            writer.Flush();
            return true;
        }
        catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

public sealed class SingleInstanceServer(Action<string> onCommand) : IDisposable
{
    private readonly CancellationTokenSource _cts = new();

    public void Start() => _ = Task.Run(() => RunAsync(_cts.Token));

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var server = CreateServer();
                await server.WaitForConnectionAsync(ct);
                using var reader = new StreamReader(server);
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

    private static NamedPipeServerStream CreateServer()
    {
        NamedPipeServerStream Create() => new(SingleInstance.PipeName, PipeDirection.In, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            return Create();
        }
        catch (IOException) when (!OperatingSystem.IsWindows())
        {
            // A crashed daemon can leave its socket file behind.
            File.Delete(Path.Combine(Path.GetTempPath(), "CoreFxPipe_" + SingleInstance.PipeName));
            return Create();
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
