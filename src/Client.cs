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
                return (new ClientSession(c, s), "Access granted", p.Files ?? Array.Empty<string>());

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
    private bool disposed;

    internal ClientSession(TcpClient client, NetworkStream stream)
    {
        this.client = client;
        this.stream = stream;
    }

    public async Task<bool> DownloadAsync(
        string file, string destination, IProgress<(long completed, long total)>? progress = null, CancellationToken ct = default)
    {
        if (disposed) throw new ObjectDisposedException(nameof(ClientSession));

        var id = Guid.NewGuid().ToString("N");
        await Protocol.SendAsync(stream, new("DOWNLOAD_REQUEST", id, FileName: Path.GetFileName(file)), ct);
        var p = await Protocol.ReceiveAsync(stream, ct);

        if (p?.Type != "DOWNLOAD_ACCEPTED")
            throw new InvalidOperationException(p?.Message ?? "Download denied");

        var directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        await using var fs = new FileStream(destination, FileMode.Create, FileAccess.Write, System.IO.FileShare.None);
        var buf = new byte[64 * 64 * 16];
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

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try { stream.Dispose(); } catch { }
        try { client.Dispose(); } catch { }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
