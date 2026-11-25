using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace CookWithWhatYouHave.Api.Recipes;

public sealed class GeminiClient(
    HttpClient httpClient,
    IOptions<GeminiOptions> options) : IEmbeddingClient, IRecipeGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<float[]> EmbedQueryAsync(string text, CancellationToken cancellationToken) =>
        EmbedAsync(text, "RETRIEVAL_QUERY", cancellationToken);

    public async Task<IReadOnlyList<float[]>> EmbedDocumentsAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken)
    {
        if (texts.Count == 0)
        {
            return [];
        }

        var settings = options.Value;
        EnsureKey(settings);

        var requests = texts.Select(text => new
        {
            model = $"models/{settings.EmbeddingModel}",
            content = new { parts = new[] { new { text } } },
            taskType = "RETRIEVAL_DOCUMENT",
            outputDimensionality = settings.EmbeddingDimensions
        });

        using var response = await httpClient.PostAsJsonAsync(
            $"v1beta/models/{settings.EmbeddingModel}:batchEmbedContents",
            new { requests },
            JsonOptions,
            cancellationToken);

        await EnsureSuccessAsync(response);
        var body = await response.Content.ReadFromJsonAsync<BatchEmbeddingResponse>(JsonOptions, cancellationToken)
            ?? throw new RecipeGenerationUnavailableException("Gemini no devolvió embeddings.");

        if (body.Embeddings.Count != texts.Count)
        {
            throw new RecipeGenerationUnavailableException("Gemini devolvió un número inesperado de embeddings.");
        }

        return body.Embeddings.Select(item => ReadVector(item.Values, settings.EmbeddingDimensions)).ToArray();
    }

    public async Task<RecipeDraft> GenerateAsync(RecipePrompt prompt, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        EnsureKey(settings);

        var payload = new
        {
            contents = new[]
            {
                new { role = "user", parts = new[] { new { text = prompt.Text } } }
            },
            generationConfig = new
            {
                temperature = 0.4,
                responseMimeType = "application/json",
                responseSchema = ResponseSchema
            }
        };

        using var response = await httpClient.PostAsJsonAsync(
            $"v1beta/models/{settings.Model}:generateContent",
            payload,
            JsonOptions,
            cancellationToken);

        await EnsureSuccessAsync(response);
        var body = await response.Content.ReadFromJsonAsync<GenerateResponse>(JsonOptions, cancellationToken);
        var text = body?.Candidates.FirstOrDefault()?.Content.Parts.FirstOrDefault()?.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new RecipeGenerationUnavailableException("Gemini no devolvió una receta.");
        }

        var draft = JsonSerializer.Deserialize<RecipeDraft>(StripFences(text), JsonOptions);
        return draft ?? throw new RecipeGenerationUnavailableException("La receta de Gemini no se pudo leer.");
    }

    private async Task<float[]> EmbedAsync(string text, string taskType, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        EnsureKey(settings);

        var payload = new
        {
            content = new { parts = new[] { new { text } } },
            taskType,
            outputDimensionality = settings.EmbeddingDimensions
        };

        using var response = await httpClient.PostAsJsonAsync(
            $"v1beta/models/{settings.EmbeddingModel}:embedContent",
            payload,
            JsonOptions,
            cancellationToken);

        await EnsureSuccessAsync(response);
        var body = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(JsonOptions, cancellationToken)
            ?? throw new RecipeGenerationUnavailableException("Gemini no devolvió un embedding.");

        return ReadVector(body.Embedding.Values, settings.EmbeddingDimensions);
    }

    private static void EnsureKey(GeminiOptions settings)
    {
        if (!settings.HasApiKey)
        {
            throw new RecipeGenerationUnavailableException(
                "Falta GEMINI_API_KEY. Añádela en .env y reinicia la API.");
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var detail = $"Gemini respondió {(int)response.StatusCode}.";
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (document.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("message", out var message)
                && message.GetString() is { Length: > 0 } text)
            {
                detail = text;
            }
        }
        catch (JsonException)
        {
        }

        throw new RecipeGenerationUnavailableException(detail);
    }

    private static float[] ReadVector(IReadOnlyList<float> values, int expected)
    {
        if (values.Count != expected)
        {
            throw new RecipeGenerationUnavailableException(
                $"El embedding tiene {values.Count} dimensiones y la base espera {expected}.");
        }

        return values.ToArray();
    }

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

    private static readonly object ResponseSchema = new
    {
        type = "object",
        properties = new
        {
            title = new { type = "string" },
            summary = new { type = "string" },
            leftoverPitch = new { type = "string" },
            substitutions = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        original = new { type = "string" },
                        localEquivalent = new { type = "string" }
                    },
                    required = new[] { "original", "localEquivalent" }
                }
            },
            usedIngredients = new { type = "array", items = new { type = "string" } },
            missingIngredients = new { type = "array", items = new { type = "string" } },
            steps = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        order = new { type = "integer" },
                        instruction = new { type = "string" },
                        temperature = new { type = "string" },
                        duration = new { type = "string" }
                    },
                    required = new[] { "order", "instruction", "temperature", "duration" }
                }
            }
        },
        required = new[]
        {
            "title", "summary", "leftoverPitch", "substitutions",
            "usedIngredients", "missingIngredients", "steps"
        }
    };

    private sealed class EmbeddingResponse
    {
        public EmbeddingValues Embedding { get; set; } = new();
    }

    private sealed class BatchEmbeddingResponse
    {
        public List<EmbeddingValues> Embeddings { get; set; } = [];
    }

    private sealed class EmbeddingValues
    {
        public List<float> Values { get; set; } = [];
    }

    private sealed class GenerateResponse
    {
        public List<GenerateCandidate> Candidates { get; set; } = [];
    }

    private sealed class GenerateCandidate
    {
        public GenerateContent Content { get; set; } = new();
    }

    private sealed class GenerateContent
    {
        public List<GeneratePart> Parts { get; set; } = [];
    }

    private sealed class GeneratePart
    {
        public string Text { get; set; } = "";
    }
}
