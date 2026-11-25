using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CookWithWhatYouHave.Api.Recipes;
using CookWithWhatYouHave.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CookWithWhatYouHave.Api.Tests;

public sealed class RecipeGenerationTests
{
    [Fact]
    public async Task Retrieved_recipe_is_included_in_the_prompt()
    {
        var generator = new CapturingGenerator();
        var orchestrator = CreateOrchestrator(generator, new FakeOpenFoodFacts(null), new EmptyPrices());

        await orchestrator.GenerateAsync(SampleRequest(), CancellationToken.None);

        Assert.Contains("TEST-MIGAS-TOKEN", generator.Prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Product_alert_appears_only_when_open_food_facts_returns_data()
    {
        var withProduct = CreateOrchestrator(
            new CapturingGenerator(),
            new FakeOpenFoodFacts(new ProductFact("Tomate frito", "d", 4, "gluten", 1.4, 6)),
            new EmptyPrices());
        var withoutProduct = CreateOrchestrator(new CapturingGenerator(), new FakeOpenFoodFacts(null), new EmptyPrices());

        var alerted = await withProduct.GenerateAsync(SampleRequest(), CancellationToken.None);
        var plain = await withoutProduct.GenerateAsync(SampleRequest(), CancellationToken.None);

        Assert.Contains(alerted.Blocks, block => block is ProductAlertBlock);
        Assert.DoesNotContain(plain.Blocks, block => block is ProductAlertBlock);
    }

    [Fact]
    public async Task Superfudis_failure_keeps_the_recipe_without_prices()
    {
        var orchestrator = CreateOrchestrator(new CapturingGenerator(), new FakeOpenFoodFacts(null), new ThrowingPrices());

        var screen = await orchestrator.GenerateAsync(SampleRequest(), CancellationToken.None);

        Assert.Contains(screen.Blocks, block => block is RecipeHeroBlock);
        Assert.DoesNotContain(screen.Blocks, block => block is PriceComparisonBlock);
    }

    [Fact]
    public async Task Empty_ingredients_return_bad_request()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/recipes/generate", new
        {
            ingredients = "",
            useLeftovers = false,
            cookingMethod = "PanOven"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public void Price_parser_picks_the_cheapest_retailer()
    {
        using var document = JsonDocument.Parse(
            """
            {"retailers":[{"retailer":"Carrefour","total":5.10},{"retailer":"Mercadona","total":4.50}]}
            """);

        var quote = SuperfudisClient.Parse(document.RootElement);

        Assert.NotNull(quote);
        Assert.Equal("Mercadona", quote.CheapestRetailer);
        Assert.Equal(4.50m, quote.Total);
    }

    [SkippableFact]
    public async Task Gemini_embeds_and_generates_when_the_key_is_configured()
    {
        var key = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        Skip.If(string.IsNullOrWhiteSpace(key), "GEMINI_API_KEY is not set.");

        var settings = new GeminiOptions
        {
            ApiKey = key!,
            Model = Environment.GetEnvironmentVariable("GEMINI_MODEL") ?? "gemini-3.6-flash",
            EmbeddingModel = Environment.GetEnvironmentVariable("GEMINI_EMBEDDING_MODEL") ?? "gemini-embedding-2",
            EmbeddingDimensions = int.TryParse(Environment.GetEnvironmentVariable("GEMINI_EMBEDDING_DIMENSIONS"), out var dimensions)
                ? dimensions
                : 768
        };

        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://generativelanguage.googleapis.com/"),
            Timeout = TimeSpan.FromSeconds(45)
        };
        httpClient.DefaultRequestHeaders.Add("x-goog-api-key", settings.ApiKey);
        var client = new GeminiClient(httpClient, Options.Create(settings));

        var embedding = await client.EmbedQueryAsync("tortilla de patatas", CancellationToken.None);
        var draft = await client.GenerateAsync(
            new RecipePrompt("Devuelve una receta mínima de tortilla francesa. leftoverPitch vacío. Sin sustituciones. Un solo paso."),
            CancellationToken.None);

        Assert.Equal(settings.EmbeddingDimensions, embedding.Length);
        Assert.False(string.IsNullOrWhiteSpace(draft.Title));
    }

    private static GenerateRecipeRequest SampleRequest() => new()
    {
        Ingredients = "pan duro y ajo",
        UseLeftovers = true,
        CookingMethod = CookingMethod.PanOven
    };

    private static RecipeOrchestrator CreateOrchestrator(
        CapturingGenerator generator,
        IOpenFoodFactsClient openFoodFacts,
        ISuperfudisClient prices) =>
        new(
            Options.Create(new GeminiOptions { ApiKey = "test-key" }),
            new FakeEmbeddings(),
            new FakeRetriever(),
            openFoodFacts,
            generator,
            prices,
            new InMemoryRecipeStore(),
            NullLogger<RecipeOrchestrator>.Instance);

    private sealed class FakeEmbeddings : IEmbeddingClient
    {
        public Task<float[]> EmbedQueryAsync(string text, CancellationToken cancellationToken) =>
            Task.FromResult(new float[768]);

        public Task<IReadOnlyList<float[]>> EmbedDocumentsAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeRetriever : IRecipeRetriever
    {
        public Task<IReadOnlyList<RetrievedRecipe>> SearchAsync(string query, float[] embedding, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RetrievedRecipe>>(
            [
                new RetrievedRecipe(Guid.NewGuid(), "TEST-MIGAS-TOKEN", "pan duro", "Dorar el pan.", "Andalucía")
            ]);
    }

    private sealed class FakeOpenFoodFacts(ProductFact? product) : IOpenFoodFactsClient
    {
        public Task<ProductFact?> FindAsync(string ingredients, string? barcode, CancellationToken cancellationToken) =>
            Task.FromResult(product);
    }

    private sealed class EmptyPrices : ISuperfudisClient
    {
        public Task<PriceQuote?> CompareMissingAsync(IReadOnlyList<string> missingIngredients, CancellationToken cancellationToken) =>
            Task.FromResult<PriceQuote?>(null);
    }

    private sealed class ThrowingPrices : ISuperfudisClient
    {
        public Task<PriceQuote?> CompareMissingAsync(IReadOnlyList<string> missingIngredients, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Superfudis is down.");
    }

    private sealed class CapturingGenerator : IRecipeGenerator
    {
        public string? Prompt { get; private set; }

        public Task<RecipeDraft> GenerateAsync(RecipePrompt prompt, CancellationToken cancellationToken)
        {
            Prompt = prompt.Text;
            return Task.FromResult(new RecipeDraft
            {
                Title = "Migas",
                Summary = "Pan del día anterior.",
                LeftoverPitch = "Aprovecha el pan duro.",
                UsedIngredients = ["pan duro"],
                MissingIngredients = ["ajo"],
                Steps = [new StepDraft { Order = 1, Instruction = "Dorar el pan." }]
            });
        }
    }

    private sealed class InMemoryRecipeStore : IGeneratedRecipeStore
    {
        public Task SaveAsync(RecipeScreenResponse screen, string ingredients, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<RecipeScreenResponse?> GetAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<RecipeScreenResponse?>(null);

        public Task<IReadOnlyList<RecipeListItemResponse>> ListAsync(int take, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RecipeListItemResponse>>([]);
    }
}
