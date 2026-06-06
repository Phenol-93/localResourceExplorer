using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LocalResourceExplorer.Models;

namespace LocalResourceExplorer.Services;

public sealed class OpenAiCompatibleProvider : IAiProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly AiSettings settings;
    private readonly HttpClient httpClient;

    public OpenAiCompatibleProvider(AiSettings settings, HttpClient? httpClient = null)
    {
        this.settings = settings;
        this.httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    }

    public async Task<IReadOnlyList<AiSuggestion>> SuggestOrganizationAsync(
        AiSuggestionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            throw new AiServiceException("请先在设置中填写 AI Base URL。");
        }

        if (string.IsNullOrWhiteSpace(settings.ModelName))
        {
            throw new AiServiceException("请先在设置中填写 AI 模型名称。");
        }

        var endpoint = BuildChatCompletionsEndpoint(settings.BaseUrl);
        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint);
        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        }

        var payload = new ChatCompletionRequest
        {
            Model = settings.ModelName.Trim(),
            Messages =
            [
                new ChatMessage("system", BuildSystemPrompt()),
                new ChatMessage("user", BuildUserPrompt(request))
            ],
            Temperature = 0.2,
            ResponseFormat = new ResponseFormat("json_object")
        };

        message.Content = new StringContent(
            JsonSerializer.Serialize(payload, JsonOptions),
            Encoding.UTF8,
            "application/json");

        try
        {
            using var response = await httpClient.SendAsync(message, cancellationToken);
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                AppLog.AiError(
                    new AiServiceException($"HTTP {(int)response.StatusCode}"),
                    "AI service returned an unsuccessful response");
                throw new AiServiceException($"AI 服务返回 HTTP {(int)response.StatusCode}，请检查 Base URL、模型名称或 Key。");
            }

            var chatResponse = JsonSerializer.Deserialize<ChatCompletionResponse>(responseContent, JsonOptions);
            var content = chatResponse?.Choices?.FirstOrDefault()?.Message?.Content;
            if (string.IsNullOrWhiteSpace(content))
            {
                AppLog.AiError(
                    new AiServiceException("Empty AI response content"),
                    "AI service returned empty suggestion content");
                throw new AiServiceException("AI 服务没有返回可解析的建议内容。");
            }

            return ParseSuggestions(content);
        }
        catch (AiServiceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLog.AiError(ex, "AI chat completion request failed");
            throw new AiServiceException($"AI 调用失败：{ex.Message}", ex);
        }
    }

    private static Uri BuildChatCompletionsEndpoint(string baseUrl)
    {
        var trimmed = baseUrl.Trim().TrimEnd('/');
        if (!trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            trimmed += "/chat/completions";
        }

        return new Uri(trimmed, UriKind.Absolute);
    }

    private static string BuildSystemPrompt()
    {
        return """
            You are an assistant for a local resource catalog app.
            You must only organize records using the metadata provided by the user.
            Never ask for file contents. Never assume access to files. Never suggest moving, deleting, or renaming real files.
            Return only valid JSON with a top-level "suggestions" array.
            Each item must use snake_case keys: resource_id, resource_name, suggested_title, suggested_collections, suggested_tags, suggested_note, reason.
            Use existing collection and tag names when suitable. New names are only suggestions and require user confirmation later.
            """;
    }

    private static string BuildUserPrompt(AiSuggestionRequest request)
    {
        var safePayload = new
        {
            allowed_fields = new[]
            {
                "file_name",
                "extension",
                "size_bytes",
                "modified_at",
                "duration_ms",
                "existing_collection_names",
                "existing_tag_names",
                "note"
            },
            existing_collection_names = request.ExistingCollectionNames,
            existing_tag_names = request.ExistingTagNames,
            resources = request.Resources.Select(resource => new
            {
                resource_id = resource.ResourceId,
                file_name = resource.FileName,
                extension = resource.Extension,
                size_bytes = resource.SizeBytes,
                modified_at = resource.ModifiedAt,
                duration_ms = resource.DurationMs,
                note = string.IsNullOrWhiteSpace(resource.Note) ? null : resource.Note
            })
        };

        return $"""
            Please suggest catalog metadata for these local resource records.
            Do not request or infer file contents. Do not suggest changing real files.
            Return JSON only.

            {JsonSerializer.Serialize(safePayload, JsonOptions)}
            """;
    }

    private static IReadOnlyList<AiSuggestion> ParseSuggestions(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            if (!document.RootElement.TryGetProperty("suggestions", out var suggestionsElement) ||
                suggestionsElement.ValueKind != JsonValueKind.Array)
            {
                throw new AiServiceException("AI 返回内容缺少 suggestions 数组。");
            }

            var suggestions = new List<AiSuggestion>();
            foreach (var item in suggestionsElement.EnumerateArray())
            {
                suggestions.Add(new AiSuggestion
                {
                    ResourceId = GetLong(item, "resource_id"),
                    ResourceName = GetString(item, "resource_name") ?? string.Empty,
                    SuggestedTitle = GetString(item, "suggested_title"),
                    SuggestedCollections = GetStringArray(item, "suggested_collections"),
                    SuggestedTags = GetStringArray(item, "suggested_tags"),
                    SuggestedNote = GetString(item, "suggested_note"),
                    Reason = GetString(item, "reason")
                });
            }

            return suggestions;
        }
        catch (AiServiceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLog.AiError(ex, "AI suggestion response parsing failed");
            throw new AiServiceException($"AI 返回内容解析失败：{ex.Message}", ex);
        }
    }

    private static long GetLong(JsonElement item, string propertyName)
    {
        return item.TryGetProperty(propertyName, out var value) && value.TryGetInt64(out var result)
            ? result
            : 0;
    }

    private static string? GetString(JsonElement item, string propertyName)
    {
        return item.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static IReadOnlyList<string> GetStringArray(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value
            .EnumerateArray()
            .Where(element => element.ValueKind == JsonValueKind.String)
            .Select(element => element.GetString())
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => text!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private sealed record ChatMessage(string Role, string Content);

    private sealed record ResponseFormat(string Type);

    private sealed class ChatCompletionRequest
    {
        public string Model { get; set; } = string.Empty;

        public IReadOnlyList<ChatMessage> Messages { get; set; } = [];

        public double Temperature { get; set; }

        [JsonPropertyName("response_format")]
        public ResponseFormat? ResponseFormat { get; set; }
    }

    private sealed class ChatCompletionResponse
    {
        public IReadOnlyList<ChatChoice>? Choices { get; set; }
    }

    private sealed class ChatChoice
    {
        public ChatResponseMessage? Message { get; set; }
    }

    private sealed class ChatResponseMessage
    {
        public string? Content { get; set; }
    }
}
