using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using RadmirMonitor.Models;

namespace RadmirMonitor.Services;

public sealed class MobileServer : IDisposable
{
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private IPAddress? _address;
    private readonly Func<List<DailyStat>> _historyProvider;
    private readonly Func<string> _nickProvider;
    private readonly Func<string> _tokenProvider;
    public const int Port = 8765;

    public MobileServer(Func<List<DailyStat>> historyProvider, Func<string> nickProvider, Func<string> tokenProvider)
    {
        _historyProvider = historyProvider;
        _nickProvider = nickProvider;
        _tokenProvider = tokenProvider;
    }

    public bool IsRunning => _listener is not null;

    public void Start()
    {
        if (_listener is not null) return;
        _cts = new CancellationTokenSource();
        _address = LocalIp();
        _listener = new TcpListener(_address, Port);
        _listener.Start();
        _ = Task.Run(() => AcceptLoop(_cts.Token));
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        try { _listener?.Stop(); } catch { }
        _cts?.Dispose(); _cts = null; _listener = null; _address = null;
    }

    public string Url()
    {
        var ip = _address ?? LocalIp();
        return $"http://{ip}:{Port}/norm?token={Uri.EscapeDataString(_tokenProvider())}";
    }

    private async Task AcceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener is not null)
        {
            TcpClient? client = null;
            try
            {
                client = await _listener.AcceptTcpClientAsync(ct);
                _ = Task.Run(() => Handle(client), ct);
            }
            catch (OperationCanceledException) { break; }
            catch { client?.Dispose(); await Task.Delay(250, ct); }
        }
    }

    private async Task Handle(TcpClient client)
    {
        using (client)
        using (var stream = client.GetStream())
        using (var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, true))
        {
            var first = await reader.ReadLineAsync() ?? "";
            string? line;
            while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync())) { }
            var parts = first.Split(' ');
            var target = parts.Length > 1 ? parts[1] : "/";
            var uri = new Uri("http://localhost" + target);
            var token = ParseQueryValue(uri.Query, "token");
            if (!CryptographicEquals(token, _tokenProvider()))
            {
                await Write(stream, 403, "text/plain; charset=utf-8", "Forbidden");
                return;
            }
            if (!uri.AbsolutePath.Equals("/norm", StringComparison.OrdinalIgnoreCase))
            {
                await Write(stream, 404, "text/plain; charset=utf-8", "Not found");
                return;
            }
            await Write(stream, 200, "text/html; charset=utf-8", BuildHtml());
        }
    }

    private string BuildHtml()
    {
        static string H(string? s) => WebUtility.HtmlEncode(s ?? "");
        var sb = new StringBuilder();
        sb.Append("<!doctype html><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'>");
        sb.Append("<style>body{margin:0;background:#0b1119;color:#f4f7fb;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',sans-serif}main{max-width:960px;margin:auto;padding:18px}h1{font-size:22px;margin:0 0 14px}.card{background:#142032;border:1px solid #24334a;border-radius:14px;overflow:hidden}table{width:100%;border-collapse:collapse;font-size:13px}th{background:#18263a;color:#a9bdd8;text-align:center;padding:10px 6px;position:sticky;top:0}td{padding:10px 6px;border-top:1px solid #203149;text-align:center}tr:nth-child(even){background:#101a28}.nick{color:#4ca6ff;font-weight:700}</style>");
        sb.Append("<main><h1>RADMIR MONITOR · <span class='nick'>").Append(H(_nickProvider())).Append("</span></h1><div class='card'><table><thead><tr><th>Дата</th><th>Реп</th><th>Бан</th><th>Мут</th><th>Кик</th><th>Варн</th><th>Дем</th><th>Онлайн</th></tr></thead><tbody>");
        foreach (var r in _historyProvider().Take(31))
            sb.Append("<tr><td>").Append(H(r.Day)).Append("</td><td>").Append(r.Report).Append("</td><td>").Append(r.Ban).Append("</td><td>").Append(r.Mute).Append("</td><td>").Append(r.Kick).Append("</td><td>").Append(r.Warn).Append("</td><td>").Append(r.Jail).Append("</td><td>").Append(H(r.OnlineText)).Append("</td></tr>");
        sb.Append("</tbody></table></div></main>");
        return sb.ToString();
    }

    private static async Task Write(NetworkStream stream, int code, string type, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var head = Encoding.ASCII.GetBytes($"HTTP/1.1 {code} {(code == 200 ? "OK" : "Error")}\r\nContent-Type: {type}\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\nCache-Control: no-store\r\n\r\n");
        await stream.WriteAsync(head); await stream.WriteAsync(bytes);
    }

    private static IPAddress LocalIp()
    {
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces().Where(x => x.OperationalStatus == OperationalStatus.Up))
        {
            var address = adapter.GetIPProperties().UnicastAddresses
                .Select(x => x.Address)
                .FirstOrDefault(IsPrivateIpv4);
            if (address is not null) return address;
        }
        return IPAddress.Loopback;
    }

    private static bool IsPrivateIpv4(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10 ||
               (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
               (bytes[0] == 192 && bytes[1] == 168) ||
               (bytes[0] == 169 && bytes[1] == 254);
    }

    private static string ParseQueryValue(string query, string key)
    {
        var raw = query.TrimStart('?');
        foreach (var part in raw.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (Uri.UnescapeDataString(pair[0]).Equals(key, StringComparison.OrdinalIgnoreCase))
                return pair.Length > 1 ? Uri.UnescapeDataString(pair[1].Replace('+', ' ')) : "";
        }
        return "";
    }

    private static bool CryptographicEquals(string a, string b)
    {
        var aa = Encoding.UTF8.GetBytes(a); var bb = Encoding.UTF8.GetBytes(b);
        if (aa.Length != bb.Length) return false;
        var diff = 0; for (var i = 0; i < aa.Length; i++) diff |= aa[i] ^ bb[i];
        return diff == 0;
    }

    public void Dispose() => Stop();
}
