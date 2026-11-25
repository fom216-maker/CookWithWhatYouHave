namespace CookWithWhatYouHave.Api.Recipes;

public sealed class GeminiOptions
{
    public const string SectionName = "Gemini";

    public string ApiKey { get; set; } = "";

    public string Model { get; set; } = "gemini-3.6-flash";

    public string EmbeddingModel { get; set; } = "gemini-embedding-2";

    public int EmbeddingDimensions { get; set; } = 768;

    public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);
}

public sealed class AiOptions
{
    public const string SectionName = "Ai";

    public string Provider { get; set; } = "Gemini";
}

public sealed class CloudflareOptions
{
    public const string SectionName = "Cloudflare";

    public string ApiToken { get; set; } = "";

    public string AccountId { get; set; } = "";

    public string Model { get; set; } = "@cf/meta/llama-3.3-70b-instruct-fp8-fast";

    public string EmbeddingModel { get; set; } = "@cf/google/embeddinggemma-2";

    public int EmbeddingDimensions { get; set; } = 768;

    public bool HasCredentials =>
        !string.IsNullOrWhiteSpace(ApiToken) && !string.IsNullOrWhiteSpace(AccountId);
}

public sealed class OpenFoodFactsOptions
{
    public const string SectionName = "OpenFoodFacts";

    public string BaseUrl { get; set; } = "https://es.openfoodfacts.org";

    public string UserAgent { get; set; } = "CookWithWhatYouHave/1.0 (dev)";
}

public sealed class SuperfudisOptions
{
    public const string SectionName = "Superfudis";

    public string BaseUrl { get; set; } = "https://supercomparador-i9tw.onrender.com";
}
