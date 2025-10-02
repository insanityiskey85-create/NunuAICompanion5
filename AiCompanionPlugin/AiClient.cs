using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;

namespace AiCompanionPlugin;

public sealed class AiClient : System.IDisposable
{
    private readonly IPluginLog log;
    private readonly Configuration config;
    private readonly PersonaManager persona;
    private readonly HttpClient http;

    public AiClient(IPluginLog log, Configuration config, PersonaManager persona)
    {
        this.log = log; this.config = config; this.persona = persona;
        this.http = new HttpClient { Timeout = System.TimeSpan.FromSeconds(120) };
    }

    public void Dispose() => http.Dispose();

    public async IAsyncEnumerable<string> ChatStreamAsync(List<ChatMessage> history, string userInput, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
    {
        var url = config.BackendBaseUrl.TrimEnd('/') + "/v1/chat/completions";
        var messages = BuildMessages(history, userInput);
        var body = new
        {
            model = config.Model,
            messages = messages.Select(m => new { role = m.Role, content = m.Content }).ToArray(),
            stream = true,
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        if (!string.IsNullOrWhiteSpace(config.ApiKey))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        using var stream = await resp.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !token.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (!line.StartsWith("data:")) continue; // naive SSE parsing
            var json = line.Substring(5).Trim();
            if (json == "[DONE]") yield break;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var delta = root.GetProperty("choices")[0].GetProperty("delta");
                if (delta.TryGetProperty("content", out var content))
                {
                    var tokenText = content.GetString();
                    if (!string.IsNullOrEmpty(tokenText))
                        yield return tokenText;
                }
            }
            catch { /* ignore partials */ }
        }
    }

    public async Task<string> ChatOnceAsync(List<ChatMessage> history, string userInput, CancellationToken token)
    {
        var url = config.BackendBaseUrl.TrimEnd('/') + "/v1/chat/completions";
        var messages = BuildMessages(history, userInput);
        var body = new
        {
            model = config.Model,
            messages = messages.Select(m => new { role = m.Role, content = m.Content }).ToArray(),
            stream = false,
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        if (!string.IsNullOrWhiteSpace(config.ApiKey))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        using var resp = await http.SendAsync(req, token).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(token).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
    }

    private List<ChatMessage> BuildMessages(List<ChatMessage> history, string userInput)
    {
        var msgs = new List<ChatMessage>
        {
            new ChatMessage("system", persona.ActiveSystemPrompt)
        };

        var take = System.Math.Max(2, config.MaxHistoryMessages);
        foreach (var m in history.Skip(System.Math.Max(0, history.Count - take)))
            msgs.Add(m);

        msgs.Add(new ChatMessage("user", userInput));
        return msgs;
    }
}
