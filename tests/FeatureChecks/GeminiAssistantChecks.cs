using System.Drawing;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using DynamicIslandWin.Services;

internal static class GeminiAssistantChecks
{
    internal static async Task<int> RunAsync()
    {
        var screen = new Rectangle(-1920, 0, 1920, 1080);
        var app = new Rectangle(-900, 0, 820, 500);
        var island = new Rectangle(-1000, 8, 300, 40);
        var crop = GeminiIslandAnalysis.CaptureBounds(island, app, screen);
        Check(crop == new Rectangle(-900, 0, 216, 64), "The crop includes only the intersection and limited context inside the active window");
        Check(GeminiIslandAnalysis.CaptureBounds(new(0, 8, 300, 40), app, screen).IsEmpty,
            "A window on a different screen never produces a crop");
        Check(GeminiIslandAnalysis.CaptureBounds(new(-1000, 8, 103, 40), app, screen).IsEmpty,
            "A window touching a few edge pixels never produces a crop");

        var handler = new GeminiResponse();
        using var client = new HttpClient(handler);
        var overlap = new Rectangle(0, 8, 200, 40);
        byte[] image = [1, 2, 3];
        async Task<bool> Analyze(string key = "test-key")
            => await GeminiIslandAnalysis.IsObstructingAsync(key, image, overlap, CancellationToken.None, client);

        await Expect<ArgumentException>(() => Analyze(""));
        Check(handler.Calls == 0, "The API requires a key");
        Check(await Analyze(), "A positive Gemini decision hides the capsule");
        handler.Decision = """{"obstructs":false}""";
        Check(!await Analyze(), "A negative Gemini decision leaves the capsule visible");
        handler.Decision = """{"obstructs":"true"}""";
        await Expect<FormatException>(() => Analyze());
        handler.Status = HttpStatusCode.Forbidden;
        await Expect<HttpRequestException>(() => Analyze());
        Console.WriteLine("PASS: Gemini key requirement, clipped capture bounds, REST request and obstruction decisions.");
        return 0;
    }

    private static void Check(bool passed, string message)
    {
        if (!passed) throw new InvalidOperationException(message);
    }

    private static async Task Expect<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private sealed class GeminiResponse : HttpMessageHandler
    {
        internal int Calls { get; private set; }
        internal string Decision { get; set; } = """{"obstructs":true}""";
        internal HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Check(request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsoluteUri == "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.5-flash-lite:generateContent" &&
                request.Headers.GetValues("x-goog-api-key").Single() == "test-key",
                "The secret travels in the header to the Gemini endpoint");
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var parts = payload.RootElement.GetProperty("contents")[0].GetProperty("parts");
            Check(parts[0].GetProperty("text").GetString()!.Contains("x=0, y=8, width=200, height=40") &&
                parts[1].GetProperty("inlineData").GetProperty("data").GetString() == "AQID",
                "Gemini sees the crop and coordinates relative to that crop");
            return new(Status)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    candidates = new[] { new { finishReason = "STOP", content = new
                    {
                        parts = new[] { new { text = Decision } }
                    } } }
                }))
            };
        }
    }
}
