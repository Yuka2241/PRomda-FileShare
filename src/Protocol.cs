using System.IO;
using System.Text.Json;

namespace PRomda.FileShare;

public sealed record Packet(string Type, string? RequestId = null, string? Code = null, string? FileName = null,
    long Size = 0, string? Message = null, string[]? Files = null);

public static class Protocol
{
    public static async Task SendAsync(Stream s, Packet p, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(p) + "\n";
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        await s.WriteAsync(bytes, ct);
        await s.FlushAsync(ct);
    }

    public static async Task<Packet?> ReceiveAsync(Stream s, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[4096];
        while (true)
        {
            var n = await s.ReadAsync(buffer.AsMemory(), ct);
            if (n == 0) return null;

            var start = 0;
            for (var i = 0; i < n; i++)
            {
                if (buffer[i] != (byte)'\n') continue;
                if (i > start) ms.Write(buffer, start, i - start);
                if (ms.Length > 1024 * 1024) throw new InvalidDataException("Packet too large");
                return JsonSerializer.Deserialize<Packet>(ms.ToArray());
            }

            ms.Write(buffer, start, n);
            if (ms.Length > 1024 * 1024) throw new InvalidDataException("Packet too large");
        }
    }
}
