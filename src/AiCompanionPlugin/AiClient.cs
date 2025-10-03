// SPDX-License-Identifier: MIT
// AiCompanionPlugin - AiClient.cs

#nullable enable
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace AiCompanionPlugin
{
    /// <summary>
    /// Minimal chat client that posts to an OpenAI-compatible chat completions endpoint.
    /// </summary>
    public sealed class AiClient : IDisposable
    {
        private readonly Configuration config;
        private readonly HttpClient http;

        public AiClient(Configuration config)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            http = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(Math.Max(5, config.RequestTimeoutSeconds))
            };
        }

        public async Task<string> CompleteAsync(
            IReadOnlyList<(string role, string content)> messages,
            CancellationToken cancellationToken = default)
        {
            if (messages is null || messages.Count == 0)
                throw new ArgumentException("messages must contain at least one item", nameof(messages));

            var trimmed = TrimHistory(messages, config.MaxHistoryMessages);

            var req = new ChatRequest
            {
                Model = config.Model,
                Messages = new List<ChatMessage>(trimmed.Count),
                Temperature = config.Temperature,
                MaxTokens = config.MaxTokens > 0 ? config.MaxTokens : null
            };

            foreach (var (role, content) in trimmed)
                req.Messages!.Add(new ChatMessage { Role = role, Content = content });

            var url = config.BackendBaseUrl?.Trim();
            if (string.IsNullOrWhiteSpace(url))
                throw new InvalidOperationException("BackendBaseUrl is empty. Set it in Configuration.");

            using var httpReq = new HttpRequestMessage(HttpMethod.Post, url);
            if (!string.IsNullOrWhiteSpace(config.ApiKey))
            {
                httpReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", config.ApiKey);
            }

            httpReq.Headers.TryAddWithoutValidation("Accept", "application/json");
            httpReq.Content = new StringContent(JsonSerializer.Serialize(req, JsonOptions), Encoding.UTF8, "application/json");

            using var resp = await http.SendAsync(httpReq, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"AI backend error {(int)resp.StatusCode} {resp.ReasonPhrase}: {body}");
            }

            // Try OpenAI chat response
            var chat = TryDeserialize<ChatResponse>(body);
            if (chat?.Choices is { Count: > 0 })
            {
                var first = chat.Choices[0];
                var content = first.Message?.Content ?? first.Delta?.Content ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(content))
                    return content.Trim();
            }

            // Legacy "text" response
            var legacy = TryDeserialize<LegacyCompletionResponse>(body);
            if (legacy?.Choices is { Count: > 0 })
            {
                var text = legacy.Choices[0].Text ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(text))
                    return text.Trim();
            }

            return body;
        }

        private static IReadOnlyList<(string role, string content)> TrimHistory(
            IReadOnlyList<(string role, string content)> messages, int max)
        {
            if (max <= 0 || messages.Count <= max) return messages;
            var start = Math.Max(0, messages.Count - max);
            var list = new List<(string role, string content)>(max);
            for (int i = start; i < messages.Count; i++) list.Add(messages[i]);
            return list;
        }

        public void Dispose() => http.Dispose();

        // ---------------- DTOs ----------------

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private static T? TryDeserialize<T>(string json)
        {
            try { return JsonSerializer.Deserialize<T>(json, JsonOptions); }
            catch { return default; }
        }

        private sealed class ChatRequest
        {
            [JsonPropertyName("model")]
            public string? Model { get; set; }

            [JsonPropertyName("messages")]
            public List<ChatMessage>? Messages { get; set; }

            [JsonPropertyName("temperature")]
            public double? Temperature { get; set; }

            [JsonPropertyName("max_tokens")]
            public int? MaxTokens { get; set; }
        }

        public sealed class ChatMessage
        {
            [JsonPropertyName("role")]
            public string Role { get; set; } = "user";

            [JsonPropertyName("content")]
            public string Content { get; set; } = "";
        }

        public sealed class ChatResponse
        {
            [JsonPropertyName("choices")]
            public List<ChatChoice>? Choices { get; set; }
        }

        public sealed class ChatChoice
        {
            [JsonPropertyName("message")]
            public ChatMessage? Message { get; set; }

            [JsonPropertyName("delta")]
            public ChatMessage? Delta { get; set; }
        }

        public sealed class LegacyCompletionResponse
        {
            [JsonPropertyName("choices")]
            public List<LegacyChoice>? Choices { get; set; }
        }

        public sealed class LegacyChoice
        {
            [JsonPropertyName("text")]
            public string? Text { get; set; }
        }
    }
}
