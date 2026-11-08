using System.IO;
using System.Diagnostics;
using System.Text;
using RadmirMonitor.Models;

namespace RadmirMonitor.Services;

public sealed class ChatLogMonitor : IDisposable
{
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private long _lastSize;
    private DateTime _lastWriteUtc;
    private DateTimeOffset _lastActivity = DateTimeOffset.MinValue;
    private double _onlineSeconds;

    public bool IsRunning => _cts is not null;
    public event Action<ParsedAction>? ActionParsed;
    public event Action<string>? Log;
    public event Action? OnlineMinute;

    public void Start(string path, string nick)
    {
        Stop();
        if (!File.Exists(path)) throw new FileNotFoundException("chatlog.txt не найден", path);
        _cts = new CancellationTokenSource();
        var fi = new FileInfo(path);
        _lastSize = fi.Length;
        _lastWriteUtc = fi.LastWriteTimeUtc;
        _onlineSeconds = 0;
        _loop = Task.Run(() => Loop(path, nick, _cts.Token));
        Log?.Invoke($"Мониторинг запущен: {path}");
    }

    public void Stop()
    {
        if (_cts is null) return;
        _cts.Cancel();
        _cts.Dispose();
        _cts = null;
        _loop = null;
        Log?.Invoke("Мониторинг остановлен");
    }

    private async Task Loop(string path, string nick, CancellationToken ct)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp1251 = Encoding.GetEncoding(1251);
        var lastTick = Stopwatch.GetTimestamp();

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var fi = new FileInfo(path);
                // RADMIR may recreate/truncate chatlog.txt. Never rewind to byte 0 here:
                // doing so replays old report/punishment lines and inflates counters.
                if (fi.Length < _lastSize || fi.LastWriteTimeUtc < _lastWriteUtc)
                {
                    _lastSize = fi.Length;
                    _lastWriteUtc = fi.LastWriteTimeUtc;
                    Log?.Invoke("chatlog.txt пересоздан · продолжаю с конца файла");
                }
                if (fi.Length > _lastSize)
                {
                    byte[] buffer;
                    await using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    {
                        fs.Seek(_lastSize, SeekOrigin.Begin);
                        var count = checked((int)Math.Min(int.MaxValue, fi.Length - _lastSize));
                        buffer = new byte[count];
                        _ = await fs.ReadAsync(buffer, ct);
                    }
                    _lastSize = fi.Length;
                    _lastWriteUtc = fi.LastWriteTimeUtc;
                    _lastActivity = DateTimeOffset.Now;
                    var text = cp1251.GetString(buffer);
                    foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                    {
                        // Online is counted from the actual game process.  Generic chat lines
                        // containing the word "онлайн" are intentionally NOT allowed to overwrite
                        // today's value: RADMIR has several unrelated online/AFK/stat messages.
                        var action = ChatParser.ParseAdminAction(line, nick);
                        if (action is not null) ActionParsed?.Invoke(action);
                    }
                }

                var nowTick = Stopwatch.GetTimestamp();
                var elapsed = (nowTick - lastTick) / (double)Stopwatch.Frequency;
                lastTick = nowTick;
                var recentChat = DateTimeOffset.Now - _lastActivity < TimeSpan.FromMinutes(3);
                if (IsGameRunning() || recentChat)
                {
                    _onlineSeconds += elapsed;
                    while (_onlineSeconds >= 60)
                    {
                        _onlineSeconds -= 60;
                        OnlineMinute?.Invoke();
                    }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Log?.Invoke("Ошибка chatlog: " + ex.Message);
            }

            try { await Task.Delay(350, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    public static bool IsGameRunning()
    {
        // Do not use a broad "radmir" substring here: RadmirMonitor.exe itself
        // contains that word and previously made the app count its own uptime as game online.
        var gameNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "gta_sa",
            "samp",
            "multiplayer_browser_cr",
            "crmp"
        };
        try
        {
            var ownPid = Environment.ProcessId;
            return Process.GetProcesses().Any(p =>
            {
                try { return p.Id != ownPid && gameNames.Contains(p.ProcessName); }
                catch { return false; }
            });
        }
        catch { return false; }
    }

    public void Dispose() => Stop();
}
