// SPDX-License-Identifier: MIT
// AiCompanionPlugin - AiClient.cs
//
// Minimal OpenAI-compatible client (non-streaming).
// Honors Configuration.AllowInsecureTls for self-signed localhost HTTPS.

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;

namespace AiCompanionPlugin
{
    public sealed class AiClient : IDisposable
    {
        private readonly Configuration config;
        private readonly IPluginLog? log;
        private readonly HttpClient http;
        private readonly HttpMessageHandler handler;
        private readonly JsonSerializerOptions jsonOptions;

        public AiClient(Configuration config, IPluginLog? log)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.log = log;

            var baseUrl = (config.BackendBaseUrl ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new InvalidOperationException("BackendBaseUrl is not set.");

            if (!baseUrl.EndsWith("/"))
                baseUrl += "/";

            // TLS handler: optionally allow self-signed (dev)
            if (config.AllowInsecureTls)
            {
                var h = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = InsecureCertOk
                };
                handler = h;
            }
            else
            {
                handler = new HttpClientHandler(); // default validation
            }

            http = new HttpClient(handler)
            {
                BaseAddress = new Uri(baseUrl),
                Timeout = TimeSpan.FromSeconds(Math.Clamp(config.RequestTimeoutSeconds <= 0 ? 60 : config.RequestTimeoutSeconds, 5, 600))
            };

            if (!string.IsNullOrWhiteSpace(config.ApiKey))
            {
                http.DefaultRequestHeaders.Remove("Authorization");
                http.DefaultRequestHeaders.Add("Authorization", $"Bearer {config.ApiKey}");
            }

            http.DefaultRequestHeaders.Remove("Accept");
            http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");

            jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            log?.Info($"[AI] HttpClient ready. Base={http.BaseAddress} InsecureTLS={config.AllowInsecureTls}");
        }

        private static bool InsecureCertOk(HttpRequestMessage _, X509Certificate2? __, X509Chain? ___, SslPolicyErrors errors)
        {
            // Accept any cert when opted-in (DEV ONLY!)
            return true;
        }

        public async Task<string> GetChatCompletionAsync(
            string systemPrompt,
            IReadOnlyList<(string role, string content)> prior,
            string userMessage,
            CancellationToken ct)
        {
            var messages = new List<ChatMessage>(capacity: 32);
            if (!string.IsNullOrWhiteSpace(systemPrompt))
                messages.Add(new ChatMessage("system", systemPrompt));

            int keep = Math.Max(0, config.MaxHistoryMessages);
            var historyPairs = prior
                .Where(p => p.role is "user" or "assistant")
                .TakeLast(keep);
            foreach (var (role, content) in historyPairs)
            {
                if (!string.IsNullOrWhiteSpace(content))
                    messages.Add(new ChatMessage(role, content));
            }

            messages.Add(new ChatMessage("user", userMessage ?? string.Empty));

            var req = new ChatRequest
            {
                Model = string.IsNullOrWhiteSpace(config.Model) ? "gpt-4o-mini" : config.Model.Trim(),
                Temperature = Math.Clamp(config.Temperature, 0, 2),
                MaxTokens = config.MaxTokens > 0 ? config.MaxTokens : null,
                Messages = messages
            };

            var endpoint = ResolveEndpoint(config.BackendBaseUrl);
            var payload = JsonSerializer.Serialize(req, jsonOptions);
            var httpContent = new StringContent(payload, Encoding.UTF8, "application/json");

            log?.Info($"[AI] POST {endpoint} (model={req.Model}, temp={req.Temperature}, max_tokens={(req.MaxTokens?.ToString() ?? "auto")})");

            using var resp = await http.PostAsync(endpoint, httpContent, ct).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                var reason = body;
                try
                {
                    var err = JsonSerializer.Deserialize<ErrorResponse>(body, jsonOptions);
                    if (err?.Error?.Message is { } msg && msg.Length > 0)
                        reason = msg;
                }
                catch { }

                throw new InvalidOperationException($"AI server returned {(int)resp.StatusCode}: {reason}");
            }

            // Try OpenAI chat format first
            try
            {
                var chat = JsonSerializer.Deserialize<ChatResponse>(body, jsonOptions);
                var text = chat?.Choices?.FirstOrDefault()?.Message?.Content;
                if (!string.IsNullOrWhiteSpace(text))
                    return text!;
            }
            catch { }

            // Legacy completion (choices[0].text])
            try
            {
                var legacy = JsonSerializer.Deserialize<LegacyCompletionResponse>(body, jsonOptions);
                var text = legacy?.Choices?.FirstOrDefault()?.Text;
                if (!string.IsNullOrWhiteSpace(text))
                    return text!;
            }
            catch { }

            log?.Warning("[AI] Unknown response payload, returning empty content.");
            return string.Empty;
        }

        private static string ResolveEndpoint(string? baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
                return "v1/chat/completions";

            var trimmed = baseUrl.Trim();
            if (trimmed.EndsWith("/v1/chat/completions", StringComparison.OrdinalIgnoreCase))
                return trimmed;

            return "v1/chat/completions";
        }

        public void Dispose()
        {
            http.Dispose();
            handler.Dispose();
        }

        // --------- JSON models ---------
        internal sealed class ChatRequest
        {
            [JsonPropertyName("model")] public string Model { get; set; } = "gpt-4o-mini";
            [JsonPropertyName("temperature")] public double? Temperature { get; set; }
            [JsonPropertyName("max_tokens")] public int? MaxTokens { get; set; }
            [JsonPropertyName("messages")] public List<ChatMessage> Messages { get; set; } = new();
        }

        internal sealed class ChatMessage
        {
            [JsonPropertyName("role")] public string Role { get; set; }
            [JsonPropertyName("content")] public string Content { get; set; }

            [JsonConstructor]
            public ChatMessage(string role, string content)
            {
                Role = role;
                Content = content;
            }
        }

        internal sealed class ChatResponse
        {
            [JsonPropertyName("choices")] public List<ChatChoice>? Choices { get; set; }
        }

        internal sealed class ChatChoice
        {
            [JsonPropertyName("message")] public ChatMessage? Message { get; set; }
        }

        internal sealed class LegacyCompletionResponse
        {
            [JsonPropertyName("choices")] public List<LegacyChoice>? Choices { get; set; }
        }

        internal sealed class LegacyChoice
        {
            [JsonPropertyName("text")] public string? Text { get; set; }
        }

        internal sealed class ErrorResponse
        {
            [JsonPropertyName("error")] public ErrorBody? Error { get; set; }
        }

        internal sealed class ErrorBody
        {
            [JsonPropertyName("message")] public string? Message { get; set; }
        }
    }
}
