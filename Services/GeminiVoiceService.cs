using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using NAudio.Wave;

namespace DynamicIslandWin.Services;

internal static class GeminiKeyStore
{
    private static string KeyPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIslandWin", "gemini.key");
    internal static void Save(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) { if (File.Exists(KeyPath)) File.Delete(KeyPath); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(KeyPath)!);
        File.WriteAllBytes(KeyPath, ProtectedData.Protect(Encoding.UTF8.GetBytes(key.Trim()), null, DataProtectionScope.CurrentUser));
    }
    internal static string Read()
    {
        try { if (File.Exists(KeyPath)) return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(KeyPath), null, DataProtectionScope.CurrentUser)); }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException) { }
        return Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? "";
    }
}

internal static class VoiceLauncher
{
    private static readonly Dictionary<string, string> Targets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["youtube"] = "https://www.youtube.com/", ["google"] = "https://www.google.com/",
        ["steam"] = "steam://open/main", ["roblox"] = "https://www.roblox.com/",
        ["spotify"] = "spotify:", ["telegram"] = "tg://", ["discord"] = "discord://"
    };
    internal static string? Resolve(string target)
    {
        if (Targets.TryGetValue(target, out string? value)) return value;
        return Uri.TryCreate(target, UriKind.Absolute, out var uri) && uri.Scheme == "https"
            && string.IsNullOrEmpty(uri.UserInfo) && uri.HostNameType == UriHostNameType.Dns && !uri.IsLoopback
            && !uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ? uri.AbsoluteUri : null;
    }
    internal static bool Open(string target)
    {
        var value = Resolve(target);
        if (value == null) return false;
        try { Process.Start(new ProcessStartInfo(value) { UseShellExecute = true }); return true; } catch { return false; }
    }
}

internal sealed class GeminiVoiceService : IAsyncDisposable
{
    private readonly ClientWebSocket _socket = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Channel<byte[]> _outbox = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.DropOldest });
    private readonly SemaphoreSlim _sendLock = new(1);
    private WaveInEvent? _microphone;
    private WaveOutEvent? _speaker;
    private BufferedWaveProvider? _buffer;
    private Task? _sendTask, _receiveTask;
    private int _disposed;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal event Action<string>? Status;
    internal event Action<string>? Transcript;
    internal float Gain { get; set; } = .8f;
    internal bool Connected => _socket.State == WebSocketState.Open && _ready.Task.IsCompletedSuccessfully;

    internal async Task StartAsync(string key, string model, string voice)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("Добавьте API-ключ Gemini в настройках чата.");
        if (!System.Text.RegularExpressions.Regex.IsMatch(model, "^[a-zA-Z0-9._-]{1,100}$")) throw new FormatException("Недопустимое имя модели.");
        _socket.Options.SetRequestHeader("x-goog-api-key", key);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        await _socket.ConnectAsync(new Uri("wss://generativelanguage.googleapis.com/ws/google.ai.generativelanguage.v1beta.GenerativeService.BidiGenerateContent"), timeout.Token);
        await SendAsync(new
        {
            setup = new
            {
                model = "models/" + model,
                generationConfig = new { responseModalities = new[] { "AUDIO" }, speechConfig = new { voiceConfig = new { prebuiltVoiceConfig = new { voiceName = voice } } } },
                inputAudioTranscription = new { }, outputAudioTranscription = new { },
                systemInstruction = new { parts = new[] { new { text = "Ты помощник OC - Dynamic Island. Отвечай кратко на языке пользователя. Открывай приложения и сайты только по явной просьбе пользователя через open_target. Никогда не выполняй команды оболочки." } } },
                tools = new[] { new { functionDeclarations = new[] { new { name = "open_target", description = "Открыть по просьбе пользователя сайт https или youtube, google, steam, roblox, spotify, telegram, discord.", parameters = new { type = "OBJECT", properties = new { target = new { type = "STRING" } }, required = new[] { "target" } } } } } }
            }
        });
        _buffer = new BufferedWaveProvider(new WaveFormat(24000, 16, 1)) { BufferDuration = TimeSpan.FromSeconds(20), DiscardOnBufferOverflow = true };
        _speaker = new WaveOutEvent(); _speaker.Init(_buffer); _speaker.Play();
        _receiveTask = ReceiveAsync();
        await _ready.Task.WaitAsync(timeout.Token);
        _sendTask = SendAudioAsync();
        _microphone = new WaveInEvent { WaveFormat = new WaveFormat(16000, 16, 1), BufferMilliseconds = 50 };
        _microphone.DataAvailable += (_, e) => { if (!_stop.IsCancellationRequested) _outbox.Writer.TryWrite(e.Buffer.AsSpan(0, e.BytesRecorded).ToArray()); };
        _microphone.RecordingStopped += (_, e) => { if (e.Exception != null) { Status?.Invoke("Микрофон недоступен. Проверьте разрешения Windows."); _stop.Cancel(); } };
        _microphone.StartRecording();
        Status?.Invoke("Микрофон включён · разговор с Gemini");
    }

    internal Task SendTextAsync(string text) => SendAsync(new { realtimeInput = new { text } });
    private async Task SendAsync(object value)
    {
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(value);
        await _sendLock.WaitAsync(_stop.Token);
        try { await _socket.SendAsync(data, WebSocketMessageType.Text, true, _stop.Token); }
        finally { _sendLock.Release(); }
    }
    private async Task SendAudioAsync()
    {
        try
        {
            await foreach (var bytes in _outbox.Reader.ReadAllAsync(_stop.Token))
                await SendAsync(new { realtimeInput = new { audio = new { data = Convert.ToBase64String(bytes), mimeType = "audio/pcm;rate=16000" } } });
        }
        catch (OperationCanceledException) { } catch (Exception ex) { AppLog.Error("Gemini audio stream", ex); Status?.Invoke("Соединение прервано."); _stop.Cancel(); }
    }
    private async Task ReceiveAsync()
    {
        try
        {
            byte[] bytes = new byte[32 * 1024];
            while (!_stop.IsCancellationRequested)
            {
                using var message = new MemoryStream();
                ValueWebSocketReceiveResult result;
                do
                {
                    result = await _socket.ReceiveAsync(bytes.AsMemory(), _stop.Token);
                    if (result.MessageType == WebSocketMessageType.Close) { Status?.Invoke("Голосовой сеанс завершён сервером."); return; }
                    message.Write(bytes, 0, result.Count);
                    if (message.Length > 4_000_000) throw new InvalidDataException("Voice frame too large");
                } while (!result.EndOfMessage);
                using var json = JsonDocument.Parse(message.ToArray());
                var root = json.RootElement;
                if (root.TryGetProperty("setupComplete", out _)) _ready.TrySetResult();
                if (root.TryGetProperty("error", out _)) { Status?.Invoke("Gemini отклонил запрос. Проверьте ключ, модель и доступ Live API."); return; }
                if (root.TryGetProperty("serverContent", out var content))
                {
                    if (content.TryGetProperty("interrupted", out var interrupted) && interrupted.GetBoolean()) _buffer?.ClearBuffer();
                    foreach (string property in new[] { "inputTranscription", "outputTranscription" })
                        if (content.TryGetProperty(property, out var transcription) && transcription.TryGetProperty("text", out var text))
                            Transcript?.Invoke((property == "inputTranscription" ? "Вы: " : "Gemini: ") + text.GetString());
                    if (content.TryGetProperty("modelTurn", out var turn) && turn.TryGetProperty("parts", out var parts))
                        foreach (var part in parts.EnumerateArray())
                            if (part.TryGetProperty("inlineData", out var audio) && audio.TryGetProperty("data", out var data)
                                && audio.TryGetProperty("mimeType", out var mime) && mime.GetString()?.StartsWith("audio/pcm", StringComparison.Ordinal) == true)
                            {
                                byte[] pcm = Convert.FromBase64String(data.GetString()!);
                                for (int i = 0; i + 1 < pcm.Length; i += 2)
                                {
                                    short sample = BitConverter.ToInt16(pcm, i);
                                    short adjusted = (short)Math.Clamp(sample * Gain, short.MinValue, short.MaxValue);
                                    pcm[i] = (byte)adjusted; pcm[i + 1] = (byte)(adjusted >> 8);
                                }
                                _buffer?.AddSamples(pcm, 0, pcm.Length);
                            }
                }
                if (root.TryGetProperty("toolCall", out var tool) && tool.TryGetProperty("functionCalls", out var calls))
                    foreach (var call in calls.EnumerateArray())
                    {
                        string name = call.GetProperty("name").GetString() ?? "";
                        bool opened = name == "open_target" && call.TryGetProperty("args", out var args)
                            && args.TryGetProperty("target", out var target) && VoiceLauncher.Open(target.GetString() ?? "");
                        await SendAsync(new { toolResponse = new { functionResponses = new[] { new { id = call.GetProperty("id").GetString(), name, response = new { opened } } } } });
                    }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { AppLog.Error("Gemini receive", ex); Status?.Invoke("Ошибка голосового соединения. Проверьте сеть и модель."); }
        finally { _ready.TrySetCanceled(); _stop.Cancel(); _microphone?.StopRecording(); }
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Cancel(); _outbox.Writer.TryComplete();
        _microphone?.StopRecording(); _microphone?.Dispose();
        _speaker?.Stop(); _speaker?.Dispose(); _socket.Abort();
        try { await Task.WhenAll(_sendTask ?? Task.CompletedTask, _receiveTask ?? Task.CompletedTask); } catch (OperationCanceledException) { }
        _socket.Dispose(); _sendLock.Dispose(); _stop.Dispose();
    }
}
