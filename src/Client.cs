using System.IO;
using System.Net.Sockets;

namespace PRomda.FileShare;

public sealed class Client
{
    public async Task<(ClientSession? session, string message, string[] files)> ConnectAsync(
        string host, int port, string code, string name, CancellationToken ct = default)
    {
        var c = new TcpClient();
        try
        {
            await c.ConnectAsync(host, port, ct);
            var s = c.GetStream();
            await Protocol.SendAsync(s, new("ACCESS_REQUEST", Guid.NewGuid().ToString("N"), Code: code, Message: name), ct);
            var p = await Protocol.ReceiveAsync(s, ct);

            if (p?.Type == "ACCESS_GRANTED")
            {
                var session = new ClientSession(c, s, host, port, p.Message);
                if (!string.IsNullOrWhiteSpace(p.Message))
                    await session.StartWatcherAsync(ct);
                return (session, "Access granted", p.Files ?? Array.Empty<string>());
            }

            c.Dispose();
            return (null, p?.Message ?? "Connection failed", Array.Empty<string>());
        }
        catch
        {
            c.Dispose();
            throw;
        }
    }
}

public sealed class ClientSession : IDisposable, IAsyncDisposable
{
    private readonly TcpClient client;
    private readonly NetworkStream stream;
    private readonly string host;
    private readonly int port;
    private readonly string? sessionToken;
    private TcpClient? watcherClient;
    private NetworkStream? watcherStream;
    private CancellationTokenSource? watcherCts;
    private bool disposed;
    private readonly SemaphoreSlim downloadGate = new(1, 1);

    public event Action<string[]?>? FilesUpdated;

    internal ClientSession(TcpClient client, NetworkStream stream, string host, int port, string? sessionToken)
    {
        this.client = client;
        this.stream = stream;
        this.host = host;
        this.port = port;
        this.sessionToken = sessionToken;
    }

    internal async Task StartWatcherAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sessionToken) || disposed) return;
        watcherClient = new TcpClient();
        await watcherClient.ConnectAsync(host, port, ct);
        watcherStream = watcherClient.GetStream();
        watcherCts = new CancellationTokenSource();
        await Protocol.SendAsync(watcherStream, new("WATCH_REQUEST", Code: null, Message: sessionToken), ct);
        _ = WatchLoopAsync(watcherStream, watcherCts.Token);
    }

    private async Task WatchLoopAsync(NetworkStream watchStream, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var packet = await Protocol.ReceiveAsync(watchStream, ct);
                if (packet == null) break;
                if (packet.Type == "FILE_LIST_UPDATE")
                    FilesUpdated?.Invoke(packet.Files ?? Array.Empty<string>());
                else if (packet.Type == "WATCH_DENIED")
                    break;
            }
        }
        catch (OperationCanceledException) { }
        catch { }
    }

    public async Task<bool> DownloadAsync(
        string file, string destination, IProgress<(long completed, long total)>? progress = null, CancellationToken ct = default)
    {
        if (disposed) throw new ObjectDisposedException(nameof(ClientSession));
        await downloadGate.WaitAsync(ct);
        try
        {
            var id = Guid.NewGuid().ToString("N");
            await Protocol.SendAsync(stream, new("DOWNLOAD_REQUEST", id, FileName: Path.GetFileName(file)), ct);
            var p = await Protocol.ReceiveAsync(stream, ct);

            if (p?.Type != "DOWNLOAD_ACCEPTED")
                throw new InvalidOperationException(p?.Message ?? "Download denied");

            var directory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

            await using var fs = new FileStream(destination, FileMode.Create, FileAccess.Write, System.IO.FileShare.None);
            var buf = new byte[64 * 1024];
            long total = 0;

            while (total < p.Size)
            {
                var wanted = (int)Math.Min(buf.Length, p.Size - total);
                var n = await stream.ReadAsync(buf.AsMemory(0, wanted), ct);
                if (n <= 0) break;
                await fs.WriteAsync(buf.AsMemory(0, n), ct);
                total += n;
                progress?.Report((total, p.Size));
            }

            return total == p.Size;
        }
        finally
        {
            downloadGate.Release();
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try { watcherCts?.Cancel(); } catch { }
        try { watcherStream?.Dispose(); } catch { }
        try { watcherClient?.Dispose(); } catch { }
        try { stream.Dispose(); } catch { }
        try { client.Dispose(); } catch { }
        watcherCts?.Dispose();
        downloadGate.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
