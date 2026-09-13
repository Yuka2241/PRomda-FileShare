using System.Security.Cryptography;
using System.Text;

namespace PRomda.FileShare;

public static class CodeGenerator
{
    private const string Chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    public static string RandomToken(int bytes = 16)
    {
        var b = RandomNumberGenerator.GetBytes(bytes);
        return Convert.ToHexString(b);
    }
    public static string CreateLibraryCode(string host, int port, string libraryId)
    {
        var raw = $"{host}|{port}|{libraryId}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw))
            .Replace("+","-").Replace("/","_").TrimEnd('=');
    }
    public static bool TryParse(string code, out string host, out int port, out string libraryId)
    {
        host = ""; port = 0; libraryId = "";
        try
        {
            var s = code.Replace("-","+").Replace("_","/");
            s = s.PadRight(s.Length + (4-s.Length%4)%4, '=');
            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(s));
            var p = raw.Split('|');
            if (p.Length != 3 || !int.TryParse(p[1], out port)) return false;
            host=p[0]; libraryId=p[2];
            return !string.IsNullOrWhiteSpace(host) && port is > 0 and <= 65535 && libraryId.Length > 0;
        } catch { return false; }
    }
}
