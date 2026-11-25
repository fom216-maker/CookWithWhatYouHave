using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace CookWithWhatYouHave.Api.Recipes;

public sealed class CloudflareClient(
    HttpClient httpClient,
    IOptions<CloudflareOptions> options) : IEmbeddingClient, IRecipeGenerator, IRecipeModel
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public bool IsConfigured => options.Value.HasCredentials;

    public string MissingKeyMessage =>
        "Faltan CLOUDFLARE_API_TOKEN y CLOUDFLARE_ACCOUNT_ID. Créalos en el panel de Cloudflare y reinicia la API.";

    public Task<float[]> EmbedQueryAsync(string text, CancellationToken cancellationToken) =>
        EmbedFirstAsync([AsQuery(text)], cancellationToken);

    public Task<IReadOnlyList<float[]>> EmbedDocumentsAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken) =>
        EmbedAsync(texts.Select(AsDocument).ToArray(), cancellationToken);

    public async Task<RecipeDraft> GenerateAsync(RecipePrompt prompt, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        EnsureCredentials(settings);

        using var response = await httpClient.PostAsync(
            "v1/chat/completions",
            JsonContent(new JsonObject
            {
                ["model"] = settings.Model,
                ["temperature"] = 0.4,
                ["max_tokens"] = 2048,
                ["messages"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["role"] = "user",
                        ["content"] = prompt.Text
                    }
                },
                ["response_format"] = new JsonObject
                {
                    ["type"] = "json_schema",
                    ["json_schema"] = new JsonObject
                    {
                        ["name"] = "recipe",
                        ["strict"] = true,
                        ["schema"] = JsonNode.Parse(RecipeSchema)
                    }
                }
            }),
            cancellationToken);

        var body = await ReadBodyAsync(response, cancellationToken);
        var text = ReadChatText(body);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new RecipeGenerationUnavailableException("Cloudflare no devolvió una receta.");
        }

        try
        {
            var draft = JsonSerializer.Deserialize<RecipeDraft>(StripFences(text), JsonOptions);
            return draft ?? throw new RecipeGenerationUnavailableException("La receta de Cloudflare no se pudo leer.");
        }
        catch (JsonException)
        {
            throw new RecipeGenerationUnavailableException("Cloudflare devolvió una receta incompleta.");
        }
    }

    public static IReadOnlyList<float[]> ReadEmbeddings(JsonElement body, int expectedCount, int expectedDimensions)
    {
        body = Unwrap(body);
        if (!body.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            throw new RecipeGenerationUnavailableException("Cloudflare no devolvió embeddings.");
        }

        if (data.GetArrayLength() > 0 && data[0].ValueKind == JsonValueKind.Array)
        {
            var direct = data.EnumerateArray().Select(item => ReadVector(item, expectedDimensions)).ToArray();
            if (direct.Length != expectedCount)
            {
                throw new RecipeGenerationUnavailableException("Cloudflare devolvió un número inesperado de embeddings.");
            }

            return direct;
        }

        var vectors = new float[expectedCount][];
        var seen = 0;
        foreach (var item in data.EnumerateArray())
        {
            var index = item.TryGetProperty("index", out var indexElement) ? indexElement.GetInt32() : seen;
            if (index < 0 || index >= expectedCount || !item.TryGetProperty("embedding", out var embedding))
            {
                throw new RecipeGenerationUnavailableException("Cloudflare devolvió un embedding que no se pudo leer.");
            }

            vectors[index] = ReadVector(embedding, expectedDimensions);
            seen++;
        }

        if (seen != expectedCount || vectors.Any(vector => vector is null))
        {
            throw new RecipeGenerationUnavailableException("Cloudflare devolvió un número inesperado de embeddings.");
        }

        return vectors;
    }

    public static string? ReadChatText(JsonElement body)
    {
        body = Unwrap(body);
        if (!body.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var message = choices.EnumerateArray().FirstOrDefault();
        if (message.ValueKind != JsonValueKind.Object
            || !message.TryGetProperty("message", out var contentOwner)
            || !contentOwner.TryGetProperty("content", out var content))
        {
            return null;
        }

        return content.ValueKind switch
        {
            JsonValueKind.String => content.GetString(),
            JsonValueKind.Array => string.Concat(content.EnumerateArray().Select(ReadPart)),
            _ => null
        };
    }

    private async Task<float[]> EmbedFirstAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        var vectors = await EmbedAsync(texts, cancellationToken);
        return vectors[0];
    }

    private async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        if (texts.Count == 0)
        {
            return [];
        }

        var settings = options.Value;
        EnsureCredentials(settings);
        var input = new JsonArray();
        foreach (var text in texts)
        {
            input.Add(text);
        }

        using var response = await httpClient.PostAsync(
            new Uri(httpClient.BaseAddress!, "run/" + settings.EmbeddingModel.Replace("@", "%40", StringComparison.Ordinal)),
            JsonContent(new JsonObject
            {
                ["input"] = input,
                ["dimensions"] = settings.EmbeddingDimensions
            }),
            cancellationToken);

        var body = await ReadBodyAsync(response, cancellationToken);
        return ReadEmbeddings(body, texts.Count, settings.EmbeddingDimensions);
    }

    private static JsonElement Unwrap(JsonElement body) =>
        body.ValueKind == JsonValueKind.Object
        && body.TryGetProperty("result", out var result)
        && result.ValueKind == JsonValueKind.Object
            ? result
            : body;

    private static async Task<JsonElement> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        JsonElement body;
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
            body = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            body = default;
        }

        var failed = !response.IsSuccessStatusCode
            || (body.ValueKind == JsonValueKind.Object
                && body.TryGetProperty("success", out var success)
                && success.ValueKind == JsonValueKind.False);
        if (!failed)
        {
            return body;
        }

        throw new RecipeGenerationUnavailableException(ReadError(body, (int)response.StatusCode));
    }

    private static string ReadError(JsonElement body, int statusCode)
    {
        if (body.ValueKind == JsonValueKind.Object
            && body.TryGetProperty("errors", out var errors)
            && errors.ValueKind == JsonValueKind.Array)
        {
            var first = errors.EnumerateArray().FirstOrDefault();
            if (first.ValueKind == JsonValueKind.Object
                && first.TryGetProperty("message", out var message)
                && message.GetString() is { Length: > 0 } text)
            {
                return text;
            }
        }

        if (body.ValueKind == JsonValueKind.Object
            && body.TryGetProperty("error", out var error))
        {
            if (error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("message", out var message)
                && message.GetString() is { Length: > 0 } text)
            {
                return text;
            }

            if (error.ValueKind == JsonValueKind.String && error.GetString() is { Length: > 0 } plain)
            {
                return plain;
            }
        }

        return $"Cloudflare respondió {statusCode}.";
    }

    private static float[] ReadVector(JsonElement embedding, int expected)
    {
        if (embedding.ValueKind != JsonValueKind.Array)
        {
            throw new RecipeGenerationUnavailableException("Cloudflare devolvió un embedding que no se pudo leer.");
        }

        var values = embedding.EnumerateArray().Select(item => item.GetSingle()).ToArray();
        if (values.Length != expected)
        {
            throw new RecipeGenerationUnavailableException(
                $"El embedding tiene {values.Length} dimensiones y la base espera {expected}.");
        }

        return values;
    }

    private static string? ReadPart(JsonElement part)
    {
        if (part.ValueKind == JsonValueKind.String)
        {
            return part.GetString();
        }

        if (part.ValueKind == JsonValueKind.Object && part.TryGetProperty("text", out var text))
        {
            return text.GetString();
        }

        return null;
    }

    private static void EnsureCredentials(CloudflareOptions settings)
    {
        if (!settings.HasCredentials)
        {
            throw new RecipeGenerationUnavailableException(
                "Faltan CLOUDFLARE_API_TOKEN y CLOUDFLARE_ACCOUNT_ID. Créalos en el panel de Cloudflare y reinicia la API.");
        }
    }

    private static StringContent JsonContent(JsonNode payload) =>
        new(payload.ToJsonString(), System.Text.Encoding.UTF8, "application/json");

    private static string AsQuery(string text) =>
        text.StartsWith("task:", StringComparison.OrdinalIgnoreCase)
            ? text
            : $"task: search result | query: {text}";

    private static string AsDocument(string text) =>
        text.StartsWith("title:", StringComparison.OrdinalIgnoreCase)
            ? text
            : $"title: none | text: {text}";

    private static string StripFences(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstNewLine = trimmed.IndexOf('\n');
        var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        if (firstNewLine < 0 || lastFence <= firstNewLine)
        {
            return trimmed;
        }

        return trimmed[(firstNewLine + 1)..lastFence].Trim();
    }

    private const string RecipeSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "title": { "type": "string" },
            "summary": { "type": "string" },
            "leftoverPitch": { "type": "string" },
            "substitutions": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "original": { "type": "string" },
                  "localEquivalent": { "type": "string" }
                },
                "required": ["original", "localEquivalent"]
              }
            },
            "usedIngredients": { "type": "array", "items": { "type": "string" } },
            "missingIngredients": { "type": "array", "items": { "type": "string" } },
            "steps": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "order": { "type": "integer" },
                  "instruction": { "type": "string" },
                  "temperature": { "type": "string" },
                  "duration": { "type": "string" }
                },
                "required": ["order", "instruction", "temperature", "duration"]
              }
            }
          },
          "required": ["title", "summary", "leftoverPitch", "substitutions", "usedIngredients", "missingIngredients", "steps"]
        }
        """;
}

public sealed class UnavailableRecipeModel(string message) : IEmbeddingClient, IRecipeGenerator, IRecipeModel
{
    public bool IsConfigured => false;

    public string MissingKeyMessage => message;

    public Task<float[]> EmbedQueryAsync(string text, CancellationToken cancellationToken) =>
        throw new RecipeGenerationUnavailableException(message);

    public Task<IReadOnlyList<float[]>> EmbedDocumentsAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken) =>
        throw new RecipeGenerationUnavailableException(message);

    public Task<RecipeDraft> GenerateAsync(RecipePrompt prompt, CancellationToken cancellationToken) =>
        throw new RecipeGenerationUnavailableException(message);
}
