using System;
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

public sealed class AiClient : IDisposable
{
    private readonly IPluginLog log;
    private readonly Configuration config;
    private readonly PersonaManager persona;
    private readonly HttpClient http;

    private enum BackendKind { OpenAI, Ollama }
    private (BackendKind kind, string url)? resolved; // cached resolved endpoint

    public AiClient(IPluginLog log, Configuration config, PersonaManager persona)
    {
        this.log = log; this.config = config; this.persona = persona;
        this.http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
    }

    public void Dispose() => http.Dispose();

    // -------- public API --------

    public async IAsyncEnumerable<string> ChatStreamAsync(
        List<ChatMessage> history,
        string userInput,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
    {
        var (kind, url) = await EnsureResolvedAsync(token).ConfigureAwait(false);

        // Build minimal body according to backend
        var msgs = BuildMessages(history, userInput);

        if (kind == BackendKind.OpenAI)
        {
            var body = new
            {
                model = config.Model,
                messages = msgs.Select(m => new { role = m.Role, content = m.Content }).ToArray(),
                stream = true,
            };

            using var req = MakeJsonRequest(url, body);
            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            // If server returns 404, nuke resolution and retry once
            if ((int)resp.StatusCode == 404)
            {
                resolved = null;
                (kind, url) = await EnsureResolvedAsync(token).ConfigureAwait(false);
                using var req2 = MakeJsonRequest(url, body);
                using var resp2 = await http.SendAsync(req2, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                resp2.EnsureSuccessStatusCode();
                await foreach (var piece in ReadOpenAIStreamAsync(resp2, token)) yield return piece;
                yield break;
            }

            resp.EnsureSuccessStatusCode();
            await foreach (var piece in ReadOpenAIStreamAsync(resp, token)) yield return piece;
        }
        else // Ollama: streaming differs; safest path is non-stream and yield once
        {
            var text = await ChatOnceAsync(history, userInput, token).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(text))
                yield return text;
        }
    }

    public async Task<string> ChatOnceAsync(List<ChatMessage> history, string userInput, CancellationToken token)
    {
        var (kind, url) = await EnsureResolvedAsync(token).ConfigureAwait(false);
        var msgs = BuildMessages(history, userInput);

        if (kind == BackendKind.OpenAI)
        {
            var body = new
            {
                model = config.Model,
                messages = msgs.Select(m => new { role = m.Role, content = m.Content }).ToArray(),
                stream = false,
            };

            using var req = MakeJsonRequest(url, body);
            using var resp = await http.SendAsync(req, token).ConfigureAwait(false);
            if ((int)resp.StatusCode == 404)
            {
                resolved = null;
                (kind, url) = await EnsureResolvedAsync(token).ConfigureAwait(false);
                using var req2 = MakeJsonRequest(url, body);
                using var resp2 = await http.SendAsync(req2, token).ConfigureAwait(false);
                resp2.EnsureSuccessStatusCode();
                var json2 = await resp2.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                return ExtractOpenAIText(json2);
            }

            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            return ExtractOpenAIText(json);
        }
        else // Ollama
        {
            var body = new
            {
                model = config.Model,
                messages = msgs.Select(m => new { role = m.Role, content = m.Content }).ToArray(),
                stream = false
            };

            using var req = MakeJsonRequest(url, body);
            using var resp = await http.SendAsync(req, token).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            // Ollama: { message: { content: "..." }, done: true, ... }
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("message", out var msg) &&
                msg.TryGetProperty("content", out var content))
                return content.GetString() ?? string.Empty;

            // Some proxies wrap to OpenAI shape
            try { return ExtractOpenAIText(json); } catch { }
            return string.Empty;
        }
    }

    public async Task<(bool ok, string detail)> TestConnectionAsync(CancellationToken token)
    {
        try
        {
            var (kind, url) = await EnsureResolvedAsync(token).ConfigureAwait(false);
            var _ = await ChatOnceAsync(new List<ChatMessage>(), "ping", token).ConfigureAwait(false);
            var kindName = kind == BackendKind.OpenAI ? "OpenAI-compatible" : "Ollama";
            return (true, $"Connected: {kindName} at {url}");
        }
        catch (Exception ex)
        {
            return (false, $"Failed: {ex.Message}");
        }
    }

    // -------- internals --------

    private HttpRequestMessage MakeJsonRequest(string url, object bodyObj)
    {
        var json = JsonSerializer.Serialize(bodyObj);
        var req = new HttpRequestMessage(HttpMethod.Post, url);
        if (!string.IsNullOrWhiteSpace(config.ApiKey))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return req;
    }

    private static string ExtractOpenAIText(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("choices")[0]
            .TryGetProperty("message", out var msg)
            ? msg.GetProperty("content").GetString() ?? string.Empty
            : doc.RootElement.GetProperty("choices")[0]
                .GetProperty("delta").GetProperty("content").GetString() ?? string.Empty;
    }

    private async IAsyncEnumerable<string> ReadOpenAIStreamAsync(HttpResponseMessage resp, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
    {
        using var stream = await resp.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !token.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (!line.StartsWith("data:")) continue;
            var payload = line.Substring(5).Trim();
            if (payload == "[DONE]") yield break;

            string? toEmit = null;
            try
            {
                using var doc = JsonDocument.Parse(payload);
                var delta = doc.RootElement.GetProperty("choices")[0].GetProperty("delta");
                if (delta.TryGetProperty("content", out var content))
                {
                    var tokenText = content.GetString();
                    if (!string.IsNullOrEmpty(tokenText))
                        toEmit = tokenText;
                }
            }
            catch
            {
                // ignore partials
            }

            if (toEmit is not null)
                yield return toEmit;
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416:Validate platform compatibility", Justification = "<Pending>")]
    private async Task<(BackendKind kind, string url)> EnsureResolvedAsync(CancellationToken token)
    {
        if (resolved is { } r) return r;

        var baseUrl = config.BackendBaseUrl?.TrimEnd('/') ?? "";
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("Backend Base URL is empty.");

        var attempts = new List<(BackendKind kind, string url, object probeBody, Func<string, bool> validator)>();

        // OpenAI-compatible chat completions
        var openAiUrl = $"{baseUrl}/v1/chat/completions";
        var openAiProbe = new
        {
            model = config.Model,
            messages = new[] { new { role = "user", content = "ping" } },
            stream = false
        };
        attempts.Add((BackendKind.OpenAI, openAiUrl, openAiProbe, json =>
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.TryGetProperty("choices", out _);
            }
            catch { return false; }
        }
        ));

        // Ollama native chat
        var ollamaUrl = $"{baseUrl}/api/chat";
        var ollamaProbe = new
        {
            model = config.Model,
            messages = new[] { new { role = "user", content = "ping" } },
            stream = false
        };
        attempts.Add((BackendKind.Ollama, ollamaUrl, ollamaProbe, json =>
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.TryGetProperty("message", out _)
                    || doc.RootElement.TryGetProperty("choices", out _); // some proxies wrap
            }
            catch { return false; }
        }
        ));

        // Try each candidate
        foreach (var (kind, url, body, validate) in attempts)
        {
            try
            {
                using var req = MakeJsonRequest(url, body);
                using var resp = await http.SendAsync(req, token).ConfigureAwait(false);
                if ((int)resp.StatusCode == 404) { continue; } // try next
                var text = await resp.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode && validate(text))
                {
                    resolved = (kind, url);
                    log.Info($"AI endpoint resolved: {kind} at {url}");
                    return resolved.Value;
                }
                else
                {
                    log.Warning($"Probe failed {url}: {(int)resp.StatusCode} {resp.ReasonPhrase}");
                }
            }
            catch (Exception ex)
            {
                log.Warning(ex, $"Probe exception for {url}");
            }
        }

        throw new InvalidOperationException("Unable to resolve AI backend endpoint. Tried /v1/chat/completions and /api/chat; got 404/invalid responses. Check Base URL and model.");
    }

    private List<ChatMessage> BuildMessages(List<ChatMessage> history, string userInput)
    {
        var msgs = new List<ChatMessage> { new("system", persona.ActiveSystemPrompt) };
        var take = Math.Max(2, config.MaxHistoryMessages);
        foreach (var m in history.Skip(Math.Max(0, history.Count - take))) msgs.Add(m);
        if (!string.IsNullOrWhiteSpace(userInput))
            msgs.Add(new ChatMessage("user", userInput));
        return msgs;
    }
}
