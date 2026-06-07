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
            Each suggestion must use snake_case keys: resource_id, resource_name, suggested_title, collection, category, tags, suggested_note, reason.
            collection, category, and each tag must be objects with id, name, and is_new.
            Do not output global tags. Tags must belong to the selected or suggested collection.
            Categories must belong to the selected or suggested collection.
            If existing collections, categories, or collection tags are not suitable, suggest new items with is_new true. New items are only suggestions and require user confirmation later.
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
                "existing_collections",
                "existing_categories_by_collection",
                "existing_collection_tags_by_collection",
                "target_collection",
                "existing_resource_placements",
                "note"
            },
            target_collection = request.TargetCollectionId is null ? null : new
            {
                id = request.TargetCollectionId,
                name = request.TargetCollectionName
            },
            existing_collections = request.ExistingCollections.Select(collection => new
            {
                id = collection.Id,
                name = collection.Name,
                categories = collection.Categories.Select(category => new
                {
                    id = category.Id,
                    name = category.Name
                }),
                tags = collection.Tags.Select(tag => new
                {
                    id = tag.Id,
                    name = tag.Name
                })
            }),
            resources = request.Resources.Select(resource => new
            {
                resource_id = resource.ResourceId,
                file_name = resource.FileName,
                extension = resource.Extension,
                size_bytes = resource.SizeBytes,
                modified_at = resource.ModifiedAt,
                duration_ms = resource.DurationMs,
                existing_resource_placements = resource.ExistingPlacements.Select(placement => new
                {
                    collection_id = placement.CollectionId,
                    collection_name = placement.CollectionName,
                    category_id = placement.CategoryId,
                    category_name = placement.CategoryName
                }),
                note = string.IsNullOrWhiteSpace(resource.Note) ? null : resource.Note
            })
        };

        return $$"""
            Please suggest catalog metadata for these local resource records.
            Do not request or infer file contents. Do not suggest changing real files.
            Do not output global tags. Every tag must be scoped to the suggested collection.
            If target_collection is provided, prefer organizing resources inside that collection unless clearly unsuitable.
            Return this JSON shape:
            {
              "suggestions": [
                {
                  "resource_id": 123,
                  "resource_name": "file.ext",
                  "suggested_title": "display title",
                  "collection": { "id": 1, "name": "collection name", "is_new": false },
                  "category": { "id": 2, "name": "category name", "is_new": false },
                  "tags": [{ "id": 5, "name": "tag name", "is_new": false }],
                  "suggested_note": "optional note polish",
                  "reason": "brief reason"
                }
              ]
            }
            Return JSON only.

            {{JsonSerializer.Serialize(safePayload, JsonOptions)}}
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
                    Collection = GetSuggestedEntity(item, "collection"),
                    Category = GetSuggestedEntity(item, "category"),
                    Tags = GetSuggestedEntityArray(item, "tags"),
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

    private static AiSuggestedEntity? GetSuggestedEntity(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var id = GetOptionalLong(value, "id");
        var name = GetString(value, "name") ?? string.Empty;
        var isNew = GetBool(value, "is_new") || GetBool(value, "suggestion_new") || id is null or <= 0;
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return new AiSuggestedEntity
        {
            Id = id,
            Name = name.Trim(),
            IsNew = isNew
        };
    }

    private static IReadOnlyList<AiSuggestedEntity> GetSuggestedEntityArray(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value
            .EnumerateArray()
            .Where(element => element.ValueKind == JsonValueKind.Object)
            .Select(element =>
            {
                var id = GetOptionalLong(element, "id");
                var name = GetString(element, "name") ?? string.Empty;
                var isNew = GetBool(element, "is_new") || GetBool(element, "suggestion_new") || id is null or <= 0;
                return string.IsNullOrWhiteSpace(name)
                    ? null
                    : new AiSuggestedEntity
                    {
                        Id = id,
                        Name = name.Trim(),
                        IsNew = isNew
                    };
            })
            .Where(entity => entity is not null)
            .Select(entity => entity!)
            .DistinctBy(entity => entity.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static long? GetOptionalLong(JsonElement item, string propertyName)
    {
        return item.TryGetProperty(propertyName, out var value) && value.TryGetInt64(out var result)
            ? result
            : null;
    }

    private static bool GetBool(JsonElement item, string propertyName)
    {
        return item.TryGetProperty(propertyName, out var value) &&
            (value.ValueKind == JsonValueKind.True ||
                (value.ValueKind == JsonValueKind.String &&
                    bool.TryParse(value.GetString(), out var parsed) &&
                    parsed));
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
