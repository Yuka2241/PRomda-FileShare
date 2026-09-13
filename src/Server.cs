using System.IO;
using System.Net;
using System.Net.Sockets;

namespace PRomda.FileShare;

public sealed class Server
{
    private readonly Func<AccessRequest, Task<bool>> accessApproval;
    private readonly Dictionary<string, string> libraries = new(StringComparer.OrdinalIgnoreCase);
    private TcpListener? listener;
    private CancellationTokenSource? cts;

    public int Port { get; }

    public Server(int port, Func<AccessRequest, Task<bool>> accessApproval)
    {
        Port = port;
        this.accessApproval = accessApproval;
    }

    public void AddLibrary(string id, string folder) => libraries[id] = folder;

    public void Start()
    {
        if (listener != null) return;
        cts = new();
        listener = new TcpListener(IPAddress.Any, Port);
        listener.Start();
        _ = AcceptLoop(cts.Token);
    }

    public void Stop()
    {
        try { cts?.Cancel(); listener?.Stop(); } catch { }
        listener = null;
    }

    private async Task AcceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && listener != null)
        {
            try
            {
                var c = await listener.AcceptTcpClientAsync(ct);
                _ = Handle(c, ct);
            }
            catch (OperationCanceledException) { break; }
            catch { }
        }
    }

    private async Task Handle(TcpClient c, CancellationToken ct)
    {
        using (c)
        using (var s = c.GetStream())
        {
            try
            {
                var hello = await Protocol.ReceiveAsync(s, ct);
                if (hello?.Type != "ACCESS_REQUEST" || string.IsNullOrWhiteSpace(hello.Code)) return;

                if (!libraries.TryGetValue(hello.Code, out var folder))
                {
                    await Protocol.SendAsync(s, new("ACCESS_DENIED", Message: "Library not found"), ct);
                    return;
                }

                var req = new AccessRequest(
                    hello.RequestId ?? Guid.NewGuid().ToString("N"),
                    hello.Code,
                    hello.Message ?? "Unknown user");

                // One permission request per connection, at connection time only.
                var ok = await accessApproval(req);
                if (!ok)
                {
                    await Protocol.SendAsync(s, new("ACCESS_DENIED", Message: "Owner denied access"), ct);
                    return;
                }

                var list = Directory.Exists(folder)
                    ? Directory.EnumerateFiles(folder)
                        .Select(Path.GetFileName)
                        .Where(x => x != null)
                        .Cast<string>()
                        .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                        .ToArray()
                    : Array.Empty<string>();

                await Protocol.SendAsync(s, new("ACCESS_GRANTED", Files: list), ct);

                // Keep the approved TCP session alive. Downloads require no extra approval.
                while (!ct.IsCancellationRequested)
                {
                    var p = await Protocol.ReceiveAsync(s, ct);
                    if (p == null) return;
                    if (p.Type != "DOWNLOAD_REQUEST" || string.IsNullOrWhiteSpace(p.FileName)) continue;

                    var safe = Path.GetFileName(p.FileName);
                    var path = Path.GetFullPath(Path.Combine(folder, safe));
                    var basePath = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

                    if (!path.StartsWith(basePath, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                    {
                        await Protocol.SendAsync(s, new("DOWNLOAD_DENIED", RequestId: p.RequestId, Message: "File not found"), ct);
                        continue;
                    }

                    var size = new FileInfo(path).Length;
                    await Protocol.SendAsync(s, new("DOWNLOAD_ACCEPTED", RequestId: p.RequestId, FileName: safe, Size: size), ct);

                    await using var fs = File.OpenRead(path);
                    var buf = new byte[64 * 1024];
                    int n;
                    while ((n = await fs.ReadAsync(buf.AsMemory(0, buf.Length), ct)) > 0)
                        await s.WriteAsync(buf.AsMemory(0, n), ct);
                    await s.FlushAsync(ct);
                }
            }
            catch (OperationCanceledException) { }
            catch { }
        }
    }
}
